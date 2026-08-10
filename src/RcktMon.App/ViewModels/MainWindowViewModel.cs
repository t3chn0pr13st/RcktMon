using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RcktMon.App.Formatting;
using RcktMon.App.Localization;
using RcktMon.App.Threading;
using RcktMon.Client.Core.Connection;
using RcktMon.Client.Core.Market;
using RcktMon.Client.Core.Platform;
using RcktMon.Client.Core.Security;
using RcktMon.Client.Core.Settings;

namespace RcktMon.App.ViewModels;

/// <summary>
/// The shell: owns the session, the pages, and the one timer that drives both.
/// </summary>
/// <remarks>
/// <para>
/// A single 100 ms drain feeds every page. The alternative — a timer per page — was what
/// made the pre-rewrite app's threading so hard to reason about: it ran an update poll, a
/// WebSocket loop, a Telegram poll and several <c>while(true)</c> loops with no
/// cancellation between them, and shut down by calling <c>Process.Kill</c>.
/// </para>
/// <para>
/// Everything here takes a cancellation token and stops when the window closes.
/// </para>
/// </remarks>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>How often queued messages are applied on the UI thread.</summary>
    private static readonly TimeSpan DrainInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How often the clock-driven text — countdowns, ages — is refreshed.</summary>
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How stale quotes may get during an open session before the grid is dimmed.
    /// </summary>
    /// <remarks>
    /// Only meaningful while the exchange is actually trading — outside a session a
    /// motionless grid is correct, and warning about it would train users to ignore the
    /// warning.
    /// </remarks>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);

    private readonly IAppPaths _paths;
    private readonly ISecretStore _secrets;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly HashSet<string> _pagesThatFaulted = [];
    private readonly JsonFileStore<ClientSettings> _settingsFile;
    private readonly JsonFileStore<ViewCollection> _viewsFile;
    private readonly ClientSettings _settings;
    private readonly ViewCollection _views;

    private readonly DispatcherTimer _drainTimer;
    private readonly DispatcherTimer _clockTimer;

    private ClientSession _session;
    private IDisposable _statusSubscription;
    private Task _sessionTask;

    public MainWindowViewModel(IAppPaths paths, ILoggerFactory? loggerFactory = null)
    {
        _paths = paths;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<MainWindowViewModel>();
        _secrets = SecretStoreFactory.Create(paths.ConfigDirectory);

        _settingsFile = new JsonFileStore<ClientSettings>(Path.Combine(paths.ConfigDirectory, "settings.json"));
        _viewsFile = new JsonFileStore<ViewCollection>(Path.Combine(paths.ConfigDirectory, "views.json"));

        _settings = _settingsFile.Load();
        _views = _viewsFile.Load();

        if (string.IsNullOrEmpty(_settings.DeviceId))
        {
            _settings.DeviceId = Guid.NewGuid().ToString("D");
        }

        if (_views.Views.Count == 0)
        {
            _views.Views.AddRange(ViewPresets.Create());
        }

        // Before anything subscribes: pages, columns and chips all take their text from
        // the localizer at construction.
        Localizer.Instance.Use(_settings.Locale);

        _session = CreateSession();
        _statusSubscription = SubscribeStatus(_session);

        Pages = [];
        BuildPages();

        _sessionTask = _session.Start();

        _drainTimer = new DispatcherTimer(DrainInterval, DispatcherPriority.Background, (_, _) => Drain());
        _clockTimer = new DispatcherTimer(ClockInterval, DispatcherPriority.Background, (_, _) => Tick());

        _drainTimer.Start();
        _clockTimer.Start();

        if (!_settings.OnboardingCompleted)
        {
            Onboarding = CreateOnboarding();
        }

        ApplyTheme(_settings.Theme);
    }

    /// <summary>Rail destinations, in order.</summary>
    public ObservableCollection<PageViewModel> Pages { get; }

    /// <summary>Persisted settings, exposed for the window to read its geometry.</summary>
    public ClientSettings Settings => _settings;

    [ObservableProperty] private PageViewModel? _currentPage;

    [ObservableProperty] private ConnectionStatus _status = new(ConnectionState.Disconnected);

    [ObservableProperty] private string? _accountName;

    [ObservableProperty] private string? _serverVersion;

    [ObservableProperty] private string _serverAddress = string.Empty;

    [ObservableProperty] private bool _isRailExpanded = true;

    /// <summary>Rail width for the current expanded state.</summary>
    public double RailWidth => IsRailExpanded ? 208 : 56;

    partial void OnIsRailExpandedChanged(bool value) => OnPropertyChanged(nameof(RailWidth));

    [ObservableProperty] private OnboardingViewModel? _onboarding;

    /// <summary>Set while quotes have gone stale during an open session.</summary>
    [ObservableProperty] private bool _isStale;

    [ObservableProperty] private string _marketPhase = string.Empty;

    [ObservableProperty] private string _marketCountdown = string.Empty;

    [ObservableProperty] private int _signalsToday;

    [ObservableProperty] private int _updatesPerSecond;

    [ObservableProperty] private string _dataAge = "—";

    [ObservableProperty] private string? _activeProfileName;

    /// <summary>True while onboarding is covering the app.</summary>
    public bool IsOnboarding => Onboarding is not null;

    partial void OnOnboardingChanged(OnboardingViewModel? value) => OnPropertyChanged(nameof(IsOnboarding));

    partial void OnCurrentPageChanged(PageViewModel? oldValue, PageViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is null)
        {
            return;
        }

        newValue.IsActive = true;
        newValue.Activated();

        _settings.StartPage = newValue.Key;
        Persist();
    }

    /// <summary>Switches pages by key. Used by the rail and by cross-page links.</summary>
    [RelayCommand]
    public void Navigate(string? key)
    {
        PageViewModel? page = Pages.FirstOrDefault(p => p.Key == key);
        if (page is not null)
        {
            CurrentPage = page;
        }
    }

    [RelayCommand]
    private void ToggleRail() => IsRailExpanded = !IsRailExpanded;

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemePreference next = Application.Current?.ActualThemeVariant == ThemeVariant.Dark
            ? ThemePreference.Light
            : ThemePreference.Dark;

        _settings.Theme = next;
        ApplyTheme(next);
        Persist();
    }

    /// <summary>Tears the session down and starts a fresh one against the current settings.</summary>
    [RelayCommand]
    public void Reconnect() => _ = RestartSessionAsync();

    /// <summary>Writes settings and views to disk. Debounced by the file store.</summary>
    public void Persist()
    {
        _views.Views.Clear();

        if (Pages.OfType<MonitorViewModel>().FirstOrDefault() is { } monitor)
        {
            _views.Views.AddRange(monitor.AllViews);
        }

        _settingsFile.Save(_settings);
        _viewsFile.Save(_views);
    }

    /// <summary>Switches the interface language and remembers the choice.</summary>
    /// <remarks>
    /// Bound resource strings follow the localizer on their own. What needs a nudge is
    /// text that has already been composed: a converter's output, a formatted sentence,
    /// a list of cards. Everything below is that nudge, and it runs once per switch.
    /// </remarks>
    public void ApplyLanguage(string code)
    {
        _settings.Locale = code;
        Localizer.Instance.Use(code);

        OnPropertyChanged(nameof(Status));
        _session.Quotes.RefreshFormattedText();

        foreach (PageViewModel page in Pages)
        {
            page.LanguageChanged();
        }

        Tick();
        Persist();

        // The session carries the locale: signal headlines, preset names and the account
        // label are rendered by the server in the language it was told at handshake. A
        // language change therefore has to re-establish the session, or new alerts would
        // keep arriving in the old language for the rest of the run.
        Reconnect();
    }

    /// <summary>Applies a theme preference to the running application.</summary>
    public void ApplyTheme(ThemePreference preference)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>Selects an instrument and shows it in the monitor's inspector.</summary>
    public void Inspect(InstrumentRow row)
    {
        if (Pages.OfType<MonitorViewModel>().FirstOrDefault() is { } monitor)
        {
            monitor.SelectedRow = row;
        }
    }

    /// <summary>
    /// Applies everything the stores have queued and hands the result to every page.
    /// </summary>
    /// <remarks>
    /// Public because the drain is the shell's contract with the rest of the app, not an
    /// implementation detail of its timer: tests drive it directly rather than depending
    /// on dispatcher timer semantics.
    /// </remarks>
    public void Drain()
    {
        DrainResult result = _session.Drain();

        // Pages update even when hidden: a rail badge that only refreshes on the page it
        // counts would be useless.
        //
        // Each page is isolated, because they share one timer: without this, a throw in
        // one page's update silently stops every page after it in the list, and the
        // symptom shows up somewhere unrelated. Logged once per page so a real fault is
        // loud without flooding at ten times a second.
        foreach (PageViewModel page in Pages)
        {
            try
            {
                page.Drained(result);
            }
            catch (Exception ex)
            {
                if (_pagesThatFaulted.Add(page.Key))
                {
                    _logger.LogError(ex, "Page {Page} failed to apply an update", page.Key);
                }
            }
        }

        if (result.QuoteMessages > 0)
        {
            _drainedMessages += result.QuoteMessages;
        }

        if (result.NewSignals.Count > 0)
        {
            SignalsToday = _session.Signals.CountFrom(DateTimeOffset.Now.Date);
        }
    }

    private int _drainedMessages;
    private DateTimeOffset _lastRateSample = DateTimeOffset.UtcNow;

    private void Tick()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Message rate, sampled once a second from the drain counter rather than measured
        // per message.
        double elapsed = (now - _lastRateSample).TotalSeconds;
        if (elapsed >= 1)
        {
            UpdatesPerSecond = (int)Math.Round(_drainedMessages / elapsed);
            _drainedMessages = 0;
            _lastRateSample = now;
        }

        MarketPhase phase = _session.MarketStatus.Phase("MOEX", now);
        MarketPhase = Humanize.Phase(phase.Kind);
        MarketCountdown = phase switch
        {
            { NextChangeAt: { } next } => string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                phase.NextChangeOpens ? Strings.Shell_Opens : Strings.Shell_Closes,
                Humanize.Countdown(next - now)),
            { Kind: not MarketPhaseKind.Unknown, IsTradingDay: false } => Strings.Shell_ClosedToday,
            _ => string.Empty,
        };

        DateTimeOffset? lastQuote = _session.Quotes.LastQuoteAt;
        DataAge = lastQuote is null
            ? "—"
            : string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Strings.Shell_Seconds,
                (int)(now - lastQuote.Value).TotalSeconds);

        // Only warn while the exchange is genuinely open. A motionless grid on a Sunday
        // is not a fault, and crying wolf about it is how a real warning gets ignored.
        IsStale = phase.IsTrading
            && Status.IsUsable
            && lastQuote is not null
            && now - lastQuote.Value > StaleAfter;

        if (Onboarding is { } onboarding)
        {
            onboarding.Status = Status;
            onboarding.AccountName = AccountName;
        }
    }

    private ClientSession CreateSession()
    {
        var uri = new Uri(_settings.ServerAddress, UriKind.Absolute);

        var settings = new ConnectionSettings
        {
            ServerUri = uri,
            AccessKey = _secrets.Read(SecretKeys.AccessKey) ?? "rm_local_development",
            DeviceId = _settings.DeviceId,
            Locale = string.IsNullOrEmpty(_settings.Locale)
                ? System.Globalization.CultureInfo.CurrentUICulture.Name
                : _settings.Locale,
            TickIntervalMs = _settings.TickIntervalMs,
            WantMonthStats = true,
            WantSparkline = true,

            // Cleartext is only ever allowed against loopback, which is what the fake
            // server binds to. A remote plaintext address is refused outright.
            AllowInsecureHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback,
        };

        ServerAddress = uri.ToString();
        return new ClientSession(settings, _loggerFactory);
    }

    private IDisposable SubscribeStatus(ClientSession session) =>
        session.Connection.Status
            .ObserveOn(UiThreadScheduler.Instance)
            .Subscribe(status =>
            {
                Status = status;
                AccountName = session.Connection.Session?.AccountDisplayName;
                ServerVersion = session.Connection.Session?.ServerVersion;
            });

    private void BuildPages()
    {
        var monitor = new MonitorViewModel(_session, _settings, Persist);
        monitor.LoadViews(_views.Views, _settings.ActiveView);

        Pages.Add(new DashboardViewModel(_session, Navigate, Inspect));
        Pages.Add(monitor);
        Pages.Add(new SignalsViewModel(_session, OpenInstrument));
        Pages.Add(new InstrumentsViewModel(_session));
        Pages.Add(new SettingsViewModel(
            _session, _settings, _secrets, _paths, Persist, ApplyTheme, Reconnect, ApplyLanguage));

        CurrentPage = Pages.FirstOrDefault(p => p.Key == _settings.StartPage) ?? Pages[1];
    }

    private void OpenInstrument(string uid)
    {
        if (_session.Quotes.Lookup(uid) is { } row)
        {
            Inspect(row);
            Navigate("monitor");
        }
    }

    private OnboardingViewModel CreateOnboarding() => new(
        _settings,
        _secrets,
        Persist,
        Reconnect,
        () => _session.Connection.Profiles,
        () => Onboarding = null);

    private async Task RestartSessionAsync()
    {
        _drainTimer.Stop();

        _statusSubscription.Dispose();

        ClientSession old = _session;
        DisposePages();

        await old.DisposeAsync();

        _session = CreateSession();
        _statusSubscription = SubscribeStatus(_session);

        BuildPages();

        _sessionTask = _session.Start();
        _drainTimer.Start();
    }

    private void DisposePages()
    {
        foreach (PageViewModel page in Pages)
        {
            (page as IDisposable)?.Dispose();
        }

        Pages.Clear();
        CurrentPage = null;
    }

    public void Dispose()
    {
        _drainTimer.Stop();
        _clockTimer.Stop();

        _statusSubscription.Dispose();
        DisposePages();

        Persist();
        _settingsFile.Dispose();
        _viewsFile.Dispose();

        _session.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // The supervisor loop exits on the session's cancellation token; observing it here
        // keeps a late failure from surfacing as an unobserved task exception.
        try
        {
            _sessionTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }
}

/// <summary>The saved views that ship with the app.</summary>
internal static class ViewPresets
{
    public static IEnumerable<SavedView> Create() =>
    [
        new() { Name = "All", NameKey = "View_All", IsPreset = true },
        new() { Name = "Watchlist", NameKey = "View_Watchlist", IsPreset = true, WatchlistOnly = true },
        new() { Name = "RUB movers", NameKey = "View_RubMovers", IsPreset = true, Currencies = ["RUB"] },
        new()
        {
            Name = "Volume spikes",
            NameKey = "View_VolumeSpikes",
            IsPreset = true,
            UnusualVolumeOnly = true,
            SortColumn = MonitorColumn.DayVolOfAvg,
        },
    ];
}

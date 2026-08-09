using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcktMon.App.Formatting;
using RcktMon.Client.Core.Market;
using RcktMon.Client.Core.Platform;
using RcktMon.Client.Core.Security;
using RcktMon.Client.Core.Settings;
using RcktMon.Protocol.V1;

namespace RcktMon.App.ViewModels;

/// <summary>
/// Settings, saved as you type.
/// </summary>
/// <remarks>
/// No Save or Apply button anywhere. The pre-rewrite pane had both, plus a settings-changed
/// event that some consumers listened to and some did not, which made "did that take
/// effect?" a genuinely hard question. Here every change is written immediately and
/// acknowledged inline.
/// </remarks>
public sealed partial class SettingsViewModel : PageViewModel, IDisposable
{
    /// <summary>How long the inline "Сохранено" acknowledgement stays up.</summary>
    private static readonly TimeSpan AcknowledgementLifetime = TimeSpan.FromSeconds(2);

    /// <summary>Debounce before asking the server to re-estimate the alert rate.</summary>
    private static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(700);

    private readonly ClientSession _session;
    private readonly ClientSettings _settings;
    private readonly ISecretStore _secrets;
    private readonly IAppPaths _paths;
    private readonly Action _persist;
    private readonly Action<ThemePreference> _applyTheme;
    private readonly Action _reconnect;

    private readonly System.Timers.Timer _previewTimer;

    private DateTimeOffset _savedAt;
    private bool _loading;

    public SettingsViewModel(
        ClientSession session,
        ClientSettings settings,
        ISecretStore secrets,
        IAppPaths paths,
        Action persist,
        Action<ThemePreference> applyTheme,
        Action reconnect)
        : base("settings", "Настройки", "Icon.Settings")
    {
        _session = session;
        _settings = settings;
        _secrets = secrets;
        _paths = paths;
        _persist = persist;
        _applyTheme = applyTheme;
        _reconnect = reconnect;

        _loading = true;
        _serverAddress = settings.ServerAddress;
        _accessKey = secrets.Read(SecretKeys.AccessKey) ?? string.Empty;
        _theme = settings.Theme;
        _compactDensity = settings.CompactDensity;
        _showSparklines = settings.ShowSparklines;
        _colourBlindSafe = settings.ColourBlindSafe;
        _tickIntervalMs = settings.TickIntervalMs;
        _deviceId = settings.DeviceId;
        _loading = false;

        _previewTimer = new System.Timers.Timer(PreviewDebounce.TotalMilliseconds) { AutoReset = false };

        // The timer fires on a thread pool thread and the handler touches bound
        // collections, so it has to hop back before doing anything.
        _previewTimer.Elapsed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RequestPreviewAsync());
    }

    /// <summary>Alert profiles held on the server for this account.</summary>
    public ObservableCollection<AlertProfileViewModel> Profiles { get; } = [];

    /// <summary>Server-curated starting points.</summary>
    public ObservableCollection<AlertProfileViewModel> Presets { get; } = [];

    // ---- connection ----

    [ObservableProperty] private string _serverAddress;

    [ObservableProperty] private string _accessKey;

    [ObservableProperty] private string _deviceId;

    /// <summary>True once the address or key differs from what the running session used.</summary>
    [ObservableProperty] private bool _connectionNeedsRestart;

    // ---- appearance ----

    [ObservableProperty] private ThemePreference _theme;

    [ObservableProperty] private bool _compactDensity;

    [ObservableProperty] private bool _showSparklines;

    [ObservableProperty] private bool _colourBlindSafe;

    [ObservableProperty] private int _tickIntervalMs;

    // ---- profiles ----

    [ObservableProperty] private AlertProfileViewModel? _selectedProfile;

    [ObservableProperty] private bool _isLoadingProfiles;

    [ObservableProperty] private string? _profileError;

    [ObservableProperty] private int _estimatedSignalsPerDay;

    [ObservableProperty] private string _previewDetail = string.Empty;

    [ObservableProperty] private bool _isPreviewing;

    public ObservableCollection<string> PreviewSamples { get; } = [];

    // ---- acknowledgement ----

    [ObservableProperty] private bool _showSaved;

    /// <summary>Where diagnostics and logs are written, shown so the folder can be found.</summary>
    public string LogDirectory => _paths.LogDirectory;

    public string ConfigDirectory => _paths.ConfigDirectory;

    /// <summary>Which backend is holding the access key, stated plainly.</summary>
    public string SecretBackend => _secrets switch
    {
        KeychainSecretStore => "Связка ключей macOS",
        DpapiSecretStore => "DPAPI (текущий пользователь Windows)",
        _ => "Зашифрованный файл (AES-GCM) в каталоге настроек",
    };

    public override void Activated()
    {
        if (Profiles.Count == 0)
        {
            _ = LoadProfilesAsync();
        }
    }

    public override void Drained(in DrainResult result)
    {
        if (ShowSaved && DateTimeOffset.UtcNow - _savedAt > AcknowledgementLifetime)
        {
            ShowSaved = false;
        }
    }

    // ---- persistence ----

    partial void OnServerAddressChanged(string value)
    {
        _settings.ServerAddress = value.Trim();
        ConnectionNeedsRestart = true;
        Save();
    }

    partial void OnAccessKeyChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        string trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            _secrets.Delete(SecretKeys.AccessKey);
        }
        else
        {
            _secrets.Write(SecretKeys.AccessKey, trimmed);
        }

        ConnectionNeedsRestart = true;
        Acknowledge();
    }

    partial void OnThemeChanged(ThemePreference value)
    {
        _settings.Theme = value;
        _applyTheme(value);
        Save();
    }

    partial void OnCompactDensityChanged(bool value)
    {
        _settings.CompactDensity = value;
        Save();
    }

    partial void OnShowSparklinesChanged(bool value)
    {
        _settings.ShowSparklines = value;
        Save();
    }

    partial void OnColourBlindSafeChanged(bool value)
    {
        _settings.ColourBlindSafe = value;

        // Swaps the direction hues for blue/orange across both themes. Applied live
        // rather than on restart, because the only way to judge a palette is to see it.
        RcktMonApp.SetColourBlindPalette(value);
        Save();
    }

    partial void OnTickIntervalMsChanged(int value)
    {
        _settings.TickIntervalMs = Math.Clamp(value, 250, 5000);
        ConnectionNeedsRestart = true;
        Save();
    }

    // ---- profiles ----

    private async Task LoadProfilesAsync()
    {
        if (_session.Connection.Profiles is not { } client)
        {
            ProfileError = "Нет соединения с сервером — профили недоступны.";
            return;
        }

        IsLoadingProfiles = true;
        ProfileError = null;

        try
        {
            ListProfilesResponse response = await client.ListAsync(CancellationToken.None);

            Profiles.Clear();
            foreach (AlertProfile profile in response.Profiles)
            {
                Profiles.Add(new AlertProfileViewModel(profile, OnProfileEdited));
            }

            Presets.Clear();
            foreach (AlertProfile preset in response.Presets)
            {
                Presets.Add(new AlertProfileViewModel(preset, static () => { }));
            }

            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == response.ActiveProfileId)
                ?? Profiles.FirstOrDefault();

            await RequestPreviewAsync();
        }
        catch (Grpc.Core.RpcException ex)
        {
            ProfileError = $"Не удалось загрузить профили: {ex.Status.Detail}";
        }
        finally
        {
            IsLoadingProfiles = false;
        }
    }

    /// <summary>Queues a save and a fresh estimate whenever a threshold changes.</summary>
    private void OnProfileEdited()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async Task RequestPreviewAsync()
    {
        if (SelectedProfile is not { } profile || _session.Connection.Profiles is not { } client)
        {
            return;
        }

        IsPreviewing = true;

        try
        {
            AlertProfile wire = profile.ToProto();

            // Save and estimate in one pass: the estimate is only meaningful for rules
            // the server has actually accepted.
            if (!profile.IsPreset)
            {
                await client.UpsertAsync(wire, CancellationToken.None);
            }

            PreviewProfileResponse preview = await client.PreviewAsync(wire, lookbackDays: 1, CancellationToken.None);

            EstimatedSignalsPerDay = preview.EstimatedSignalsPerDay;

            PreviewSamples.Clear();
            foreach (string sample in preview.SampleHeadlines)
            {
                PreviewSamples.Add(sample);
            }

            PreviewDetail = string.Join(
                " · ",
                preview.ByKind
                    .Where(static b => b.Count > 0)
                    .Select(b => $"{Humanize.SignalKindName(b.Kind)}: {b.Count}"));

            Acknowledge();
        }
        catch (Grpc.Core.RpcException ex)
        {
            ProfileError = $"Сервер отклонил профиль: {ex.Status.Detail}";
        }
        finally
        {
            IsPreviewing = false;
        }
    }

    partial void OnSelectedProfileChanged(AlertProfileViewModel? value) => _ = RequestPreviewAsync();

    /// <summary>Copies a preset into a new editable profile.</summary>
    [RelayCommand]
    private async Task UsePresetAsync(AlertProfileViewModel? preset)
    {
        if (preset is null || _session.Connection.Profiles is not { } client)
        {
            return;
        }

        AlertProfile wire = preset.ToProto();
        wire.Id = string.Empty;
        wire.PresetKey = string.Empty;
        wire.Name = preset.Name;

        try
        {
            AlertProfile created = await client.UpsertAsync(wire, CancellationToken.None);
            var vm = new AlertProfileViewModel(created, OnProfileEdited);

            Profiles.Add(vm);
            SelectedProfile = vm;

            await client.SetActiveAsync(created.Id, CancellationToken.None);
            Acknowledge();
        }
        catch (Grpc.Core.RpcException ex)
        {
            ProfileError = $"Не удалось создать профиль: {ex.Status.Detail}";
        }
    }

    [RelayCommand]
    private async Task ActivateProfileAsync()
    {
        if (SelectedProfile is not { } profile || _session.Connection.Profiles is not { } client)
        {
            return;
        }

        try
        {
            await client.SetActiveAsync(profile.Id, CancellationToken.None);

            foreach (AlertProfileViewModel other in Profiles)
            {
                other.IsActive = ReferenceEquals(other, profile);
            }

            Acknowledge();
        }
        catch (Grpc.Core.RpcException ex)
        {
            ProfileError = $"Не удалось активировать профиль: {ex.Status.Detail}";
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile || _session.Connection.Profiles is not { } client)
        {
            return;
        }

        try
        {
            await client.DeleteAsync(profile.Id, CancellationToken.None);
            Profiles.Remove(profile);
            SelectedProfile = Profiles.FirstOrDefault();
            Acknowledge();
        }
        catch (Grpc.Core.RpcException ex)
        {
            ProfileError = ex.Status.Detail;
        }
    }

    [RelayCommand]
    private void ReloadProfiles() => _ = LoadProfilesAsync();

    /// <summary>Rebuilds the session against the current address and key.</summary>
    [RelayCommand]
    private void Reconnect()
    {
        ConnectionNeedsRestart = false;
        _reconnect();
    }

    private void Save()
    {
        if (_loading)
        {
            return;
        }

        _persist();
        Acknowledge();
    }

    private void Acknowledge()
    {
        _savedAt = DateTimeOffset.UtcNow;
        ShowSaved = true;
    }

    public void Dispose() => _previewTimer.Dispose();
}

using System.Reactive.Linq;
using RcktMon.App.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RcktMon.Client.Core.Connection;
using RcktMon.Client.Core.Market;

namespace RcktMon.App.ViewModels;

/// <summary>Root view model: owns the connection and the screens hanging off it.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly QuoteStore _store;
    private readonly RcktMonConnection _connection;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IDisposable _statusSubscription;

    public MainWindowViewModel(ConnectionSettings settings, ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;

        _store = new QuoteStore();
        Monitor = new MonitorViewModel(_store);

        _connection = new RcktMonConnection(
            settings,
            _store,
            loggerFactory.CreateLogger<RcktMonConnection>());

        ServerAddress = settings.ServerUri.ToString();

        _statusSubscription = _connection.Status
            .ObserveOn(UiThreadScheduler.Instance)
            .Subscribe(status =>
            {
                Status = status;
                AccountName = _connection.Session?.AccountDisplayName;
                ServerVersion = _connection.Session?.ServerVersion;
            });
    }

    public MonitorViewModel Monitor { get; }

    [ObservableProperty] private ConnectionStatus _status = new(ConnectionState.Disconnected);

    [ObservableProperty] private string? _accountName;

    [ObservableProperty] private string? _serverVersion;

    [ObservableProperty] private string _serverAddress;

    /// <summary>Starts the connection supervisor. Runs until the window closes.</summary>
    public void Start() => _ = _connection.RunAsync(_lifetime.Token);

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Avalonia.Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _statusSubscription.Dispose();
        Monitor.Dispose();
        _store.Dispose();
        _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _lifetime.Dispose();
    }
}

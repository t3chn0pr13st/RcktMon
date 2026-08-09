using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using RcktMon.App.ViewModels;
using RcktMon.App.Views;
using RcktMon.Client.Core.Connection;

namespace RcktMon.App;

/// <summary>The Avalonia application. Composition happens in the desktop head.</summary>
public sealed partial class RcktMonApp : Avalonia.Application
{
    /// <summary>
    /// Supplied by the executable before <c>Run</c>. Kept as a property rather than a
    /// constructor parameter because Avalonia instantiates the Application itself.
    /// </summary>
    public static ConnectionSettings? Settings { get; set; }

    /// <summary>Logger factory supplied by the host, if any.</summary>
    public static ILoggerFactory? LoggerFactory { get; set; }

    private MainWindowViewModel? _mainViewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ConnectionSettings settings = Settings
                ?? throw new InvalidOperationException(
                    $"{nameof(RcktMonApp)}.{nameof(Settings)} must be set before the app runs.");

            _mainViewModel = new MainWindowViewModel(settings, LoggerFactory);

            desktop.MainWindow = new MainWindow { DataContext = _mainViewModel };

            // Cancel the connection supervisor and stop the drain timer on exit. The
            // pre-rewrite app called Process.Kill() here because its background loops had
            // no cancellation; every loop in this client takes a token.
            desktop.ShutdownRequested += (_, _) => _mainViewModel?.Dispose();

            _mainViewModel.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

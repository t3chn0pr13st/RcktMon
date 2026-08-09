using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Microsoft.Extensions.Logging;
using RcktMon.App.ViewModels;
using RcktMon.App.Views;
using RcktMon.Client.Core.Platform;

namespace RcktMon.App;

/// <summary>The Avalonia application. Composition happens in the desktop head.</summary>
public sealed partial class RcktMonApp : Avalonia.Application
{
    private const string ColourBlindPalette = "avares://RcktMon.App/Themes/ColourBlind.axaml";

    private static ResourceInclude? _colourBlindDictionary;

    /// <summary>
    /// Supplied by the executable before <c>Run</c>. Kept as a property rather than a
    /// constructor parameter because Avalonia instantiates the Application itself.
    /// </summary>
    public static IAppPaths? Paths { get; set; }

    /// <summary>Logger factory supplied by the host, if any.</summary>
    public static ILoggerFactory? LoggerFactory { get; set; }

    private MainWindowViewModel? _mainViewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IAppPaths paths = Paths
                ?? throw new InvalidOperationException(
                    $"{nameof(RcktMonApp)}.{nameof(Paths)} must be set before the app runs.");

            _mainViewModel = new MainWindowViewModel(paths, LoggerFactory);

            SetColourBlindPalette(_mainViewModel.Settings.ColourBlindSafe);

            desktop.MainWindow = new MainWindow { DataContext = _mainViewModel };

            // Cancel the connection supervisor and stop the timers on exit. The
            // pre-rewrite app called Process.Kill() here because its background loops had
            // no cancellation; every loop in this client takes a token.
            desktop.ShutdownRequested += (_, _) => _mainViewModel?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Merges or removes the colour-blind-safe palette over the base tokens.
    /// </summary>
    /// <remarks>
    /// Only the hues that carry direction are overridden, so surfaces, borders and text
    /// keep their contrast-checked values in both themes.
    /// </remarks>
    public static void SetColourBlindPalette(bool enabled)
    {
        if (Current is not { } app)
        {
            return;
        }

        if (enabled)
        {
            _colourBlindDictionary ??= new ResourceInclude((Uri?)null) { Source = new Uri(ColourBlindPalette) };

            if (!app.Resources.MergedDictionaries.Contains(_colourBlindDictionary))
            {
                app.Resources.MergedDictionaries.Add(_colourBlindDictionary);
            }
        }
        else if (_colourBlindDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_colourBlindDictionary);
        }
    }
}

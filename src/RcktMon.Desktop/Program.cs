using Avalonia;
using RcktMon.App;
using RcktMon.Client.Core.Platform;
using RcktMon.Client.Core.Security;
using RcktMon.Client.Core.Settings;
using RcktMon.Client.Core.Studio;

namespace RcktMon.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        // Both deep links use the same shell. A Studio launch must not load broker or server
        // credentials from the environment, or write a legacy monitor configuration.
        RcktMonApp.StartInStudio = args.Contains("--studio", StringComparer.Ordinal);
        RcktMonApp.StartInMonitor = !RcktMonApp.StartInStudio && args.Contains("--monitor", StringComparer.Ordinal);
        RcktMonApp.ConnectConfiguredServer = !RcktMonApp.StartInStudio &&
            !string.IsNullOrWhiteSpace(Argument(args, "--server")) && !string.IsNullOrWhiteSpace(Argument(args, "--key"));
        if (!RcktMonApp.StartInStudio) ApplyCommandLineOverrides(args, paths);

        RcktMonApp.Paths = paths;

        using var shutdown = new CancellationTokenSource();
        Task companion = StartBundledFeedAsync(shutdown.Token);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            shutdown.Cancel();
            companion.GetAwaiter().GetResult();
        }
    }

    // Optional private market-data helper installed inside this .app only. Never discover,
    // build or execute scripts from the user's PATH, working directory or settings.
    private static async Task StartBundledFeedAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var macos = new DirectoryInfo(AppContext.BaseDirectory);
            if (macos.Name != "MacOS" || macos.Parent is not { Name: "Contents" } contents ||
                !File.Exists(Path.Combine(macos.FullName, "rocketmon-desktop.bundle"))) return;
            string helper = Path.Combine(contents.FullName, "Helpers", "SignalStudio", "RcktMon.Server.SignalStudio");
            if (!File.Exists(helper)) return;
            string library = Path.Combine(contents.FullName, "Resources", "studio-recordings.path");
            string? recordings = File.Exists(library) ? (await File.ReadAllTextAsync(library, ct).ConfigureAwait(false)).TrimEnd('\r', '\n') : null;
            if (recordings != null && !Path.IsPathFullyQualified(recordings)) return;
            await StudioCompanion.RunAsync(helper, recordings, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            // Do not prevent the shell/trading/settings from opening if local data is unavailable.
            // StudioView owns its offline state. Never fall back to samples or a remote executable.
        }
    }

    /// <summary>Used by the Avalonia XAML previewer as well as by <see cref="Main"/>.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RcktMonApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Lets <c>--server</c> and <c>--key</c> point a dev build at the fake server.
    /// </summary>
    /// <remarks>
    /// These write through to the same places the settings pane writes to — the settings
    /// file and the OS keychain — rather than living in a parallel configuration path, so
    /// there is exactly one answer to "where is the client getting its address from".
    /// Manual trading credentials are never accepted via arguments or these settings.
    /// </remarks>
    private static void ApplyCommandLineOverrides(string[] args, IAppPaths paths)
    {
        // Never combine a newly typed destination with an unrelated key inherited from a
        // shell environment. Either the operator supplied CLI overrides or this is env-only.
        bool fromCli = args.Contains("--server", StringComparer.Ordinal) || args.Contains("--key", StringComparer.Ordinal);
        string? server = fromCli ? Argument(args, "--server") : Environment.GetEnvironmentVariable("RCKTMON_SERVER");
        string? key = fromCli ? Argument(args, "--key") : Environment.GetEnvironmentVariable("RCKTMON_KEY");

        if (server is null && key is null)
        {
            return;
        }

        if (key is not null)
        {
            SecretStoreFactory.Create(paths.ConfigDirectory).Write(SecretKeys.AccessKey, key);
        }

        if (server is null)
        {
            return;
        }

        using var store = new JsonFileStore<ClientSettings>(
            Path.Combine(paths.ConfigDirectory, "settings.json"),
            TimeSpan.Zero);

        ClientSettings settings = store.Load();
        bool explicitPair = fromCli && !string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(key);
        if (explicitPair) settings.MonitorConnectionPending = false;
        else if (!string.Equals(settings.ServerAddress, server, StringComparison.Ordinal))
            settings.MonitorConnectionPending = true;
        settings.ServerAddress = server;

        if (string.IsNullOrEmpty(settings.DeviceId))
        {
            settings.DeviceId = Guid.NewGuid().ToString("D");
        }

        store.Save(settings);
        store.Flush();
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

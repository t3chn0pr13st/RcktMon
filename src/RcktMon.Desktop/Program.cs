using Avalonia;
using RcktMon.App;
using RcktMon.Client.Core.Platform;
using RcktMon.Client.Core.Security;
using RcktMon.Client.Core.Settings;

namespace RcktMon.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        ApplyCommandLineOverrides(args, paths);

        RcktMonApp.Paths = paths;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
    /// There is deliberately no broker token here or anywhere else in the client.
    /// </remarks>
    private static void ApplyCommandLineOverrides(string[] args, IAppPaths paths)
    {
        string? server = Argument(args, "--server") ?? Environment.GetEnvironmentVariable("RCKTMON_SERVER");
        string? key = Argument(args, "--key") ?? Environment.GetEnvironmentVariable("RCKTMON_KEY");

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

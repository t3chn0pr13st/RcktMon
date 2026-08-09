using Avalonia;
using RcktMon.App;
using RcktMon.Client.Core.Connection;
using RcktMon.Client.Core.Platform;

namespace RcktMon.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        RcktMonApp.Settings = BuildSettings(args, paths);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Used by the Avalonia XAML previewer as well as by <see cref="Main"/>.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RcktMonApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static ConnectionSettings BuildSettings(string[] args, IAppPaths paths)
    {
        string server = Argument(args, "--server")
            ?? Environment.GetEnvironmentVariable("RCKTMON_SERVER")
            ?? "http://localhost:5299";

        // The access key is normally read from the OS keychain. Until onboarding exists,
        // accept it from the command line or the environment so the client can be pointed
        // at the fake server. There is deliberately no broker token anywhere here.
        string key = Argument(args, "--key")
            ?? Environment.GetEnvironmentVariable("RCKTMON_KEY")
            ?? "rm_local_development";

        var uri = new Uri(server, UriKind.Absolute);

        return new ConnectionSettings
        {
            ServerUri = uri,
            AccessKey = key,
            DeviceId = ResolveDeviceId(paths),
            Locale = System.Globalization.CultureInfo.CurrentUICulture.Name,
            AllowInsecureHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback,
            WantMonthStats = true,
        };
    }

    /// <summary>
    /// A stable per-installation id, so the account owner can see and revoke one machine.
    /// </summary>
    /// <remarks>
    /// Not a secret and not derived from hardware. The pre-rewrite build keyed its
    /// settings encryption off the Windows registry MachineGuid, which had no
    /// cross-platform equivalent and silently wiped every stored credential elsewhere.
    /// </remarks>
    private static string ResolveDeviceId(IAppPaths paths)
    {
        string path = Path.Combine(paths.ConfigDirectory, "device-id");

        try
        {
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path).Trim();
                if (Guid.TryParse(existing, out _))
                {
                    return existing;
                }
            }

            string created = Guid.NewGuid().ToString("D");
            File.WriteAllText(path, created);
            return created;
        }
        catch (IOException)
        {
            // A read-only or unavailable config directory should not stop the app from
            // starting; the server treats an unknown device id as a new install.
            return Guid.NewGuid().ToString("D");
        }
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

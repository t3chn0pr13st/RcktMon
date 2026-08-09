namespace RcktMon.Client.Core.Platform;

/// <summary>
/// Where the client is allowed to write on each platform.
/// </summary>
public interface IAppPaths
{
    /// <summary>User settings and saved views. Small, backed up, survives reinstall.</summary>
    string ConfigDirectory { get; }

    /// <summary>Regenerable data such as the instrument catalog snapshot.</summary>
    string CacheDirectory { get; }

    /// <summary>Log files and diagnostics.</summary>
    string LogDirectory { get; }

    /// <summary>Creates every directory if it does not already exist.</summary>
    void EnsureCreated();
}

/// <summary>
/// Per-OS application directories following each platform's own convention.
/// </summary>
/// <remarks>
/// The pre-rewrite app wrote <c>settings.json</c>, <c>columns.json</c> and
/// <c>WindowPos.json</c> as bare relative filenames, so they resolved against the process
/// working directory. That happens to work for a Windows app launched from its install
/// folder and fails immediately elsewhere: a macOS <c>.app</c> bundle launches with a
/// working directory of <c>/</c>, and the bundle itself is read-only and code-signed.
/// </remarks>
public sealed class AppPaths : IAppPaths
{
    private const string AppName = "RcktMon";
    private const string BundleId = "ru.technopriest.rcktmon";

    public AppPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            ConfigDirectory = Path.Combine(roaming, AppName);
            CacheDirectory = Path.Combine(local, AppName, "Cache");
            LogDirectory = Path.Combine(local, AppName, "Logs");
        }
        else if (OperatingSystem.IsMacOS())
        {
            string home = HomeDirectory();

            ConfigDirectory = Path.Combine(home, "Library", "Application Support", AppName);
            CacheDirectory = Path.Combine(home, "Library", "Caches", BundleId);
            LogDirectory = Path.Combine(home, "Library", "Logs", AppName);
        }
        else
        {
            // XDG Base Directory Specification, with the spec's own defaults.
            string home = HomeDirectory();

            ConfigDirectory = Path.Combine(
                Xdg("XDG_CONFIG_HOME", Path.Combine(home, ".config")), "rcktmon");
            CacheDirectory = Path.Combine(
                Xdg("XDG_CACHE_HOME", Path.Combine(home, ".cache")), "rcktmon");
            LogDirectory = Path.Combine(
                Xdg("XDG_STATE_HOME", Path.Combine(home, ".local", "state")), "rcktmon");
        }
    }

    public string ConfigDirectory { get; }

    public string CacheDirectory { get; }

    public string LogDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogDirectory);

        if (!OperatingSystem.IsWindows())
        {
            // The config directory holds the device id and connection settings. On a
            // shared machine there is no reason for anyone else to read them.
            TrySetOwnerOnlyPermissions(ConfigDirectory);
        }
    }

    private static string HomeDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // GetFolderPath can return empty in sandboxed or service contexts; fall back to
        // the environment before giving up, so we never build paths off "".
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        }

        if (string.IsNullOrEmpty(home))
        {
            throw new InvalidOperationException(
                "Cannot determine the user's home directory; RcktMon has nowhere to store settings.");
        }

        return home;
    }

    private static string Xdg(string variable, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(variable);

        // The spec says a relative value must be ignored, not resolved.
        return !string.IsNullOrEmpty(value) && Path.IsPathRooted(value) ? value : fallback;
    }

    private static void TrySetOwnerOnlyPermissions(string directory)
    {
        // Repeated here rather than relying on the caller's guard: the platform analyzer
        // reasons per method, and this is cheap.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort. A filesystem that cannot express Unix modes (an SMB share, a
            // container bind mount) is not a reason to refuse to start.
        }
    }
}

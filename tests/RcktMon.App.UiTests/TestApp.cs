using Avalonia;
using Avalonia.Headless;
using RcktMon.App;
using RcktMon.Client.Core.Platform;
using Xunit;

// One Avalonia application per process, so the tests that drive it cannot overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace RcktMon.App.UiTests;

/// <summary>
/// The application under test, running on Avalonia's headless platform.
/// </summary>
/// <remarks>
/// Skia is deliberately left on — <c>UseHeadlessDrawing = false</c> — so a captured frame
/// contains the pixels the app would really show. With the null drawing backend the visual
/// tree can be inspected but nothing is rasterised, which misses exactly the class of bug
/// a UI smoke test is for: a control that lays out fine and draws nothing.
/// </remarks>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RcktMonApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

/// <summary>
/// One headless Avalonia session, shared by every test in the class.
/// </summary>
/// <remarks>
/// <c>Application.Current</c> is a process-wide singleton, so a session per test would
/// have the second one setting up over the first. Sharing it also keeps the suite fast:
/// the Skia and font initialisation happens once.
/// </remarks>
public sealed class HeadlessSessionFixture : IDisposable
{
    public HeadlessSessionFixture() => Session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    public HeadlessUnitTestSession Session { get; }

    /// <summary>Runs a test body on the session's UI thread and rethrows any failure.</summary>
    public void Run(Action body) => Session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();

    public void Dispose() => Session.Dispose();
}

/// <summary>Throwaway application directories, one per test.</summary>
public sealed class TempPaths : IAppPaths, IDisposable
{
    public TempPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "rcktmon-uitests", Guid.NewGuid().ToString("N"));

        ConfigDirectory = Path.Combine(root, "config");
        CacheDirectory = Path.Combine(root, "cache");
        LogDirectory = Path.Combine(root, "logs");

        Root = root;
        EnsureCreated();
    }

    public string Root { get; }

    public string ConfigDirectory { get; }

    public string CacheDirectory { get; }

    public string LogDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}

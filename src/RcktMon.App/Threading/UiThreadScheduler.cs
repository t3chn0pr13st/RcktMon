using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using Avalonia.Threading;

namespace RcktMon.App.Threading;

/// <summary>
/// An Rx <see cref="IScheduler"/> that runs work on the Avalonia UI thread.
/// </summary>
/// <remarks>
/// Avalonia 11 shipped <c>Avalonia.Threading.AvaloniaScheduler</c>; Avalonia 12 does not,
/// and the only alternative in the box is capturing a <see cref="SynchronizationContext"/>
/// at construction time — which silently binds to the wrong thread if the owner happens to
/// be built off the UI thread. Going through <see cref="Dispatcher.UIThread"/> explicitly
/// removes that failure mode.
/// </remarks>
public sealed class UiThreadScheduler : IScheduler
{
    public static UiThreadScheduler Instance { get; } = new();

    private UiThreadScheduler()
    {
    }

    public DateTimeOffset Now => DateTimeOffset.Now;

    public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
    {
        var subscription = new SingleAssignmentDisposable();

        if (Dispatcher.UIThread.CheckAccess())
        {
            // Already on the UI thread: run inline so a synchronous pipeline stage does
            // not get pushed to the next dispatcher frame for no reason.
            subscription.Disposable = action(this, state);
            return subscription;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!subscription.IsDisposed)
                {
                    subscription.Disposable = action(this, state);
                }
            },
            DispatcherPriority.Background);

        return subscription;
    }

    public IDisposable Schedule<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
    {
        TimeSpan delay = dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime;

        if (delay == TimeSpan.Zero)
        {
            return Schedule(state, action);
        }

        var subscription = new SingleAssignmentDisposable();

        // Declared before the handler so the local function can capture it; assigning it
        // in the same statement that references it is a definite-assignment error.
        DispatcherTimer? timer = null;

        void OnTick(object? sender, EventArgs e)
        {
            timer!.Stop();
            timer.Tick -= OnTick;

            if (!subscription.IsDisposed)
            {
                subscription.Disposable = action(this, state);
            }
        }

        timer = new DispatcherTimer(delay, DispatcherPriority.Background, OnTick);
        timer.Start();

        return new CompositeDisposable(
            subscription,
            Disposable.Create(() =>
            {
                timer.Stop();
                timer.Tick -= OnTick;
            }));
    }

    public IDisposable Schedule<TState>(TState state, DateTimeOffset dueTime, Func<IScheduler, TState, IDisposable> action)
        => Schedule(state, dueTime - Now, action);
}

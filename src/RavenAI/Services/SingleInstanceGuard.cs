using System.Threading;

namespace RavenAI.Services;

/// <summary>
/// Enforces a single running instance of the app for the current user session.
///
/// The first instance owns a named <see cref="Mutex"/> and listens on a named, auto-reset
/// <see cref="EventWaitHandle"/>. A second launch sees the mutex already exists, sets the
/// event (asking the running instance to surface its window), and then exits. This needs no
/// pipes or sockets — the two named kernel objects are enough to coordinate.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    // Stable, app-unique names. The "Local\" prefix scopes them to the current login session
    // (one instance per desktop user), and the embedded GUID guarantees no collision with any
    // other application's kernel objects.
    private const string MutexName = @"Local\RavenAI.SingleInstance.b6f2c1a4-8e3d-4a17-9c5e-2f0a7d914e6b";
    private const string EventName = @"Local\RavenAI.Activate.b6f2c1a4-8e3d-4a17-9c5e-2f0a7d914e6b";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateSignal;
    private RegisteredWaitHandle? _registration;

    /// <summary>True when this process is the first (and only) instance.</summary>
    public bool IsFirstInstance { get; }

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsFirstInstance = createdNew;
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
    }

    /// <summary>
    /// Called by the first instance. Runs <paramref name="onActivate"/> whenever a later
    /// launch signals it wants the running window surfaced. The callback fires on a thread-pool
    /// thread, so the handler must marshal any UI work onto the dispatcher itself.
    /// </summary>
    public void ListenForActivation(Action onActivate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activateSignal,
            (_, _) => onActivate(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Called by a later instance: ask the already-running instance to surface itself.</summary>
    public void SignalExistingInstance() => _activateSignal.Set();

    public void Dispose()
    {
        _registration?.Unregister(waitObject: null);
        _activateSignal.Dispose();
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* not the owning thread — nothing to release */ }
        }
        _mutex.Dispose();
    }
}

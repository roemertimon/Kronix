using System;
using System.Threading;

namespace Kronix.Services;

/// <summary>
/// Ensures only one Kronix process runs per Windows session. A second launch signals the first
/// instance (so it can bring its window to the front) and then exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly string _mutexName;
    private readonly string _eventName;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _registration;
    private bool _ownsMutex;

    public SingleInstanceGuard(string name = "Kronix.SingleInstance")
    {
        _mutexName = $@"Local\{name}.Mutex";
        _eventName = $@"Local\{name}.Activate";
    }

    /// <summary>Returns true if this process is the first (primary) instance.</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out bool createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }

        return createdNew;
    }

    /// <summary>Starts listening for activation requests from later launches (primary instance only).</summary>
    public void StartListening(Action onActivationRequested)
    {
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => onActivationRequested(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Asks the already running instance to show itself. Returns false if none is listening.</summary>
    public bool SignalExistingInstance()
    {
        if (!EventWaitHandle.TryOpenExisting(_eventName, out EventWaitHandle? handle))
        {
            return false;
        }

        using (handle)
        {
            return handle.Set();
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;
        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_mutex is not null)
        {
            if (_ownsMutex)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Released from a different thread; the OS frees it on process exit anyway.
                }
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}

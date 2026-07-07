using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;

namespace AndroidTreeView.Adb.Services;

/// <summary>
/// Singleton holder for the currently resolved adb location. The locator writes to it; command
/// executors read from it. Access is guarded so reads and writes are consistent across threads.
/// </summary>
public sealed class AdbEnvironment : IAdbEnvironment
{
    private readonly object _gate = new();
    private AdbLocation? _location;

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _location is not null;
            }
        }
    }

    public AdbLocation? Location
    {
        get
        {
            lock (_gate)
            {
                return _location;
            }
        }
    }

    public string ExecutablePath
    {
        get
        {
            lock (_gate)
            {
                return _location?.ExecutablePath ?? throw new AdbNotFoundException();
            }
        }
    }

    public event EventHandler? Changed;

    public void Set(AdbLocation? location)
    {
        lock (_gate)
        {
            _location = location;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

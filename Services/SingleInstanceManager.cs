using System;
using System.Threading;
using System.Threading.Tasks;

namespace Speakly.Services
{
    public sealed class SingleInstanceManager : IDisposable
    {
        private const string MutexName = "Local\\Speakly.SingleInstance";
        private const string ActivateEventName = "Local\\Speakly.Activate";

        private Mutex? _mutex;
        private EventWaitHandle? _activateEvent;
        private CancellationTokenSource? _listenerCts;
        private Task? _listenerTask;

        public bool TryAcquirePrimaryInstance()
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isPrimary);
            if (!isPrimary)
            {
                _mutex.Dispose();
                _mutex = null;
                return false;
            }

            _activateEvent = new EventWaitHandle(
                initialState: false,
                mode: EventResetMode.AutoReset,
                name: ActivateEventName);

            return true;
        }

        public static void SignalPrimaryInstance()
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivateEventName);
                activationEvent.Set();
            }
            catch
            {
                // Primary instance is not ready yet or not running.
            }
        }

        public void StartActivationListener(Action onActivate)
        {
            if (_activateEvent == null || _listenerCts != null)
            {
                return;
            }

            _listenerCts = new CancellationTokenSource();
            var token = _listenerCts.Token;

            _listenerTask = Task.Run(() =>
            {
                var waits = new WaitHandle[] { _activateEvent, token.WaitHandle };
                while (!token.IsCancellationRequested)
                {
                    var signaled = WaitHandle.WaitAny(waits);
                    if (signaled == 1)
                    {
                        break;
                    }

                    try
                    {
                        onActivate();
                    }
                    catch
                    {
                        // Keep listener alive; activation should not crash the app.
                    }
                }
            }, token);
        }

        public void Dispose()
        {
            try
            {
                _listenerCts?.Cancel();
            }
            catch
            {
                // Ignore listener cancellation failures.
            }

            try
            {
                _listenerTask?.Wait(100);
            }
            catch
            {
                // Ignore shutdown timing issues.
            }

            _listenerTask = null;
            _listenerCts?.Dispose();
            _listenerCts = null;

            _activateEvent?.Dispose();
            _activateEvent = null;

            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
                // Mutex may already be released or never acquired.
            }

            _mutex?.Dispose();
            _mutex = null;
        }
    }
}

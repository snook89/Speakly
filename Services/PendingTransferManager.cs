using System;

namespace Speakly.Services
{
    public sealed class PendingTransfer
    {
        public PendingTransfer(
            string text,
            TargetWindowContext targetContext,
            DateTime createdAtUtc,
            DateTime expiresAtUtc,
            string failureCode,
            string operationId)
        {
            Id = Guid.NewGuid();
            Text = text ?? string.Empty;
            TargetContext = targetContext;
            CreatedAtUtc = createdAtUtc;
            ExpiresAtUtc = expiresAtUtc;
            FailureCode = failureCode ?? string.Empty;
            OperationId = operationId ?? string.Empty;
        }

        public Guid Id { get; }
        public string Text { get; }
        public TargetWindowContext TargetContext { get; }
        public DateTime CreatedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public string FailureCode { get; }
        public string OperationId { get; }

        public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAtUtc;

        public string TargetDisplayName =>
            string.IsNullOrWhiteSpace(TargetContext.ProcessName)
                ? "target app"
                : TargetContext.ProcessName;
    }

    public sealed class PendingTransferManager
    {
        private readonly object _sync = new();
        private PendingTransfer? _pending;

        public PendingTransfer? Replace(PendingTransfer transfer)
        {
            lock (_sync)
            {
                var replaced = _pending;
                _pending = transfer;
                return replaced;
            }
        }

        public PendingTransfer? GetActiveOrExpire(DateTime utcNow, out PendingTransfer? expired)
        {
            lock (_sync)
            {
                expired = null;

                if (_pending == null)
                {
                    return null;
                }

                if (_pending.IsExpired(utcNow))
                {
                    expired = _pending;
                    _pending = null;
                    return null;
                }

                return _pending;
            }
        }

        public PendingTransfer? Clear()
        {
            lock (_sync)
            {
                var previous = _pending;
                _pending = null;
                return previous;
            }
        }

        public bool TryConsume(Guid id, out PendingTransfer? consumed)
        {
            lock (_sync)
            {
                if (_pending == null || _pending.Id != id)
                {
                    consumed = null;
                    return false;
                }

                consumed = _pending;
                _pending = null;
                return true;
            }
        }

        public static bool IsForegroundMatch(TargetWindowContext foreground, TargetWindowContext target)
        {
            if (!foreground.IsValid || !target.IsValid)
            {
                return false;
            }

            if (foreground.Handle == target.Handle)
            {
                return true;
            }

            if (foreground.ProcessId != 0 && target.ProcessId != 0 && foreground.ProcessId == target.ProcessId)
            {
                return true;
            }

            return false;
        }
    }
}

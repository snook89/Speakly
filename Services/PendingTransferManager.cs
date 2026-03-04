using System;
using System.IO;
using System.Text.Json;

namespace Speakly.Services
{
    public sealed class PendingTransfer
    {
        public PendingTransfer(
            string text,
            TargetWindowContext targetContext,
            DateTime createdAtUtc,
            DateTime? expiresAtUtc,
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
        public DateTime? ExpiresAtUtc { get; }
        public string FailureCode { get; }
        public string OperationId { get; }

        public bool IsExpired(DateTime utcNow) =>
            ExpiresAtUtc.HasValue && utcNow >= ExpiresAtUtc.Value;

        public string TargetDisplayName =>
            string.IsNullOrWhiteSpace(TargetContext.ProcessName)
                ? "target app"
                : TargetContext.ProcessName;
    }

    public sealed class PendingTransferManager
    {
        private const string PendingTransferFileName = "pending_transfer.json";
        private readonly object _sync = new();
        private readonly string _storePath;
        private PendingTransfer? _pending;

        public PendingTransferManager()
            : this(BuildDefaultStorePath())
        {
        }

        public PendingTransferManager(string storePath)
        {
            _storePath = storePath ?? throw new ArgumentNullException(nameof(storePath));
            _pending = LoadFromDisk(_storePath);
        }

        public PendingTransfer? Replace(PendingTransfer transfer)
        {
            lock (_sync)
            {
                var replaced = _pending;
                _pending = transfer;
                PersistUnsafe();
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
                    PersistUnsafe();
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
                PersistUnsafe();
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
                PersistUnsafe();
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

        private void PersistUnsafe()
        {
            try
            {
                if (_pending == null)
                {
                    if (File.Exists(_storePath))
                    {
                        File.Delete(_storePath);
                    }

                    return;
                }

                var directory = Path.GetDirectoryName(_storePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var record = PendingTransferRecord.FromDomain(_pending);
                var json = JsonSerializer.Serialize(record, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                var tempPath = $"{_storePath}.tmp";
                File.WriteAllText(tempPath, json);
                if (File.Exists(_storePath))
                {
                    File.Delete(_storePath);
                }

                File.Move(tempPath, _storePath);
            }
            catch
            {
                // Keep runtime behavior even if persistence fails.
            }
        }

        private static PendingTransfer? LoadFromDisk(string storePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(storePath) || !File.Exists(storePath))
                {
                    return null;
                }

                var json = File.ReadAllText(storePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var record = JsonSerializer.Deserialize<PendingTransferRecord>(json);
                return record?.ToDomain();
            }
            catch
            {
                return null;
            }
        }

        private static string BuildDefaultStorePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Speakly", PendingTransferFileName);
        }

        private sealed class PendingTransferRecord
        {
            public string Text { get; set; } = string.Empty;
            public long Handle { get; set; }
            public uint ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public string WindowTitle { get; set; } = string.Empty;
            public DateTime CapturedAtUtc { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public DateTime? ExpiresAtUtc { get; set; }
            public string FailureCode { get; set; } = string.Empty;
            public string OperationId { get; set; } = string.Empty;

            public static PendingTransferRecord FromDomain(PendingTransfer pending)
            {
                return new PendingTransferRecord
                {
                    Text = pending.Text,
                    Handle = pending.TargetContext.Handle.ToInt64(),
                    ProcessId = pending.TargetContext.ProcessId,
                    ProcessName = pending.TargetContext.ProcessName,
                    WindowTitle = pending.TargetContext.WindowTitle,
                    CapturedAtUtc = pending.TargetContext.CapturedAtUtc,
                    CreatedAtUtc = pending.CreatedAtUtc,
                    ExpiresAtUtc = pending.ExpiresAtUtc,
                    FailureCode = pending.FailureCode,
                    OperationId = pending.OperationId
                };
            }

            public PendingTransfer ToDomain()
            {
                var target = new TargetWindowContext(
                    handle: new IntPtr(Handle),
                    processId: ProcessId,
                    processName: ProcessName ?? string.Empty,
                    windowTitle: WindowTitle ?? string.Empty,
                    capturedAtUtc: CapturedAtUtc);

                return new PendingTransfer(
                    text: Text ?? string.Empty,
                    targetContext: target,
                    createdAtUtc: CreatedAtUtc,
                    expiresAtUtc: ExpiresAtUtc,
                    failureCode: FailureCode ?? string.Empty,
                    operationId: OperationId ?? string.Empty);
            }
        }
    }
}

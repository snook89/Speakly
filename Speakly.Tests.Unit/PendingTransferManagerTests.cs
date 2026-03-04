using System;
using Speakly.Services;

namespace Speakly.Tests.Unit
{
    public class PendingTransferManagerTests
    {
        [Fact]
        public void GetActiveOrExpire_ReturnsPending_WhenNotExpired()
        {
            var manager = new PendingTransferManager();
            var now = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
            var target = new TargetWindowContext(new IntPtr(123), 456, "notepad", "Untitled - Notepad", now);
            var pending = new PendingTransfer("hello", target, now, now.AddMinutes(5), "focus_restore_failed", "op1");

            manager.Replace(pending);

            var active = manager.GetActiveOrExpire(now.AddMinutes(1), out var expired);

            Assert.Null(expired);
            Assert.NotNull(active);
            Assert.Equal(pending.Id, active!.Id);
        }

        [Fact]
        public void GetActiveOrExpire_ClearsPending_WhenExpired()
        {
            var manager = new PendingTransferManager();
            var now = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
            var target = new TargetWindowContext(new IntPtr(123), 456, "notepad", "Untitled - Notepad", now);
            var pending = new PendingTransfer("hello", target, now, now.AddSeconds(10), "focus_restore_failed", "op1");
            manager.Replace(pending);

            var active = manager.GetActiveOrExpire(now.AddSeconds(11), out var expired);
            var secondRead = manager.GetActiveOrExpire(now.AddSeconds(12), out var secondExpired);

            Assert.Null(active);
            Assert.NotNull(expired);
            Assert.Equal(pending.Id, expired!.Id);
            Assert.Null(secondRead);
            Assert.Null(secondExpired);
        }

        [Fact]
        public void IsForegroundMatch_MatchesHandleOrProcessId()
        {
            var capturedAt = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
            var target = new TargetWindowContext(new IntPtr(500), 700, "notepad++", "notes.txt", capturedAt);

            var handleMatch = new TargetWindowContext(new IntPtr(500), 900, "chrome", "Other", capturedAt);
            var processMatch = new TargetWindowContext(new IntPtr(999), 700, "notepad++", "Other title", capturedAt);
            var noMatch = new TargetWindowContext(new IntPtr(111), 222, "code", "VS Code", capturedAt);

            Assert.True(PendingTransferManager.IsForegroundMatch(handleMatch, target));
            Assert.True(PendingTransferManager.IsForegroundMatch(processMatch, target));
            Assert.False(PendingTransferManager.IsForegroundMatch(noMatch, target));
        }
    }
}

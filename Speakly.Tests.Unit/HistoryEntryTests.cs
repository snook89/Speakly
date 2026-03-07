using System;
using Speakly.Services;
using Xunit;

namespace Speakly.Tests.Unit
{
    public class HistoryEntryTests
    {
        [Theory]
        [InlineData("HistoryRetry", "HistoryRetry")]
        [InlineData("historyreprocess", "HistoryReprocess")]
        [InlineData("", "Live")]
        [InlineData("unknown", "Live")]
        public void NormalizeActionSource_ReturnsSupportedValue(string input, string expected)
        {
            Assert.Equal(expected, HistoryEntry.NormalizeActionSource(input));
        }

        [Fact]
        public void CompareSummary_RetryWithSameText_ReportsUnchanged()
        {
            var entry = new HistoryEntry
            {
                ActionSource = "HistoryRetry",
                RefinedText = "Same final text",
                SourceRefinedText = "Same final text",
                SourceTimestamp = new DateTime(2026, 3, 7, 12, 15, 0)
            };

            Assert.True(entry.HasSourceComparison);
            Assert.Equal("PREVIOUS INSERTED TEXT", entry.CompareLabel);
            Assert.Contains("Final text is unchanged", entry.CompareSummary);
        }

        [Fact]
        public void CompareSummary_ReprocessWithDifferentText_ReportsChanged()
        {
            var entry = new HistoryEntry
            {
                ActionSource = "HistoryReprocess",
                RefinedText = "New refined output",
                SourceRefinedText = "Old refined output",
                SourceTimestamp = new DateTime(2026, 3, 7, 12, 16, 0)
            };

            Assert.True(entry.HasSourceComparison);
            Assert.Equal("PREVIOUS REFINED TEXT", entry.CompareLabel);
            Assert.Contains("Final text changed", entry.CompareSummary);
        }
    }
}

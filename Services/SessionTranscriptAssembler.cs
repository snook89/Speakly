using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Speakly.Services
{
    public static class SessionTranscriptAssembler
    {
        public static string MergeFinalSegments(IEnumerable<string> segments)
        {
            if (segments == null) return string.Empty;

            string merged = string.Empty;
            foreach (var rawSegment in segments)
            {
                var segment = NormalizeWhitespace(rawSegment);
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(merged))
                {
                    merged = segment;
                    continue;
                }

                if (string.Equals(segment, merged, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (merged.EndsWith(segment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (segment.EndsWith(merged, StringComparison.OrdinalIgnoreCase))
                {
                    merged = segment;
                    continue;
                }

                int overlap = FindLongestSuffixPrefixOverlap(merged, segment);
                if (overlap > 0)
                {
                    merged = NormalizeWhitespace(merged + segment.Substring(overlap));
                }
                else
                {
                    merged = NormalizeWhitespace($"{merged} {segment}");
                }
            }

            return merged;
        }

        private static string NormalizeWhitespace(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var trimmed = value.Trim();
            return Regex.Replace(trimmed, "\\s+", " ");
        }

        private static int FindLongestSuffixPrefixOverlap(string left, string right)
        {
            int max = Math.Min(left.Length, right.Length);
            for (int length = max; length > 0; length--)
            {
                if (left.EndsWith(right.Substring(0, length), StringComparison.OrdinalIgnoreCase))
                {
                    return length;
                }
            }

            return 0;
        }
    }
}

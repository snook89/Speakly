using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Speakly.Services
{
    internal sealed class UpdateReleaseNotesContent
    {
        public string Summary { get; }
        public IReadOnlyList<string> Highlights { get; }

        public UpdateReleaseNotesContent(string summary, IReadOnlyList<string> highlights)
        {
            Summary = string.IsNullOrWhiteSpace(summary)
                ? "This release includes improvements and fixes."
                : summary.Trim();
            Highlights = highlights;
        }
    }

    internal static class UpdateReleaseNotesFormatter
    {
        private static readonly Regex NumberedBulletRegex = new(@"^\d+[\.\)]\s+", RegexOptions.Compiled);
        private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);
        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

        public static UpdateReleaseNotesContent Parse(string version, string? markdown, string? html)
        {
            var normalized = !string.IsNullOrWhiteSpace(markdown)
                ? NormalizeMarkdown(markdown!)
                : NormalizeHtml(html);

            var paragraphs = ExtractParagraphs(normalized);
            var highlights = ExtractHighlights(normalized);

            var summary = paragraphs.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = $"What's new in {version}";
            }

            if (highlights.Count == 0)
            {
                highlights = paragraphs
                    .Skip(1)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Take(4)
                    .ToList();
            }

            if (highlights.Count == 0)
            {
                highlights.Add("This update includes fixes, polish, and quality improvements.");
            }

            return new UpdateReleaseNotesContent(summary!, highlights);
        }

        private static string NormalizeMarkdown(string markdown)
        {
            var normalized = markdown.Replace("\r\n", "\n");
            normalized = MarkdownLinkRegex.Replace(normalized, "$1");
            normalized = normalized.Replace("`", string.Empty);
            return normalized;
        }

        private static string NormalizeHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var normalized = html!
                .Replace("<li>", "\n- ", StringComparison.OrdinalIgnoreCase)
                .Replace("</li>", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
                .Replace("</div>", "\n\n", StringComparison.OrdinalIgnoreCase);

            normalized = HtmlTagRegex.Replace(normalized, string.Empty);
            return WebUtility.HtmlDecode(normalized);
        }

        private static List<string> ExtractParagraphs(string text)
        {
            var paragraphs = new List<string>();
            var lines = text.Split('\n');
            var current = new List<string>();
            bool inCodeBlock = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inCodeBlock = !inCodeBlock;
                    continue;
                }

                if (inCodeBlock)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph(current, paragraphs);
                    continue;
                }

                if (IsBulletLine(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    FlushParagraph(current, paragraphs);
                    continue;
                }

                current.Add(CleanLine(line));
            }

            FlushParagraph(current, paragraphs);
            return paragraphs;
        }

        private static List<string> ExtractHighlights(string text)
        {
            var highlights = new List<string>();
            bool inCodeBlock = false;

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inCodeBlock = !inCodeBlock;
                    continue;
                }

                if (inCodeBlock || !IsBulletLine(line))
                {
                    continue;
                }

                var cleaned = CleanBullet(line);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    continue;
                }

                if (!highlights.Any(existing => string.Equals(existing, cleaned, StringComparison.OrdinalIgnoreCase)))
                {
                    highlights.Add(cleaned);
                }

                if (highlights.Count >= 6)
                {
                    break;
                }
            }

            return highlights;
        }

        private static bool IsBulletLine(string line)
        {
            return line.StartsWith("- ", StringComparison.Ordinal)
                || line.StartsWith("* ", StringComparison.Ordinal)
                || line.StartsWith("+ ", StringComparison.Ordinal)
                || NumberedBulletRegex.IsMatch(line);
        }

        private static string CleanBullet(string line)
        {
            var cleaned = line;
            if (cleaned.StartsWith("- ", StringComparison.Ordinal) ||
                cleaned.StartsWith("* ", StringComparison.Ordinal) ||
                cleaned.StartsWith("+ ", StringComparison.Ordinal))
            {
                cleaned = cleaned[2..];
            }
            else
            {
                cleaned = NumberedBulletRegex.Replace(cleaned, string.Empty);
            }

            return CleanLine(cleaned);
        }

        private static string CleanLine(string line)
        {
            var cleaned = line.Trim();
            cleaned = cleaned.Trim('*', '_', '#', '-', ' ');
            cleaned = Regex.Replace(cleaned, @"\s+", " ");
            return WebUtility.HtmlDecode(cleaned).Trim();
        }

        private static void FlushParagraph(List<string> current, List<string> paragraphs)
        {
            if (current.Count == 0)
            {
                return;
            }

            var paragraph = string.Join(" ", current.Select(CleanLine)).Trim();
            if (!string.IsNullOrWhiteSpace(paragraph))
            {
                paragraphs.Add(paragraph);
            }

            current.Clear();
        }
    }
}

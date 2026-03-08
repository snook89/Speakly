using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Speakly.Config;

namespace Speakly.Services
{
    internal sealed record ElevenLabsRealtimeMessage(
        string MessageType,
        string Text = "",
        string Error = "");

    internal static class ElevenLabsRealtimeProtocol
    {
        internal const string DefaultModel = "scribe_v2_realtime";
        private const string BaseWebSocketUrl = "wss://api.elevenlabs.io/v1/speech-to-text/realtime";

        internal static string BuildSocketUrl(AppConfig config)
        {
            var parameters = new List<string>
            {
                $"model_id={Uri.EscapeDataString(ConfigManager.ResolveSttModel("ElevenLabs", config.ElevenLabsSttModel))}",
                "audio_format=pcm_16000",
                "include_timestamps=false",
                "commit_strategy=manual"
            };

            var languageCode = ResolveLanguageCode(config.Language);
            if (!string.IsNullOrWhiteSpace(languageCode))
            {
                parameters.Add($"language_code={Uri.EscapeDataString(languageCode)}");
            }

            return $"{BaseWebSocketUrl}?{string.Join("&", parameters)}";
        }

        internal static string ResolveLanguageCode(string? configuredLanguage)
        {
            var normalized = configuredLanguage?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (string.Equals(normalized, "layout", StringComparison.OrdinalIgnoreCase))
            {
                return InputLanguageResolver.ResolveCurrentLanguageCode("en");
            }

            if (string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "multi", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return normalized.ToLowerInvariant();
        }

        internal static string BuildInputAudioChunk(byte[] pcmChunk, int sampleRate, bool commit)
        {
            var payload = new Dictionary<string, object?>
            {
                ["message_type"] = "input_audio_chunk",
                ["audio_base_64"] = Convert.ToBase64String(pcmChunk ?? Array.Empty<byte>()),
                ["sample_rate"] = sampleRate
            };

            if (commit)
            {
                payload["commit"] = true;
            }

            return JsonSerializer.Serialize(payload);
        }

        internal static ElevenLabsRealtimeMessage ParseMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ElevenLabsRealtimeMessage("unknown");
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var messageType = GetString(root, "type", "message_type", "event") ?? "unknown";

            if (string.Equals(messageType, "error", StringComparison.OrdinalIgnoreCase) ||
                messageType.EndsWith("_error", StringComparison.OrdinalIgnoreCase))
            {
                var error = GetString(root, "message", "error", "detail")
                    ?? ExtractNestedMessage(root);
                return new ElevenLabsRealtimeMessage(messageType, Error: error ?? "Unknown ElevenLabs realtime error");
            }

            if (string.Equals(messageType, "session_started", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(messageType, "session_initiated", StringComparison.OrdinalIgnoreCase))
            {
                return new ElevenLabsRealtimeMessage("session_started");
            }

            if (string.Equals(messageType, "transcript", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(messageType, "partial_transcript", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(messageType, "partial_transcript_with_timestamps", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(messageType, "committed_transcript", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(messageType, "committed_transcript_with_timestamps", StringComparison.OrdinalIgnoreCase))
            {
                var text = GetString(root, "text", "transcript")
                    ?? ExtractNestedTranscript(root)
                    ?? string.Empty;

                var isFinal = string.Equals(messageType, "committed_transcript", StringComparison.OrdinalIgnoreCase)
                    || (root.TryGetProperty("is_final", out var isFinalProp) &&
                        isFinalProp.ValueKind == JsonValueKind.True);

                return new ElevenLabsRealtimeMessage(isFinal ? "committed_transcript" : "partial_transcript", text);
            }

            return new ElevenLabsRealtimeMessage(messageType);
        }

        private static string? ExtractNestedTranscript(JsonElement root)
        {
            foreach (var propertyName in new[] { "transcript", "result", "data" })
            {
                if (!root.TryGetProperty(propertyName, out var nested))
                {
                    continue;
                }

                if (nested.ValueKind == JsonValueKind.String)
                {
                    return nested.GetString();
                }

                if (nested.ValueKind == JsonValueKind.Object)
                {
                    var nestedText = GetString(nested, "text", "transcript");
                    if (!string.IsNullOrWhiteSpace(nestedText))
                    {
                        return nestedText;
                    }
                }
            }

            return null;
        }

        private static string? ExtractNestedMessage(JsonElement root)
        {
            foreach (var propertyName in new[] { "error", "data" })
            {
                if (!root.TryGetProperty(propertyName, out var nested) || nested.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var nestedMessage = GetString(nested, "message", "detail", "error");
                if (!string.IsNullOrWhiteSpace(nestedMessage))
                {
                    return nestedMessage;
                }
            }

            return null;
        }

        private static string? GetString(JsonElement element, params string[] propertyNames)
        {
            return propertyNames
                .Select(name => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
    }
}

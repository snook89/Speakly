using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Speakly.Services;

namespace Speakly.Config
{
    public class AppConfig
    {
        public const int CurrentConfigVersion = 3;
        public const string DefaultRefinementPrompt =
            "Role and Objective:\n" +
            "You refine speech-to-text transcripts for clarity, grammatical correctness, and formatting compliance.\n\n" +
            "Rules:\n" +
            "1) Preserve the original meaning and intent.\n" +
            "2) Correct grammar, punctuation, capitalization, and obvious transcription errors.\n" +
            "3) Do not add new facts, assumptions, or commentary.\n" +
            "4) If the transcript ends with a user format instruction (e.g., \"as bullets\", \"as email\", \"in JSON\"), apply that format but do not include the instruction text itself in the output.\n" +
            "5) Keep names, numbers, links, and technical terms accurate.\n" +
            "6) Never answer as a chatbot, never ask follow-up questions, and never provide explanations.\n" +
            "7) If input is mixed, noisy, or unclear, return the original transcript unchanged.\n\n" +
            "Output contract:\n" +
            "Return only the refined final text as a single plain string. No explanations, no labels, no markdown fences.";

        [JsonPropertyName("config_version")]
        public int ConfigVersion { get; set; } = CurrentConfigVersion;

        [JsonPropertyName("first_run_completed")]
        public bool FirstRunCompleted { get; set; }

        [JsonPropertyName("active_profile_id")]
        public string ActiveProfileId { get; set; } = string.Empty;

        [JsonPropertyName("profiles")]
        public List<AppProfile> Profiles { get; set; } = new();

        [JsonPropertyName("history_retention_days")]
        public int HistoryRetentionDays { get; set; } = 30;

        [JsonPropertyName("privacy_mode")]
        public string PrivacyMode { get; set; } = "normal";

        [JsonPropertyName("hotkey")]
        public string Hotkey { get; set; } = "Space"; // Legacy: maps to PTT

        [JsonPropertyName("ptt_hotkey")]
        public string PttHotkey { get; set; } = "Space"; // Hold-to-talk

        [JsonPropertyName("record_hotkey")]
        public string RecordHotkey { get; set; } = "F9"; // Toggle-record

        [JsonPropertyName("stt_model")]
        public string SttModel { get; set; } = "Deepgram";

        [JsonPropertyName("enable_stt_failover")]
        public bool EnableSttFailover { get; set; } = true;

        [JsonPropertyName("stt_failover_order")]
        public List<string> SttFailoverOrder { get; set; } = new List<string> { "Deepgram", "OpenAI", "OpenRouter" };

        [JsonPropertyName("audio_device")]
        public string AudioDevice { get; set; } = "Default";

        [JsonPropertyName("refinement_model")]
        public string RefinementModel { get; set; } = "OpenAI";

        [JsonPropertyName("refinement_prompt")]
        public string RefinementPrompt { get; set; } = DefaultRefinementPrompt;

        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "Dark";

        [JsonPropertyName("overlay_skin")]
        public string OverlaySkin { get; set; } = "Lavender";

        // Per-Service STT Models
        [JsonPropertyName("deepgram_model")]
        public string DeepgramModel { get; set; } = "nova-2";

        [JsonPropertyName("deepgram_api_base_url")]
        public string DeepgramApiBaseUrl { get; set; } = "https://api.deepgram.com";

        [JsonPropertyName("openai_stt_model")]
        public string OpenAISttModel { get; set; } = "whisper-1";

        [JsonPropertyName("openrouter_stt_model")]
        public string OpenRouterSttModel { get; set; } = "openai/whisper-large-v3";

        // Per-Service Refinement Models
        [JsonPropertyName("openai_refinement_model")]
        public string OpenAIRefinementModel { get; set; } = "gpt-4o-mini";

        [JsonPropertyName("cerebras_refinement_model")]
        public string CerebrasRefinementModel { get; set; } = "llama3.1-8b";

        [JsonPropertyName("cerebras_max_completion_tokens")]
        public int CerebrasMaxCompletionTokens { get; set; } = 256;

        [JsonPropertyName("cerebras_timeout_seconds")]
        public int CerebrasTimeoutSeconds { get; set; } = 60;

        [JsonPropertyName("cerebras_max_retries")]
        public int CerebrasMaxRetries { get; set; } = 2;

        [JsonPropertyName("cerebras_retry_base_delay_ms")]
        public int CerebrasRetryBaseDelayMs { get; set; } = 400;

        [JsonPropertyName("cerebras_version_patch")]
        public string CerebrasVersionPatch { get; set; } = string.Empty;

        [JsonPropertyName("openrouter_refinement_model")]
        public string OpenRouterRefinementModel { get; set; } = "google/gemini-2.0-flash-001";

        [JsonPropertyName("openrouter_favorite_models")]
        public List<string> OpenRouterFavoriteModels { get; set; } = new List<string>();

        [JsonPropertyName("openai_refinement_favorite_models")]
        public List<string> OpenAIRefinementFavoriteModels { get; set; } = new List<string>();

        [JsonPropertyName("cerebras_refinement_favorite_models")]
        public List<string> CerebrasRefinementFavoriteModels { get; set; } = new List<string>();

        [JsonPropertyName("deepgram_favorite_models")]
        public List<string> DeepgramFavoriteModels { get; set; } = new List<string>();

        [JsonPropertyName("openai_stt_favorite_models")]
        public List<string> OpenAISttFavoriteModels { get; set; } = new List<string>();

        [JsonPropertyName("openrouter_stt_favorite_models")]
        public List<string> OpenRouterSttFavoriteModels { get; set; } = new List<string>();

        [JsonPropertyName("enable_debug_logs")]
        public bool EnableDebugLogs { get; set; } = false;

        [JsonPropertyName("restore_clipboard")]
        public bool RestoreClipboard { get; set; } = true;
        
        [JsonPropertyName("minimize_to_tray")]
        public bool MinimizeToTray { get; set; } = true;

        [JsonPropertyName("show_overlay")]
        public bool ShowOverlay { get; set; } = true;

        [JsonPropertyName("language")]
        public string Language { get; set; } = "en";

        [JsonPropertyName("sample_rate")]
        public int SampleRate { get; set; } = 16000;

        [JsonPropertyName("channels")]
        public int Channels { get; set; } = 1;

        [JsonPropertyName("chunk_size")]
        public int ChunkSize { get; set; } = 4096;

        [JsonPropertyName("save_debug_records")]
        public bool SaveDebugRecords { get; set; } = false;

        [JsonPropertyName("enable_refinement")]
        public bool EnableRefinement { get; set; } = true;

        [JsonPropertyName("copy_to_clipboard")]
        public bool CopyToClipboard { get; set; } = false;

        [JsonIgnore]
        public string OpenAIApiKey { get; set; } = "";

        [JsonIgnore]
        public string DeepgramApiKey { get; set; } = "";

        [JsonIgnore]
        public string CerebrasApiKey { get; set; } = "";

        [JsonIgnore]
        public string OpenRouterApiKey { get; set; } = "";

        // Backward-compat plaintext load fields (read-only for migration).
        [JsonPropertyName("openai_api_key")]
        public string? LegacyOpenAIApiKey
        {
            get => null;
            set
            {
                if (string.IsNullOrWhiteSpace(OpenAIApiKey) && !string.IsNullOrWhiteSpace(value))
                    OpenAIApiKey = value;
            }
        }

        [JsonPropertyName("deepgram_api_key")]
        public string? LegacyDeepgramApiKey
        {
            get => null;
            set
            {
                if (string.IsNullOrWhiteSpace(DeepgramApiKey) && !string.IsNullOrWhiteSpace(value))
                    DeepgramApiKey = value;
            }
        }

        [JsonPropertyName("cerebras_api_key")]
        public string? LegacyCerebrasApiKey
        {
            get => null;
            set
            {
                if (string.IsNullOrWhiteSpace(CerebrasApiKey) && !string.IsNullOrWhiteSpace(value))
                    CerebrasApiKey = value;
            }
        }

        [JsonPropertyName("openrouter_api_key")]
        public string? LegacyOpenRouterApiKey
        {
            get => null;
            set
            {
                if (string.IsNullOrWhiteSpace(OpenRouterApiKey) && !string.IsNullOrWhiteSpace(value))
                    OpenRouterApiKey = value;
            }
        }

        [JsonPropertyName("openai_api_key_enc")]
        public string OpenAIApiKeyEnc { get; set; } = "";

        [JsonPropertyName("deepgram_api_key_enc")]
        public string DeepgramApiKeyEnc { get; set; } = "";

        [JsonPropertyName("cerebras_api_key_enc")]
        public string CerebrasApiKeyEnc { get; set; } = "";

        [JsonPropertyName("openrouter_api_key_enc")]
        public string OpenRouterApiKeyEnc { get; set; } = "";

        // Window state logic
        [JsonPropertyName("main_window_left")]
        public double MainWindowLeft { get; set; } = double.NaN;

        [JsonPropertyName("main_window_top")]
        public double MainWindowTop { get; set; } = double.NaN;

        [JsonPropertyName("main_window_width")]
        public double MainWindowWidth { get; set; } = 820;

        [JsonPropertyName("main_window_height")]
        public double MainWindowHeight { get; set; } = 640;

        [JsonPropertyName("overlay_left")]
        public double OverlayLeft { get; set; } = double.NaN;

        [JsonPropertyName("overlay_top")]
        public double OverlayTop { get; set; } = 50;

        [JsonPropertyName("overlay_width")]
        public double OverlayWidth { get; set; } = double.NaN;

        [JsonPropertyName("overlay_height")]
        public double OverlayHeight { get; set; } = double.NaN;
    }

    public static class ConfigManager
    {
        private static readonly string AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Speakly"
        );

        private static readonly string ConfigPath = Path.Combine(
            AppDataDirectory,
            "config.json"
        );

        private static readonly string LegacyConfigPath = Path.Combine(
            AppContext.BaseDirectory,
            "config.json"
        );

        private static AppConfig? _currentConfig;
        private static readonly object SaveLock = new();
        private static Timer? _saveDebounceTimer;

        public static AppConfig Config
        {
            get
            {
                if (_currentConfig == null)
                {
                    Load();
                }
                return _currentConfig!;
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    _currentConfig = LoadFromPath(ConfigPath);
                }
                else if (File.Exists(LegacyConfigPath))
                {
                    // Migrate legacy config from executable directory to %AppData%\Speakly.
                    _currentConfig = LoadFromPath(LegacyConfigPath);
                    Save();
                }
                else
                {
                    _currentConfig = new AppConfig();
                    MigrateConfig(_currentConfig);
                    PrepareSecrets(_currentConfig);
                    Save();
                }
            }
            catch
            {
                _currentConfig = new AppConfig();
                MigrateConfig(_currentConfig);
            }
        }

        public static void SaveDebounced(int delayMs = 400)
        {
            lock (SaveLock)
            {
                _saveDebounceTimer ??= new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
                _saveDebounceTimer.Change(Math.Max(100, delayMs), Timeout.Infinite);
            }
        }

        public static void Save()
        {
            try
            {
                string? directory = Path.GetDirectoryName(ConfigPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (_currentConfig == null) return;

                MigrateConfig(_currentConfig);
                PrepareSecrets(_currentConfig);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                string json = JsonSerializer.Serialize(_currentConfig, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        private static AppConfig LoadFromPath(string path)
        {
            string json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            HydrateSecrets(config);
            MigrateConfig(config);
            return config;
        }

        public static AppProfile BuildDefaultProfile(AppConfig config)
        {
            return new AppProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Default",
                SttProvider = config.SttModel,
                SttModel = ResolveSttModel(config),
                RefinementEnabled = config.EnableRefinement,
                RefinementProvider = config.RefinementModel,
                RefinementModel = ResolveRefinementModel(config),
                RefinementPrompt = config.RefinementPrompt,
                Language = config.Language,
                CopyToClipboard = config.CopyToClipboard,
                EnableSttFailover = config.EnableSttFailover,
                SttFailoverOrder = config.SttFailoverOrder?.ToList() ?? new List<string> { "Deepgram", "OpenAI", "OpenRouter" }
            };
        }

        public static AppProfile GetActiveProfile()
        {
            var config = Config;
            MigrateConfig(config);
            var active = config.Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, config.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
            return active ?? config.Profiles[0];
        }

        public static void SetActiveProfile(string? profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId)) return;
            var config = Config;
            var match = config.Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
            if (match == null) return;
            config.ActiveProfileId = match.Id;
            SaveDebounced();
        }

        public static void EnsureProfileSyncToLegacyFields(AppProfile profile)
        {
            if (profile == null) return;
            var config = Config;

            config.SttModel = profile.SttProvider;
            config.EnableRefinement = profile.RefinementEnabled;
            config.RefinementModel = profile.RefinementProvider;
            config.RefinementPrompt = profile.RefinementPrompt;
            config.Language = profile.Language;
            config.CopyToClipboard = profile.CopyToClipboard;
            config.EnableSttFailover = profile.EnableSttFailover;
            config.SttFailoverOrder = profile.SttFailoverOrder?.ToList() ?? new List<string>();

            switch (profile.SttProvider)
            {
                case "OpenAI":
                    config.OpenAISttModel = profile.SttModel;
                    break;
                case "OpenRouter":
                    config.OpenRouterSttModel = profile.SttModel;
                    break;
                default:
                    config.DeepgramModel = profile.SttModel;
                    break;
            }

            switch (profile.RefinementProvider)
            {
                case "OpenRouter":
                    config.OpenRouterRefinementModel = profile.RefinementModel;
                    break;
                case "Cerebras":
                    config.CerebrasRefinementModel = profile.RefinementModel;
                    break;
                default:
                    config.OpenAIRefinementModel = profile.RefinementModel;
                    break;
            }
        }

        private static void MigrateConfig(AppConfig config)
        {
            config.ConfigVersion = AppConfig.CurrentConfigVersion;
            config.HistoryRetentionDays = Math.Clamp(config.HistoryRetentionDays, 1, 3650);
            if (string.IsNullOrWhiteSpace(config.PrivacyMode))
                config.PrivacyMode = "normal";
            config.CerebrasMaxCompletionTokens = Math.Clamp(config.CerebrasMaxCompletionTokens, 16, 65536);
            config.CerebrasTimeoutSeconds = Math.Clamp(config.CerebrasTimeoutSeconds, 10, 300);
            config.CerebrasMaxRetries = Math.Clamp(config.CerebrasMaxRetries, 0, 6);
            config.CerebrasRetryBaseDelayMs = Math.Clamp(config.CerebrasRetryBaseDelayMs, 100, 5000);
            config.CerebrasVersionPatch = config.CerebrasVersionPatch?.Trim() ?? string.Empty;

            if (config.Profiles == null)
                config.Profiles = new List<AppProfile>();

            if (config.Profiles.Count == 0)
            {
                config.Profiles.Add(BuildDefaultProfile(config));
            }

            foreach (var profile in config.Profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Id))
                    profile.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(profile.Name))
                    profile.Name = "Profile";
                if (profile.ProcessNames == null)
                    profile.ProcessNames = new List<string>();
                profile.ProcessNames = profile.ProcessNames
                    .Select(ProfileHelpers.NormalizeProcessName)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (profile.SttFailoverOrder == null || profile.SttFailoverOrder.Count == 0)
                    profile.SttFailoverOrder = new List<string> { "Deepgram", "OpenAI", "OpenRouter" };
                if (string.IsNullOrWhiteSpace(profile.RefinementPrompt))
                    profile.RefinementPrompt = AppConfig.DefaultRefinementPrompt;
            }

            if (string.IsNullOrWhiteSpace(config.ActiveProfileId) ||
                !config.Profiles.Any(p => string.Equals(p.Id, config.ActiveProfileId, StringComparison.OrdinalIgnoreCase)))
            {
                config.ActiveProfileId = config.Profiles[0].Id;
            }
        }

        private static string ResolveSttModel(AppConfig config)
        {
            return config.SttModel switch
            {
                "OpenAI" => config.OpenAISttModel,
                "OpenRouter" => config.OpenRouterSttModel,
                _ => config.DeepgramModel
            };
        }

        private static string ResolveRefinementModel(AppConfig config)
        {
            return config.RefinementModel switch
            {
                "OpenRouter" => config.OpenRouterRefinementModel,
                "Cerebras" => config.CerebrasRefinementModel,
                _ => config.OpenAIRefinementModel
            };
        }

        private static void HydrateSecrets(AppConfig config)
        {
            var openAi = SecretStore.Unprotect(config.OpenAIApiKeyEnc);
            if (!string.IsNullOrWhiteSpace(openAi)) config.OpenAIApiKey = openAi;

            var deepgram = SecretStore.Unprotect(config.DeepgramApiKeyEnc);
            if (!string.IsNullOrWhiteSpace(deepgram)) config.DeepgramApiKey = deepgram;

            var cerebras = SecretStore.Unprotect(config.CerebrasApiKeyEnc);
            if (!string.IsNullOrWhiteSpace(cerebras)) config.CerebrasApiKey = cerebras;

            var openRouter = SecretStore.Unprotect(config.OpenRouterApiKeyEnc);
            if (!string.IsNullOrWhiteSpace(openRouter)) config.OpenRouterApiKey = openRouter;
        }

        private static void PrepareSecrets(AppConfig config)
        {
            config.OpenAIApiKeyEnc = SecretStore.Protect(config.OpenAIApiKey);
            config.DeepgramApiKeyEnc = SecretStore.Protect(config.DeepgramApiKey);
            config.CerebrasApiKeyEnc = SecretStore.Protect(config.CerebrasApiKey);
            config.OpenRouterApiKeyEnc = SecretStore.Protect(config.OpenRouterApiKey);
        }
    }
}

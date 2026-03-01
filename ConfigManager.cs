using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Speakly.Config
{
    public class AppConfig
    {
        [JsonPropertyName("hotkey")]
        public string Hotkey { get; set; } = "Space"; // Legacy: maps to PTT

        [JsonPropertyName("ptt_hotkey")]
        public string PttHotkey { get; set; } = "Space"; // Hold-to-talk

        [JsonPropertyName("record_hotkey")]
        public string RecordHotkey { get; set; } = "F9"; // Toggle-record

        [JsonPropertyName("stt_model")]
        public string SttModel { get; set; } = "Deepgram";

        [JsonPropertyName("audio_device")]
        public string AudioDevice { get; set; } = "Default";

        [JsonPropertyName("refinement_model")]
        public string RefinementModel { get; set; } = "OpenAI";

        [JsonPropertyName("refinement_prompt")]
        public string RefinementPrompt { get; set; } = "You are a professional assistant. Fix any typos or grammatical errors in the following text while maintaining the original meaning and tone. Return ONLY the corrected text.";

        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "Dark";

        // Per-Service STT Models
        [JsonPropertyName("deepgram_model")]
        public string DeepgramModel { get; set; } = "nova-2";

        [JsonPropertyName("openai_stt_model")]
        public string OpenAISttModel { get; set; } = "whisper-1";

        // Per-Service Refinement Models
        [JsonPropertyName("openai_refinement_model")]
        public string OpenAIRefinementModel { get; set; } = "gpt-4o-mini";

        [JsonPropertyName("cerebras_refinement_model")]
        public string CerebrasRefinementModel { get; set; } = "llama3.1-8b";

        [JsonPropertyName("openrouter_refinement_model")]
        public string OpenRouterRefinementModel { get; set; } = "google/gemini-2.0-flash-001";

        [JsonPropertyName("enable_debug_logs")]
        public bool EnableDebugLogs { get; set; } = false;

        [JsonPropertyName("restore_clipboard")]
        public bool RestoreClipboard { get; set; } = true;
        
        [JsonPropertyName("minimize_to_tray")]
        public bool MinimizeToTray { get; set; } = true;

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

        [JsonPropertyName("openai_api_key")]
        public string OpenAIApiKey { get; set; } = "";
        
        [JsonPropertyName("deepgram_api_key")]
        public string DeepgramApiKey { get; set; } = "";
        
        [JsonPropertyName("cerebras_api_key")]
        public string CerebrasApiKey { get; set; } = "";
        
        [JsonPropertyName("openrouter_api_key")]
        public string OpenRouterApiKey { get; set; } = "";

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
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Speakly",
            "config.json"
        );

        private static AppConfig? _currentConfig;

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
                    string json = File.ReadAllText(ConfigPath);
                    _currentConfig = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
                else
                {
                    _currentConfig = new AppConfig();
                    Save();
                }
            }
            catch
            {
                _currentConfig = new AppConfig();
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

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_currentConfig, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}

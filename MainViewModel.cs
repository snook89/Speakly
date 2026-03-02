using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NAudio.Wave;
using Speakly.Config;
using Speakly.Helpers;
using Speakly.Services;

namespace Speakly.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private readonly Dictionary<string, List<string>> _dynamicSttModels = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _dynamicRefinementModels = new(StringComparer.OrdinalIgnoreCase);
        private bool _isRefreshingModels;
        private string _modelRefreshStatus = "Model list: defaults loaded";

        public event Action? SaveSucceeded;
        public event Action<string, string, string, string>? ApiTestCompleted;
        public event Action<bool>? RefreshModelsCompleted;

        public ICommand SaveCommand { get; }
        public ICommand RefreshModelsCommand { get; }
        public ICommand SaveCurrentPromptCommand { get; }
        public ICommand DeleteSelectedPromptCommand { get; }

        public bool IsRefreshingModels
        {
            get => _isRefreshingModels;
            private set
            {
                _isRefreshingModels = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ModelRefreshStatus
        {
            get => _modelRefreshStatus;
            private set
            {
                _modelRefreshStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasModelRefreshStatus));
            }
        }

        public bool HasModelRefreshStatus => !string.IsNullOrWhiteSpace(ModelRefreshStatus);

        public string Hotkey 
        { 
            get => ConfigManager.Config.Hotkey; 
            set { ConfigManager.Config.Hotkey = value; OnPropertyChanged(); } 
        }

        public string PttHotkey
        {
            get => ConfigManager.Config.PttHotkey;
            set { ConfigManager.Config.PttHotkey = value; OnPropertyChanged(); }
        }

        public string RecordHotkey
        {
            get => ConfigManager.Config.RecordHotkey;
            set { ConfigManager.Config.RecordHotkey = value; OnPropertyChanged(); }
        }

        public string SttModel
        {
            get => ConfigManager.Config.SttModel;
            set 
            { 
                ConfigManager.Config.SttModel = value; 
                UpdateSttModelList();
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(SelectedSttModelString));
            }
        }

        public string AudioDevice
        {
            get => ConfigManager.Config.AudioDevice;
            set { ConfigManager.Config.AudioDevice = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> AvailableAudioDevices { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableOverlaySkins { get; } = new ObservableCollection<string>
        {
            "Lavender",
            "Midnight",
            "Sakura",
            "Forest",
            "Ember"
        };

        public ObservableCollection<string> AvailableSttModels { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableRefinementModels { get; } = new ObservableCollection<string>();

        public string SelectedSttModelString
        {
            get
            {
                return SttModel switch
                {
                    "Deepgram" => ConfigManager.Config.DeepgramModel,
                    "OpenAI" => ConfigManager.Config.OpenAISttModel,
                    _ => ""
                };
            }
            set
            {
                var normalized = value?.Trim();
                if (string.IsNullOrWhiteSpace(normalized)) return;

                if (SttModel == "Deepgram") ConfigManager.Config.DeepgramModel = normalized;
                else if (SttModel == "OpenAI") ConfigManager.Config.OpenAISttModel = normalized;
                OnPropertyChanged();
            }
        }

        public string SelectedRefinementModelString
        {
            get
            {
                return RefinementModel switch
                {
                    "OpenAI" => ConfigManager.Config.OpenAIRefinementModel,
                    "Cerebras" => ConfigManager.Config.CerebrasRefinementModel,
                    "OpenRouter" => ConfigManager.Config.OpenRouterRefinementModel,
                    _ => ""
                };
            }
            set
            {
                var normalized = value?.Trim();
                if (string.IsNullOrWhiteSpace(normalized)) return;

                if (RefinementModel == "OpenAI") ConfigManager.Config.OpenAIRefinementModel = normalized;
                else if (RefinementModel == "Cerebras") ConfigManager.Config.CerebrasRefinementModel = normalized;
                else if (RefinementModel == "OpenRouter") ConfigManager.Config.OpenRouterRefinementModel = normalized;
                OnPropertyChanged();
            }
        }

        public string DeepgramApiKey
        {
            get => ConfigManager.Config.DeepgramApiKey;
            set { ConfigManager.Config.DeepgramApiKey = value; OnPropertyChanged(); }
        }

        public string OpenAIApiKey
        {
            get => ConfigManager.Config.OpenAIApiKey;
            set { ConfigManager.Config.OpenAIApiKey = value; OnPropertyChanged(); }
        }

        public string OpenRouterApiKey
        {
            get => ConfigManager.Config.OpenRouterApiKey;
            set { ConfigManager.Config.OpenRouterApiKey = value; OnPropertyChanged(); }
        }

        public string CerebrasApiKey
        {
            get => ConfigManager.Config.CerebrasApiKey;
            set { ConfigManager.Config.CerebrasApiKey = value; OnPropertyChanged(); }
        }

        public string RefinementModel
        {
            get => ConfigManager.Config.RefinementModel;
            set 
            { 
                ConfigManager.Config.RefinementModel = value; 
                UpdateRefinementModelList();
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(SelectedRefinementModelString));
            }
        }

        public string RefinementPrompt
        {
            get => ConfigManager.Config.RefinementPrompt;
            set
            {
                ConfigManager.Config.RefinementPrompt = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRefinementPromptMissing));
                OnPropertyChanged(nameof(RefinementTabHeader));
            }
        }

        public bool IsRefinementPromptMissing => string.IsNullOrWhiteSpace(RefinementPrompt);
        public string RefinementTabHeader => IsRefinementPromptMissing ? "Refinement ⚠" : "Refinement";

        public bool MinimizeToTray
        {
            get => ConfigManager.Config.MinimizeToTray;
            set { ConfigManager.Config.MinimizeToTray = value; OnPropertyChanged(); }
        }

        public bool ShowOverlay
        {
            get => ConfigManager.Config.ShowOverlay;
            set
            {
                ConfigManager.Config.ShowOverlay = value;
                App.SetOverlayVisible(value);
                OnPropertyChanged();
            }
        }

        public int SampleRate
        {
            get => ConfigManager.Config.SampleRate;
            set { ConfigManager.Config.SampleRate = value; OnPropertyChanged(); }
        }

        public int Channels
        {
            get => ConfigManager.Config.Channels;
            set { ConfigManager.Config.Channels = value; OnPropertyChanged(); }
        }

        public int ChunkSize
        {
            get => ConfigManager.Config.ChunkSize;
            set { ConfigManager.Config.ChunkSize = value; OnPropertyChanged(); }
        }

        public bool SaveDebugRecords
        {
            get => ConfigManager.Config.SaveDebugRecords;
            set { ConfigManager.Config.SaveDebugRecords = value; OnPropertyChanged(); }
        }

        public bool EnableRefinement
        {
            get => ConfigManager.Config.EnableRefinement;
            set { ConfigManager.Config.EnableRefinement = value; OnPropertyChanged(); }
        }

        public bool CopyToClipboard
        {
            get => ConfigManager.Config.CopyToClipboard;
            set { ConfigManager.Config.CopyToClipboard = value; OnPropertyChanged(); }
        }

        public string Language
        {
            get => ConfigManager.Config.Language;
            set { ConfigManager.Config.Language = value; OnPropertyChanged(); }
        }

        public ObservableCollection<HistoryEntry> HistoryEntries { get; } = new ObservableCollection<HistoryEntry>();

        // ── Prompt Library ────────────────────────────────────────────────────

        public ObservableCollection<PromptEntry> SavedPrompts { get; } = new ObservableCollection<PromptEntry>();

        private PromptEntry? _selectedPromptEntry;
        public PromptEntry? SelectedPromptEntry
        {
            get => _selectedPromptEntry;
            set
            {
                _selectedPromptEntry = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDeleteSelectedPrompt));
                if (value != null)
                    RefinementPrompt = value.Text;
            }
        }

        private string _newPromptName = "";
        public string NewPromptName
        {
            get => _newPromptName;
            set { _newPromptName = value; OnPropertyChanged(); }
        }

        public bool CanDeleteSelectedPrompt =>
            _selectedPromptEntry != null && !_selectedPromptEntry.IsBuiltIn;

        private List<PromptEntry> _promptList = new();

        private void ReloadSavedPrompts()
        {
            _promptList = PromptLibraryManager.Load();
            SavedPrompts.Clear();
            foreach (var p in _promptList)
                SavedPrompts.Add(p);
        }

        public ICommand ToggleFavoriteModelCommand => ToggleRefinementFavoriteModelCommand;

        public ICommand ToggleRefinementFavoriteModelCommand => new RelayCommand(
            obj => ToggleRefinementFavorite(obj as string ?? string.Empty),
            obj => GetRefinementFavoriteList() != null
                   && obj is string s && !string.IsNullOrWhiteSpace(s)
        );

        public ICommand ToggleSttFavoriteModelCommand => new RelayCommand(
            obj => ToggleSttFavorite(obj as string ?? string.Empty),
            obj => GetSttFavoriteList() != null
                   && obj is string s && !string.IsNullOrWhiteSpace(s)
        );

        public ICommand CopyHistoryCommand => new RelayCommand(obj => {
            if (obj is string text) Clipboard.SetText(text);
        });

        public string ApiTestStatus
        {
            get => _apiTestStatus;
            set { _apiTestStatus = value; OnPropertyChanged(); }
        }
        private string _apiTestStatus = "Ready";

        public ICommand TestApiCommand => new RelayCommand(async _ => await TestApiConnectionsAsync());

        public bool EnableDebugLogs
        {
            get => ConfigManager.Config.EnableDebugLogs;
            set { ConfigManager.Config.EnableDebugLogs = value; OnPropertyChanged(); }
        }

        public string Theme
        {
            get => ConfigManager.Config.Theme;
            set 
            { 
                ConfigManager.Config.Theme = value; 
                App.SetTheme(value);
                OnPropertyChanged(); 
            }
        }

        public string OverlaySkin
        {
            get => ConfigManager.Config.OverlaySkin;
            set
            {
                var normalized = value?.Trim();
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    normalized = "Lavender";
                }

                if (!AvailableOverlaySkins.Any(s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    normalized = "Lavender";
                }

                ConfigManager.Config.OverlaySkin = normalized;
                App.SetOverlaySkin(normalized);
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            LoadAudioDevices();
            UpdateSttModelList();
            UpdateRefinementModelList();
            _ = RefreshProviderModelsAsync();
            
            foreach (var entry in HistoryManager.GetHistory())
            {
                HistoryEntries.Add(entry);
            }

            SaveCommand = new RelayCommand(_ => {
                ConfigManager.Save();
                SaveSucceeded?.Invoke();
            });

            RefreshModelsCommand = new RelayCommand(
                async _ => await RefreshProviderModelsAsync(true),
                _ => !IsRefreshingModels);

            ReloadSavedPrompts();

            SaveCurrentPromptCommand = new RelayCommand(_ =>
            {
                var name = NewPromptName?.Trim();
                if (string.IsNullOrWhiteSpace(name)) return;

                _promptList = PromptLibraryManager.AddOrUpdate(_promptList, name, RefinementPrompt);
                SavedPrompts.Clear();
                foreach (var p in _promptList) SavedPrompts.Add(p);

                // Select the just-saved entry
                var saved = SavedPrompts.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (saved != null) SelectedPromptEntry = saved;
                NewPromptName = "";
            },
            _ => !string.IsNullOrWhiteSpace(NewPromptName));

            DeleteSelectedPromptCommand = new RelayCommand(_ =>
            {
                if (_selectedPromptEntry == null || _selectedPromptEntry.IsBuiltIn) return;
                var deleteName = _selectedPromptEntry.Name;
                _promptList = PromptLibraryManager.Delete(_promptList, deleteName);
                SavedPrompts.Clear();
                foreach (var p in _promptList) SavedPrompts.Add(p);
                SelectedPromptEntry = SavedPrompts.FirstOrDefault();
            },
            _ => CanDeleteSelectedPrompt);
        }

        private void LoadAudioDevices()
        {
            AvailableAudioDevices.Clear();
            AvailableAudioDevices.Add("Default");

            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var capabilities = WaveInEvent.GetCapabilities(i);
                AvailableAudioDevices.Add(capabilities.ProductName);
            }
        }

        private void UpdateSttModelList()
        {
            AvailableSttModels.Clear();
            if (TryApplyDynamicModels(AvailableSttModels, _dynamicSttModels, SttModel))
            {
                PrioritizeSttFavorites();
                EnsureCurrentModelInList(AvailableSttModels, SelectedSttModelString);
                OnPropertyChanged(nameof(SelectedSttModelString));
                return;
            }

            if (SttModel == "Deepgram")
            {
                AvailableSttModels.Add("nova-2");
                AvailableSttModels.Add("nova-2-ea");
                AvailableSttModels.Add("nova-2-phone");
                AvailableSttModels.Add("nova-2-medical");
                AvailableSttModels.Add("nova-3");
                AvailableSttModels.Add("whisper-tiny");
                AvailableSttModels.Add("whisper-small");
                AvailableSttModels.Add("whisper-medium");
                AvailableSttModels.Add("whisper-large");
            }
            else if (SttModel == "OpenAI")
            {
                AvailableSttModels.Add("whisper-1");
                AvailableSttModels.Add("whisper-1-hd");
            }
            PrioritizeSttFavorites();
            EnsureCurrentModelInList(AvailableSttModels, SelectedSttModelString);
            OnPropertyChanged(nameof(SelectedSttModelString));
        }

        private void UpdateRefinementModelList()
        {
            AvailableRefinementModels.Clear();
            if (TryApplyDynamicModels(AvailableRefinementModels, _dynamicRefinementModels, RefinementModel))
            {
                PrioritizeRefinementFavorites();
                EnsureCurrentModelInList(AvailableRefinementModels, SelectedRefinementModelString);
                OnPropertyChanged(nameof(SelectedRefinementModelString));
                return;
            }

            if (RefinementModel == "OpenAI")
            {
                AvailableRefinementModels.Add("gpt-4o-mini");
                AvailableRefinementModels.Add("gpt-4o");
                AvailableRefinementModels.Add("gpt-4-turbo");
                AvailableRefinementModels.Add("gpt-3.5-turbo");
            }
            else if (RefinementModel == "Cerebras")
            {
                AvailableRefinementModels.Add("llama3.1-8b");
                AvailableRefinementModels.Add("llama3.1-70b");
            }
            else if (RefinementModel == "OpenRouter")
            {
                AvailableRefinementModels.Add("google/gemini-2.0-flash-001");
                AvailableRefinementModels.Add("google/gemini-2.0-pro-exp-02-05:free");
                AvailableRefinementModels.Add("google/gemini-flash-1.5");
                AvailableRefinementModels.Add("anthropic/claude-3-haiku");
                AvailableRefinementModels.Add("anthropic/claude-3.5-sonnet");
                AvailableRefinementModels.Add("meta-llama/llama-3.1-8b-instruct:free");
                AvailableRefinementModels.Add("meta-llama/llama-3.1-70b-instruct");
                AvailableRefinementModels.Add("x-ai/grok-2");
                AvailableRefinementModels.Add("deepseek/deepseek-chat");
            }
            PrioritizeRefinementFavorites();
            EnsureCurrentModelInList(AvailableRefinementModels, SelectedRefinementModelString);
            OnPropertyChanged(nameof(SelectedRefinementModelString));
        }

        public void ToggleSttFavorite(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return;

            var favorites = GetSttFavoriteList();
            if (favorites == null) return;

            ToggleFavoriteEntry(favorites, modelId.Trim());
            UpdateSttModelList();
            ConfigManager.Save();
        }

        public void ToggleRefinementFavorite(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return;

            var favorites = GetRefinementFavoriteList();
            if (favorites == null) return;

            ToggleFavoriteEntry(favorites, modelId.Trim());
            UpdateRefinementModelList();
            ConfigManager.Save();
        }

        public bool IsSttModelFavorite(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return false;
            var favorites = GetSttFavoriteList();
            return favorites != null && favorites.Any(x => string.Equals(x, modelId, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsRefinementModelFavorite(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return false;
            var favorites = GetRefinementFavoriteList();
            return favorites != null && favorites.Any(x => string.Equals(x, modelId, StringComparison.OrdinalIgnoreCase));
        }

        private void PrioritizeSttFavorites()
        {
            PrioritizeFavorites(AvailableSttModels, GetSttFavoriteList());
        }

        private void PrioritizeRefinementFavorites()
        {
            PrioritizeFavorites(AvailableRefinementModels, GetRefinementFavoriteList());
        }

        private List<string>? GetSttFavoriteList()
        {
            return SttModel switch
            {
                "Deepgram" => ConfigManager.Config.DeepgramFavoriteModels ??= new List<string>(),
                "OpenAI" => ConfigManager.Config.OpenAISttFavoriteModels ??= new List<string>(),
                _ => null
            };
        }

        private List<string>? GetRefinementFavoriteList()
        {
            return RefinementModel switch
            {
                "OpenAI" => ConfigManager.Config.OpenAIRefinementFavoriteModels ??= new List<string>(),
                "Cerebras" => ConfigManager.Config.CerebrasRefinementFavoriteModels ??= new List<string>(),
                "OpenRouter" => ConfigManager.Config.OpenRouterFavoriteModels ??= new List<string>(),
                _ => null
            };
        }

        private static void ToggleFavoriteEntry(List<string> favorites, string modelId)
        {
            int existingIndex = favorites.FindIndex(x => string.Equals(x, modelId, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                favorites.RemoveAt(existingIndex);
            }
            else
            {
                favorites.Insert(0, modelId);
            }
        }

        private static void PrioritizeFavorites(ObservableCollection<string> models, List<string>? favorites)
        {
            if (models.Count == 0 || favorites == null || favorites.Count == 0) return;

            var current = models.ToList();
            var favoriteSet = new HashSet<string>(favorites, StringComparer.OrdinalIgnoreCase);

            var preferred = favorites
                .Where(f => current.Contains(f, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rest = current
                .Where(m => !favoriteSet.Contains(m))
                .ToList();

            models.Clear();
            foreach (var model in preferred)
            {
                models.Add(model);
            }

            foreach (var model in rest)
            {
                models.Add(model);
            }
        }

        private static void EnsureCurrentModelInList(ObservableCollection<string> target, string currentModel)
        {
            if (string.IsNullOrWhiteSpace(currentModel)) return;
            if (!target.Contains(currentModel))
            {
                target.Insert(0, currentModel);
            }
        }

        private bool TryApplyDynamicModels(ObservableCollection<string> target, Dictionary<string, List<string>> catalog, string provider)
        {
            if (!catalog.TryGetValue(provider, out var models) || models.Count == 0)
            {
                return false;
            }

            foreach (var model in models)
            {
                target.Add(model);
            }

            return true;
        }

        private async Task RefreshProviderModelsAsync(bool userInitiated = false)
        {
            if (IsRefreshingModels)
            {
                return;
            }

            IsRefreshingModels = true;
            ModelRefreshStatus = "Refreshing provider models...";

            try
            {
                var config = ConfigManager.Config;

                if (!string.IsNullOrWhiteSpace(config.DeepgramApiKey))
                {
                    var deepgramModels = await FetchDeepgramModelsAsync(config.DeepgramApiKey.Trim());
                    if (deepgramModels.Count > 0)
                    {
                        _dynamicSttModels["Deepgram"] = deepgramModels;
                    }
                }

                if (!string.IsNullOrWhiteSpace(config.OpenAIApiKey))
                {
                    var openAiModels = await FetchModelsFromEndpointAsync(
                        "https://api.openai.com/v1/models",
                        new AuthenticationHeaderValue("Bearer", config.OpenAIApiKey.Trim()));

                    var openAiSttModels = openAiModels
                        .Where(IsOpenAiSttModel)
                        .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var openAiRefinementModels = openAiModels
                        .Where(IsOpenAiRefinementModel)
                        .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (openAiSttModels.Count > 0)
                    {
                        _dynamicSttModels["OpenAI"] = openAiSttModels;
                    }

                    if (openAiRefinementModels.Count > 0)
                    {
                        _dynamicRefinementModels["OpenAI"] = openAiRefinementModels;
                    }
                }

                if (!string.IsNullOrWhiteSpace(config.CerebrasApiKey))
                {
                    var cerebrasModels = await FetchModelsFromEndpointAsync(
                        "https://api.cerebras.ai/v1/models",
                        new AuthenticationHeaderValue("Bearer", config.CerebrasApiKey.Trim()));

                    if (cerebrasModels.Count > 0)
                    {
                        _dynamicRefinementModels["Cerebras"] = cerebrasModels
                            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                }

                if (!string.IsNullOrWhiteSpace(config.OpenRouterApiKey))
                {
                    var openRouterRefinementModels = await FetchOpenRouterChatModelsAsync(config.OpenRouterApiKey.Trim());

                    if (openRouterRefinementModels.Count > 0)
                        _dynamicRefinementModels["OpenRouter"] = openRouterRefinementModels;
                }

                UpdateSttModelList();
                UpdateRefinementModelList();

                var activeSources = _dynamicSttModels.Keys
                    .Concat(_dynamicRefinementModels.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();

                ModelRefreshStatus = activeSources.Count > 0
                    ? $"Model list refreshed from: {string.Join(", ", activeSources)}"
                    : "Model list: using built-in defaults";

                if (userInitiated)
                {
                    RefreshModelsCompleted?.Invoke(true);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("RefreshProviderModelsAsync", ex);
                ModelRefreshStatus = "Model refresh failed — using available defaults";

                if (userInitiated)
                {
                    RefreshModelsCompleted?.Invoke(false);
                }
            }
            finally
            {
                IsRefreshingModels = false;
            }
        }

        /// <summary>
        /// Fetches OpenRouter chat/completion models suitable for text refinement.
        /// Only includes models whose architecture.modality ends with "->text",
        /// which excludes image-generation, embedding, and audio-only model types.
        /// Whisper/STT-class model IDs are also excluded since OpenRouter does not
        /// support audio transcription endpoints.
        /// </summary>
        private async Task<List<string>> FetchOpenRouterChatModelsAsync(string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadAsStringAsync();
            var refinementModels = new List<string>();

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                return refinementModels;

            foreach (var item in dataArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("id", out var idProp)) continue;
                var id = idProp.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;

                // Only include models whose output is text (excludes image-gen, embeddings, etc.)
                if (item.TryGetProperty("architecture", out var arch) && arch.ValueKind == JsonValueKind.Object)
                {
                    if (arch.TryGetProperty("modality", out var modalityProp))
                    {
                        var modality = modalityProp.GetString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(modality) &&
                            !modality.EndsWith("->text", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                }

                // Exclude whisper/audio-only model IDs — OpenRouter doesn't route audio endpoints
                if (IsOpenRouterSttModel(id)) continue;

                refinementModels.Add(id);
            }

            refinementModels.Sort(StringComparer.OrdinalIgnoreCase);
            return refinementModels;
        }

        private async Task<List<string>> FetchDeepgramModelsAsync(string apiKey)
        {
            var models = await FetchModelsFromEndpointAsync(
                "https://api.deepgram.com/v1/models",
                new AuthenticationHeaderValue("Token", apiKey));

            return models
                .Where(m => m.Contains("nova", StringComparison.OrdinalIgnoreCase) ||
                            m.Contains("whisper", StringComparison.OrdinalIgnoreCase) ||
                            m.Contains("flux", StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<string>> FetchModelsFromEndpointAsync(string url, AuthenticationHeaderValue authHeader)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = authHeader;

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return new List<string>();
            }

            var json = await response.Content.ReadAsStringAsync();
            return ExtractModelIds(json);
        }

        private static List<string> ExtractModelIds(string json)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                CollectModelIdsFromArray(dataArray, results);
            }

            if (root.TryGetProperty("models", out var modelsArray) && modelsArray.ValueKind == JsonValueKind.Array)
            {
                CollectModelIdsFromArray(modelsArray, results);
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                CollectModelIdsFromArray(root, results);
            }

            return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void CollectModelIdsFromArray(JsonElement arrayElement, HashSet<string> results)
        {
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) results.Add(value.Trim());
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object) continue;

                foreach (var propertyName in new[] { "id", "model", "name" })
                {
                    if (item.TryGetProperty(propertyName, out var valueElement) && valueElement.ValueKind == JsonValueKind.String)
                    {
                        var value = valueElement.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) results.Add(value.Trim());
                    }
                }
            }
        }

        private static bool IsOpenAiSttModel(string modelId)
        {
            return modelId.Contains("whisper", StringComparison.OrdinalIgnoreCase)
                   || modelId.Contains("transcribe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOpenRouterSttModel(string modelId)
        {
            // Match audio/speech/whisper-class models available via OpenRouter
            return modelId.Contains("whisper",    StringComparison.OrdinalIgnoreCase)
                || modelId.Contains("transcri",   StringComparison.OrdinalIgnoreCase)
                || modelId.Contains("audio",      StringComparison.OrdinalIgnoreCase)
                || modelId.Contains("speech",     StringComparison.OrdinalIgnoreCase)
                || modelId.Contains("asr",        StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOpenAiRefinementModel(string modelId)
        {
            var normalized = modelId.Trim().ToLowerInvariant();
            return normalized.StartsWith("gpt-")
                   || normalized.StartsWith("o1")
                   || normalized.StartsWith("o3")
                   || normalized.StartsWith("o4");
        }

        private async Task TestApiConnectionsAsync()
        {
            ApiTestStatus = "Testing...";
            var sb = new StringBuilder();

            sb.AppendLine("--- API TEST RESULTS ---");
            
            var dg = await ApiTester.TestDeepgramAsync(DeepgramApiKey);
            sb.AppendLine($"Deepgram: {dg}");

            var oa = await ApiTester.TestOpenAIAsync(OpenAIApiKey);
            sb.AppendLine($"OpenAI: {oa}");

            var cr = await ApiTester.TestCerebrasAsync(CerebrasApiKey);
            sb.AppendLine($"Cerebras: {cr}");

            var or = await ApiTester.TestOpenRouterAsync(OpenRouterApiKey);
            sb.AppendLine($"OpenRouter: {or}");

            ApiTestStatus = sb.ToString();
            ApiTestCompleted?.Invoke(dg, oa, cr, or);
            ApiTestStatus = "Ready";
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

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
        private string _lastInsertionMethod = "N/A";
        private string _lastInsertionStatus = "No insertion yet";
        private string _healthSummary = "Health checks not run yet.";
        private string _healthDetails = string.Empty;

        public event Action<string, string, string, string>? ApiTestCompleted;
        public event Action<bool>? RefreshModelsCompleted;

        public ICommand RefreshModelsCommand { get; }
        public ICommand SaveCurrentPromptCommand { get; }
        public ICommand DeleteSelectedPromptCommand { get; }
        public ICommand RecoverOverlayCommand { get; }
        public ICommand RunHealthCheckCommand { get; }
        public ICommand ToggleRefinementQuickCommand { get; }
        public ICommand CycleProfileCommand { get; }
        public ICommand SetProfileByIdCommand { get; }

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

        public string LastInsertionMethod
        {
            get => _lastInsertionMethod;
            private set
            {
                _lastInsertionMethod = value;
                OnPropertyChanged();
            }
        }

        public string LastInsertionStatus
        {
            get => _lastInsertionStatus;
            private set
            {
                _lastInsertionStatus = value;
                OnPropertyChanged();
            }
        }

        public string HealthSummary
        {
            get => _healthSummary;
            private set
            {
                _healthSummary = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasHealthDetails));
            }
        }

        public string HealthDetails
        {
            get => _healthDetails;
            private set
            {
                _healthDetails = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasHealthDetails));
            }
        }

        public bool HasHealthDetails => !string.IsNullOrWhiteSpace(HealthDetails);

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
                SyncActiveProfileFromLegacy();
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
                    "OpenRouter" => ConfigManager.Config.OpenRouterSttModel,
                    _ => ""
                };
            }
            set
            {
                var normalized = value?.Trim();
                if (string.IsNullOrWhiteSpace(normalized)) return;
                if (!IsInModelList(AvailableSttModels, normalized)) return;

                if (SttModel == "Deepgram") ConfigManager.Config.DeepgramModel = normalized;
                else if (SttModel == "OpenAI") ConfigManager.Config.OpenAISttModel = normalized;
                else if (SttModel == "OpenRouter") ConfigManager.Config.OpenRouterSttModel = normalized;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
            }
        }

        public bool EnableSttFailover
        {
            get => ConfigManager.Config.EnableSttFailover;
            set { ConfigManager.Config.EnableSttFailover = value; SyncActiveProfileFromLegacy(); OnPropertyChanged(); }
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
                if (!IsInModelList(AvailableRefinementModels, normalized)) return;

                if (RefinementModel == "OpenAI") ConfigManager.Config.OpenAIRefinementModel = normalized;
                else if (RefinementModel == "Cerebras") ConfigManager.Config.CerebrasRefinementModel = normalized;
                else if (RefinementModel == "OpenRouter") ConfigManager.Config.OpenRouterRefinementModel = normalized;
                SyncActiveProfileFromLegacy();
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
                SyncActiveProfileFromLegacy();
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
                SyncActiveProfileFromLegacy();
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
            set
            {
                ConfigManager.Config.SampleRate = Clamp(value, 8000, 48000);
                OnPropertyChanged();
            }
        }

        public int Channels
        {
            get => ConfigManager.Config.Channels;
            set
            {
                ConfigManager.Config.Channels = Clamp(value, 1, 2);
                OnPropertyChanged();
            }
        }

        public int ChunkSize
        {
            get => ConfigManager.Config.ChunkSize;
            set
            {
                ConfigManager.Config.ChunkSize = Clamp(value, 256, 32768);
                OnPropertyChanged();
            }
        }

        public bool SaveDebugRecords
        {
            get => ConfigManager.Config.SaveDebugRecords;
            set { ConfigManager.Config.SaveDebugRecords = value; OnPropertyChanged(); }
        }

        public bool EnableRefinement
        {
            get => ConfigManager.Config.EnableRefinement;
            set { ConfigManager.Config.EnableRefinement = value; SyncActiveProfileFromLegacy(); OnPropertyChanged(); }
        }

        public bool CopyToClipboard
        {
            get => ConfigManager.Config.CopyToClipboard;
            set { ConfigManager.Config.CopyToClipboard = value; SyncActiveProfileFromLegacy(); OnPropertyChanged(); }
        }

        public string Language
        {
            get => ConfigManager.Config.Language;
            set { ConfigManager.Config.Language = value; SyncActiveProfileFromLegacy(); OnPropertyChanged(); }
        }

        public ObservableCollection<HistoryEntry> HistoryEntries { get; } = new ObservableCollection<HistoryEntry>();
        public ObservableCollection<AppProfile> Profiles { get; } = new ObservableCollection<AppProfile>();

        private AppProfile? _selectedProfile;
        public AppProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                _selectedProfile = value;
                if (value != null)
                {
                    ConfigManager.SetActiveProfile(value.Id);
                    ApplyProfileToLegacyConfig(value);
                    RefreshFromConfig();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveProfileName));
            }
        }

        public string ActiveProfileName => SelectedProfile?.Name ?? "Default";

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

        private void LoadProfiles()
        {
            Profiles.Clear();
            foreach (var profile in ConfigManager.Config.Profiles)
            {
                Profiles.Add(profile);
            }

            var active = ConfigManager.GetActiveProfile();
            SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Id, active.Id, StringComparison.OrdinalIgnoreCase))
                              ?? Profiles.FirstOrDefault();
        }

        private void ApplyProfileToLegacyConfig(AppProfile profile)
        {
            ConfigManager.EnsureProfileSyncToLegacyFields(profile);
        }

        private void SyncActiveProfileFromLegacy()
        {
            var active = SelectedProfile ?? ConfigManager.GetActiveProfile();
            if (active == null) return;

            active.SttProvider = ConfigManager.Config.SttModel;
            active.SttModel = SelectedSttModelString;
            active.RefinementEnabled = ConfigManager.Config.EnableRefinement;
            active.RefinementProvider = ConfigManager.Config.RefinementModel;
            active.RefinementModel = SelectedRefinementModelString;
            active.RefinementPrompt = ConfigManager.Config.RefinementPrompt;
            active.Language = ConfigManager.Config.Language;
            active.CopyToClipboard = ConfigManager.Config.CopyToClipboard;
            active.EnableSttFailover = ConfigManager.Config.EnableSttFailover;
            active.SttFailoverOrder = ConfigManager.Config.SttFailoverOrder.ToList();
        }

        private void CycleProfile()
        {
            if (Profiles.Count == 0) return;
            int index = SelectedProfile == null ? 0 : Profiles.IndexOf(SelectedProfile);
            if (index < 0) index = 0;
            int next = (index + 1) % Profiles.Count;
            SelectedProfile = Profiles[next];
        }

        private void RefreshFromConfig()
        {
            UpdateSttModelList();
            UpdateRefinementModelList();
            OnPropertyChanged(nameof(SttModel));
            OnPropertyChanged(nameof(SelectedSttModelString));
            OnPropertyChanged(nameof(EnableRefinement));
            OnPropertyChanged(nameof(RefinementModel));
            OnPropertyChanged(nameof(SelectedRefinementModelString));
            OnPropertyChanged(nameof(RefinementPrompt));
            OnPropertyChanged(nameof(Language));
            OnPropertyChanged(nameof(CopyToClipboard));
            OnPropertyChanged(nameof(EnableSttFailover));
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

        public void SetLastInsertionStatus(string method, bool success, string errorCode)
        {
            LastInsertionMethod = string.IsNullOrWhiteSpace(method) ? "Unknown" : method;
            LastInsertionStatus = success
                ? $"OK ({LastInsertionMethod})"
                : $"Failed ({LastInsertionMethod}){(string.IsNullOrWhiteSpace(errorCode) ? string.Empty : $": {errorCode}")}";
        }

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
            LoadProfiles();
            LoadAudioDevices();
            UpdateSttModelList();
            UpdateRefinementModelList();
            _ = RefreshProviderModelsAsync();
            
            foreach (var entry in HistoryManager.GetHistory())
            {
                HistoryEntries.Add(entry);
            }

            ToggleRefinementQuickCommand = new RelayCommand(_ =>
            {
                EnableRefinement = !EnableRefinement;
            });

            CycleProfileCommand = new RelayCommand(_ => CycleProfile());

            SetProfileByIdCommand = new RelayCommand(obj =>
            {
                if (obj is string profileId)
                {
                    var match = Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
                    if (match != null) SelectedProfile = match;
                }
            });

            RecoverOverlayCommand = new RelayCommand(_ =>
            {
                App.SetOverlayVisible(true);
                App.RecoverOverlayPosition();
            });

            RunHealthCheckCommand = new RelayCommand(_ => RunHealthChecks());

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

            RunHealthChecks();

            PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(LastInsertionMethod) or nameof(LastInsertionStatus) or nameof(HealthSummary) or nameof(HealthDetails) or nameof(ApiTestStatus))
                    return;

                ConfigManager.SaveDebounced();
            };
        }

        private void LoadAudioDevices()
        {
            AvailableAudioDevices.Clear();
            AvailableAudioDevices.Add("Default");

            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var capabilities = WaveInEvent.GetCapabilities(i);
                var deviceName = capabilities.ProductName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(deviceName))
                {
                    deviceName = $"(Unnamed input device {i + 1})";
                }

                if (!AvailableAudioDevices.Any(x => string.Equals(x, deviceName, StringComparison.OrdinalIgnoreCase)))
                {
                    AvailableAudioDevices.Add(deviceName);
                }
            }

            if (string.IsNullOrWhiteSpace(ConfigManager.Config.AudioDevice))
            {
                ConfigManager.Config.AudioDevice = "Default";
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
            else if (SttModel == "OpenRouter")
            {
                AvailableSttModels.Add("openai/whisper-large-v3");
                AvailableSttModels.Add("openai/whisper-1");
                AvailableSttModels.Add("openai/whisper-large-v3-turbo");
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
            ConfigManager.SaveDebounced();
        }

        public void ToggleRefinementFavorite(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return;

            var favorites = GetRefinementFavoriteList();
            if (favorites == null) return;

            ToggleFavoriteEntry(favorites, modelId.Trim());
            UpdateRefinementModelList();
            ConfigManager.SaveDebounced();
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
                "OpenRouter" => ConfigManager.Config.OpenRouterSttFavoriteModels ??= new List<string>(),
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

        private static bool IsInModelList(ObservableCollection<string> models, string candidate)
        {
            return models.Any(m => string.Equals(m, candidate, StringComparison.OrdinalIgnoreCase));
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
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
                    var openRouterSttModels = await FetchOpenRouterSttModelsAsync(config.OpenRouterApiKey.Trim());
                    if (openRouterSttModels.Count > 0)
                    {
                        _dynamicSttModels["OpenRouter"] = openRouterSttModels;
                    }

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

                // Exclude whisper/audio-only model IDs — OpenRouter doesn't route audio endpoints
                if (IsOpenRouterSttModel(id)) continue;

                // Keep only refinement-capable text models. For OpenRouter, some entries
                // (including free variants) expose modality as "text" rather than "...->text".
                if (!IsOpenRouterTextRefinementModel(item, id)) continue;

                refinementModels.Add(id);
            }

            refinementModels.Sort(StringComparer.OrdinalIgnoreCase);
            return refinementModels;
        }

        private async Task<List<string>> FetchOpenRouterSttModelsAsync(string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadAsStringAsync();
            var sttModels = new List<string>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                return sttModels;

            foreach (var item in dataArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String) continue;

                var id = idProp.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!IsOpenRouterSttModel(id)) continue;

                sttModels.Add(id);
            }

            sttModels.Sort(StringComparer.OrdinalIgnoreCase);
            return sttModels;
        }

        private static bool IsOpenRouterTextRefinementModel(JsonElement item, string modelId)
        {
            var isFree = modelId.Contains(":free", StringComparison.OrdinalIgnoreCase);

            if (!item.TryGetProperty("architecture", out var arch) || arch.ValueKind != JsonValueKind.Object)
            {
                // If modality is missing, keep behavior permissive.
                return true;
            }

            if (!arch.TryGetProperty("modality", out var modalityProp) || modalityProp.ValueKind != JsonValueKind.String)
            {
                return true;
            }

            var modality = modalityProp.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(modality))
            {
                return true;
            }

            // Common OpenRouter forms: "text->text", "image+text->text", and sometimes plain "text".
            if (modality.EndsWith("->text", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(modality, "text", StringComparison.OrdinalIgnoreCase)) return true;

            // For free variants, be a bit more permissive if text is present at all.
            if (isFree && modality.Contains("text", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
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

        public void RunHealthChecks()
        {
            var result = HealthCheckService.Run(ConfigManager.Config);
            HealthSummary = result.Summary;
            HealthDetails = result.Details;
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

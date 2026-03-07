using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
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
        private string _pendingTransferStatus = "No pending auto-paste.";
        private string _healthSummary = "Health checks not run yet.";
        private string _healthDetails = string.Empty;
        private string _newProfileName = string.Empty;
        private string _profileDraftName = string.Empty;
        private string _profileProcessNamesInput = string.Empty;
        private string _profileStatusMessage = "Tip: create additional profiles and map them to process names (for example: code, notepad).";
        private string _currentTargetProcessStatus = "Current app: (not detected)";
        private string _matchedProfileForTargetStatus = "Matched profile: (none)";
        private bool _isCaptureProfileProcessInProgress;
        private bool _isApplyingPromptPresetFromProfile;
        private string _globalDictionaryTermsText = string.Empty;
        private string _profileDictionaryTermsText = string.Empty;
        private string _selectedDictionarySuggestion = string.Empty;
        private string _selectedCorrectionSuggestionKey = string.Empty;
        private string _newSnippetTrigger = string.Empty;
        private string _newSnippetReplacement = string.Empty;
        private string _lastContextUsageStatus = "Context used: none yet.";
        private static readonly string[] DeepgramMultilingualCodes =
        {
            "en", "es", "fr", "de", "hi", "ru", "pt", "ja", "it", "nl"
        };

        public event Action<string, string, string, string>? ApiTestCompleted;
        public event Action<bool>? RefreshModelsCompleted;

        public ICommand RefreshModelsCommand { get; }
        public ICommand SaveCurrentPromptCommand { get; }
        public ICommand DeleteSelectedPromptCommand { get; }
        public ICommand RunHealthCheckCommand { get; }
        public ICommand ToggleRefinementQuickCommand { get; }
        public ICommand CycleProfileCommand { get; }
        public ICommand CycleDictationModeCommand { get; }
        public ICommand SetDictationModeCommand { get; }
        public ICommand SetProfileByIdCommand { get; }
        public ICommand CreateProfileCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand CaptureProfileProcessFromCurrentAppCommand { get; }
        public ICommand RefreshTargetProfileMatchCommand { get; }
        public ICommand OpenDebugLogsCommand { get; }
        public ICommand ClearPendingTransferCommand { get; }
        public ICommand AddSuggestionToGlobalDictionaryCommand { get; }
        public ICommand AddSuggestionToProfileDictionaryCommand { get; }
        public ICommand DismissDictionarySuggestionCommand { get; }
        public ICommand ClearDictionarySuggestionsCommand { get; }
        public ICommand ImportGlobalDictionaryCommand { get; }
        public ICommand ExportGlobalDictionaryCommand { get; }
        public ICommand CopyHistoryCommand { get; }
        public ICommand CopyOriginalHistoryCommand { get; }
        public ICommand RetryHistoryInsertCommand { get; }
        public ICommand ReprocessHistoryEntryCommand { get; }
        public ICommand TogglePinnedHistoryCommand { get; }
        public ICommand SaveSnippetCommand { get; }
        public ICommand DeleteSelectedSnippetCommand { get; }
        public ICommand AddCorrectionSuggestionToDictionaryCommand { get; }
        public ICommand AddCorrectionSuggestionToSnippetCommand { get; }
        public ICommand DismissCorrectionSuggestionCommand { get; }
        public ICommand ClearCorrectionSuggestionsCommand { get; }

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
                OnPropertyChanged(nameof(IsOpenRouterSttProvider));
                NotifyDeepgramLanguageGuardChanged();
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
        public ObservableCollection<string> AvailableOverlayStyles { get; } = new ObservableCollection<string>
        {
            "Rectangular",
            "Circle"
        };
        public ObservableCollection<string> AvailableDictationModes { get; } = new ObservableCollection<string>(
            DictationExperienceService.GetAvailableModes());
        public ObservableCollection<string> AvailableVoiceCommandModes { get; } = new ObservableCollection<string>(
            DictationExperienceService.GetAvailableVoiceCommandModes());
        public ObservableCollection<string> AvailableContextualRefinementModes { get; } = new ObservableCollection<string>(
            DictationExperienceService.GetAvailableContextualRefinementModes());
        public ObservableCollection<string> AvailableStylePresets { get; } = new ObservableCollection<string>(
            DictationExperienceService.GetAvailableStylePresets());
        public ObservableCollection<string> AvailableTelemetryLevels { get; } = new ObservableCollection<string>
        {
            "minimal",
            "normal",
            "verbose"
        };
        public ObservableCollection<string> AvailableTelemetryRedactionModes { get; } = new ObservableCollection<string>
        {
            "strict",
            "hash",
            "off"
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
                NotifyDeepgramLanguageGuardChanged();
            }
        }

        public bool EnableSttFailover
        {
            get => ConfigManager.Config.EnableSttFailover;
            set { ConfigManager.Config.EnableSttFailover = value; SyncActiveProfileFromLegacy(); OnPropertyChanged(); }
        }

        public bool IsOpenRouterSttProvider =>
            string.Equals(SttModel, "OpenRouter", StringComparison.OrdinalIgnoreCase);

        public bool OpenRouterSttShowAllModels
        {
            get => ConfigManager.Config.OpenRouterSttShowAllModels;
            set
            {
                if (ConfigManager.Config.OpenRouterSttShowAllModels == value)
                {
                    return;
                }

                ConfigManager.Config.OpenRouterSttShowAllModels = value;
                OnPropertyChanged();
                _ = RefreshProviderModelsAsync(true);
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

        public string DeepgramApiBaseUrl
        {
            get => ConfigManager.Config.DeepgramApiBaseUrl;
            set
            {
                ConfigManager.Config.DeepgramApiBaseUrl = NormalizeDeepgramApiBaseUrl(value);
                OnPropertyChanged();
            }
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
                OnPropertyChanged(nameof(IsCerebrasRefinementProvider));
            }
        }

        public bool IsCerebrasRefinementProvider =>
            string.Equals(RefinementModel, "Cerebras", StringComparison.OrdinalIgnoreCase);

        public int CerebrasMaxCompletionTokens
        {
            get => ConfigManager.Config.CerebrasMaxCompletionTokens;
            set
            {
                ConfigManager.Config.CerebrasMaxCompletionTokens = Clamp(value, 16, 65536);
                OnPropertyChanged();
            }
        }

        public int CerebrasTimeoutSeconds
        {
            get => ConfigManager.Config.CerebrasTimeoutSeconds;
            set
            {
                ConfigManager.Config.CerebrasTimeoutSeconds = Clamp(value, 10, 300);
                OnPropertyChanged();
            }
        }

        public int CerebrasMaxRetries
        {
            get => ConfigManager.Config.CerebrasMaxRetries;
            set
            {
                ConfigManager.Config.CerebrasMaxRetries = Clamp(value, 0, 6);
                OnPropertyChanged();
            }
        }

        public int CerebrasRetryBaseDelayMs
        {
            get => ConfigManager.Config.CerebrasRetryBaseDelayMs;
            set
            {
                ConfigManager.Config.CerebrasRetryBaseDelayMs = Clamp(value, 100, 5000);
                OnPropertyChanged();
            }
        }

        public string CerebrasVersionPatch
        {
            get => ConfigManager.Config.CerebrasVersionPatch;
            set
            {
                ConfigManager.Config.CerebrasVersionPatch = (value ?? string.Empty).Trim();
                OnPropertyChanged();
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
                OnPropertyChanged(nameof(PromptStyleConflictWarning));
                OnPropertyChanged(nameof(HasPromptStyleConflictWarning));
            }
        }

        public bool IsRefinementPromptMissing => string.IsNullOrWhiteSpace(RefinementPrompt);
        public string RefinementTabHeader => IsRefinementPromptMissing ? "Refinement ⚠" : "Refinement";
        public string PromptStyleConflictWarning =>
            DictationExperienceService.GetPromptStyleConflictWarning(RefinementPrompt, StylePreset, CustomStylePrompt);
        public bool HasPromptStyleConflictWarning => !string.IsNullOrWhiteSpace(PromptStyleConflictWarning);

        public bool MinimizeToTray
        {
            get => ConfigManager.Config.MinimizeToTray;
            set { ConfigManager.Config.MinimizeToTray = value; OnPropertyChanged(); }
        }

        public string DictationMode
        {
            get => DictationExperienceService.NormalizeMode(ConfigManager.Config.DictationMode);
            set
            {
                var normalized = DictationExperienceService.NormalizeMode(value);
                ConfigManager.Config.DictationMode = normalized;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveDictationModeTitle));
                OnPropertyChanged(nameof(ActiveDictationModeDescription));
                NotifySelectedProfileSummaryChanged();
            }
        }

        public string ActiveDictationModeTitle => $"Mode: {DictationMode}";

        public string ActiveDictationModeDescription => DictationExperienceService.DescribeMode(DictationMode);

        public string StylePreset
        {
            get => DictationExperienceService.NormalizeStylePreset(ConfigManager.Config.StylePreset);
            set
            {
                var normalized = DictationExperienceService.NormalizeStylePreset(value);
                ConfigManager.Config.StylePreset = normalized;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                OnPropertyChanged(nameof(StylePresetDescription));
                OnPropertyChanged(nameof(IsCustomStylePreset));
                OnPropertyChanged(nameof(PromptStyleConflictWarning));
                OnPropertyChanged(nameof(HasPromptStyleConflictWarning));
                NotifySelectedProfileSummaryChanged();
            }
        }

        public string StylePresetDescription => DictationExperienceService.DescribeStylePreset(StylePreset);

        public bool IsCustomStylePreset => string.Equals(
            StylePreset,
            DictationExperienceService.StylePresetCustom,
            StringComparison.OrdinalIgnoreCase);

        public string CustomStylePrompt
        {
            get => ConfigManager.Config.CustomStylePrompt;
            set
            {
                ConfigManager.Config.CustomStylePrompt = value ?? string.Empty;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                OnPropertyChanged(nameof(PromptStyleConflictWarning));
                OnPropertyChanged(nameof(HasPromptStyleConflictWarning));
                NotifySelectedProfileSummaryChanged();
            }
        }

        public string ContextConfigurationSummary =>
            !EnableRefinement
                ? "Context: Refinement disabled"
                : $"{DictationExperienceService.DescribeContextUsage(ConfigManager.Config)} | {DictationExperienceService.DescribeContextualRefinementMode(ConfigManager.Config.ContextualRefinementMode)}";

        public string LastContextUsageStatus
        {
            get => _lastContextUsageStatus;
            private set
            {
                if (string.Equals(_lastContextUsageStatus, value, StringComparison.Ordinal))
                    return;
                _lastContextUsageStatus = value;
                OnPropertyChanged();
            }
        }

        public bool EnableVoiceCommands
        {
            get => ConfigManager.Config.EnableVoiceCommands;
            set
            {
                ConfigManager.Config.EnableVoiceCommands = value;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                NotifySelectedProfileSummaryChanged();
            }
        }

        public string VoiceCommandMode
        {
            get => DictationExperienceService.NormalizeVoiceCommandMode(ConfigManager.Config.VoiceCommandMode);
            set
            {
                var normalized = DictationExperienceService.NormalizeVoiceCommandMode(value);
                ConfigManager.Config.VoiceCommandMode = normalized;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                NotifySelectedProfileSummaryChanged();
            }
        }

        public bool UseAppContextForRefinement
        {
            get => ConfigManager.Config.UseAppContextForRefinement;
            set
            {
                ConfigManager.Config.UseAppContextForRefinement = value;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ContextConfigurationSummary));
                NotifySelectedProfileSummaryChanged();
            }
        }

        public string ContextualRefinementMode
        {
            get => DictationExperienceService.NormalizeContextualRefinementMode(ConfigManager.Config.ContextualRefinementMode);
            set
            {
                var normalized = DictationExperienceService.NormalizeContextualRefinementMode(value);
                ConfigManager.Config.ContextualRefinementMode = normalized;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ContextConfigurationSummary));
                NotifySelectedProfileSummaryChanged();
            }
        }

        public bool UseWindowTitleContextForRefinement
        {
            get => ConfigManager.Config.UseWindowTitleContextForRefinement;
            set
            {
                ConfigManager.Config.UseWindowTitleContextForRefinement = value;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ContextConfigurationSummary));
                NotifySelectedProfileSummaryChanged();
            }
        }

        public bool UseSelectedTextContextForRefinement
        {
            get => ConfigManager.Config.UseSelectedTextContextForRefinement;
            set
            {
                ConfigManager.Config.UseSelectedTextContextForRefinement = value;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ContextConfigurationSummary));
                NotifySelectedProfileSummaryChanged();
            }
        }

        public bool UseClipboardContextForRefinement
        {
            get => ConfigManager.Config.UseClipboardContextForRefinement;
            set
            {
                ConfigManager.Config.UseClipboardContextForRefinement = value;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ContextConfigurationSummary));
                NotifySelectedProfileSummaryChanged();
            }
        }

        public bool EnableSnippets
        {
            get => ConfigManager.Config.EnableSnippets;
            set
            {
                ConfigManager.Config.EnableSnippets = value;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
            }
        }

        public bool LearnFromRefinementCorrections
        {
            get => ConfigManager.Config.LearnFromRefinementCorrections;
            set
            {
                ConfigManager.Config.LearnFromRefinementCorrections = value;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
            }
        }

        public bool StartWithWindows
        {
            get => ConfigManager.Config.StartWithWindows;
            set
            {
                if (ConfigManager.Config.StartWithWindows == value)
                {
                    return;
                }

                if (!App.SetStartWithWindowsEnabled(value))
                {
                    OnPropertyChanged();
                    return;
                }

                OnPropertyChanged();
            }
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

        public bool OverlayAutoHideEnabled
        {
            get => ConfigManager.Config.OverlayAutoHideEnabled;
            set
            {
                ConfigManager.Config.OverlayAutoHideEnabled = value;
                App.SetOverlayAutoHideEnabled(value);
                OnPropertyChanged();
            }
        }

        public bool DeferredTargetPasteEnabled
        {
            get => ConfigManager.Config.DeferredTargetPasteEnabled;
            set
            {
                ConfigManager.Config.DeferredTargetPasteEnabled = value;
                App.SetDeferredTargetPasteEnabled(value);
                OnPropertyChanged();
            }
        }

        public bool AutoMicGainEnabled
        {
            get => ConfigManager.Config.AutoMicGainEnabled;
            set
            {
                ConfigManager.Config.AutoMicGainEnabled = value;
                OnPropertyChanged();
            }
        }

        public bool DynamicNormalizationEnabled
        {
            get => ConfigManager.Config.DynamicNormalizationEnabled;
            set
            {
                ConfigManager.Config.DynamicNormalizationEnabled = value;
                OnPropertyChanged();
            }
        }

        public bool NoiseGateEnabled
        {
            get => ConfigManager.Config.NoiseGateEnabled;
            set
            {
                ConfigManager.Config.NoiseGateEnabled = value;
                OnPropertyChanged();
            }
        }

        public int NoiseGateThresholdDb
        {
            get => ConfigManager.Config.NoiseGateThresholdDb;
            set
            {
                ConfigManager.Config.NoiseGateThresholdDb = Clamp(value, -80, -10);
                OnPropertyChanged();
            }
        }

        public double AutoMicGainTargetRms
        {
            get => ConfigManager.Config.AutoMicGainTargetRms;
            set
            {
                ConfigManager.Config.AutoMicGainTargetRms = Math.Clamp(value, 0.02, 0.4);
                OnPropertyChanged();
            }
        }

        public double NormalizationTargetPeak
        {
            get => ConfigManager.Config.NormalizationTargetPeak;
            set
            {
                ConfigManager.Config.NormalizationTargetPeak = Math.Clamp(value, 0.2, 0.99);
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

        public bool TelemetryEnabled
        {
            get => ConfigManager.Config.TelemetryEnabled;
            set { ConfigManager.Config.TelemetryEnabled = value; OnPropertyChanged(); }
        }

        public string TelemetryLevel
        {
            get => ConfigManager.Config.TelemetryLevel;
            set
            {
                var normalized = value?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(normalized)) normalized = "normal";
                if (!AvailableTelemetryLevels.Contains(normalized)) normalized = "normal";
                ConfigManager.Config.TelemetryLevel = normalized;
                OnPropertyChanged();
            }
        }

        public int TelemetryRetentionDays
        {
            get => ConfigManager.Config.TelemetryRetentionDays;
            set
            {
                ConfigManager.Config.TelemetryRetentionDays = Math.Clamp(value, 1, 3650);
                OnPropertyChanged();
            }
        }

        public int TelemetryMaxFileMb
        {
            get => ConfigManager.Config.TelemetryMaxFileMb;
            set
            {
                ConfigManager.Config.TelemetryMaxFileMb = Math.Clamp(value, 1, 512);
                OnPropertyChanged();
            }
        }

        public string TelemetryRedactionMode
        {
            get => ConfigManager.Config.TelemetryRedactionMode;
            set
            {
                var normalized = value?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(normalized)) normalized = "strict";
                if (!AvailableTelemetryRedactionModes.Contains(normalized)) normalized = "strict";
                ConfigManager.Config.TelemetryRedactionMode = normalized;
                OnPropertyChanged();
            }
        }

        public bool EnableRefinement
        {
            get => ConfigManager.Config.EnableRefinement;
            set
            {
                ConfigManager.Config.EnableRefinement = value;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ContextConfigurationSummary));
            }
        }

        public bool CopyToClipboard
        {
            get => ConfigManager.Config.CopyToClipboard;
            set { ConfigManager.Config.CopyToClipboard = value; SyncActiveProfileFromLegacy(); OnPropertyChanged(); }
        }

        public string Language
        {
            get => ConfigManager.Config.Language;
            set
            {
                ConfigManager.Config.Language = value;
                SyncActiveProfileFromLegacy();
                OnPropertyChanged();
                NotifyDeepgramLanguageGuardChanged();
            }
        }

        public string GlobalDictionaryTermsText
        {
            get => _globalDictionaryTermsText;
            set
            {
                _globalDictionaryTermsText = value ?? string.Empty;
                ConfigManager.Config.PersonalDictionaryGlobal = PersonalDictionaryService.ParseTerms(_globalDictionaryTermsText);
                OnPropertyChanged();
            }
        }

        public string ProfileDictionaryTermsText
        {
            get => _profileDictionaryTermsText;
            set
            {
                _profileDictionaryTermsText = value ?? string.Empty;
                var active = SelectedProfile ?? ConfigManager.GetActiveProfile();
                if (active != null)
                {
                    active.DictionaryTerms = PersonalDictionaryService.ParseTerms(_profileDictionaryTermsText);
                }
                OnPropertyChanged();
            }
        }

        public string SelectedDictionarySuggestion
        {
            get => _selectedDictionarySuggestion;
            set
            {
                _selectedDictionarySuggestion = value ?? string.Empty;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasDictionarySuggestions => DictionarySuggestions.Count > 0;
        public bool HasCorrectionSuggestions => CorrectionSuggestions.Count > 0;

        public string DeepgramLanguageGuardMessage => BuildDeepgramLanguageGuardMessage();

        public bool HasDeepgramLanguageGuard => !string.IsNullOrWhiteSpace(DeepgramLanguageGuardMessage);

        public ObservableCollection<HistoryEntry> HistoryEntries { get; } = new ObservableCollection<HistoryEntry>();
        public ObservableCollection<AppProfile> Profiles { get; } = new ObservableCollection<AppProfile>();
        public ObservableCollection<string> DictionarySuggestions { get; } = new ObservableCollection<string>();
        public ObservableCollection<SnippetEntry> SavedSnippets { get; } = new ObservableCollection<SnippetEntry>();
        public ObservableCollection<CorrectionSuggestionEntry> CorrectionSuggestions { get; } = new ObservableCollection<CorrectionSuggestionEntry>();

        private SnippetEntry? _selectedSnippetEntry;
        public SnippetEntry? SelectedSnippetEntry
        {
            get => _selectedSnippetEntry;
            set
            {
                if (ReferenceEquals(_selectedSnippetEntry, value))
                    return;

                _selectedSnippetEntry = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDeleteSelectedSnippet));

                if (value != null)
                {
                    NewSnippetTrigger = value.Trigger;
                    NewSnippetReplacement = value.Replacement;
                }

                CommandManager.InvalidateRequerySuggested();
            }
        }

        private CorrectionSuggestionEntry? _selectedCorrectionSuggestion;
        public CorrectionSuggestionEntry? SelectedCorrectionSuggestion
        {
            get => _selectedCorrectionSuggestion;
            set
            {
                _selectedCorrectionSuggestion = value;
                _selectedCorrectionSuggestionKey = value == null
                    ? string.Empty
                    : BuildCorrectionSuggestionKey(value);
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string NewSnippetTrigger
        {
            get => _newSnippetTrigger;
            set
            {
                _newSnippetTrigger = value ?? string.Empty;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string NewSnippetReplacement
        {
            get => _newSnippetReplacement;
            set
            {
                _newSnippetReplacement = value ?? string.Empty;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool CanDeleteSelectedSnippet => _selectedSnippetEntry != null;

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
                    ApplyPromptPresetForProfile(value);
                    LoadProfileEditorFields(value);
                    ProfileStatusMessage = $"Active profile: {value.Name}";
                    RefreshTargetProfileMatch();
                    TelemetryManager.Track(
                        name: "profile_switch",
                        result: "ok",
                        data: new Dictionary<string, string>
                        {
                            ["profile_id"] = value.Id,
                            ["profile_name"] = value.Name
                        });
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveProfileName));
                OnPropertyChanged(nameof(CanDeleteSelectedProfile));
                OnPropertyChanged(nameof(CanCycleProfile));
                OnPropertyChanged(nameof(ActiveDictationModeTitle));
                OnPropertyChanged(nameof(ActiveDictationModeDescription));
                NotifySelectedProfileSummaryChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ActiveProfileName => SelectedProfile?.Name ?? "Default";

        public string NewProfileName
        {
            get => _newProfileName;
            set
            {
                _newProfileName = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ProfileDraftName
        {
            get => _profileDraftName;
            set
            {
                _profileDraftName = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ProfileProcessNamesInput
        {
            get => _profileProcessNamesInput;
            set
            {
                _profileProcessNamesInput = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ProfileStatusMessage
        {
            get => _profileStatusMessage;
            private set
            {
                _profileStatusMessage = value;
                OnPropertyChanged();
            }
        }

        public string CurrentTargetProcessStatus
        {
            get => _currentTargetProcessStatus;
            private set
            {
                if (string.Equals(_currentTargetProcessStatus, value, StringComparison.Ordinal))
                    return;
                _currentTargetProcessStatus = value;
                OnPropertyChanged();
            }
        }

        public string MatchedProfileForTargetStatus
        {
            get => _matchedProfileForTargetStatus;
            private set
            {
                if (string.Equals(_matchedProfileForTargetStatus, value, StringComparison.Ordinal))
                    return;
                _matchedProfileForTargetStatus = value;
                OnPropertyChanged();
            }
        }

        public string SelectedProfileSttSummary
        {
            get
            {
                var profile = SelectedProfile ?? ConfigManager.GetActiveProfile();
                if (profile == null) return "STT: n/a";
                return $"STT: {profile.SttProvider} / {profile.SttModel}";
            }
        }

        public string SelectedProfileRefinementSummary
        {
            get
            {
                var profile = SelectedProfile ?? ConfigManager.GetActiveProfile();
                if (profile == null) return "Refinement: n/a";
                if (!profile.RefinementEnabled) return "Refinement: Disabled";
                return $"Refinement: {profile.RefinementProvider} / {profile.RefinementModel}";
            }
        }

        public string SelectedProfileRuntimeSummary
        {
            get
            {
                var profile = SelectedProfile ?? ConfigManager.GetActiveProfile();
                if (profile == null) return "Language: n/a | Clipboard: n/a | Failover: n/a";
                return $"Language: {profile.Language} | Clipboard: {(profile.CopyToClipboard ? "On" : "Off")} | STT failover: {(profile.EnableSttFailover ? "On" : "Off")}";
            }
        }

        public string SelectedProfileModeSummary
        {
            get
            {
                var profile = SelectedProfile ?? ConfigManager.GetActiveProfile();
                if (profile == null)
                {
                    return "Mode: n/a | Commands: n/a | Context: n/a";
                }

                var commands = profile.EnableVoiceCommands
                    ? profile.VoiceCommandMode
                    : "Off";
                var context = DictationExperienceService.DescribeContextUsage(new AppConfig
                {
                    ContextualRefinementMode = profile.ContextualRefinementMode,
                    UseAppContextForRefinement = profile.UseAppContextForRefinement,
                    UseWindowTitleContextForRefinement = profile.UseWindowTitleContextForRefinement,
                    UseSelectedTextContextForRefinement = profile.UseSelectedTextContextForRefinement,
                    UseClipboardContextForRefinement = profile.UseClipboardContextForRefinement
                });
                var contextMode = DictationExperienceService.DescribeContextualRefinementMode(profile.ContextualRefinementMode);
                var style = DictationExperienceService.DescribeStyleSummary(profile.StylePreset, profile.CustomStylePrompt);
                return $"Mode: {profile.DictationMode} | {style} | Commands: {commands} | {context} | {contextMode}";
            }
        }

        public string SelectedProfileDictionarySummary
        {
            get
            {
                var profile = SelectedProfile ?? ConfigManager.GetActiveProfile();
                if (profile == null) return "Dictionary terms: 0 | Mapped apps: 0";
                return $"Dictionary terms: {profile.DictionaryTerms.Count} | Mapped apps: {profile.ProcessNames.Count}";
            }
        }

        public bool CanDeleteSelectedProfile => Profiles.Count > 1 && SelectedProfile != null;
        public bool CanCycleProfile => Profiles.Count > 1;

        // ── Prompt Library ────────────────────────────────────────────────────

        public ObservableCollection<PromptEntry> SavedPrompts { get; } = new ObservableCollection<PromptEntry>();

        private PromptEntry? _selectedPromptEntry;
        public PromptEntry? SelectedPromptEntry
        {
            get => _selectedPromptEntry;
            set
            {
                if (ReferenceEquals(_selectedPromptEntry, value))
                    return;

                _selectedPromptEntry = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDeleteSelectedPrompt));

                var active = SelectedProfile ?? ConfigManager.GetActiveProfile();
                if (value != null)
                {
                    if (!_isApplyingPromptPresetFromProfile && active != null)
                        active.PromptPresetName = value.Name;
                    RefinementPrompt = value.Text;
                    return;
                }

                if (!_isApplyingPromptPresetFromProfile && active != null)
                {
                    active.PromptPresetName = string.Empty;
                }
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
        private List<SnippetEntry> _snippetList = new();

        private void ReloadSavedPrompts()
        {
            _promptList = PromptLibraryManager.Load();
            SavedPrompts.Clear();
            foreach (var p in _promptList)
                SavedPrompts.Add(p);

            var active = SelectedProfile ?? ConfigManager.GetActiveProfile();
            if (active != null)
            {
                ApplyPromptPresetForProfile(active);
                if (SelectedPromptEntry != null || !string.IsNullOrWhiteSpace(active.RefinementPrompt))
                    return;
            }

            if (SelectedPromptEntry == null || !SavedPrompts.Contains(SelectedPromptEntry))
            {
                _isApplyingPromptPresetFromProfile = true;
                try
                {
                    SelectedPromptEntry = FindPromptByName("General")
                        ?? SavedPrompts.FirstOrDefault();
                }
                finally
                {
                    _isApplyingPromptPresetFromProfile = false;
                }
            }
        }

        private void ReloadSavedSnippets()
        {
            _snippetList = SnippetLibraryManager.Load();
            SavedSnippets.Clear();
            foreach (var snippet in _snippetList)
            {
                SavedSnippets.Add(snippet);
            }

            if (SelectedSnippetEntry != null)
            {
                SelectedSnippetEntry = SavedSnippets.FirstOrDefault(entry =>
                    string.Equals(entry.Trigger, SelectedSnippetEntry.Trigger, StringComparison.OrdinalIgnoreCase));
            }
        }

        private PromptEntry? FindPromptByName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return SavedPrompts.FirstOrDefault(p =>
                string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private PromptEntry? FindPromptByText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return SavedPrompts.FirstOrDefault(p =>
                string.Equals(p.Text, text, StringComparison.Ordinal));
        }

        private void ApplyPromptPresetForProfile(AppProfile profile)
        {
            if (profile == null)
                return;

            PromptEntry? target = null;
            if (!string.IsNullOrWhiteSpace(profile.PromptPresetName))
                target = FindPromptByName(profile.PromptPresetName);

            target ??= FindPromptByText(profile.RefinementPrompt);

            if (target == null && string.IsNullOrWhiteSpace(profile.RefinementPrompt))
                target = FindPromptByName("General") ?? SavedPrompts.FirstOrDefault();

            _isApplyingPromptPresetFromProfile = true;
            try
            {
                SelectedPromptEntry = target;
            }
            finally
            {
                _isApplyingPromptPresetFromProfile = false;
            }

            if (target != null)
                profile.PromptPresetName = target.Name;
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
            OnPropertyChanged(nameof(CanDeleteSelectedProfile));
            OnPropertyChanged(nameof(CanCycleProfile));
        }

        private void ReloadProfiles(string? selectedProfileId = null)
        {
            Profiles.Clear();
            foreach (var profile in ConfigManager.Config.Profiles)
            {
                Profiles.Add(profile);
            }

            var targetId = string.IsNullOrWhiteSpace(selectedProfileId)
                ? ConfigManager.Config.ActiveProfileId
                : selectedProfileId.Trim();

            SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Id, targetId, StringComparison.OrdinalIgnoreCase))
                              ?? Profiles.FirstOrDefault();
            OnPropertyChanged(nameof(CanDeleteSelectedProfile));
            OnPropertyChanged(nameof(CanCycleProfile));
            CommandManager.InvalidateRequerySuggested();
        }

        private static string BuildProfileProcessDisplay(AppProfile profile)
        {
            if (profile.ProcessNames.Count == 0) return string.Empty;
            return string.Join(", ", profile.ProcessNames);
        }

        private void LoadProfileEditorFields(AppProfile profile)
        {
            ProfileDraftName = profile.Name;
            ProfileProcessNamesInput = BuildProfileProcessDisplay(profile);
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
            if (SelectedPromptEntry != null)
                active.PromptPresetName = SelectedPromptEntry.Name;
            active.DictationMode = DictationMode;
            active.StylePreset = StylePreset;
            active.CustomStylePrompt = CustomStylePrompt;
            active.Language = ConfigManager.Config.Language;
            active.CopyToClipboard = ConfigManager.Config.CopyToClipboard;
            active.DictionaryTerms = PersonalDictionaryService.ParseTerms(_profileDictionaryTermsText);
            active.EnableVoiceCommands = ConfigManager.Config.EnableVoiceCommands;
            active.VoiceCommandMode = VoiceCommandMode;
            active.ContextualRefinementMode = ContextualRefinementMode;
            active.UseAppContextForRefinement = ConfigManager.Config.UseAppContextForRefinement;
            active.UseWindowTitleContextForRefinement = ConfigManager.Config.UseWindowTitleContextForRefinement;
            active.UseSelectedTextContextForRefinement = ConfigManager.Config.UseSelectedTextContextForRefinement;
            active.UseClipboardContextForRefinement = ConfigManager.Config.UseClipboardContextForRefinement;
            active.EnableSnippets = ConfigManager.Config.EnableSnippets;
            active.LearnFromRefinementCorrections = ConfigManager.Config.LearnFromRefinementCorrections;
            active.EnableSttFailover = ConfigManager.Config.EnableSttFailover;
            active.SttFailoverOrder = ConfigManager.Config.SttFailoverOrder.ToList();
            NotifySelectedProfileSummaryChanged();
        }

        private void CycleProfile()
        {
            if (Profiles.Count == 0) return;
            if (Profiles.Count == 1)
            {
                ProfileStatusMessage = "Only one profile exists. Create another profile to cycle.";
                TelemetryManager.Track("profile_cycle", level: "warning", success: false, result: "single_profile");
                return;
            }

            int index = SelectedProfile == null ? 0 : Profiles.IndexOf(SelectedProfile);
            if (index < 0) index = 0;
            int next = (index + 1) % Profiles.Count;
            SelectedProfile = Profiles[next];
            ProfileStatusMessage = $"Active profile: {SelectedProfile?.Name ?? "Default"}";
            TelemetryManager.Track(
                name: "profile_cycle",
                result: "ok",
                data: new Dictionary<string, string>
                {
                    ["profile_id"] = SelectedProfile?.Id ?? string.Empty,
                    ["profile_name"] = SelectedProfile?.Name ?? string.Empty
                });
        }

        private void CreateProfile()
        {
            var baseProfile = SelectedProfile ?? ConfigManager.GetActiveProfile();
            var requestedName = NewProfileName?.Trim();

            var profileName = string.IsNullOrWhiteSpace(requestedName)
                ? $"Profile {Profiles.Count + 1}"
                : requestedName;

            if (ConfigManager.Config.Profiles.Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)))
            {
                ProfileStatusMessage = $"Profile \"{profileName}\" already exists.";
                return;
            }

            var newProfile = new AppProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = profileName,
                ProcessNames = new List<string>(),
                SttProvider = baseProfile.SttProvider,
                SttModel = baseProfile.SttModel,
                RefinementEnabled = baseProfile.RefinementEnabled,
                RefinementProvider = baseProfile.RefinementProvider,
                RefinementModel = baseProfile.RefinementModel,
                RefinementPrompt = baseProfile.RefinementPrompt,
                PromptPresetName = !string.IsNullOrWhiteSpace(baseProfile.PromptPresetName)
                    ? baseProfile.PromptPresetName
                    : SelectedPromptEntry?.Name ?? string.Empty,
                DictationMode = DictationExperienceService.NormalizeMode(baseProfile.DictationMode),
                StylePreset = DictationExperienceService.NormalizeStylePreset(baseProfile.StylePreset),
                CustomStylePrompt = baseProfile.CustomStylePrompt?.Trim() ?? string.Empty,
                Language = baseProfile.Language,
                CopyToClipboard = baseProfile.CopyToClipboard,
                DictionaryTerms = baseProfile.DictionaryTerms.ToList(),
                EnableVoiceCommands = baseProfile.EnableVoiceCommands,
                VoiceCommandMode = DictationExperienceService.NormalizeVoiceCommandMode(baseProfile.VoiceCommandMode),
                ContextualRefinementMode = DictationExperienceService.NormalizeContextualRefinementMode(baseProfile.ContextualRefinementMode),
                UseAppContextForRefinement = baseProfile.UseAppContextForRefinement,
                UseWindowTitleContextForRefinement = baseProfile.UseWindowTitleContextForRefinement,
                UseSelectedTextContextForRefinement = baseProfile.UseSelectedTextContextForRefinement,
                UseClipboardContextForRefinement = baseProfile.UseClipboardContextForRefinement,
                EnableSnippets = baseProfile.EnableSnippets,
                LearnFromRefinementCorrections = baseProfile.LearnFromRefinementCorrections,
                EnableSttFailover = baseProfile.EnableSttFailover,
                SttFailoverOrder = baseProfile.SttFailoverOrder.ToList()
            };

            ConfigManager.Config.Profiles.Add(newProfile);
            ConfigManager.Config.ActiveProfileId = newProfile.Id;
            NewProfileName = string.Empty;

            ReloadProfiles(newProfile.Id);
            ConfigManager.SaveDebounced();
            ProfileStatusMessage = $"Created profile \"{newProfile.Name}\".";
            NotifySelectedProfileSummaryChanged();
            RefreshTargetProfileMatch();
            TelemetryManager.Track(
                name: "profile_create",
                result: "ok",
                data: new Dictionary<string, string>
                {
                    ["profile_id"] = newProfile.Id,
                    ["profile_name"] = newProfile.Name
                });
        }

        private void SaveProfile()
        {
            if (SelectedProfile == null) return;

            var proposedName = ProfileDraftName?.Trim();
            if (string.IsNullOrWhiteSpace(proposedName))
            {
                ProfileStatusMessage = "Profile name cannot be empty.";
                TelemetryManager.Track("profile_save", level: "warning", success: false, result: "empty_name");
                return;
            }

            bool duplicateName = ConfigManager.Config.Profiles.Any(p =>
                !string.Equals(p.Id, SelectedProfile.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Name, proposedName, StringComparison.OrdinalIgnoreCase));
            if (duplicateName)
            {
                ProfileStatusMessage = $"Another profile already uses \"{proposedName}\".";
                TelemetryManager.Track("profile_save", level: "warning", success: false, result: "duplicate_name");
                return;
            }

            var parsedProcesses = (ProfileProcessNamesInput ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ProfileHelpers.NormalizeProcessName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            SelectedProfile.Name = proposedName;
            SelectedProfile.ProcessNames = parsedProcesses;
            ConfigManager.SetActiveProfile(SelectedProfile.Id);
            ConfigManager.SaveDebounced();

            var selectedId = SelectedProfile.Id;
            ReloadProfiles(selectedId);
            ProfileStatusMessage = $"Saved profile \"{proposedName}\" ({parsedProcesses.Count} process mapping(s)).";
            NotifySelectedProfileSummaryChanged();
            RefreshTargetProfileMatch();
            TelemetryManager.Track(
                name: "profile_save",
                result: "ok",
                data: new Dictionary<string, string>
                {
                    ["profile_id"] = selectedId,
                    ["profile_name"] = proposedName,
                    ["mapped_process_count"] = parsedProcesses.Count.ToString()
                });
        }

        private void DeleteSelectedProfile()
        {
            if (SelectedProfile == null || Profiles.Count <= 1)
            {
                return;
            }

            string deletedId = SelectedProfile.Id;
            string deletedName = SelectedProfile.Name;

            var nextProfile = ConfigManager.Config.Profiles
                .FirstOrDefault(p => !string.Equals(p.Id, deletedId, StringComparison.OrdinalIgnoreCase));
            if (nextProfile == null)
            {
                return;
            }

            ConfigManager.Config.Profiles.RemoveAll(p => string.Equals(p.Id, deletedId, StringComparison.OrdinalIgnoreCase));
            ConfigManager.Config.ActiveProfileId = nextProfile.Id;
            ConfigManager.EnsureProfileSyncToLegacyFields(nextProfile);
            ConfigManager.SaveDebounced();

            ReloadProfiles(nextProfile.Id);
            ProfileStatusMessage = $"Deleted profile \"{deletedName}\".";
            NotifySelectedProfileSummaryChanged();
            RefreshTargetProfileMatch();
            TelemetryManager.Track(
                name: "profile_delete",
                result: "ok",
                data: new Dictionary<string, string>
                {
                    ["profile_id"] = deletedId,
                    ["profile_name"] = deletedName
                });
        }

        private void CycleDictationMode()
        {
            var nextMode = DictationExperienceService.GetNextMode(DictationMode);
            DictationMode = nextMode;
            ProfileStatusMessage = $"Dictation mode: {nextMode}";
            TelemetryManager.Track(
                name: "dictation_mode_change",
                result: "ok",
                data: new Dictionary<string, string>
                {
                    ["mode"] = nextMode,
                    ["source"] = "cycle"
                });
        }

        public void RefreshTargetProfileMatch()
        {
            UpdateTargetProfileMatch(TextInserter.CaptureForegroundWindowContext());
        }

        public void UpdateTargetProfileMatch(TargetWindowContext context)
        {
            var normalizedProcess = ProfileHelpers.NormalizeProcessName(context.ProcessName);
            if (string.IsNullOrWhiteSpace(normalizedProcess))
            {
                CurrentTargetProcessStatus = "Current app: (not detected)";
                var fallback = ConfigManager.GetActiveProfile();
                MatchedProfileForTargetStatus = $"Matched profile: {fallback?.Name ?? "Default"}";
                return;
            }

            CurrentTargetProcessStatus = $"Current app: {normalizedProcess}";
            var match = ProfileResolverService.ResolveForForegroundWindow(context.Handle);
            MatchedProfileForTargetStatus = $"Matched profile: {match.Name}";
        }

        private static bool IsSpeaklyProcess(string normalizedProcessName)
        {
            return string.Equals(normalizedProcessName, "speakly", StringComparison.OrdinalIgnoreCase);
        }

        private async Task CaptureProcessForSelectedProfileAsync()
        {
            if (SelectedProfile == null || _isCaptureProfileProcessInProgress)
            {
                return;
            }

            _isCaptureProfileProcessInProgress = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                ProfileStatusMessage = "Switch to the target app now. Capturing foreground process for 5 seconds...";
                var context = await CapturePreferredForegroundWindowAsync(TimeSpan.FromSeconds(5));
                UpdateTargetProfileMatch(context);

                var normalizedProcess = ProfileHelpers.NormalizeProcessName(context.ProcessName);
                if (string.IsNullOrWhiteSpace(normalizedProcess) || IsSpeaklyProcess(normalizedProcess))
                {
                    ProfileStatusMessage = "No external app was captured. Click again, then immediately focus your target app.";
                    return;
                }

                var processes = (ProfileProcessNamesInput ?? string.Empty)
                    .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(ProfileHelpers.NormalizeProcessName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                bool added = false;
                if (!processes.Contains(normalizedProcess, StringComparer.OrdinalIgnoreCase))
                {
                    processes.Add(normalizedProcess);
                    added = true;
                }

                SelectedProfile.ProcessNames = processes;
                ProfileProcessNamesInput = string.Join(", ", processes);
                ConfigManager.SetActiveProfile(SelectedProfile.Id);
                ConfigManager.SaveDebounced();
                NotifySelectedProfileSummaryChanged();
                RefreshTargetProfileMatch();

                ProfileStatusMessage = added
                    ? $"Added process \"{normalizedProcess}\" to profile \"{SelectedProfile.Name}\"."
                    : $"Process \"{normalizedProcess}\" is already mapped to profile \"{SelectedProfile.Name}\".";
            }
            finally
            {
                _isCaptureProfileProcessInProgress = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private static async Task<TargetWindowContext> CapturePreferredForegroundWindowAsync(TimeSpan timeout)
        {
            var deadlineUtc = DateTime.UtcNow + timeout;
            var latest = TextInserter.CaptureForegroundWindowContext();

            while (DateTime.UtcNow < deadlineUtc)
            {
                await Task.Delay(120);
                var sample = TextInserter.CaptureForegroundWindowContext();
                var normalizedProcess = ProfileHelpers.NormalizeProcessName(sample.ProcessName);
                if (string.IsNullOrWhiteSpace(normalizedProcess))
                {
                    continue;
                }

                latest = sample;
                if (!IsSpeaklyProcess(normalizedProcess))
                {
                    return sample;
                }
            }

            return latest;
        }

        private void NotifySelectedProfileSummaryChanged()
        {
            OnPropertyChanged(nameof(SelectedProfileSttSummary));
            OnPropertyChanged(nameof(SelectedProfileRefinementSummary));
            OnPropertyChanged(nameof(SelectedProfileRuntimeSummary));
            OnPropertyChanged(nameof(SelectedProfileModeSummary));
            OnPropertyChanged(nameof(SelectedProfileDictionarySummary));
        }

        public void SetLastContextUsageStatus(string status)
        {
            LastContextUsageStatus = string.IsNullOrWhiteSpace(status)
                ? "Context used: none."
                : status.Trim();
        }

        private void OpenDebugLogs()
        {
            var logPath = Logger.LogFilePath;
            var logDirectory = Path.GetDirectoryName(logPath);

            try
            {
                Logger.EnsureLogFileExists();

                if (File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{logPath}\"")
                    {
                        UseShellExecute = true
                    });
                    return;
                }

                if (!string.IsNullOrWhiteSpace(logDirectory))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logDirectory}\"")
                    {
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("OpenDebugLogs", ex);
            }
        }

        private void RefreshFromConfig()
        {
            UpdateSttModelList();
            UpdateRefinementModelList();
            RefreshDictionaryTextFromConfig();
            OnPropertyChanged(nameof(SttModel));
            OnPropertyChanged(nameof(IsOpenRouterSttProvider));
            OnPropertyChanged(nameof(SelectedSttModelString));
            OnPropertyChanged(nameof(OpenRouterSttShowAllModels));
            OnPropertyChanged(nameof(DeepgramApiBaseUrl));
            OnPropertyChanged(nameof(EnableRefinement));
            OnPropertyChanged(nameof(RefinementModel));
            OnPropertyChanged(nameof(IsCerebrasRefinementProvider));
            OnPropertyChanged(nameof(SelectedRefinementModelString));
            OnPropertyChanged(nameof(CerebrasMaxCompletionTokens));
            OnPropertyChanged(nameof(CerebrasTimeoutSeconds));
            OnPropertyChanged(nameof(CerebrasMaxRetries));
            OnPropertyChanged(nameof(CerebrasRetryBaseDelayMs));
            OnPropertyChanged(nameof(CerebrasVersionPatch));
            OnPropertyChanged(nameof(DictationMode));
            OnPropertyChanged(nameof(ActiveDictationModeTitle));
            OnPropertyChanged(nameof(ActiveDictationModeDescription));
            OnPropertyChanged(nameof(StylePreset));
            OnPropertyChanged(nameof(StylePresetDescription));
            OnPropertyChanged(nameof(IsCustomStylePreset));
            OnPropertyChanged(nameof(CustomStylePrompt));
            OnPropertyChanged(nameof(EnableVoiceCommands));
            OnPropertyChanged(nameof(VoiceCommandMode));
            OnPropertyChanged(nameof(ContextualRefinementMode));
            OnPropertyChanged(nameof(ContextConfigurationSummary));
            OnPropertyChanged(nameof(RefinementPrompt));
            OnPropertyChanged(nameof(Language));
            OnPropertyChanged(nameof(CopyToClipboard));
            OnPropertyChanged(nameof(UseAppContextForRefinement));
            OnPropertyChanged(nameof(UseWindowTitleContextForRefinement));
            OnPropertyChanged(nameof(UseSelectedTextContextForRefinement));
            OnPropertyChanged(nameof(UseClipboardContextForRefinement));
            OnPropertyChanged(nameof(EnableSnippets));
            OnPropertyChanged(nameof(LearnFromRefinementCorrections));
            OnPropertyChanged(nameof(EnableSttFailover));
            OnPropertyChanged(nameof(DeferredTargetPasteEnabled));
            OnPropertyChanged(nameof(StartWithWindows));
            OnPropertyChanged(nameof(AutoMicGainEnabled));
            OnPropertyChanged(nameof(DynamicNormalizationEnabled));
            OnPropertyChanged(nameof(NoiseGateEnabled));
            OnPropertyChanged(nameof(NoiseGateThresholdDb));
            OnPropertyChanged(nameof(AutoMicGainTargetRms));
            OnPropertyChanged(nameof(NormalizationTargetPeak));
            OnPropertyChanged(nameof(GlobalDictionaryTermsText));
            OnPropertyChanged(nameof(ProfileDictionaryTermsText));
            NotifyDeepgramLanguageGuardChanged();
            NotifySelectedProfileSummaryChanged();
        }

        private void RefreshDictionaryTextFromConfig()
        {
            _globalDictionaryTermsText = PersonalDictionaryService.SerializeTerms(ConfigManager.Config.PersonalDictionaryGlobal);
            var active = SelectedProfile ?? ConfigManager.GetActiveProfile();
            _profileDictionaryTermsText = PersonalDictionaryService.SerializeTerms(active?.DictionaryTerms);
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

        public void SetLastInsertionStatus(string method, bool success, string errorCode)
        {
            LastInsertionMethod = string.IsNullOrWhiteSpace(method) ? "Unknown" : method;
            LastInsertionStatus = success
                ? $"OK ({LastInsertionMethod})"
                : $"Failed ({LastInsertionMethod}){(string.IsNullOrWhiteSpace(errorCode) ? string.Empty : $": {errorCode}")}";
        }

        public string PendingTransferStatus
        {
            get => _pendingTransferStatus;
            private set
            {
                if (string.Equals(_pendingTransferStatus, value, StringComparison.Ordinal))
                {
                    return;
                }

                _pendingTransferStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPendingTransfer));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasPendingTransfer =>
            !string.Equals(PendingTransferStatus, "No pending auto-paste.", StringComparison.OrdinalIgnoreCase);

        public void SetPendingTransferStatus(string status)
        {
            PendingTransferStatus = string.IsNullOrWhiteSpace(status)
                ? "No pending auto-paste."
                : status.Trim();
        }

        public void AddDictionarySuggestions(IEnumerable<string> candidates)
        {
            var known = BuildKnownDictionarySet();
            bool changed = false;
            foreach (var candidate in candidates ?? Enumerable.Empty<string>())
            {
                var normalized = candidate?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                if (known.Contains(normalized))
                {
                    continue;
                }

                if (DictionarySuggestions.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                DictionarySuggestions.Add(normalized);
                changed = true;
            }

            if (changed)
            {
                OnPropertyChanged(nameof(HasDictionarySuggestions));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public void AddCorrectionSuggestions(IEnumerable<CorrectionSuggestionEntry> candidates)
        {
            var knownDictionary = BuildKnownDictionarySet();
            var knownSnippets = new HashSet<string>(
                SavedSnippets.Select(entry => BuildCorrectionSuggestionKey(entry.Trigger, entry.Replacement)),
                StringComparer.OrdinalIgnoreCase);
            bool changed = false;

            foreach (var candidate in candidates ?? Enumerable.Empty<CorrectionSuggestionEntry>())
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.SourceText) || string.IsNullOrWhiteSpace(candidate.SuggestedText))
                {
                    continue;
                }

                if (candidate.CanAddToDictionary && knownDictionary.Contains(candidate.SuggestedText.Trim()))
                {
                    continue;
                }

                if (candidate.CanSaveAsSnippet && knownSnippets.Contains(BuildCorrectionSuggestionKey(candidate)))
                {
                    continue;
                }

                if (CorrectionSuggestions.Any(existing =>
                        string.Equals(BuildCorrectionSuggestionKey(existing), BuildCorrectionSuggestionKey(candidate), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                CorrectionSuggestions.Add(candidate);
                changed = true;
            }

            if (changed)
            {
                OnPropertyChanged(nameof(HasCorrectionSuggestions));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private HashSet<string> BuildKnownDictionarySet()
        {
            var active = SelectedProfile ?? ConfigManager.GetActiveProfile();
            return new HashSet<string>(
                PersonalDictionaryService.GetCombinedTerms(ConfigManager.Config, active, maxTerms: 1000),
                StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildCorrectionSuggestionKey(CorrectionSuggestionEntry suggestion)
        {
            return BuildCorrectionSuggestionKey(suggestion.SourceText, suggestion.SuggestedText);
        }

        private static string BuildCorrectionSuggestionKey(string source, string target)
        {
            return $"{source.Trim()} => {target.Trim()}";
        }

        private void AddSuggestionToGlobalDictionary(string term)
        {
            var normalized = term?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var terms = ConfigManager.Config.PersonalDictionaryGlobal ?? new List<string>();
            if (!terms.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                terms.Add(normalized);
            }

            ConfigManager.Config.PersonalDictionaryGlobal = terms
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            RefreshDictionaryTextFromConfig();
            OnPropertyChanged(nameof(GlobalDictionaryTermsText));
            RemoveSuggestionFromQueue(normalized);
        }

        private void AddSuggestionToProfileDictionary(string term)
        {
            var normalized = term?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var active = SelectedProfile ?? ConfigManager.GetActiveProfile();
            if (active == null)
            {
                return;
            }

            var terms = active.DictionaryTerms ?? new List<string>();
            if (!terms.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                terms.Add(normalized);
            }

            active.DictionaryTerms = terms
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            RefreshDictionaryTextFromConfig();
            OnPropertyChanged(nameof(ProfileDictionaryTermsText));
            RemoveSuggestionFromQueue(normalized);
        }

        private void DismissSuggestion(string term)
        {
            RemoveSuggestionFromQueue(term);
        }

        private void AddCorrectionSuggestionToDictionary(CorrectionSuggestionEntry? suggestion)
        {
            if (suggestion == null || !suggestion.CanAddToDictionary)
            {
                return;
            }

            AddSuggestionToGlobalDictionary(suggestion.SuggestedText);
            RemoveCorrectionSuggestionFromQueue(suggestion);
        }

        private void AddCorrectionSuggestionAsSnippet(CorrectionSuggestionEntry? suggestion)
        {
            if (suggestion == null || !suggestion.CanSaveAsSnippet)
            {
                return;
            }

            _snippetList = SnippetLibraryManager.AddOrUpdate(_snippetList, suggestion.SourceText, suggestion.SuggestedText);
            SavedSnippets.Clear();
            foreach (var snippet in _snippetList)
            {
                SavedSnippets.Add(snippet);
            }

            SelectedSnippetEntry = SavedSnippets.FirstOrDefault(entry =>
                string.Equals(entry.Trigger, suggestion.SourceText, StringComparison.OrdinalIgnoreCase));
            RemoveCorrectionSuggestionFromQueue(suggestion);
        }

        private void DismissCorrectionSuggestion(CorrectionSuggestionEntry? suggestion)
        {
            RemoveCorrectionSuggestionFromQueue(suggestion);
        }

        private void RemoveSuggestionFromQueue(string term)
        {
            var normalized = term?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var existing = DictionarySuggestions
                .FirstOrDefault(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                return;
            }

            DictionarySuggestions.Remove(existing);
            if (string.Equals(SelectedDictionarySuggestion, existing, StringComparison.OrdinalIgnoreCase))
            {
                SelectedDictionarySuggestion = string.Empty;
            }

            OnPropertyChanged(nameof(HasDictionarySuggestions));
            CommandManager.InvalidateRequerySuggested();
        }

        private void RemoveCorrectionSuggestionFromQueue(CorrectionSuggestionEntry? suggestion)
        {
            if (suggestion == null)
            {
                return;
            }

            var existing = CorrectionSuggestions.FirstOrDefault(entry =>
                string.Equals(BuildCorrectionSuggestionKey(entry), BuildCorrectionSuggestionKey(suggestion), StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                return;
            }

            CorrectionSuggestions.Remove(existing);
            if (string.Equals(_selectedCorrectionSuggestionKey, BuildCorrectionSuggestionKey(existing), StringComparison.OrdinalIgnoreCase))
            {
                SelectedCorrectionSuggestion = null;
            }

            OnPropertyChanged(nameof(HasCorrectionSuggestions));
            CommandManager.InvalidateRequerySuggested();
        }

        private void ImportGlobalDictionary()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Import Personal Dictionary",
                    Filter = "Text Files (*.txt;*.csv)|*.txt;*.csv|All Files (*.*)|*.*",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var content = File.ReadAllText(dialog.FileName);
                ConfigManager.Config.PersonalDictionaryGlobal = PersonalDictionaryService.ParseTerms(content);
                RefreshDictionaryTextFromConfig();
                OnPropertyChanged(nameof(GlobalDictionaryTermsText));
            }
            catch (Exception ex)
            {
                Logger.LogException("ImportGlobalDictionary", ex);
            }
        }

        private void ExportGlobalDictionary()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Export Personal Dictionary",
                    Filter = "Text Files (*.txt)|*.txt|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    FileName = "speakly-dictionary.txt",
                    AddExtension = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var payload = PersonalDictionaryService.SerializeTerms(ConfigManager.Config.PersonalDictionaryGlobal);
                File.WriteAllText(dialog.FileName, payload);
            }
            catch (Exception ex)
            {
                Logger.LogException("ExportGlobalDictionary", ex);
            }
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
                const string darkTheme = "Dark";
                ConfigManager.Config.Theme = darkTheme;
                App.SetTheme(darkTheme);
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

        public string OverlayStyle
        {
            get => ConfigManager.Config.OverlayStyle;
            set
            {
                var normalized = value?.Trim();
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    normalized = "Rectangular";
                }

                if (!AvailableOverlayStyles.Any(s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    normalized = "Rectangular";
                }

                ConfigManager.Config.OverlayStyle = normalized;
                App.SetOverlayStyle(normalized);
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

            CycleProfileCommand = new RelayCommand(_ => CycleProfile(), _ => CanCycleProfile);
            CycleDictationModeCommand = new RelayCommand(_ => CycleDictationMode(), _ => AvailableDictationModes.Count > 1);
            SetDictationModeCommand = new RelayCommand(obj =>
            {
                var requestedMode = obj as string;
                if (string.IsNullOrWhiteSpace(requestedMode))
                {
                    return;
                }

                DictationMode = requestedMode;
                ProfileStatusMessage = $"Dictation mode: {DictationMode}";
            });

            CreateProfileCommand = new RelayCommand(
                _ => CreateProfile(),
                _ => true);

            SaveProfileCommand = new RelayCommand(
                _ => SaveProfile(),
                _ => SelectedProfile != null);

            DeleteProfileCommand = new RelayCommand(
                _ => DeleteSelectedProfile(),
                _ => CanDeleteSelectedProfile);

            CaptureProfileProcessFromCurrentAppCommand = new RelayCommand(
                _ => _ = CaptureProcessForSelectedProfileAsync(),
                _ => SelectedProfile != null && !_isCaptureProfileProcessInProgress);

            RefreshTargetProfileMatchCommand = new RelayCommand(
                _ => RefreshTargetProfileMatch(),
                _ => true);

            OpenDebugLogsCommand = new RelayCommand(_ => OpenDebugLogs());
            ClearPendingTransferCommand = new RelayCommand(
                _ => App.ClearDeferredTargetPaste(),
                _ => HasPendingTransfer);
            AddSuggestionToGlobalDictionaryCommand = new RelayCommand(
                obj => AddSuggestionToGlobalDictionary((obj as string) ?? SelectedDictionarySuggestion),
                obj => !string.IsNullOrWhiteSpace((obj as string) ?? SelectedDictionarySuggestion));
            AddSuggestionToProfileDictionaryCommand = new RelayCommand(
                obj => AddSuggestionToProfileDictionary((obj as string) ?? SelectedDictionarySuggestion),
                obj => !string.IsNullOrWhiteSpace((obj as string) ?? SelectedDictionarySuggestion)
                    && (SelectedProfile ?? ConfigManager.GetActiveProfile()) != null);
            DismissDictionarySuggestionCommand = new RelayCommand(
                obj => DismissSuggestion((obj as string) ?? SelectedDictionarySuggestion),
                obj => !string.IsNullOrWhiteSpace((obj as string) ?? SelectedDictionarySuggestion));
            ClearDictionarySuggestionsCommand = new RelayCommand(
                _ =>
                {
                    DictionarySuggestions.Clear();
                    SelectedDictionarySuggestion = string.Empty;
                    OnPropertyChanged(nameof(HasDictionarySuggestions));
                    CommandManager.InvalidateRequerySuggested();
                },
                _ => HasDictionarySuggestions);
            ImportGlobalDictionaryCommand = new RelayCommand(_ => ImportGlobalDictionary());
            ExportGlobalDictionaryCommand = new RelayCommand(_ => ExportGlobalDictionary());
            CopyHistoryCommand = new RelayCommand(obj =>
            {
                if (obj is HistoryEntry entry && !string.IsNullOrWhiteSpace(entry.RefinedText))
                {
                    Clipboard.SetText(entry.RefinedText);
                }
            });
            CopyOriginalHistoryCommand = new RelayCommand(obj =>
            {
                if (obj is HistoryEntry entry && !string.IsNullOrWhiteSpace(entry.OriginalText))
                {
                    Clipboard.SetText(entry.OriginalText);
                }
            });
            RetryHistoryInsertCommand = new RelayCommand(
                async obj =>
                {
                    if (obj is HistoryEntry entry)
                    {
                        await App.RetryHistoryInsertAsync(entry);
                    }
                },
                obj => obj is HistoryEntry entry && entry.CanReplayText);
            ReprocessHistoryEntryCommand = new RelayCommand(
                async obj =>
                {
                    if (obj is HistoryEntry entry)
                    {
                        await App.ReprocessHistoryEntryAsync(entry);
                    }
                },
                obj => obj is HistoryEntry entry && entry.CanReprocess);
            TogglePinnedHistoryCommand = new RelayCommand(
                obj =>
                {
                    if (obj is HistoryEntry entry)
                    {
                        entry.Pinned = !entry.Pinned;
                        HistoryManager.SetPinned(entry.Id, entry.Pinned);
                    }
                },
                obj => obj is HistoryEntry);

            SetProfileByIdCommand = new RelayCommand(obj =>
            {
                if (obj is string profileId)
                {
                    var match = Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        SelectedProfile = match;
                        ProfileStatusMessage = $"Active profile: {match.Name}";
                    }
                }
            });

            RunHealthCheckCommand = new RelayCommand(_ => RunHealthChecks());

            RefreshModelsCommand = new RelayCommand(
                async _ => await RefreshProviderModelsAsync(true),
                _ => !IsRefreshingModels);

            ReloadSavedPrompts();
            ReloadSavedSnippets();

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

            SaveSnippetCommand = new RelayCommand(_ =>
            {
                var trigger = NewSnippetTrigger?.Trim();
                var replacement = NewSnippetReplacement?.Trim();
                if (string.IsNullOrWhiteSpace(trigger) || string.IsNullOrWhiteSpace(replacement))
                {
                    return;
                }

                _snippetList = SnippetLibraryManager.AddOrUpdate(_snippetList, trigger, replacement);
                SavedSnippets.Clear();
                foreach (var snippet in _snippetList)
                {
                    SavedSnippets.Add(snippet);
                }

                SelectedSnippetEntry = SavedSnippets.FirstOrDefault(entry =>
                    string.Equals(entry.Trigger, trigger, StringComparison.OrdinalIgnoreCase));
                NewSnippetTrigger = string.Empty;
                NewSnippetReplacement = string.Empty;
            },
            _ => !string.IsNullOrWhiteSpace(NewSnippetTrigger) && !string.IsNullOrWhiteSpace(NewSnippetReplacement));

            DeleteSelectedSnippetCommand = new RelayCommand(_ =>
            {
                if (_selectedSnippetEntry == null)
                {
                    return;
                }

                var trigger = _selectedSnippetEntry.Trigger;
                _snippetList = SnippetLibraryManager.Delete(_snippetList, trigger);
                SavedSnippets.Clear();
                foreach (var snippet in _snippetList)
                {
                    SavedSnippets.Add(snippet);
                }

                SelectedSnippetEntry = SavedSnippets.FirstOrDefault();
                if (_selectedSnippetEntry == null)
                {
                    NewSnippetTrigger = string.Empty;
                    NewSnippetReplacement = string.Empty;
                }
            },
            _ => CanDeleteSelectedSnippet);

            AddCorrectionSuggestionToDictionaryCommand = new RelayCommand(
                obj => AddCorrectionSuggestionToDictionary(obj as CorrectionSuggestionEntry ?? SelectedCorrectionSuggestion),
                obj => (obj as CorrectionSuggestionEntry ?? SelectedCorrectionSuggestion)?.CanAddToDictionary == true);
            AddCorrectionSuggestionToSnippetCommand = new RelayCommand(
                obj => AddCorrectionSuggestionAsSnippet(obj as CorrectionSuggestionEntry ?? SelectedCorrectionSuggestion),
                obj => (obj as CorrectionSuggestionEntry ?? SelectedCorrectionSuggestion)?.CanSaveAsSnippet == true);
            DismissCorrectionSuggestionCommand = new RelayCommand(
                obj => DismissCorrectionSuggestion(obj as CorrectionSuggestionEntry ?? SelectedCorrectionSuggestion),
                obj => (obj as CorrectionSuggestionEntry ?? SelectedCorrectionSuggestion) != null);
            ClearCorrectionSuggestionsCommand = new RelayCommand(
                _ =>
                {
                    CorrectionSuggestions.Clear();
                    SelectedCorrectionSuggestion = null;
                    OnPropertyChanged(nameof(HasCorrectionSuggestions));
                    CommandManager.InvalidateRequerySuggested();
                },
                _ => HasCorrectionSuggestions);

            RunHealthChecks();
            RefreshDictionaryTextFromConfig();
            RefreshTargetProfileMatch();

            PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(LastInsertionMethod)
                    or nameof(LastInsertionStatus)
                    or nameof(PendingTransferStatus)
                    or nameof(HasPendingTransfer)
                    or nameof(HealthSummary)
                    or nameof(HealthDetails)
                    or nameof(ApiTestStatus)
                    or nameof(DeepgramLanguageGuardMessage)
                    or nameof(HasDeepgramLanguageGuard)
                    or nameof(SelectedDictionarySuggestion)
                    or nameof(HasDictionarySuggestions)
                    or nameof(SelectedCorrectionSuggestion)
                    or nameof(HasCorrectionSuggestions)
                    or nameof(SelectedSnippetEntry)
                    or nameof(CanDeleteSelectedSnippet)
                    or nameof(NewSnippetTrigger)
                    or nameof(NewSnippetReplacement)
                    or nameof(NewProfileName)
                    or nameof(ProfileDraftName)
                    or nameof(ProfileProcessNamesInput)
                    or nameof(ProfileStatusMessage)
                    or nameof(CurrentTargetProcessStatus)
                    or nameof(MatchedProfileForTargetStatus)
                    or nameof(SelectedProfileSttSummary)
                    or nameof(SelectedProfileRefinementSummary)
                    or nameof(SelectedProfileRuntimeSummary)
                    or nameof(SelectedProfileModeSummary)
                    or nameof(SelectedProfileDictionarySummary)
                    or nameof(CanDeleteSelectedProfile)
                    or nameof(ActiveDictationModeTitle)
                    or nameof(ActiveDictationModeDescription)
                    or nameof(StylePresetDescription)
                    or nameof(IsCustomStylePreset)
                    or nameof(ContextConfigurationSummary)
                    or nameof(LastContextUsageStatus))
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
                AvailableSttModels.Add("nova-3");
                AvailableSttModels.Add("nova-2");
                AvailableSttModels.Add("nova-2-phonecall");
                AvailableSttModels.Add("nova-2-medical");
            }
            else if (SttModel == "OpenAI")
            {
                AvailableSttModels.Add("whisper-1");
                AvailableSttModels.Add("whisper-1-hd");
            }
            else if (SttModel == "OpenRouter")
            {
                AvailableSttModels.Add("openai/gpt-audio-mini");
                AvailableSttModels.Add("openai/gpt-audio");
                AvailableSttModels.Add("google/gemini-2.0-flash-001");
                AvailableSttModels.Add("mistralai/voxtral-small-24b-2507");
                AvailableSttModels.Add("openai/whisper-large-v3");
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
                AvailableRefinementModels.Add("gpt-4.1-mini");
                AvailableRefinementModels.Add("gpt-4o-mini");
                AvailableRefinementModels.Add("gpt-4o");
                AvailableRefinementModels.Add("gpt-4-turbo");
                AvailableRefinementModels.Add("gpt-3.5-turbo");
            }
            else if (RefinementModel == "Cerebras")
            {
                AvailableRefinementModels.Add("llama3.1-8b");
                AvailableRefinementModels.Add("llama-3.3-70b");
                AvailableRefinementModels.Add("qwen-3-32b");
                AvailableRefinementModels.Add("qwen-3-235b-a22b-instruct-2507");
                AvailableRefinementModels.Add("gpt-oss-120b");
                AvailableRefinementModels.Add("zai-glm-4.7");
            }
            else if (RefinementModel == "OpenRouter")
            {
                AvailableRefinementModels.Add("openai/gpt-4.1-mini");
                AvailableRefinementModels.Add("google/gemini-2.5-flash");
                AvailableRefinementModels.Add("openai/gpt-4o-mini");
                AvailableRefinementModels.Add("qwen/qwen3-235b-a22b-2507");
                AvailableRefinementModels.Add("google/gemini-3.1-flash-lite-preview");
                AvailableRefinementModels.Add("google/gemini-2.5-flash-lite");
                AvailableRefinementModels.Add("anthropic/claude-3.5-sonnet");
                AvailableRefinementModels.Add("anthropic/claude-3-haiku");
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

        private static string BuildProviderSummary(IEnumerable<string> models, int maxProviders = 6)
        {
            var summary = models
                .Select(model =>
                {
                    var normalized = (model ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        return "(other)";
                    }

                    var slashIndex = normalized.IndexOf('/');
                    if (slashIndex <= 0)
                    {
                        return "(other)";
                    }

                    return normalized[..slashIndex];
                })
                .GroupBy(provider => provider, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Key}: {group.Count()}")
                .ToList();

            if (summary.Count <= maxProviders)
            {
                return string.Join(", ", summary);
            }

            var visible = summary.Take(maxProviders);
            var remaining = summary.Count - maxProviders;
            return $"{string.Join(", ", visible)}, +{remaining} more";
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

        private static string NormalizeDeepgramApiBaseUrl(string? value)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "https://api.deepgram.com";
            }

            if (!normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://" + normalized;
            }

            return normalized.TrimEnd('/');
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
                bool cerebrasSelectedModelMissing = false;
                bool openRouterSttCatalogEmpty = false;
                bool openRouterSttUsingAllModels = false;
                int openRouterSttCount = 0;
                string openRouterSttProviderSummary = string.Empty;
                var selectedCerebrasModel = config.CerebrasRefinementModel?.Trim() ?? string.Empty;

                // Always rebuild dynamic catalogs from current provider responses.
                // This prevents stale model entries from persisting across refreshes.
                _dynamicSttModels.Clear();
                _dynamicRefinementModels.Clear();

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
                    Dictionary<string, string>? cerebrasHeaders = null;
                    if (!string.IsNullOrWhiteSpace(config.CerebrasVersionPatch))
                    {
                        cerebrasHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["X-Cerebras-Version-Patch"] = config.CerebrasVersionPatch.Trim()
                        };
                    }

                    var cerebrasModels = await FetchModelsFromEndpointAsync(
                        "https://api.cerebras.ai/v1/models",
                        new AuthenticationHeaderValue("Bearer", config.CerebrasApiKey.Trim()),
                        cerebrasHeaders);

                    if (cerebrasModels.Count > 0)
                    {
                        _dynamicRefinementModels["Cerebras"] = cerebrasModels
                            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (!string.IsNullOrWhiteSpace(selectedCerebrasModel) &&
                            !cerebrasModels.Any(m => string.Equals(m, selectedCerebrasModel, StringComparison.OrdinalIgnoreCase)))
                        {
                            cerebrasSelectedModelMissing = true;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(config.OpenRouterApiKey))
                {
                    openRouterSttUsingAllModels = config.OpenRouterSttShowAllModels;
                    var openRouterSttModels = await FetchOpenRouterSttModelsAsync(
                        config.OpenRouterApiKey.Trim(),
                        includeAllModels: openRouterSttUsingAllModels);
                    openRouterSttCount = openRouterSttModels.Count;
                    if (openRouterSttModels.Count > 0)
                    {
                        _dynamicSttModels["OpenRouter"] = openRouterSttModels;
                        openRouterSttProviderSummary = BuildProviderSummary(openRouterSttModels);
                    }
                    else
                    {
                        openRouterSttCatalogEmpty = true;
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

                if (cerebrasSelectedModelMissing && string.Equals(config.RefinementModel, "Cerebras", StringComparison.OrdinalIgnoreCase))
                {
                    ModelRefreshStatus += $" | Warning: selected Cerebras model \"{selectedCerebrasModel}\" was not returned by the current model catalog.";
                }

                if (openRouterSttCatalogEmpty)
                {
                    if (openRouterSttUsingAllModels)
                    {
                        ModelRefreshStatus += " | OpenRouter returned no models in experimental all-models mode.";
                    }
                    else
                    {
                        ModelRefreshStatus += " | OpenRouter returned no audio-input STT models; keeping built-in OpenRouter STT defaults.";
                    }
                }
                else if (openRouterSttCount > 0)
                {
                    ModelRefreshStatus += $" | OpenRouter STT models: {openRouterSttCount}";
                    if (!string.IsNullOrWhiteSpace(openRouterSttProviderSummary))
                    {
                        ModelRefreshStatus += $" ({openRouterSttProviderSummary})";
                    }
                    if (openRouterSttUsingAllModels)
                    {
                        ModelRefreshStatus += " | mode: experimental all models";
                    }
                }

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
        /// which excludes image-generation and embedding-only model types.
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

                // Exclude clearly transcription-focused model IDs from refinement list.
                if (IsOpenRouterSttModel(id)) continue;

                // Keep only refinement-capable text models. For OpenRouter, some entries
                // (including free variants) expose modality as "text" rather than "...->text".
                if (!IsOpenRouterTextRefinementModel(item, id)) continue;

                refinementModels.Add(id);
            }

            refinementModels.Sort(StringComparer.OrdinalIgnoreCase);
            return refinementModels;
        }

        private async Task<List<string>> FetchOpenRouterSttModelsAsync(string apiKey, bool includeAllModels = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadAsStringAsync();
            var sttModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                return sttModels.ToList();

            foreach (var item in dataArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String) continue;

                var id = idProp.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!includeAllModels && !IsOpenRouterAudioInputSttModel(item, id)) continue;

                sttModels.Add(id);
            }

            return sttModels
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsOpenRouterAudioInputSttModel(JsonElement item, string modelId)
        {
            if (string.Equals(modelId, "openrouter/auto", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Strong ID hints used by dedicated transcription/ASR model IDs.
            if (IsOpenRouterSttModel(modelId))
            {
                return true;
            }

            if (!item.TryGetProperty("architecture", out var arch) || arch.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            bool inputHasAudio = HasArchitectureModality(arch, "input_modalities", "audio");
            bool outputHasText = HasArchitectureModality(arch, "output_modalities", "text");

            if (inputHasAudio && outputHasText)
            {
                return true;
            }

            if (arch.TryGetProperty("modality", out var modalityProp) && modalityProp.ValueKind == JsonValueKind.String)
            {
                var modality = modalityProp.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(modality))
                {
                    return false;
                }

                if (modality.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    modality.IndexOf("->text", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasArchitectureModality(JsonElement architecture, string propertyName, string expectedValue)
        {
            if (!architecture.TryGetProperty(propertyName, out var modalities) || modalities.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var entry in modalities.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String) continue;
                var value = entry.GetString();
                if (string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
            var baseUrl = NormalizeDeepgramApiBaseUrl(ConfigManager.Config.DeepgramApiBaseUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", apiKey);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadAsStringAsync();
            return ExtractDeepgramStreamingSttModelIds(json);
        }

        private async Task<List<string>> FetchModelsFromEndpointAsync(
            string url,
            AuthenticationHeaderValue authHeader,
            IDictionary<string, string>? extraHeaders = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = authHeader;
            if (extraHeaders != null)
            {
                foreach (var entry in extraHeaders)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value)) continue;
                    request.Headers.TryAddWithoutValidation(entry.Key, entry.Value);
                }
            }

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

                foreach (var propertyName in new[] { "canonical_name", "id", "model", "name" })
                {
                    if (item.TryGetProperty(propertyName, out var valueElement) && valueElement.ValueKind == JsonValueKind.String)
                    {
                        var value = valueElement.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) results.Add(value.Trim());
                    }
                }
            }
        }

        private static List<string> ExtractDeepgramStreamingSttModelIds(string json)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("stt", out var sttArray) && sttArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in sttArray.EnumerateArray())
                {
                    if (model.ValueKind != JsonValueKind.Object) continue;

                    if (model.TryGetProperty("streaming", out var streamingProp) &&
                        streamingProp.ValueKind == JsonValueKind.False)
                    {
                        continue;
                    }

                    foreach (var propertyName in new[] { "canonical_name", "id", "model", "name" })
                    {
                        if (!model.TryGetProperty(propertyName, out var valueElement) ||
                            valueElement.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var value = valueElement.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            results.Add(value);
                            break;
                        }
                    }
                }
            }

            if (results.Count == 0)
            {
                return ExtractModelIds(json)
                    .Where(m => m.Contains("nova", StringComparison.OrdinalIgnoreCase)
                                || m.Contains("whisper", StringComparison.OrdinalIgnoreCase)
                                || m.Contains("flux", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return results.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsOpenAiSttModel(string modelId)
        {
            return modelId.Contains("whisper", StringComparison.OrdinalIgnoreCase)
                   || modelId.Contains("transcribe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOpenRouterSttModel(string modelId)
        {
            // Match dedicated transcription/ASR model IDs.
            return modelId.Contains("whisper",    StringComparison.OrdinalIgnoreCase)
                || modelId.Contains("transcri",   StringComparison.OrdinalIgnoreCase)
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
            
            var dg = await ApiTester.TestDeepgramAsync(DeepgramApiKey, DeepgramApiBaseUrl);
            sb.AppendLine($"Deepgram: {dg}");

            var oa = await ApiTester.TestOpenAIAsync(OpenAIApiKey);
            sb.AppendLine($"OpenAI: {oa}");

            var cr = await ApiTester.TestCerebrasAsync(
                CerebrasApiKey,
                ConfigManager.Config.CerebrasRefinementModel,
                ConfigManager.Config.CerebrasMaxCompletionTokens,
                ConfigManager.Config.CerebrasVersionPatch);
            sb.AppendLine($"Cerebras: {cr}");

            var or = await ApiTester.TestOpenRouterAsync(OpenRouterApiKey);
            sb.AppendLine($"OpenRouter: {or}");

            ApiTestStatus = sb.ToString();
            ApiTestCompleted?.Invoke(dg, oa, cr, or);
            TelemetryManager.Track(
                name: "api_test",
                level: "info",
                result: "completed",
                data: new Dictionary<string, string>
                {
                    ["deepgram_result"] = dg,
                    ["openai_result"] = oa,
                    ["cerebras_result"] = cr,
                    ["openrouter_result"] = or
                });
            ApiTestStatus = "Ready";
        }

        private string BuildDeepgramLanguageGuardMessage()
        {
            if (!string.Equals(SttModel, "Deepgram", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var configuredLanguage = (Language ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.Equals(configuredLanguage, "multi", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var modelId = (SelectedSttModelString ?? string.Empty).Trim().ToLowerInvariant();
            if (!modelId.StartsWith("nova-3", StringComparison.OrdinalIgnoreCase) &&
                !modelId.StartsWith("nova-2", StringComparison.OrdinalIgnoreCase))
            {
                return $"Deepgram language=multi is not supported by \"{SelectedSttModelString}\". Choose nova-3/nova-2 for multilingual mode.";
            }

            var supportedCodes = string.Join(", ", DeepgramMultilingualCodes);
            return $"Deepgram multi supports {supportedCodes}. Ukrainian is not in multi; use language=uk for Ukrainian.";
        }

        private void NotifyDeepgramLanguageGuardChanged()
        {
            OnPropertyChanged(nameof(DeepgramLanguageGuardMessage));
            OnPropertyChanged(nameof(HasDeepgramLanguageGuard));
        }

        public void RunHealthChecks()
        {
            var result = HealthCheckService.Run(ConfigManager.Config);
            HealthSummary = result.Summary;
            HealthDetails = result.Details;
            TelemetryManager.Track(
                name: "healthcheck_run",
                level: result.HasIssues ? "warning" : "info",
                success: !result.HasIssues,
                result: result.HasIssues ? "issues_detected" : "healthy");
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Text;
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

        public ICommand SaveCommand { get; }

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
                if (SttModel == "Deepgram") ConfigManager.Config.DeepgramModel = value;
                else if (SttModel == "OpenAI") ConfigManager.Config.OpenAISttModel = value;
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
                if (RefinementModel == "OpenAI") ConfigManager.Config.OpenAIRefinementModel = value;
                else if (RefinementModel == "Cerebras") ConfigManager.Config.CerebrasRefinementModel = value;
                else if (RefinementModel == "OpenRouter") ConfigManager.Config.OpenRouterRefinementModel = value;
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
            set { ConfigManager.Config.RefinementPrompt = value; OnPropertyChanged(); }
        }

        public bool MinimizeToTray
        {
            get => ConfigManager.Config.MinimizeToTray;
            set { ConfigManager.Config.MinimizeToTray = value; OnPropertyChanged(); }
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

        public string Language
        {
            get => ConfigManager.Config.Language;
            set { ConfigManager.Config.Language = value; OnPropertyChanged(); }
        }

        public ObservableCollection<HistoryEntry> HistoryEntries { get; } = new ObservableCollection<HistoryEntry>();

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

        public MainViewModel()
        {
            LoadAudioDevices();
            UpdateSttModelList();
            UpdateRefinementModelList();
            
            foreach (var entry in HistoryManager.GetHistory())
            {
                HistoryEntries.Add(entry);
            }

            SaveCommand = new RelayCommand(_ => {
                ConfigManager.Save();
                MessageBox.Show("Settings saved successfully!", "Speakly", MessageBoxButton.OK, MessageBoxImage.Information);
            });
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
            }
            OnPropertyChanged(nameof(SelectedSttModelString));
        }

        private void UpdateRefinementModelList()
        {
            AvailableRefinementModels.Clear();
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
            OnPropertyChanged(nameof(SelectedRefinementModelString));
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
            MessageBox.Show(ApiTestStatus, "API Test Results", MessageBoxButton.OK, MessageBoxImage.Information);
            ApiTestStatus = "Ready";
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

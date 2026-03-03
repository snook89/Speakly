using Speakly.Config;
using NAudio.Wave;

namespace Speakly.Services
{
    public interface IAudioRecorder : IDisposable
    {
        event EventHandler<byte[]>? AudioDataAvailable;
        event EventHandler? RecordingStopped;
        
        bool IsRecording { get; }
        void StartRecording();
        void StopRecording();
    }

    public class NAudioRecorder : IAudioRecorder
    {
        private WaveInEvent? _waveIn;
        private bool _isRecording;

        public event EventHandler<byte[]>? AudioDataAvailable;
        public event EventHandler? RecordingStopped;

        public bool IsRecording => _isRecording;

        public NAudioRecorder()
        {
            // Initialization happens on StartRecording to catch device changes
        }

        private void InitializeWaveIn()
        {
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
            }

            int deviceNumber = -1; // Default
            string targetDevice = ConfigManager.Config.AudioDevice;

            if (!string.IsNullOrEmpty(targetDevice) && targetDevice != "Default")
            {
                if (TryParseUnnamedDeviceIndex(targetDevice, out var unnamedDeviceIndex))
                {
                    if (unnamedDeviceIndex >= 0 && unnamedDeviceIndex < WaveInEvent.DeviceCount)
                    {
                        deviceNumber = unnamedDeviceIndex;
                    }
                }

                for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                {
                    var capabilities = WaveInEvent.GetCapabilities(i);
                    if (capabilities.ProductName.Contains(targetDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        deviceNumber = i;
                        break;
                    }
                }
            }

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(ConfigManager.Config.SampleRate, 16, ConfigManager.Config.Channels),
                BufferMilliseconds = ResolveBufferMilliseconds()
            };
            
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
        }

        public void StartRecording()
        {
            if (_isRecording) return;
            
            InitializeWaveIn();

            if (_waveIn == null) return;
            
            _isRecording = true;
            _waveIn.StartRecording();
        }

        public void StopRecording()
        {
            if (!_isRecording || _waveIn == null) return;
            
            _isRecording = false;
            _waveIn.StopRecording();
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRecording) return;
            
            byte[] recordedBuffer = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, recordedBuffer, 0, e.BytesRecorded);
            
            AudioDataAvailable?.Invoke(this, recordedBuffer);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _isRecording = false;
            RecordingStopped?.Invoke(this, EventArgs.Empty);
            
            if (e.Exception != null)
            {
                Console.WriteLine($"Recording Error: {e.Exception.Message}");
            }
        }

        public void Dispose()
        {
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
                _waveIn = null;
            }
            GC.SuppressFinalize(this);
        }

        private static int ResolveBufferMilliseconds()
        {
            var sampleRate = Math.Clamp(ConfigManager.Config.SampleRate, 8000, 48000);
            var channels = Math.Clamp(ConfigManager.Config.Channels, 1, 2);
            var chunkSize = Math.Clamp(ConfigManager.Config.ChunkSize, 256, 32768);

            int bytesPerSecond = sampleRate * channels * 2; // 16-bit PCM
            if (bytesPerSecond <= 0) return 50;

            var estimatedMs = (int)Math.Round(chunkSize * 1000.0 / bytesPerSecond);
            return Math.Clamp(estimatedMs, 10, 200);
        }

        private static bool TryParseUnnamedDeviceIndex(string targetDevice, out int deviceIndex)
        {
            deviceIndex = -1;
            const string prefix = "(Unnamed input device ";
            const string suffix = ")";

            if (!targetDevice.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !targetDevice.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            var numericPart = targetDevice.Substring(prefix.Length, targetDevice.Length - prefix.Length - suffix.Length);
            if (!int.TryParse(numericPart, out var oneBased)) return false;
            if (oneBased <= 0) return false;

            deviceIndex = oneBased - 1;
            return true;
        }
    }
}

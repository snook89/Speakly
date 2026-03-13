using System;
using System.Collections.Generic;
using Speakly.Config;
using SoundFlow.Extensions.WebRtc.Apm;

namespace Speakly.Services
{
    public sealed class WebRtcAudioProcessor : IAudioFrameProcessor
    {
        private readonly object _gate = new();
        private readonly List<byte> _pendingBytes = new();
        private AudioProcessingModule? _module;
        private StreamConfig? _streamConfig;
        private string _configSignature = string.Empty;
        private int _sampleRate;
        private int _channels;
        private int _frameSamplesPerChannel;
        private int _frameBytes;

        public void Reset()
        {
            lock (_gate)
            {
                _pendingBytes.Clear();
            }
        }

        public byte[] Process(byte[] input, out AudioProcessingStats stats)
        {
            if (input == null || input.Length == 0)
            {
                stats = new AudioProcessingStats(0, 0, 0, 0, 1, 0);
                return Array.Empty<byte>();
            }

            lock (_gate)
            {
                if (!EnsureInitialized())
                {
                    stats = BuildStats(input, input);
                    return input;
                }

                _pendingBytes.AddRange(input);
                if (_pendingBytes.Count < _frameBytes)
                {
                    stats = BuildStats(input, Array.Empty<byte>());
                    return Array.Empty<byte>();
                }

                var output = new List<byte>(_pendingBytes.Count);
                while (_pendingBytes.Count >= _frameBytes)
                {
                    var frame = _pendingBytes.GetRange(0, _frameBytes).ToArray();
                    _pendingBytes.RemoveRange(0, _frameBytes);

                    var processed = ProcessExactFrame(frame);
                    output.AddRange(processed);
                }

                var outputBytes = output.ToArray();
                stats = BuildStats(input, outputBytes);
                return outputBytes;
            }
        }

        public byte[] Flush(out AudioProcessingStats stats)
        {
            lock (_gate)
            {
                if (_pendingBytes.Count == 0)
                {
                    stats = new AudioProcessingStats(0, 0, 0, 0, 1, 0);
                    return Array.Empty<byte>();
                }

                if (!EnsureInitialized())
                {
                    var passthrough = _pendingBytes.ToArray();
                    _pendingBytes.Clear();
                    stats = BuildStats(passthrough, passthrough);
                    return passthrough;
                }

                var originalLength = _pendingBytes.Count;
                var padded = new byte[_frameBytes];
                for (int i = 0; i < originalLength; i++)
                {
                    padded[i] = _pendingBytes[i];
                }

                var processed = ProcessExactFrame(padded);
                var trimmed = new byte[originalLength];
                Buffer.BlockCopy(processed, 0, trimmed, 0, Math.Min(originalLength, processed.Length));
                var raw = _pendingBytes.ToArray();
                _pendingBytes.Clear();
                stats = BuildStats(raw, trimmed);
                return trimmed;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _pendingBytes.Clear();
                if (_module is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                _module = null;
                _streamConfig = null;
            }
        }

        private bool EnsureInitialized()
        {
            var config = ConfigManager.Config;
            var sampleRate = config.SampleRate;
            var channels = config.Channels;

            if (!IsSupportedSampleRate(sampleRate) || channels < 1 || channels > 2)
            {
                return false;
            }

            var signature = BuildConfigSignature(config);
            if (_module != null &&
                _streamConfig != null &&
                _sampleRate == sampleRate &&
                _channels == channels &&
                string.Equals(_configSignature, signature, StringComparison.Ordinal))
            {
                return true;
            }

            if (_module is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _module = new AudioProcessingModule();
            _module.Initialize();

            var apmConfig = new ApmConfig();
            apmConfig.SetHighPassFilter(config.WebRtcHighPassFilterEnabled);
            apmConfig.SetNoiseSuppression(
                config.WebRtcNoiseSuppressionEnabled,
                MapNoiseSuppressionLevel(config.WebRtcNoiseSuppressionLevel));
            apmConfig.SetGainController1(
                config.WebRtcAgcEnabled,
                MapGainControlMode(WebRtcAudioOptions.GainControlAdaptiveDigital),
                Math.Clamp(Math.Abs(config.WebRtcAgcTargetLevelDbfs), 0, 31),
                Math.Clamp(config.WebRtcAgcCompressionGainDb, 0, 90),
                config.WebRtcAgcLimiterEnabled);
            apmConfig.SetPreAmplifier(
                config.WebRtcPreAmpEnabled,
                Math.Clamp(config.WebRtcPreAmpGainFactor, 1.0f, 4.0f));
            _module.ApplyConfig(apmConfig);
            _sampleRate = sampleRate;
            _channels = channels;
            _frameSamplesPerChannel = AudioProcessingModule.GetFrameSize(sampleRate);
            _frameBytes = _frameSamplesPerChannel * channels * sizeof(short);
            _streamConfig = new StreamConfig(sampleRate, channels);
            _configSignature = signature;
            return true;
        }

        private byte[] ProcessExactFrame(byte[] frame)
        {
            var input = Deinterleave(frame, _channels, _frameSamplesPerChannel);
            var output = CreateChannelBuffers(_channels, _frameSamplesPerChannel);

            var result = _module!.ProcessStream(input, _streamConfig!, _streamConfig!, output);
            if (result != ApmError.NoError)
            {
                return frame;
            }

            return Interleave(output, _channels, _frameSamplesPerChannel);
        }

        private static float[][] Deinterleave(byte[] frame, int channels, int samplesPerChannel)
        {
            var buffer = CreateChannelBuffers(channels, samplesPerChannel);
            int offset = 0;
            for (int sample = 0; sample < samplesPerChannel; sample++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    short pcm = (short)(frame[offset] | (frame[offset + 1] << 8));
                    buffer[channel][sample] = pcm / 32768f;
                    offset += 2;
                }
            }

            return buffer;
        }

        private static byte[] Interleave(float[][] frame, int channels, int samplesPerChannel)
        {
            var bytes = new byte[samplesPerChannel * channels * sizeof(short)];
            int offset = 0;
            for (int sample = 0; sample < samplesPerChannel; sample++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    var value = Math.Clamp(frame[channel][sample], -1f, 1f);
                    short pcm = (short)Math.Round(value * short.MaxValue);
                    bytes[offset] = (byte)(pcm & 0xFF);
                    bytes[offset + 1] = (byte)((pcm >> 8) & 0xFF);
                    offset += 2;
                }
            }

            return bytes;
        }

        private static float[][] CreateChannelBuffers(int channels, int samplesPerChannel)
        {
            var output = new float[channels][];
            for (int channel = 0; channel < channels; channel++)
            {
                output[channel] = new float[samplesPerChannel];
            }

            return output;
        }

        private static AudioProcessingStats BuildStats(byte[] rawBytes, byte[] processedBytes)
        {
            var (rawRms, rawPeak) = Measure(rawBytes);
            var (processedRms, processedPeak) = Measure(processedBytes);

            return new AudioProcessingStats(
                rawRms,
                rawPeak,
                processedRms,
                processedPeak,
                appliedGain: 1f,
                clippedSamples: CountClippedSamples(processedBytes));
        }

        private static (float rms, float peak) Measure(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 2)
            {
                return (0, 0);
            }

            int sampleCount = bytes.Length / 2;
            double sumSq = 0;
            double peak = 0;
            for (int i = 0; i < bytes.Length - 1; i += 2)
            {
                short sample = (short)(bytes[i] | (bytes[i + 1] << 8));
                double value = sample / 32768.0;
                sumSq += value * value;
                peak = Math.Max(peak, Math.Abs(value));
            }

            return ((float)Math.Sqrt(sumSq / sampleCount), (float)peak);
        }

        private static int CountClippedSamples(byte[] bytes)
        {
            int clipped = 0;
            for (int i = 0; i < bytes.Length - 1; i += 2)
            {
                short sample = (short)(bytes[i] | (bytes[i + 1] << 8));
                if (sample == short.MaxValue || sample == short.MinValue)
                {
                    clipped++;
                }
            }

            return clipped;
        }

        private static bool IsSupportedSampleRate(int sampleRate)
        {
            return sampleRate == 8000 || sampleRate == 16000 || sampleRate == 32000 || sampleRate == 48000;
        }

        private static string BuildConfigSignature(AppConfig config)
        {
            return string.Join("|",
                config.SampleRate,
                config.Channels,
                config.WebRtcHighPassFilterEnabled,
                config.WebRtcNoiseSuppressionEnabled,
                WebRtcAudioOptions.NormalizeNoiseSuppressionLevel(config.WebRtcNoiseSuppressionLevel),
                config.WebRtcAgcEnabled,
                WebRtcAudioOptions.NormalizeGainControlMode(WebRtcAudioOptions.GainControlAdaptiveDigital),
                Math.Clamp(Math.Abs(config.WebRtcAgcTargetLevelDbfs), 0, 31),
                Math.Clamp(config.WebRtcAgcCompressionGainDb, 0, 90));
        }

        private static NoiseSuppressionLevel MapNoiseSuppressionLevel(string? level)
        {
            return WebRtcAudioOptions.NormalizeNoiseSuppressionLevel(level) switch
            {
                WebRtcAudioOptions.NoiseSuppressionLow => NoiseSuppressionLevel.Low,
                WebRtcAudioOptions.NoiseSuppressionModerate => NoiseSuppressionLevel.Moderate,
                WebRtcAudioOptions.NoiseSuppressionVeryHigh => NoiseSuppressionLevel.VeryHigh,
                _ => NoiseSuppressionLevel.High
            };
        }

        private static GainControlMode MapGainControlMode(string? mode)
        {
            return WebRtcAudioOptions.NormalizeGainControlMode(mode) switch
            {
                WebRtcAudioOptions.GainControlFixedDigital => GainControlMode.FixedDigital,
                _ => GainControlMode.AdaptiveDigital
            };
        }
    }
}

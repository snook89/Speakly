using System;
using Speakly.Config;

namespace Speakly.Services
{
    public readonly struct AudioProcessingStats
    {
        public AudioProcessingStats(
            float rawRms,
            float rawPeak,
            float processedRms,
            float processedPeak,
            float appliedGain,
            int clippedSamples)
        {
            RawRms = rawRms;
            RawPeak = rawPeak;
            ProcessedRms = processedRms;
            ProcessedPeak = processedPeak;
            AppliedGain = appliedGain;
            ClippedSamples = clippedSamples;
        }

        public float RawRms { get; }
        public float RawPeak { get; }
        public float ProcessedRms { get; }
        public float ProcessedPeak { get; }
        public float AppliedGain { get; }
        public int ClippedSamples { get; }
    }

    public sealed class ManagedAudioProcessor
    {
        private readonly object _gate = new();
        private double _agcGain = 1.0;

        public void Reset()
        {
            lock (_gate)
            {
                _agcGain = 1.0;
            }
        }

        public byte[] Process(byte[] input, out AudioProcessingStats stats)
        {
            if (input == null || input.Length < 2)
            {
                stats = new AudioProcessingStats(0, 0, 0, 0, 1, 0);
                return input ?? Array.Empty<byte>();
            }

            var output = new byte[input.Length];
            int sampleCount = input.Length / 2;
            if (sampleCount <= 0)
            {
                Buffer.BlockCopy(input, 0, output, 0, input.Length);
                stats = new AudioProcessingStats(0, 0, 0, 0, 1, 0);
                return output;
            }

            double rawSumSq = 0;
            double rawPeak = 0;
            for (int i = 0; i < input.Length - 1; i += 2)
            {
                short sample = (short)(input[i] | (input[i + 1] << 8));
                double norm = sample / 32768.0;
                rawSumSq += norm * norm;
                rawPeak = Math.Max(rawPeak, Math.Abs(norm));
            }

            double rawRms = Math.Sqrt(rawSumSq / sampleCount);
            bool agcEnabled = ConfigManager.Config.AutoMicGainEnabled;
            bool normalizeEnabled = ConfigManager.Config.DynamicNormalizationEnabled;
            bool gateEnabled = ConfigManager.Config.NoiseGateEnabled;
            double gateThreshold = Math.Pow(10.0, Math.Clamp(ConfigManager.Config.NoiseGateThresholdDb, -80, -10) / 20.0);
            double targetRms = Math.Clamp(ConfigManager.Config.AutoMicGainTargetRms, 0.02, 0.4);
            double targetPeak = Math.Clamp(ConfigManager.Config.NormalizationTargetPeak, 0.2, 0.99);

            double localAgcGain;
            lock (_gate)
            {
                if (agcEnabled)
                {
                    double desired = targetRms / Math.Max(rawRms, 1e-4);
                    desired = Math.Clamp(desired, 0.2, 8.0);
                    _agcGain = (_agcGain * 0.85) + (desired * 0.15);
                }
                else
                {
                    _agcGain = 1.0;
                }

                localAgcGain = _agcGain;
            }

            double normGain = 1.0;
            if (normalizeEnabled)
            {
                double projectedPeak = rawPeak * localAgcGain;
                if (projectedPeak > 1e-4)
                {
                    normGain = Math.Clamp(targetPeak / projectedPeak, 0.4, 2.0);
                }
            }

            double totalGain = localAgcGain * normGain;
            double outSumSq = 0;
            double outPeak = 0;
            int clippedSamples = 0;
            for (int i = 0; i < input.Length - 1; i += 2)
            {
                short sample = (short)(input[i] | (input[i + 1] << 8));
                double norm = sample / 32768.0;
                double processed = norm * totalGain;

                if (gateEnabled && Math.Abs(processed) < gateThreshold)
                {
                    processed = 0;
                }

                if (processed > 1.0)
                {
                    processed = 1.0;
                    clippedSamples++;
                }
                else if (processed < -1.0)
                {
                    processed = -1.0;
                    clippedSamples++;
                }

                outPeak = Math.Max(outPeak, Math.Abs(processed));
                outSumSq += processed * processed;

                short outSample = (short)Math.Round(processed * short.MaxValue);
                output[i] = (byte)(outSample & 0xFF);
                output[i + 1] = (byte)((outSample >> 8) & 0xFF);
            }

            double outRms = Math.Sqrt(outSumSq / sampleCount);
            stats = new AudioProcessingStats(
                rawRms: (float)rawRms,
                rawPeak: (float)rawPeak,
                processedRms: (float)outRms,
                processedPeak: (float)outPeak,
                appliedGain: (float)totalGain,
                clippedSamples: clippedSamples);
            return output;
        }
    }
}

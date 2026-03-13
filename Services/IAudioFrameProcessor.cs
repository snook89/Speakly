namespace Speakly.Services
{
    public interface IAudioFrameProcessor : IDisposable
    {
        void Reset();
        byte[] Process(byte[] input, out AudioProcessingStats stats);
        byte[] Flush(out AudioProcessingStats stats);
    }
}

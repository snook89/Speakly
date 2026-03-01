using System;
using System.Threading.Tasks;

namespace Speakly.Services
{
    public class TranscriptionEventArgs : EventArgs
    {
        public string Text { get; }
        public bool IsFinal { get; }

        public TranscriptionEventArgs(string text, bool isFinal)
        {
            Text = text;
            IsFinal = isFinal;
        }
    }

    public interface ITranscriber : IDisposable
    {
        event EventHandler<TranscriptionEventArgs> TranscriptionReceived;
        event EventHandler<string> ErrorReceived;

        bool IsConnected { get; }

        Task ConnectAsync();
        Task DisconnectAsync();
        Task SendAudioAsync(byte[] buffer);
        Task FinishStreamAsync();
        Task WaitForFinalResultAsync();
    }
}

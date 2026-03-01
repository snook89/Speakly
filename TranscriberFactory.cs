namespace Speakly.Services
{
    public static class TranscriberFactory
    {
        public static ITranscriber CreateTranscriber(string type)
        {
            switch (type?.ToLower())
            {
                case "openai":
                    return new OpenAITranscriber();
                case "deepgram":
                default:
                    // Currently Deepgram is our default and most actively supported ultra-low latency model
                    return new DeepgramTranscriber();
            }
        }
    }
}

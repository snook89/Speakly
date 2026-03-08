namespace Speakly.Services
{
    public static class TranscriberFactory
    {
        public static ITranscriber CreateTranscriber(string type)
        {
            switch (type?.ToLowerInvariant())
            {
                case "openai":
                    return new OpenAITranscriber();
                case "openrouter":
                    return new OpenRouterTranscriber();
                case "elevenlabs":
                    return new ElevenLabsTranscriber();
                case "deepgram":
                default:
                    return new DeepgramTranscriber();
            }
        }
    }
}

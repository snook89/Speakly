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
                case "openrouter":
                    return new OpenRouterTranscriber();
                case "deepgram":
                default:
                    return new DeepgramTranscriber();
            }
        }
    }
}

namespace Speakly.Services
{
    public static class TextRefinerFactory
    {
        public static ITextRefiner CreateRefiner(string provider)
        {
            switch (provider?.ToLower())
            {
                case "openrouter":
                    return new OpenRouterRefiner();
                case "cerebras":
                    return new CerebrasRefiner();
                case "openai":
                default:
                    return new OpenAIRefiner();
            }
        }
    }
}

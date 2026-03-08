using System;

namespace Speakly.Services
{
    public sealed class RefinementRequest
    {
        public string Text { get; init; } = string.Empty;
        public string Prompt { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public bool AggressiveContextRewrite { get; init; }

        public static RefinementRequest Create(
            string text,
            string prompt,
            string model,
            string contextualRefinementMode)
        {
            return new RefinementRequest
            {
                Text = text ?? string.Empty,
                Prompt = prompt ?? string.Empty,
                Model = model?.Trim() ?? string.Empty,
                AggressiveContextRewrite = string.Equals(
                    DictationExperienceService.NormalizeContextualRefinementMode(contextualRefinementMode),
                    DictationExperienceService.ContextualRefinementModeAggressiveRewrite,
                    StringComparison.OrdinalIgnoreCase)
            };
        }
    }
}

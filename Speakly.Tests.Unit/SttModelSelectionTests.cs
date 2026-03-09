using Speakly;
using Speakly.Config;
using Speakly.ViewModels;

namespace Speakly.Tests.Unit
{
    public class SttModelSelectionTests
    {
        [Fact]
        public void ResolveValidSttModelSelection_KeepsCurrentModelWhenItBelongsToProviderList()
        {
            var resolved = MainViewModel.ResolveValidSttModelSelection(
                new[] { "nova-3", "nova-2" },
                "nova-2",
                "Deepgram");

            Assert.Equal("nova-2", resolved);
        }

        [Fact]
        public void ResolveValidSttModelSelection_ReplacesStaleCrossProviderModelWithProviderDefault()
        {
            var resolved = MainViewModel.ResolveValidSttModelSelection(
                new[] { "nova-3", "nova-2", "nova-2-phonecall" },
                "scribe_v2_realtime",
                "Deepgram");

            Assert.Equal("nova-3", resolved);
        }

        [Fact]
        public void ResolveValidSttModelSelection_FallsBackToFirstModelWhenProviderDefaultIsNotPresent()
        {
            var resolved = MainViewModel.ResolveValidSttModelSelection(
                new[] { "custom-a", "custom-b" },
                "scribe_v2_realtime",
                "OpenRouter");

            Assert.Equal("custom-a", resolved);
        }

        [Fact]
        public void ResolveSttModel_DefaultsDeepgramToNova3()
        {
            var resolved = ConfigManager.ResolveSttModel("Deepgram");

            Assert.Equal("nova-3", resolved);
        }
    }
}

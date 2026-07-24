using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    [CollectionDefinition(MacReleaseCandidateTestGroup.Name, DisableParallelization = true)]
    public sealed class MacReleaseCandidateTestGroup : ICollectionFixture<MacReleaseCandidateHeadlessFixture>
    {
        public const string Name = "Mac release candidate";
    }

    public sealed class MacReleaseCandidateHeadlessFixture : AvaloniaHeadlessFixture
    {
        public MacReleaseCandidateHeadlessFixture()
            : base(global::Avalonia.Headless.AvaloniaTestIsolationLevel.PerAssembly)
        {
        }
    }
}

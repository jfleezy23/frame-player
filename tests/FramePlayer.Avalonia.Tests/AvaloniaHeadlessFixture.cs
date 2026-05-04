using System;
using System.Threading;
using Avalonia.Headless;
using FramePlayer.Avalonia;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class AvaloniaHeadlessFixture : IDisposable
    {
        private readonly string? _previousSkipRuntimeBootstrap;
        private readonly HeadlessUnitTestSession _session;

        public AvaloniaHeadlessFixture()
        {
            _previousSkipRuntimeBootstrap = Environment.GetEnvironmentVariable("FRAMEPLAYER_AVALONIA_SKIP_RUNTIME_BOOTSTRAP");
            Environment.SetEnvironmentVariable("FRAMEPLAYER_AVALONIA_SKIP_RUNTIME_BOOTSTRAP", "1");
            _session = HeadlessUnitTestSession.StartNew(typeof(App));
        }

        public void Run(Action action)
        {
            _session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _session.Dispose();
            Environment.SetEnvironmentVariable("FRAMEPLAYER_AVALONIA_SKIP_RUNTIME_BOOTSTRAP", _previousSkipRuntimeBootstrap);
        }
    }
}

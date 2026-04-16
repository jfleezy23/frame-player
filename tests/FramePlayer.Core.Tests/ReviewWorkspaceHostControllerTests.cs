using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Events;
using FramePlayer.Core.Hosting;
using FramePlayer.Core.Models;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class ReviewWorkspaceHostControllerTests
    {
        [Fact]
        public async Task OpenAsync_AddsOpenedFileToRecentFilesState()
        {
            var mediaPath = CreateTempFilePath();
            var engine = new TestVideoReviewEngine();
            var sessionCoordinator = new ReviewSessionCoordinator(engine);
            var workspaceCoordinator = new ReviewWorkspaceCoordinator(engine, sessionCoordinator);
            var recentFilesCatalog = new InMemoryRecentFilesCatalog();
            var controller = new ReviewWorkspaceHostController(workspaceCoordinator, ReviewHostCapabilities.Default, recentFilesCatalog);

            try
            {
                await controller.OpenAsync(mediaPath);

                var recentFiles = controller.CurrentViewState.RecentFiles;
                Assert.Single(recentFiles.Entries);
                Assert.Equal(Path.GetFullPath(mediaPath), recentFiles.Entries[0].FilePath);
                Assert.Equal("_1 " + Path.GetFileName(mediaPath), recentFiles.Entries[0].DisplayLabel);
                Assert.True(recentFiles.Entries[0].ExistsOnDisk);
            }
            finally
            {
                controller.Dispose();
                workspaceCoordinator.Dispose();
                sessionCoordinator.Dispose();
                engine.Dispose();
                DeleteIfExists(mediaPath);
            }
        }

        [Fact]
        public void StartupOpenFilePath_IsConsumedOnlyOnce()
        {
            var mediaPath = CreateTempFilePath();
            var engine = new TestVideoReviewEngine();
            var sessionCoordinator = new ReviewSessionCoordinator(engine);
            var workspaceCoordinator = new ReviewWorkspaceCoordinator(engine, sessionCoordinator);
            var controller = new ReviewWorkspaceHostController(workspaceCoordinator);

            try
            {
                controller.SetStartupOpenFilePath(mediaPath);

                string consumedPath;
                Assert.True(controller.TryConsumeStartupOpenFilePath(out consumedPath));
                Assert.Equal(Path.GetFullPath(mediaPath), consumedPath);
                Assert.False(controller.TryConsumeStartupOpenFilePath(out consumedPath));
                Assert.True(string.IsNullOrWhiteSpace(consumedPath));
            }
            finally
            {
                controller.Dispose();
                workspaceCoordinator.Dispose();
                sessionCoordinator.Dispose();
                engine.Dispose();
                DeleteIfExists(mediaPath);
            }
        }

        [Fact]
        public void RemoveAndClearRecentFiles_RefreshViewState()
        {
            var firstPath = CreateTempFilePath();
            var secondPath = CreateTempFilePath();
            var engine = new TestVideoReviewEngine();
            var sessionCoordinator = new ReviewSessionCoordinator(engine);
            var workspaceCoordinator = new ReviewWorkspaceCoordinator(engine, sessionCoordinator);
            var recentFilesCatalog = new InMemoryRecentFilesCatalog(firstPath, secondPath);
            var controller = new ReviewWorkspaceHostController(workspaceCoordinator, ReviewHostCapabilities.Default, recentFilesCatalog);

            try
            {
                Assert.Equal(2, controller.CurrentViewState.RecentFiles.Entries.Count);

                controller.RemoveRecentFile(firstPath);
                Assert.Single(controller.CurrentViewState.RecentFiles.Entries);
                Assert.Equal(Path.GetFullPath(secondPath), controller.CurrentViewState.RecentFiles.Entries[0].FilePath);

                controller.ClearRecentFiles();
                Assert.Empty(controller.CurrentViewState.RecentFiles.Entries);
                Assert.False(controller.CurrentViewState.RecentFiles.CanClear);
            }
            finally
            {
                controller.Dispose();
                workspaceCoordinator.Dispose();
                sessionCoordinator.Dispose();
                engine.Dispose();
                DeleteIfExists(firstPath);
                DeleteIfExists(secondPath);
            }
        }

        private static string CreateTempFilePath()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
            File.WriteAllText(tempPath, "host-controller-test");
            return tempPath;
        }

        private static void DeleteIfExists(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed class InMemoryRecentFilesCatalog : IRecentFilesCatalog
        {
            private readonly List<string> _entries;

            public InMemoryRecentFilesCatalog(params string[] entries)
            {
                _entries = entries
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(Path.GetFullPath)
                    .ToList();
            }

            public IReadOnlyList<string> Load()
            {
                return _entries.ToArray();
            }

            public void Add(string filePath)
            {
                var normalizedPath = Path.GetFullPath(filePath);
                _entries.RemoveAll(path => string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase));
                _entries.Insert(0, normalizedPath);
            }

            public void Remove(string filePath)
            {
                var normalizedPath = Path.GetFullPath(filePath);
                _entries.RemoveAll(path => string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase));
            }

            public void Clear()
            {
                _entries.Clear();
            }
        }

        private sealed class TestVideoReviewEngine : IVideoReviewEngine
        {
            private readonly VideoMediaInfo _mediaInfo;

            public TestVideoReviewEngine()
            {
                _mediaInfo = new VideoMediaInfo(
                    "test-video",
                    TimeSpan.FromSeconds(10d),
                    TimeSpan.FromMilliseconds(1000d / 30d),
                    30d,
                    1920,
                    1080,
                    "test-video",
                    0,
                    30,
                    1,
                    1,
                    1000);
                Position = ReviewPosition.Empty;
                CurrentFilePath = string.Empty;
                LastErrorMessage = string.Empty;
            }

            public bool IsMediaOpen { get; private set; }

            public bool IsPlaying { get; private set; }

            public string CurrentFilePath { get; private set; }

            public string LastErrorMessage { get; private set; }

            public VideoMediaInfo MediaInfo
            {
                get { return _mediaInfo; }
            }

            public ReviewPosition Position { get; private set; }

            public event EventHandler<VideoReviewEngineStateChangedEventArgs> StateChanged;

#pragma warning disable 0067
            public event EventHandler<FramePresentedEventArgs> FramePresented;
#pragma warning restore 0067

            public Task OpenAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
            {
                CurrentFilePath = filePath ?? string.Empty;
                IsMediaOpen = !string.IsNullOrWhiteSpace(CurrentFilePath);
                IsPlaying = false;
                Position = new ReviewPosition(TimeSpan.Zero, 0L, true, true, null, null);
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task CloseAsync()
            {
                IsMediaOpen = false;
                IsPlaying = false;
                CurrentFilePath = string.Empty;
                Position = ReviewPosition.Empty;
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task PlayAsync()
            {
                IsPlaying = true;
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task PauseAsync()
            {
                IsPlaying = false;
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task<FrameStepResult> StepForwardAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                var nextFrameIndex = (Position != null && Position.FrameIndex.HasValue ? Position.FrameIndex.Value : 0L) + 1L;
                Position = new ReviewPosition(
                    TimeSpan.FromTicks(_mediaInfo.PositionStep.Ticks * nextFrameIndex),
                    nextFrameIndex,
                    true,
                    true,
                    null,
                    null);
                RaiseStateChanged();
                return Task.FromResult(FrameStepResult.Succeeded(1, Position));
            }

            public Task<FrameStepResult> StepBackwardAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                var nextFrameIndex = Math.Max(0L, (Position != null && Position.FrameIndex.HasValue ? Position.FrameIndex.Value : 0L) - 1L);
                Position = new ReviewPosition(
                    TimeSpan.FromTicks(_mediaInfo.PositionStep.Ticks * nextFrameIndex),
                    nextFrameIndex,
                    true,
                    true,
                    null,
                    null);
                RaiseStateChanged();
                return Task.FromResult(FrameStepResult.Succeeded(-1, Position));
            }

            public Task SeekToTimeAsync(TimeSpan position, CancellationToken cancellationToken = default(CancellationToken))
            {
                var frameIndex = position <= TimeSpan.Zero
                    ? 0L
                    : (long)Math.Round(position.Ticks / (double)_mediaInfo.PositionStep.Ticks, MidpointRounding.AwayFromZero);
                Position = new ReviewPosition(position, frameIndex, true, true, null, null);
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public Task SeekToFrameAsync(long frameIndex, CancellationToken cancellationToken = default(CancellationToken))
            {
                var normalizedFrameIndex = Math.Max(0L, frameIndex);
                Position = new ReviewPosition(
                    TimeSpan.FromTicks(_mediaInfo.PositionStep.Ticks * normalizedFrameIndex),
                    normalizedFrameIndex,
                    true,
                    true,
                    null,
                    null);
                RaiseStateChanged();
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }

            private void RaiseStateChanged()
            {
                StateChanged?.Invoke(
                    this,
                    new VideoReviewEngineStateChangedEventArgs(
                        IsMediaOpen,
                        IsPlaying,
                        CurrentFilePath,
                        LastErrorMessage,
                        _mediaInfo,
                        Position));
            }
        }
    }
}

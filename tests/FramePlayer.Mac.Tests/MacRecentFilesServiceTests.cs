using System;
using System.IO;
using System.Linq;
using FramePlayer.Mac.Services;
using Xunit;

namespace FramePlayer.Mac.Tests
{
    public sealed class MacRecentFilesServiceTests
    {
        [Fact]
        public void Add_KeepsMostRecentExistingFilesWithoutDuplicates()
        {
            var root = CreateTempDirectory();
            try
            {
                var storagePath = Path.Combine(root, "state", "recent-files.txt");
                var service = new MacRecentFilesService(storagePath);
                var first = CreateVideoFile(root, "first.mp4");
                var second = CreateVideoFile(root, "second.mov");
                var missing = Path.Combine(root, "missing.mkv");

                service.Add(first);
                service.Add(second);
                service.Add(missing);
                service.Add(first);

                Assert.Equal(new[] { first, second }, service.Load());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Add_LimitsRecentFilesToTenEntries()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new MacRecentFilesService(Path.Combine(root, "recent-files.txt"));
                var files = Enumerable.Range(0, 12)
                    .Select(index => CreateVideoFile(root, "video-" + index.ToString("00") + ".mp4"))
                    .ToArray();

                foreach (var file in files)
                {
                    service.Add(file);
                }

                var recent = service.Load();
                Assert.Equal(10, recent.Count);
                Assert.Equal(files[11], recent[0]);
                Assert.Equal(files[2], recent[9]);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "frame-player-mac-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string CreateVideoFile(string root, string fileName)
        {
            var path = Path.Combine(root, fileName);
            File.WriteAllText(path, string.Empty);
            return path;
        }
    }
}

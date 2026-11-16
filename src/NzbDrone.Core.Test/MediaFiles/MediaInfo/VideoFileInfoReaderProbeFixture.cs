using System;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Categories;

namespace NzbDrone.Core.Test.MediaFiles.MediaInfo
{
    [TestFixture]
    [DiskAccessTest]
    public class VideoFileInfoReaderProbeFixture : CoreTest<VideoFileInfoReader>
    {
        private string _validFile;
        private string _garbageFile;

        [SetUp]
        public void Setup()
        {
            _validFile = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "H264_sample.mp4");

            var folder = GetTempFilePath();
            Directory.CreateDirectory(folder);
            _garbageFile = Path.Combine(folder, "Decoy.Release.720p.wmv");

            // Not a media container: ffprobe answers "Invalid data found when processing input"
            var bytes = new byte[64 * 1024];
            new Random(23).NextBytes(bytes);
            File.WriteAllBytes(_garbageFile, bytes);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.OpenReadStream(It.IsAny<string>()))
                  .Returns<string>(s => new FileStream(s, FileMode.Open, FileAccess.Read));

            GivenFFProbeHealthy(true);
        }

        private void GivenFFProbeHealthy(bool healthy)
        {
            Mocker.GetMock<IFFProbeSelfCheck>()
                  .Setup(s => s.IsHealthy())
                  .Returns(healthy);
        }

        private void GivenCorruptFileDetection(bool enabled)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.WhisparrCorruptFileDetection)
                  .Returns(enabled);
        }

        [Test]
        public void TestR2_valid_file_probe_succeeds()
        {
            var result = Subject.ProbeMediaInfo(_validFile);

            result.Outcome.Should().Be(MediaProbeOutcome.Succeeded);
            result.MediaInfo.Should().NotBeNull();
            result.MediaInfo.RunTime.Seconds.Should().Be(10);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TestR2_ffprobe_rejecting_readable_file_with_healthy_ffprobe_is_corrupt(bool corruptFileDetection)
        {
            GivenCorruptFileDetection(corruptFileDetection);

            var result = Subject.ProbeMediaInfo(_garbageFile);

            result.Outcome.Should().Be(MediaProbeOutcome.Corrupt);
            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TestR2_ffprobe_rejecting_file_when_self_check_fails_is_not_corrupt(bool corruptFileDetection)
        {
            GivenCorruptFileDetection(corruptFileDetection);
            GivenFFProbeHealthy(false);

            var result = Subject.ProbeMediaInfo(_garbageFile);

            result.Outcome.Should().Be(MediaProbeOutcome.Failed);
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void TestR2_ffprobe_rejecting_file_that_whisparr_cannot_read_is_not_corrupt()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.OpenReadStream(_garbageFile))
                  .Throws(new UnauthorizedAccessException("Access to the path is denied."));

            var result = Subject.ProbeMediaInfo(_garbageFile);

            result.Outcome.Should().Be(MediaProbeOutcome.Failed);
            ExceptionVerification.IgnoreErrors();
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void TestR2_self_check_only_runs_when_ffprobe_rejects_a_file()
        {
            Subject.ProbeMediaInfo(_validFile);

            Mocker.GetMock<IFFProbeSelfCheck>().Verify(s => s.IsHealthy(), Times.Never());
        }

        [Test]
        public void TestR2_disk_image_is_not_probed()
        {
            var iso = Path.Combine(Path.GetDirectoryName(_garbageFile), "movie.iso");
            File.WriteAllBytes(iso, new byte[16]);

            var result = Subject.ProbeMediaInfo(iso);

            result.Outcome.Should().Be(MediaProbeOutcome.NotProbed);
            result.MediaInfo.Should().BeNull();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TestR2_get_media_info_return_value_is_unchanged_for_rejected_files(bool healthy)
        {
            GivenFFProbeHealthy(healthy);

            GivenCorruptFileDetection(true);
            Subject.GetMediaInfo(_garbageFile).RunTime.Should().Be(TimeSpan.FromMinutes(-1));

            GivenCorruptFileDetection(false);
            Subject.GetMediaInfo(_garbageFile).Should().BeNull();

            ExceptionVerification.ExpectedErrors(2);
        }

        [Test]
        public void TestR2_probe_media_info_matches_get_media_info_sentinel_when_detection_enabled()
        {
            GivenCorruptFileDetection(true);

            var result = Subject.ProbeMediaInfo(_garbageFile);

            result.MediaInfo.RunTime.Should().Be(TimeSpan.FromMinutes(-1));
            ExceptionVerification.ExpectedErrors(1);
        }
    }
}

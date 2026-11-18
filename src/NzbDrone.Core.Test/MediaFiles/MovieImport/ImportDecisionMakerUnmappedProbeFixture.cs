using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport
{
    // A scene file that can't be matched is saved as an unmapped file. A failed or corrupt probe
    // (the -1min sentinel) must not be stored as its media info.
    [TestFixture]
    public class ImportDecisionMakerUnmappedProbeFixture : CoreTest<ImportDecisionMaker>
    {
        private List<string> _videoFiles;

        [SetUp]
        public void Setup()
        {
            _videoFiles = new List<string> { @"C:\Test\Unsorted\Studio.24.01.01.Some.Scene.XXX.1080p.mkv".AsOsAgnostic() };

            // No match: GetRemoteMovie returns a movie without a ForeignId, so the file is unmapped.
            Mocker.GetMock<ISceneIdentificationService>()
                  .Setup(s => s.Identify(It.IsAny<string>(), It.IsAny<ParsedMovieInfo>()))
                  .Returns(new Movie());

            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Returns<LocalMovie, DownloadClientItem>((localMovie, _) => localMovie);
        }

        // Mirrors VideoFileInfoReader: GetMediaInfo is ProbeMediaInfo(...).MediaInfo.
        private void GivenProbe(MediaInfoModel mediaInfo, MediaProbeOutcome outcome)
        {
            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.ProbeMediaInfo(It.IsAny<string>()))
                  .Returns(new MediaInfoProbeResult(mediaInfo, outcome));

            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.GetMediaInfo(It.IsAny<string>()))
                  .Returns(mediaInfo);
        }

        private MovieFile GetUnmappedFile()
        {
            var result = Subject.GetSceneImportDecisions(_videoFiles, null, null, true, false);

            return result.UnmappedFiles.Single();
        }

        [TestCase(MediaProbeOutcome.Failed)]
        [TestCase(MediaProbeOutcome.Corrupt)]
        public void should_not_store_sentinel_media_info_on_unmapped_file(MediaProbeOutcome outcome)
        {
            GivenProbe(new MediaInfoModel { RunTime = TimeSpan.FromMinutes(-1) }, outcome);

            GetUnmappedFile().MediaInfo.Should().BeNull();
        }

        [Test]
        public void should_store_media_info_from_successful_probe_on_unmapped_file()
        {
            var probed = new MediaInfoModel { Width = 1920, Height = 1080, SchemaRevision = VideoFileInfoReader.CURRENT_MEDIA_INFO_SCHEMA_REVISION };
            GivenProbe(probed, MediaProbeOutcome.Succeeded);

            GetUnmappedFile().MediaInfo.Should().BeSameAs(probed);
        }
    }
}

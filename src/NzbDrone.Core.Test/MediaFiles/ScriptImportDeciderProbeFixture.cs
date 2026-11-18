using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    // When the import script asks for a rename, the file is re-probed. Only a successful probe may
    // replace the media info recorded at import; a failed or corrupt probe (the -1min sentinel) must not.
    [TestFixture]
    public class ScriptImportDeciderProbeFixture : CoreTest<ImportScriptService>
    {
        private Movie _movie;
        private LocalMovie _localMovie;
        private MovieFile _movieFile;
        private MediaInfoModel _importedMediaInfo;
        private string _scriptMediaFile;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie
            {
                Id = 1,
                Path = @"C:\Test\Movie".AsOsAgnostic(),
                MovieMetadata = new MovieMetadata { Title = "Movie", ForeignId = "1" }
            };

            _scriptMediaFile = @"C:\Test\Movie\movie.mkv".AsOsAgnostic();

            _importedMediaInfo = new MediaInfoModel
            {
                Width = 1920,
                Height = 1080,
                RunTime = TimeSpan.FromMinutes(30),
                AudioLanguages = new List<string>(),
                Subtitles = new List<string>(),
                SchemaRevision = VideoFileInfoReader.CURRENT_MEDIA_INFO_SCHEMA_REVISION
            };

            _movieFile = new MovieFile
            {
                RelativePath = "movie.mkv",
                Quality = new QualityModel(Quality.HDTV720p),
                MediaInfo = _importedMediaInfo
            };

            _localMovie = new LocalMovie
            {
                Movie = _movie,
                MediaInfo = _importedMediaInfo,
                OldFiles = new List<DeletedMovieFile>()
            };

            Mocker.GetMock<IConfigService>().SetupGet(s => s.UseScriptImport).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.ScriptImportPath).Returns("script.sh");

            Mocker.GetMock<IProcessProvider>()
                  .Setup(s => s.StartAndCapture(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>()))
                  .Returns(new ProcessOutput
                  {
                      ExitCode = 0,
                      Lines = new List<ProcessOutputLine> { new ProcessOutputLine(ProcessOutputLevel.Standard, "[MoveStatus] RenameRequested") }
                  });
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

        private ScriptImportDecision TryImport()
        {
            return Subject.TryImport(@"C:\Test\Unsorted\movie.mkv".AsOsAgnostic(), _scriptMediaFile, _localMovie, _movieFile, TransferMode.Move);
        }

        [TestCase(MediaProbeOutcome.Failed)]
        [TestCase(MediaProbeOutcome.Corrupt)]
        public void should_keep_imported_media_info_when_rename_probe_returns_sentinel(MediaProbeOutcome outcome)
        {
            GivenProbe(new MediaInfoModel { RunTime = TimeSpan.FromMinutes(-1) }, outcome);

            TryImport().Should().Be(ScriptImportDecision.RenameRequested);

            _movieFile.MediaInfo.Should().BeSameAs(_importedMediaInfo);
        }

        [Test]
        public void should_keep_imported_media_info_when_rename_probe_returns_null()
        {
            GivenProbe(null, MediaProbeOutcome.Failed);

            TryImport().Should().Be(ScriptImportDecision.RenameRequested);

            _movieFile.MediaInfo.Should().BeSameAs(_importedMediaInfo);
        }

        [Test]
        public void should_use_media_info_from_successful_rename_probe()
        {
            var probed = new MediaInfoModel { Width = 3840, Height = 2160, SchemaRevision = VideoFileInfoReader.CURRENT_MEDIA_INFO_SCHEMA_REVISION };
            GivenProbe(probed, MediaProbeOutcome.Succeeded);

            TryImport().Should().Be(ScriptImportDecision.RenameRequested);

            _movieFile.MediaInfo.Should().BeSameAs(probed);
        }
    }
}

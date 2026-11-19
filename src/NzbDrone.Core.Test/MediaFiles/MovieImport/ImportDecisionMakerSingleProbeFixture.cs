using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators;
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport
{
    // One import decision run probes each file once: the non-sample count, Augment and NotSampleSpecification
    // share that probe. Real AggregationService, DetectSample and NotSampleSpecification; only the reader is faked.
    [TestFixture]
    public class ImportDecisionMakerSingleProbeFixture : CoreTest<ImportDecisionMaker>
    {
        private List<string> _videoFiles;
        private Movie _movie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(e => e.Path = @"C:\Test\Movie".AsOsAgnostic())
                                   .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                   .With(e => e.MovieMetadata = new MovieMetadata { Runtime = 30, ItemType = ItemType.Movie })
                                   .Build();

            _videoFiles = new List<string>
            {
                @"C:\Test\Unsorted\The.Movie.2018.DVDRip.XviD-OSiTV.mkv".AsOsAgnostic(),
                @"C:\Test\Unsorted\The.Movie.2018.DVDRip.XviD-OSiTV.part2.mkv".AsOsAgnostic()
            };

            Mocker.GetMock<IMediaFileService>()
                  .Setup(c => c.FilterExistingFiles(_videoFiles, It.IsAny<Movie>()))
                  .Returns(_videoFiles);

            Mocker.SetConstant<IEnumerable<IAggregateLocalMovie>>(new List<IAggregateLocalMovie>());
            Mocker.SetConstant<IAggregationService>(Mocker.Resolve<AggregationService>());
            Mocker.SetConstant<IDetectSample>(Mocker.Resolve<DetectSample>());
            Mocker.SetConstant<IEnumerable<IImportDecisionEngineSpecification>>(new List<IImportDecisionEngineSpecification>
            {
                new NotSampleSpecification(Mocker.Resolve<IDetectSample>(), TestLogger)
            });

            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrValidateRuntimeLimit).Returns(1);
        }

        // Mirrors VideoFileInfoReader: with detection on a rejected probe returns the -1min sentinel, otherwise null.
        private void GivenProbe(MediaProbeOutcome outcome, bool corruptFileDetection)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrCorruptFileDetection).Returns(corruptFileDetection);

            MediaInfoModel mediaInfo;

            if (outcome == MediaProbeOutcome.Succeeded)
            {
                mediaInfo = new MediaInfoModel { RunTime = TimeSpan.FromMinutes(30) };
            }
            else
            {
                mediaInfo = corruptFileDetection ? new MediaInfoModel { RunTime = TimeSpan.FromMinutes(-1) } : null;
            }

            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.ProbeMediaInfo(It.IsAny<string>()))
                  .Returns(new MediaInfoProbeResult(mediaInfo, outcome));

            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.GetMediaInfo(It.IsAny<string>()))
                  .Returns(mediaInfo);

            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.GetRunTime(It.IsAny<string>()))
                  .Returns(mediaInfo?.RunTime);
        }

        private void VerifyEachFileProbedOnce()
        {
            foreach (var file in _videoFiles)
            {
                Mocker.GetMock<IVideoFileInfoReader>().Verify(s => s.ProbeMediaInfo(file), Times.Once());
            }

            Mocker.GetMock<IVideoFileInfoReader>().Verify(s => s.ProbeMediaInfo(It.IsAny<string>()), Times.Exactly(_videoFiles.Count));
            Mocker.GetMock<IVideoFileInfoReader>().Verify(s => s.GetRunTime(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IVideoFileInfoReader>().Verify(s => s.GetMediaInfo(It.IsAny<string>()), Times.Never());
        }

        [TestCase(MediaProbeOutcome.Succeeded, true)]
        [TestCase(MediaProbeOutcome.Succeeded, false)]
        [TestCase(MediaProbeOutcome.Corrupt, true)]
        [TestCase(MediaProbeOutcome.Corrupt, false)]
        [TestCase(MediaProbeOutcome.Failed, true)]
        [TestCase(MediaProbeOutcome.Failed, false)]
        public void should_probe_each_file_once_per_decision_run(MediaProbeOutcome outcome, bool corruptFileDetection)
        {
            GivenProbe(outcome, corruptFileDetection);

            var decisions = Subject.GetImportDecisions(_videoFiles, _movie, null, null, true);

            decisions.Should().HaveCount(_videoFiles.Count);
            VerifyEachFileProbedOnce();
            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_approve_from_the_shared_successful_probe()
        {
            GivenProbe(MediaProbeOutcome.Succeeded, false);

            var decisions = Subject.GetImportDecisions(_videoFiles, _movie, null, null, true);

            decisions.Should().OnlyContain(d => d.Approved);
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_decide_the_sample_check_from_the_shared_failed_probe()
        {
            GivenProbe(MediaProbeOutcome.Failed, false);

            var decisions = Subject.GetImportDecisions(_videoFiles, _movie, null, null, true);

            decisions.Should().OnlyContain(d => d.Rejections.Select(r => r.Reason).SequenceEqual(new[] { ImportRejectionReason.SampleIndeterminate }));
            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }
    }
}

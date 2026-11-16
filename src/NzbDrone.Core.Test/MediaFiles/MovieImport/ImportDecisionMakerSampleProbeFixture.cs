using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
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
    // R5: a Sample verdict is only permanent when it was computed from the probe recorded on the file.
    // Real AggregationService, DetectSample and NotSampleSpecification; only the reader is faked so that
    // successive probes of the same file can disagree (e.g. ffprobe breaks between the two calls).
    [TestFixture]
    public class ImportDecisionMakerSampleProbeFixture : CoreTest<ImportDecisionMaker>
    {
        private static readonly HashSet<FailDownloads> AllOptions = Enum.GetValues<FailDownloads>().ToHashSet();

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

            _videoFiles = new List<string> { @"C:\Test\Unsorted\The.Movie.2018.DVDRip.XviD-OSiTV.mkv".AsOsAgnostic() };

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

            // Corrupt-file detection on, runtime validation off: the configuration where a later failed probe
            // surfaces as Sample through the -1min sentinel.
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrCorruptFileDetection).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrValidateRuntime).Returns(false);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrValidateRuntimeLimit).Returns(1);
        }

        private void GivenAggregationProbe(MediaProbeOutcome outcome, TimeSpan runtime)
        {
            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.ProbeMediaInfo(It.IsAny<string>()))
                  .Returns(new MediaInfoProbeResult(new MediaInfoModel { RunTime = runtime }, outcome));
        }

        private void GivenLaterProbesReturnFailedSentinel()
        {
            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.GetRunTime(It.IsAny<string>()))
                  .Returns(TimeSpan.FromMinutes(-1));

            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.GetMediaInfo(It.IsAny<string>()))
                  .Returns(new MediaInfoModel { RunTime = TimeSpan.FromMinutes(-1) });
        }

        private ImportDecision GetDecision()
        {
            return Subject.GetImportDecisions(_videoFiles, _movie, null, null, true).Single();
        }

        private static bool IsPermanent(ImportDecision decision)
        {
            return PermanentImportRejection.AreAllPermanent(new[] { new ImportResult(decision, "Rejected") }, AllOptions);
        }

        [Test]
        public void TestR5_healthy_file_is_not_a_permanent_sample_when_a_later_probe_fails()
        {
            GivenAggregationProbe(MediaProbeOutcome.Succeeded, TimeSpan.FromMinutes(30));
            GivenLaterProbesReturnFailedSentinel();

            var decision = GetDecision();

            decision.Rejections.Select(r => r.Reason).Should().NotContain(ImportRejectionReason.Sample);
            IsPermanent(decision).Should().BeFalse();
            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void TestR5_sample_verdict_uses_the_recorded_successful_probe()
        {
            GivenAggregationProbe(MediaProbeOutcome.Succeeded, TimeSpan.FromSeconds(60));
            GivenLaterProbesReturnFailedSentinel();

            var decision = GetDecision();

            decision.Rejections.Select(r => r.Reason).Should().Equal(ImportRejectionReason.Sample);
            IsPermanent(decision).Should().BeTrue();
            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void TestR5_sample_check_does_not_probe_again_after_a_successful_probe()
        {
            GivenAggregationProbe(MediaProbeOutcome.Succeeded, TimeSpan.FromMinutes(30));
            GivenLaterProbesReturnFailedSentinel();

            GetDecision();

            // GetNonSampleVideoFileCount still runs before any file is probed; the spec itself must not re-probe.
            Mocker.GetMock<IVideoFileInfoReader>().Verify(s => s.ProbeMediaInfo(It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IVideoFileInfoReader>().Verify(s => s.GetRunTime(It.IsAny<string>()), Times.Once());
            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void TestR5_failed_recorded_probe_keeps_todays_sample_rejection_but_transient()
        {
            GivenAggregationProbe(MediaProbeOutcome.Failed, TimeSpan.FromMinutes(-1));
            GivenLaterProbesReturnFailedSentinel();

            var decision = GetDecision();

            decision.Rejections.Select(r => r.Reason).Should().Equal(ImportRejectionReason.Sample);
            IsPermanent(decision).Should().BeFalse();
            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }
    }
}

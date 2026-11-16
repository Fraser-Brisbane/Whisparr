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
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport
{
    // R4: the classification is based on the ffprobe outcome, with WhisparrCorruptFileDetection on and off.
    [TestFixture]
    public class ImportDecisionMakerCorruptFileFixture : CoreTest<ImportDecisionMaker>
    {
        private static readonly HashSet<FailDownloads> AllOptions = Enum.GetValues<FailDownloads>().ToHashSet();
        private static readonly TimeSpan Sentinel = TimeSpan.FromMinutes(-1);

        private List<string> _videoFiles;
        private Movie _movie;
        private Mock<IImportDecisionEngineSpecification> _notQualityUpgrade;
        private List<IImportDecisionEngineSpecification> _specifications;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(e => e.Path = @"C:\Test\Movie".AsOsAgnostic())
                                   .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                   .With(e => e.MovieMetadata = new MovieMetadata { Runtime = 30, ItemType = ItemType.Movie })
                                   .Build();

            _videoFiles = new List<string> { @"C:\Test\Unsorted\The.Movie.2018.DVDRip.XviD-OSiTV.wmv".AsOsAgnostic() };

            Mocker.GetMock<IMediaFileService>()
                  .Setup(c => c.FilterExistingFiles(_videoFiles, It.IsAny<Movie>()))
                  .Returns(_videoFiles);

            // Real sample detection, so the reason that "wins" today is produced by the real code
            Mocker.SetConstant<IDetectSample>(Mocker.Resolve<DetectSample>());

            _notQualityUpgrade = new Mock<IImportDecisionEngineSpecification>();
            _notQualityUpgrade.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                              .Returns(ImportSpecDecision.Reject(ImportRejectionReason.NotQualityUpgrade, "Not an upgrade"));

            _specifications = new List<IImportDecisionEngineSpecification>
            {
                new NotSampleSpecification(Mocker.Resolve<IDetectSample>(), TestLogger)
            };

            Mocker.SetConstant<IEnumerable<IImportDecisionEngineSpecification>>(_specifications);

            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrValidateRuntimeLimit).Returns(1);
        }

        private void GivenValidateRuntime(bool enabled)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrValidateRuntime).Returns(enabled);
        }

        private void GivenNotQualityUpgrade()
        {
            _specifications.Add(_notQualityUpgrade.Object);
        }

        // Mirrors VideoFileInfoReader: with detection on a rejected probe returns the -1min sentinel, otherwise null.
        private void GivenProbe(MediaProbeOutcome outcome, bool corruptFileDetection, TimeSpan? runtime = null, bool existingFile = false)
        {
            MediaInfoModel mediaInfo;

            if (outcome == MediaProbeOutcome.Succeeded)
            {
                mediaInfo = new MediaInfoModel { RunTime = runtime ?? TimeSpan.FromMinutes(30) };
            }
            else
            {
                mediaInfo = corruptFileDetection ? new MediaInfoModel { RunTime = Sentinel } : null;
            }

            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.GetRunTime(It.IsAny<string>()))
                  .Returns(mediaInfo?.RunTime);

            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Callback<LocalMovie, DownloadClientItem>((localMovie, _) =>
                  {
                      localMovie.Movie = _movie;
                      localMovie.MediaInfo = mediaInfo;
                      localMovie.MediaProbeOutcome = outcome;
                      localMovie.ExistingFile = existingFile;
                  });
        }

        private ImportDecision GetDecision()
        {
            return Subject.GetImportDecisions(_videoFiles, _movie, null, null, true).Single();
        }

        private static IEnumerable<ImportRejectionReason> Reasons(ImportDecision decision)
        {
            return decision.Rejections.Select(r => r.Reason);
        }

        private static bool IsPermanent(ImportDecision decision)
        {
            return PermanentImportRejection.AreAllPermanent(new[] { new ImportResult(decision, "Rejected") }, AllOptions);
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void TestR4_corrupt_probe_yields_a_single_corrupt_file_rejection(bool corruptFileDetection, bool validateRuntime)
        {
            GivenValidateRuntime(validateRuntime);
            GivenProbe(MediaProbeOutcome.Corrupt, corruptFileDetection);

            var decision = GetDecision();

            Reasons(decision).Should().Equal(ImportRejectionReason.CorruptFile);
            IsPermanent(decision).Should().BeTrue();
            ExceptionVerification.IgnoreErrors();
            ExceptionVerification.IgnoreWarns();
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void TestR4_corrupt_probe_keeps_unrelated_rejections(bool corruptFileDetection, bool validateRuntime)
        {
            GivenValidateRuntime(validateRuntime);
            GivenNotQualityUpgrade();
            GivenProbe(MediaProbeOutcome.Corrupt, corruptFileDetection);

            var decision = GetDecision();

            Reasons(decision).Should().BeEquivalentTo(new[] { ImportRejectionReason.NotQualityUpgrade, ImportRejectionReason.CorruptFile });
            IsPermanent(decision).Should().BeFalse();
            ExceptionVerification.IgnoreErrors();
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void TestR4_corrupt_probe_does_not_reject_a_file_that_would_otherwise_be_approved()
        {
            GivenValidateRuntime(false);
            GivenProbe(MediaProbeOutcome.Corrupt, false, existingFile: true);

            var decision = GetDecision();

            decision.Approved.Should().BeTrue();
            ExceptionVerification.IgnoreErrors();
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void TestR4_probe_infrastructure_failure_is_never_permanent(bool corruptFileDetection, bool validateRuntime)
        {
            GivenValidateRuntime(validateRuntime);
            GivenProbe(MediaProbeOutcome.Failed, corruptFileDetection);

            var decision = GetDecision();

            decision.Approved.Should().BeFalse();
            Reasons(decision).Should().NotContain(ImportRejectionReason.CorruptFile);
            IsPermanent(decision).Should().BeFalse();
            ExceptionVerification.IgnoreErrors();
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void TestR5_real_sample_with_successful_probe_is_permanent()
        {
            GivenValidateRuntime(false);
            GivenProbe(MediaProbeOutcome.Succeeded, false, TimeSpan.FromSeconds(60));

            var decision = GetDecision();

            Reasons(decision).Should().Equal(ImportRejectionReason.Sample);
            IsPermanent(decision).Should().BeTrue();

            // The runtime-mismatch warning is logged even when runtime validation is off
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void TestR5_real_sample_rejected_by_runtime_validation_is_transient()
        {
            // Runtime validation replaces the decision with Error, which carries runtime mismatches and stays transient
            GivenValidateRuntime(true);
            GivenProbe(MediaProbeOutcome.Succeeded, false, TimeSpan.FromSeconds(60));

            var decision = GetDecision();

            Reasons(decision).Should().Equal(ImportRejectionReason.Error);
            IsPermanent(decision).Should().BeFalse();
            ExceptionVerification.IgnoreWarns();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.Download.PermanentRejectionTests
{
    [TestFixture]
    public class PermanentImportRejectionFixture
    {
        private static readonly HashSet<FailDownloads> AllOptions = Enum.GetValues<FailDownloads>().ToHashSet();

        private static FailDownloads? ExpectedOption(ImportRejectionReason reason, MediaProbeOutcome outcome)
        {
            // The complete allowlist. Anything not listed here must be transient.
            if (reason == ImportRejectionReason.Sample && outcome == MediaProbeOutcome.Succeeded)
            {
                return FailDownloads.Samples;
            }

            if (reason == ImportRejectionReason.CorruptFile || reason == ImportRejectionReason.UnsupportedExtension)
            {
                return FailDownloads.CorruptOrUnreadable;
            }

            return null;
        }

        private static ImportResult Rejected(MediaProbeOutcome outcome, params ImportRejectionReason[] reasons)
        {
            var localMovie = new LocalMovie { Path = "/downloads/file.mkv", MediaProbeOutcome = outcome };
            var rejections = reasons.Select(r => new ImportRejection(r, r.ToString())).ToArray();

            return new ImportResult(new ImportDecision(localMovie, rejections), "Rejected");
        }

        [Test]
        public void TestR5_every_rejection_reason_is_classified_by_the_explicit_allowlist()
        {
            foreach (var reason in Enum.GetValues<ImportRejectionReason>())
            {
                foreach (var outcome in Enum.GetValues<MediaProbeOutcome>())
                {
                    PermanentImportRejection.GetFailDownloadsOption(reason, outcome)
                        .Should().Be(ExpectedOption(reason, outcome), $"{reason} with probe outcome {outcome}");
                }
            }
        }

        [Test]
        public void TestR5_allowlist_contains_exactly_sample_corrupt_and_unsupported_extension()
        {
            var permanent = Enum.GetValues<ImportRejectionReason>()
                .Where(r => Enum.GetValues<MediaProbeOutcome>().Any(o => PermanentImportRejection.GetFailDownloadsOption(r, o) != null))
                .ToList();

            permanent.Should().BeEquivalentTo(new[]
            {
                ImportRejectionReason.Sample,
                ImportRejectionReason.CorruptFile,
                ImportRejectionReason.UnsupportedExtension
            });
        }

        [TestCase(MediaProbeOutcome.Failed)]
        [TestCase(MediaProbeOutcome.NotProbed)]
        [TestCase(MediaProbeOutcome.Corrupt)]
        public void TestR5_sample_without_a_successful_probe_is_transient(MediaProbeOutcome outcome)
        {
            PermanentImportRejection.AreAllPermanent(new[] { Rejected(outcome, ImportRejectionReason.Sample) }, AllOptions)
                .Should().BeFalse();
        }

        [Test]
        public void TestR6_empty_result_set_is_never_permanent()
        {
            PermanentImportRejection.AreAllPermanent(new List<ImportResult>(), AllOptions).Should().BeFalse();
        }

        [Test]
        public void TestR6_several_permanent_results_are_permanent_when_their_options_are_enabled()
        {
            var results = new[]
            {
                Rejected(MediaProbeOutcome.Succeeded, ImportRejectionReason.Sample),
                Rejected(MediaProbeOutcome.Corrupt, ImportRejectionReason.CorruptFile),
                Rejected(MediaProbeOutcome.NotProbed, ImportRejectionReason.UnsupportedExtension)
            };

            PermanentImportRejection.AreAllPermanent(results, new HashSet<FailDownloads> { FailDownloads.Samples, FailDownloads.CorruptOrUnreadable })
                .Should().BeTrue();
        }

        [Test]
        public void TestR6_not_permanent_when_one_reason_option_is_disabled()
        {
            var results = new[]
            {
                Rejected(MediaProbeOutcome.Succeeded, ImportRejectionReason.Sample),
                Rejected(MediaProbeOutcome.Corrupt, ImportRejectionReason.CorruptFile)
            };

            PermanentImportRejection.AreAllPermanent(results, new HashSet<FailDownloads> { FailDownloads.Samples }).Should().BeFalse();
            PermanentImportRejection.AreAllPermanent(results, new HashSet<FailDownloads> { FailDownloads.CorruptOrUnreadable }).Should().BeFalse();
        }

        [Test]
        public void TestR6_a_transient_rejection_on_the_same_file_makes_it_transient()
        {
            var results = new[] { Rejected(MediaProbeOutcome.Succeeded, ImportRejectionReason.Sample, ImportRejectionReason.NotQualityUpgrade) };

            PermanentImportRejection.AreAllPermanent(results, AllOptions).Should().BeFalse();
        }

        [Test]
        public void TestR6_a_transient_file_in_the_set_makes_the_set_transient()
        {
            var results = new[]
            {
                Rejected(MediaProbeOutcome.Succeeded, ImportRejectionReason.Sample),
                Rejected(MediaProbeOutcome.Succeeded, ImportRejectionReason.NotQualityUpgrade)
            };

            PermanentImportRejection.AreAllPermanent(results, AllOptions).Should().BeFalse();
        }

        [Test]
        public void TestR6_a_result_that_is_not_rejected_makes_the_set_transient()
        {
            var imported = new ImportResult(new ImportDecision(new LocalMovie { Path = "/downloads/good.mkv" }));
            var skipped = new ImportResult(new ImportDecision(new LocalMovie { Path = "/downloads/skipped.mkv" }), "Failed to move");

            PermanentImportRejection.AreAllPermanent(new[] { Rejected(MediaProbeOutcome.Succeeded, ImportRejectionReason.Sample), imported }, AllOptions)
                .Should().BeFalse();
            PermanentImportRejection.AreAllPermanent(new[] { Rejected(MediaProbeOutcome.Succeeded, ImportRejectionReason.Sample), skipped }, AllOptions)
                .Should().BeFalse();
        }

        [Test]
        public void TestR6_a_rejected_result_without_a_local_movie_is_transient_for_sample()
        {
            var result = new ImportResult(new ImportDecision(null, new ImportRejection(ImportRejectionReason.Sample, "Sample")), "Sample");

            PermanentImportRejection.AreAllPermanent(new[] { result }, AllOptions).Should().BeFalse();
        }
    }
}

using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.FailedDownloadServiceTests
{
    // A download failed by RejectedImportService must reach the blocklist and history with the rejection reason.
    [TestFixture]
    public class RejectedImportFailureMessageFixture : CoreTest<FailedDownloadService>
    {
        private TrackedDownload _trackedDownload;
        private DownloadFailedEvent _publishedEvent;

        [SetUp]
        public void Setup()
        {
            var downloadItem = Builder<DownloadClientItem>.CreateNew()
                .With(d => d.Status = DownloadItemStatus.Completed)
                .With(d => d.Title = "Studio.14.07.06.Performer.XXX.720p.MP4-GRP")
                .With(d => d.DownloadClientInfo = new DownloadClientItemClientInfo { Name = "Client" })
                .Build();

            _trackedDownload = new TrackedDownload
            {
                DownloadItem = downloadItem,
                RemoteMovie = new RemoteMovie { Release = new ReleaseInfo { IndexerId = 1 } },
                State = TrackedDownloadState.ImportPending
            };

            var grabHistory = Builder<MovieHistory>.CreateListOfSize(1)
                .All()
                .With(h => h.Data = new Dictionary<string, string>
                {
                    { "publishedDate", DateTime.UtcNow.ToString("s") },
                    { "protocol", "1" }
                })
                .BuildList();

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.Find(downloadItem.DownloadId, MovieHistoryEventType.Grabbed))
                .Returns(grabHistory);

            Mocker.GetMock<IEventAggregator>()
                .Setup(s => s.PublishEvent(It.IsAny<DownloadFailedEvent>()))
                .Callback<DownloadFailedEvent>(e => _publishedEvent = e);
        }

        private void GivenFailDownloads(params FailDownloads[] failDownloads)
        {
            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                .Setup(s => s.GetSettings(1))
                .Returns(new CachedIndexerSettings { FailDownloads = new HashSet<FailDownloads>(failDownloads) });
        }

        private static ImportResult Rejection(ImportRejectionReason reason, string message)
        {
            var localMovie = new LocalMovie { MediaProbeOutcome = MediaProbeOutcome.Succeeded };

            return new ImportResult(new ImportDecision(localMovie, new ImportRejection(reason, message)), message);
        }

        private RejectedImportService RejectedImportService => Mocker.Resolve<RejectedImportService>();

        private void AssertFailureMessage(string expected)
        {
            _trackedDownload.State.Should().Be(TrackedDownloadState.FailedPending);

            Subject.ProcessFailed(_trackedDownload);

            _publishedEvent.Should().NotBeNull();
            _publishedEvent.Message.Should().Be(expected);

            Mocker.Resolve<BlocklistService>().Handle(_publishedEvent);
            Mocker.GetMock<IBlocklistRepository>()
                .Verify(v => v.Insert(It.Is<Blocklist>(b => b.Message == expected)), Times.Once());

            Mocker.Resolve<HistoryService>().Handle(_publishedEvent);
            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<MovieHistory>(h => h.Data["Message"] == expected)), Times.Once());
        }

        [Test]
        public void should_report_sample_rejection_as_failure_reason()
        {
            GivenFailDownloads(FailDownloads.Samples);

            RejectedImportService.Process(_trackedDownload, Rejection(ImportRejectionReason.Sample, "Sample"));

            AssertFailureMessage("Import rejected permanently: Sample");
        }

        [Test]
        public void should_report_corrupt_rejection_as_failure_reason()
        {
            GivenFailDownloads(FailDownloads.CorruptOrUnreadable);

            var results = new List<ImportResult>
            {
                Rejection(ImportRejectionReason.CorruptFile, "Corrupt file, ffprobe was not able to read media information"),
                Rejection(ImportRejectionReason.UnsupportedExtension, "Invalid video file, unsupported extension: '.xyz'")
            };

            RejectedImportService.FailIfAllPermanent(_trackedDownload, results).Should().BeTrue();

            AssertFailureMessage("Import rejected permanently: Corrupt file, ffprobe was not able to read media information; Invalid video file, unsupported extension: '.xyz'");
        }

        [Test]
        public void should_report_executable_rejection_as_failure_reason()
        {
            GivenFailDownloads(FailDownloads.Executables);

            RejectedImportService.Process(_trackedDownload, Rejection(ImportRejectionReason.ExecutableFile, "Caution: Found executable file: Setup.exe"));

            AssertFailureMessage("Caution: Found executable file: Setup.exe");
        }

        [Test]
        public void should_report_dangerous_file_rejection_as_failure_reason()
        {
            GivenFailDownloads(FailDownloads.PotentiallyDangerous);

            RejectedImportService.Process(_trackedDownload, Rejection(ImportRejectionReason.DangerousFile, "Caution: Found potentially dangerous file: movie.lnk"));

            AssertFailureMessage("Caution: Found potentially dangerous file: movie.lnk");
        }

        [Test]
        public void should_keep_generic_message_when_failed_without_reason()
        {
            _trackedDownload.Fail();

            AssertFailureMessage("Failed download detected");
        }

        [Test]
        public void should_keep_encrypted_message_over_failure_reason()
        {
            _trackedDownload.DownloadItem.IsEncrypted = true;
            _trackedDownload.Fail("Import rejected permanently: Sample");

            AssertFailureMessage("Encrypted download detected");
        }

        [Test]
        public void should_clear_failure_reason_when_failed_again_without_reason()
        {
            _trackedDownload.Fail("Import rejected permanently: Sample");
            _trackedDownload.Fail();

            AssertFailureMessage("Failed download detected");
        }
    }
}

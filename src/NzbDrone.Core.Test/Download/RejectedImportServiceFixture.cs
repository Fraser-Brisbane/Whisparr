using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class RejectedImportServiceFixture : CoreTest<RejectedImportService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            var downloadItem = Builder<DownloadClientItem>.CreateNew()
                .With(d => d.Title = "Studio.14.07.06.Performer.XXX.720p.MP4-GRP")
                .Build();

            _trackedDownload = new TrackedDownload
            {
                DownloadItem = downloadItem,
                RemoteMovie = new RemoteMovie { Release = new ReleaseInfo { IndexerId = 1 } },
                State = TrackedDownloadState.ImportPending
            };
        }

        private void GivenFailDownloads(params FailDownloads[] failDownloads)
        {
            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                .Setup(s => s.GetSettings(1))
                .Returns(new CachedIndexerSettings { FailDownloads = new HashSet<FailDownloads>(failDownloads) });
        }

        private static ImportResult Rejection(ImportRejectionReason reason)
        {
            return new ImportResult(new ImportDecision(null, new ImportRejection(reason, "Rejected")), "Rejected");
        }

        [Test]
        public void should_fail_download_with_executable_when_indexer_fails_executables()
        {
            GivenFailDownloads(FailDownloads.Executables);

            Subject.Process(_trackedDownload, Rejection(ImportRejectionReason.ExecutableFile)).Should().BeTrue();

            _trackedDownload.State.Should().Be(TrackedDownloadState.FailedPending);
        }

        [Test]
        public void should_fail_download_with_dangerous_file_when_indexer_fails_dangerous_files()
        {
            GivenFailDownloads(FailDownloads.PotentiallyDangerous);

            Subject.Process(_trackedDownload, Rejection(ImportRejectionReason.DangerousFile)).Should().BeTrue();

            _trackedDownload.State.Should().Be(TrackedDownloadState.FailedPending);
        }

        [Test]
        public void should_only_warn_for_executable_when_indexer_does_not_fail_executables()
        {
            GivenFailDownloads();

            Subject.Process(_trackedDownload, Rejection(ImportRejectionReason.ExecutableFile)).Should().BeTrue();

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }
    }
}

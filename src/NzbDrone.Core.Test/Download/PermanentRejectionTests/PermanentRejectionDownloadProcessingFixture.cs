using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.PermanentRejectionTests
{
    // Drives one ProcessMonitoredDownloadsCommand tick through the real completed -> rejected -> failed
    // download services, and the real blocklist and redownload handlers of DownloadFailedEvent.
    [TestFixture]
    public class PermanentRejectionDownloadProcessingFixture : CoreTest<DownloadProcessingService>
    {
        private const int IndexerId = 7;
        private const int MovieId = 50928;

        private TrackedDownload _trackedDownload;
        private CachedIndexerSettings _indexerSettings;
        private List<ImportResult> _importResults;
        private MovieHistory _grabbedHistory;

        [SetUp]
        public void Setup()
        {
            var downloadItem = Builder<DownloadClientItem>.CreateNew()
                .With(d => d.Status = DownloadItemStatus.Completed)
                .With(d => d.DownloadId = "sabnzbd_nzo_23")
                .With(d => d.Title = "BigTitCreamPie.15.05.21.Peta.Jensen.XXX.1080p.MP4-KTR")
                .With(d => d.OutputPath = new OsPath(@"C:\Downloads\BigTitCreamPie.15.05.21.Peta.Jensen.XXX.1080p.MP4-KTR".AsOsAgnostic()))
                .Build();

            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                .With(t => t.State = TrackedDownloadState.ImportPending)
                .With(t => t.IsTrackable = true)
                .With(t => t.Protocol = Indexers.DownloadProtocol.Usenet)
                .With(t => t.DownloadItem = downloadItem)
                .With(t => t.RemoteMovie = new RemoteMovie
                {
                    Movie = new Movie { Id = MovieId },
                    Release = new ReleaseInfo { IndexerId = IndexerId }
                })
                .Build();

            _indexerSettings = new CachedIndexerSettings { FailDownloads = new HashSet<FailDownloads>() };
            _importResults = new List<ImportResult>();

            _grabbedHistory = new MovieHistory
            {
                MovieId = MovieId,
                DownloadId = downloadItem.DownloadId,
                EventType = MovieHistoryEventType.Grabbed,
                SourceTitle = downloadItem.Title,
                Date = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    { "publishedDate", DateTime.UtcNow.ToString("s") + "Z" },
                    { "size", "1000" },
                    { "protocol", "1" },
                    { "indexer", "Indexer" },
                    { MovieHistory.RELEASE_SOURCE, ReleaseSourceType.Rss.ToString() }
                }
            };

            Mocker.GetMock<IConfigService>().SetupGet(s => s.EnableCompletedDownloadHandling).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.AutoRedownloadFailed).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.AutoRedownloadFailedFromInteractiveSearch).Returns(true);

            Mocker.GetMock<ITrackedDownloadService>()
                  .Setup(s => s.GetTrackedDownloads())
                  .Returns(() => new List<TrackedDownload> { _trackedDownload });

            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                  .Setup(s => s.GetSettings(IndexerId))
                  .Returns(() => _indexerSettings);

            Mocker.GetMock<IProvideImportItemService>()
                  .Setup(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                  .Returns<DownloadClientItem, DownloadClientItem>((i, p) => i);

            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(s => s.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                  .Returns(() => _importResults);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(() => new List<MovieHistory> { _grabbedHistory });

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Find(downloadItem.DownloadId, MovieHistoryEventType.Grabbed))
                  .Returns(() => new List<MovieHistory> { _grabbedHistory });

            // Real services along the path under test
            Mocker.SetConstant<IRejectedImportService>(Mocker.Resolve<RejectedImportService>());
            Mocker.SetConstant<ICompletedDownloadService>(Mocker.Resolve<CompletedDownloadService>());
            Mocker.SetConstant<IFailedDownloadService>(Mocker.Resolve<FailedDownloadService>());

            var blocklistService = Mocker.Resolve<BlocklistService>();
            var redownloadService = Mocker.Resolve<RedownloadFailedDownloadService>();

            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent(It.IsAny<DownloadFailedEvent>()))
                  .Callback<DownloadFailedEvent>(e =>
                  {
                      blocklistService.Handle(e);
                      redownloadService.Handle(e);
                  });
        }

        private void GivenFailDownloads(params FailDownloads[] options)
        {
            _indexerSettings.FailDownloads = options.ToHashSet();
        }

        private void GivenResult(MediaProbeOutcome outcome, params ImportRejectionReason[] reasons)
        {
            var localMovie = new LocalMovie
            {
                Path = $@"C:\Downloads\file{_importResults.Count}.mkv".AsOsAgnostic(),
                MediaProbeOutcome = outcome,
                Movie = _trackedDownload.RemoteMovie.Movie
            };

            var rejections = reasons.Select(r => new ImportRejection(r, r.ToString())).ToArray();

            _importResults.Add(new ImportResult(new ImportDecision(localMovie, rejections), reasons.Select(r => r.ToString()).ToArray()));
        }

        private void GivenSample()
        {
            GivenResult(MediaProbeOutcome.Succeeded, ImportRejectionReason.Sample);
        }

        private void GivenCorrupt()
        {
            GivenResult(MediaProbeOutcome.Corrupt, ImportRejectionReason.CorruptFile);
        }

        private void GivenReleaseSource(ReleaseSourceType source)
        {
            _grabbedHistory.Data[MovieHistory.RELEASE_SOURCE] = source.ToString();
        }

        private void Tick()
        {
            Subject.Execute(new ProcessMonitoredDownloadsCommand());
        }

        private void VerifyFailedAndBlocklisted()
        {
            _trackedDownload.State.Should().Be(TrackedDownloadState.Failed);

            Mocker.GetMock<IBlocklistRepository>()
                  .Verify(s => s.Insert(It.Is<Blocklist>(b => b.MovieId == MovieId && b.SourceTitle == _trackedDownload.DownloadItem.Title)), Times.Once());
        }

        private void VerifyNotFailed()
        {
            _trackedDownload.State.Should().NotBe(TrackedDownloadState.Failed);
            _trackedDownload.State.Should().NotBe(TrackedDownloadState.FailedPending);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
            Mocker.GetMock<IBlocklistRepository>()
                  .Verify(s => s.Insert(It.IsAny<Blocklist>()), Times.Never());
        }

        private void VerifySearched(Times times)
        {
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Push(It.Is<MoviesSearchCommand>(c => c.MovieIds.SequenceEqual(new[] { MovieId })), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), times);
        }

        // R6: single result

        [Test]
        public void TestR6_single_sample_fails_when_samples_option_enabled()
        {
            GivenFailDownloads(FailDownloads.Samples);
            GivenSample();

            Tick();

            VerifyFailedAndBlocklisted();
        }

        [Test]
        public void TestR6_single_corrupt_file_fails_when_corrupt_option_enabled()
        {
            GivenFailDownloads(FailDownloads.CorruptOrUnreadable);
            GivenCorrupt();

            Tick();

            VerifyFailedAndBlocklisted();
        }

        [Test]
        public void TestR6_single_unsupported_extension_fails_when_corrupt_option_enabled()
        {
            GivenFailDownloads(FailDownloads.CorruptOrUnreadable);
            GivenResult(MediaProbeOutcome.NotProbed, ImportRejectionReason.UnsupportedExtension);

            Tick();

            VerifyFailedAndBlocklisted();
        }

        // R6: several results

        [Test]
        public void TestR6_several_permanent_files_fail_when_their_options_are_enabled()
        {
            GivenFailDownloads(FailDownloads.Samples, FailDownloads.CorruptOrUnreadable);
            GivenSample();
            GivenSample();
            GivenCorrupt();

            Tick();

            VerifyFailedAndBlocklisted();
        }

        [Test]
        public void TestR6_several_permanent_files_do_not_fail_when_one_option_is_off()
        {
            GivenFailDownloads(FailDownloads.Samples);
            GivenSample();
            GivenCorrupt();

            Tick();

            VerifyNotFailed();
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportBlocked);
        }

        [Test]
        public void TestR6_sample_plus_not_quality_upgrade_still_ends_import_blocked()
        {
            GivenFailDownloads(FailDownloads.Samples, FailDownloads.CorruptOrUnreadable);
            GivenSample();
            GivenResult(MediaProbeOutcome.Succeeded, ImportRejectionReason.NotQualityUpgrade);

            Tick();

            VerifyNotFailed();
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportBlocked);
        }

        [Test]
        public void TestR6_single_file_with_sample_and_transient_reason_only_warns()
        {
            GivenFailDownloads(FailDownloads.Samples, FailDownloads.CorruptOrUnreadable);
            GivenResult(MediaProbeOutcome.Succeeded, ImportRejectionReason.Sample, ImportRejectionReason.MinimumFreeSpace);

            Tick();

            VerifyNotFailed();
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void TestR6_empty_result_set_does_not_fail()
        {
            GivenFailDownloads(FailDownloads.Samples, FailDownloads.CorruptOrUnreadable);

            Tick();

            VerifyNotFailed();
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        [Test]
        public void TestR6_content_imported_before_is_marked_imported_not_failed()
        {
            GivenFailDownloads(FailDownloads.Samples, FailDownloads.CorruptOrUnreadable);
            GivenSample();

            Mocker.GetMock<ITrackedDownloadAlreadyImported>()
                  .Setup(s => s.IsImported(_trackedDownload, It.IsAny<List<MovieHistory>>()))
                  .Returns(true);

            Tick();

            VerifyNotFailed();
            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        [Test]
        public void TestR6_already_imported_rejection_is_transient()
        {
            GivenFailDownloads(FailDownloads.Samples, FailDownloads.CorruptOrUnreadable);
            GivenResult(MediaProbeOutcome.Succeeded, ImportRejectionReason.MovieAlreadyImported);
            GivenSample();

            Tick();

            VerifyNotFailed();
        }

        [Test]
        public void TestR6_imported_file_alongside_sample_is_imported_not_failed()
        {
            GivenFailDownloads(FailDownloads.Samples);
            GivenSample();
            _importResults.Add(new ImportResult(new ImportDecision(new LocalMovie
            {
                Path = @"C:\Downloads\movie.mkv".AsOsAgnostic(),
                Movie = _trackedDownload.RemoteMovie.Movie
            })));

            Tick();

            VerifyNotFailed();
            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        [TestCase(new FailDownloads[0])]
        [TestCase(new[] { FailDownloads.Executables, FailDownloads.PotentiallyDangerous })]
        public void TestR6_options_off_keep_todays_warning_for_a_single_permanent_file(FailDownloads[] options)
        {
            GivenFailDownloads(options);
            GivenSample();

            Tick();

            VerifyNotFailed();
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void TestR6_samples_option_off_keeps_todays_behaviour()
        {
            GivenFailDownloads(FailDownloads.CorruptOrUnreadable);
            GivenSample();

            Tick();

            VerifyNotFailed();
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void TestR6_corrupt_option_off_keeps_todays_behaviour()
        {
            GivenFailDownloads(FailDownloads.Samples);
            GivenCorrupt();

            Tick();

            VerifyNotFailed();
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void TestR6_options_off_keep_todays_import_blocked_for_several_permanent_files()
        {
            GivenSample();
            GivenCorrupt();

            Tick();

            VerifyNotFailed();
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportBlocked);
        }

        [TestCase(MediaProbeOutcome.Failed, ImportRejectionReason.Sample)]
        [TestCase(MediaProbeOutcome.Failed, ImportRejectionReason.SampleIndeterminate)]
        [TestCase(MediaProbeOutcome.Failed, ImportRejectionReason.Error)]
        [TestCase(MediaProbeOutcome.NotProbed, ImportRejectionReason.Sample)]
        public void TestR6_probe_infrastructure_failure_never_fails_the_download(MediaProbeOutcome outcome, ImportRejectionReason reason)
        {
            GivenFailDownloads(Enum.GetValues<FailDownloads>());
            GivenResult(outcome, reason);
            GivenResult(outcome, reason);

            Tick();

            VerifyNotFailed();
        }

        [Test]
        public void TestR6_unknown_indexer_only_warns()
        {
            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                  .Setup(s => s.GetSettings(IndexerId))
                  .Returns((CachedIndexerSettings)null);

            GivenSample();
            GivenSample();

            Tick();

            VerifyNotFailed();
        }

        // R7: existing behaviour

        [Test]
        public void TestR7_executable_still_fails_with_only_executables_enabled()
        {
            GivenFailDownloads(FailDownloads.Executables);
            GivenResult(MediaProbeOutcome.NotProbed, ImportRejectionReason.ExecutableFile);

            Tick();

            VerifyFailedAndBlocklisted();
        }

        [Test]
        public void TestR7_executable_is_not_failed_by_the_new_options()
        {
            GivenFailDownloads(FailDownloads.Samples, FailDownloads.CorruptOrUnreadable);
            GivenResult(MediaProbeOutcome.NotProbed, ImportRejectionReason.ExecutableFile);

            Tick();

            VerifyNotFailed();
        }

        [Test]
        public void TestR7_dangerous_file_still_fails_with_only_potentially_dangerous_enabled()
        {
            GivenFailDownloads(FailDownloads.PotentiallyDangerous);
            GivenResult(MediaProbeOutcome.NotProbed, ImportRejectionReason.DangerousFile);

            Tick();

            VerifyFailedAndBlocklisted();
        }

        // R8: re-search only where the redownload settings allow it

        [TestCase(ReleaseSourceType.Rss, true, true, true)]
        [TestCase(ReleaseSourceType.Search, true, false, true)]
        [TestCase(ReleaseSourceType.Rss, false, true, false)]
        [TestCase(ReleaseSourceType.InteractiveSearch, true, true, true)]
        [TestCase(ReleaseSourceType.InteractiveSearch, true, false, false)]
        [TestCase(ReleaseSourceType.InteractiveSearch, false, true, false)]
        public void TestR8_failed_permanent_download_is_blocklisted_and_researched_per_redownload_settings(ReleaseSourceType source, bool autoRedownload, bool fromInteractive, bool expectSearch)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.AutoRedownloadFailed).Returns(autoRedownload);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.AutoRedownloadFailedFromInteractiveSearch).Returns(fromInteractive);
            GivenReleaseSource(source);
            GivenFailDownloads(FailDownloads.Samples);
            GivenSample();

            Tick();

            VerifyFailedAndBlocklisted();
            VerifySearched(expectSearch ? Times.Once() : Times.Never());
        }

        [Test]
        public void TestR8_failed_download_is_not_processed_again_on_the_next_tick()
        {
            GivenFailDownloads(FailDownloads.Samples);
            GivenSample();

            Tick();
            Tick();

            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Verify(s => s.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()), Times.Once());
            VerifySearched(Times.Once());
        }
    }
}

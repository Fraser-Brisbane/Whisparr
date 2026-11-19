using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.MovieImport;

namespace NzbDrone.Core.Download;

public interface IRejectedImportService
{
    bool Process(TrackedDownload trackedDownload, ImportResult importResult);
    bool FailIfAllPermanent(TrackedDownload trackedDownload, List<ImportResult> importResults);
}

public class RejectedImportService : IRejectedImportService
{
    private readonly ICachedIndexerSettingsProvider _cachedIndexerSettingsProvider;
    private readonly Logger _logger;

    public RejectedImportService(ICachedIndexerSettingsProvider cachedIndexerSettingsProvider, Logger logger)
    {
        _cachedIndexerSettingsProvider = cachedIndexerSettingsProvider;
        _logger = logger;
    }

    public bool Process(TrackedDownload trackedDownload, ImportResult importResult)
    {
        if (importResult.Result != ImportResultType.Rejected || trackedDownload.RemoteMovie?.Release == null)
        {
            return false;
        }

        var indexerSettings = _cachedIndexerSettingsProvider.GetSettings(trackedDownload.RemoteMovie.Release.IndexerId);
        var rejection = importResult.ImportDecision.Rejections.FirstOrDefault();
        var rejectionReason = rejection?.Reason;

        if (indexerSettings == null)
        {
            trackedDownload.Warn(new TrackedDownloadStatusMessage(trackedDownload.DownloadItem.Title, importResult.Errors));
            return true;
        }

        if (rejectionReason == ImportRejectionReason.DangerousFile &&
            indexerSettings.FailDownloads.Contains(FailDownloads.PotentiallyDangerous))
        {
            _logger.Trace("Download '{0}' contains potentially dangerous file, marking as failed", trackedDownload.DownloadItem.Title);
            trackedDownload.Fail(rejection.Message);
        }
        else if (rejectionReason == ImportRejectionReason.ExecutableFile &&
            indexerSettings.FailDownloads.Contains(FailDownloads.Executables))
        {
            _logger.Trace("Download '{0}' contains executable file, marking as failed", trackedDownload.DownloadItem.Title);
            trackedDownload.Fail(rejection.Message);
        }
        else if (PermanentImportRejection.AreAllPermanent(new[] { importResult }, indexerSettings.FailDownloads))
        {
            FailPermanent(trackedDownload, new[] { importResult });
        }
        else
        {
            trackedDownload.Warn(new TrackedDownloadStatusMessage(trackedDownload.DownloadItem.Title, importResult.Errors));
        }

        return true;
    }

    public bool FailIfAllPermanent(TrackedDownload trackedDownload, List<ImportResult> importResults)
    {
        if (trackedDownload.RemoteMovie?.Release == null)
        {
            return false;
        }

        var indexerSettings = _cachedIndexerSettingsProvider.GetSettings(trackedDownload.RemoteMovie.Release.IndexerId);

        if (indexerSettings == null || !PermanentImportRejection.AreAllPermanent(importResults, indexerSettings.FailDownloads))
        {
            return false;
        }

        FailPermanent(trackedDownload, importResults);

        return true;
    }

    private void FailPermanent(TrackedDownload trackedDownload, IEnumerable<ImportResult> importResults)
    {
        var rejections = importResults.SelectMany(r => r.ImportDecision.Rejections).ToList();
        var reasons = rejections.Select(r => r.Reason).Distinct();
        var messages = rejections.Select(r => r.Message).Distinct();

        _logger.Info("Download '{0}' can never be imported ({1}), marking as failed", trackedDownload.DownloadItem.Title, string.Join(", ", reasons));
        trackedDownload.Fail($"Import rejected permanently: {string.Join("; ", messages)}");
    }
}

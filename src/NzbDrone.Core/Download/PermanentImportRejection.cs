using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;

namespace NzbDrone.Core.Download;

// Decides whether a completed download's import rejections can never succeed, so it can be failed instead of retried.
public static class PermanentImportRejection
{
    // Explicit allowlist: every reason not listed here, including any added in future, is transient.
    public static FailDownloads? GetFailDownloadsOption(ImportRejectionReason reason, MediaProbeOutcome probeOutcome)
    {
        switch (reason)
        {
            // Only a sample measured by a successful probe; a failed probe can also surface as Sample (-1min sentinel)
            case ImportRejectionReason.Sample when probeOutcome == MediaProbeOutcome.Succeeded:
                return FailDownloads.Samples;
            case ImportRejectionReason.CorruptFile:
            case ImportRejectionReason.UnsupportedExtension:
                return FailDownloads.CorruptOrUnreadable;
            default:
                return null;
        }
    }

    // True only when there is at least one result, every result is rejected, and every rejection of every result is
    // permanent with its FailDownloads option enabled.
    public static bool AreAllPermanent(IReadOnlyCollection<ImportResult> importResults, ICollection<FailDownloads> enabledOptions)
    {
        if (importResults == null || importResults.Count == 0 || enabledOptions == null || enabledOptions.Count == 0)
        {
            return false;
        }

        return importResults.All(result => IsPermanent(result, enabledOptions));
    }

    private static bool IsPermanent(ImportResult result, ICollection<FailDownloads> enabledOptions)
    {
        if (result.Result != ImportResultType.Rejected)
        {
            return false;
        }

        var rejections = result.ImportDecision.Rejections.ToList();

        if (rejections.Count == 0)
        {
            return false;
        }

        var probeOutcome = result.ImportDecision.LocalMovie?.MediaProbeOutcome ?? MediaProbeOutcome.NotProbed;

        return rejections.All(rejection =>
        {
            var option = GetFailDownloadsOption(rejection.Reason, probeOutcome);

            return option.HasValue && enabledOptions.Contains(option.Value);
        });
    }
}

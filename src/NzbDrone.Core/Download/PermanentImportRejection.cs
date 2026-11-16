using System.Collections.Generic;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;

namespace NzbDrone.Core.Download;

public static class PermanentImportRejection
{
    public static FailDownloads? GetFailDownloadsOption(ImportRejectionReason reason, MediaProbeOutcome probeOutcome)
    {
        return null;
    }

    public static bool AreAllPermanent(IReadOnlyCollection<ImportResult> importResults, ICollection<FailDownloads> enabledOptions)
    {
        return false;
    }
}

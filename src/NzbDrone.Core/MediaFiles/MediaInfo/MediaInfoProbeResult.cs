namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public class MediaInfoProbeResult
    {
        public MediaInfoProbeResult(MediaInfoModel mediaInfo, MediaProbeOutcome outcome)
        {
            MediaInfo = mediaInfo;
            Outcome = outcome;
        }

        public MediaInfoModel MediaInfo { get; }
        public MediaProbeOutcome Outcome { get; }
    }
}

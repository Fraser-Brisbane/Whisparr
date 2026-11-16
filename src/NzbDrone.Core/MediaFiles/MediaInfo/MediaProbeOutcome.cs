namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public enum MediaProbeOutcome
    {
        // ffprobe was not run for this file (e.g. disk images, or media info disabled for existing files)
        NotProbed,

        // ffprobe ran and returned media info
        Succeeded,

        // ffprobe is healthy, Whisparr can read the file, and ffprobe rejected its content
        Corrupt,

        // ffprobe could not be relied on (missing/broken binary, I/O or permission errors, unexpected exceptions)
        Failed
    }
}

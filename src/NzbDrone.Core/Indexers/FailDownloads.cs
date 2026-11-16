using NzbDrone.Core.Annotations;

namespace NzbDrone.Core.Indexers;

public enum FailDownloads
{
    [FieldOption(Label = "Executables")]
    Executables = 0,

    [FieldOption(Label = "Potentially Dangerous")]
    PotentiallyDangerous = 1,

    [FieldOption(Label = "Samples", Hint = "(Every file is a sample)")]
    Samples = 2,

    [FieldOption(Label = "Corrupt/Unreadable", Hint = "(ffprobe rejects every file, or none is a video)")]
    CorruptOrUnreadable = 3
}

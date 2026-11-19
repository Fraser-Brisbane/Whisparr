using System;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.MediaFiles.MovieImport
{
    public interface IDetectSample
    {
        DetectSampleResult IsSample(MovieMetadata movie, string path);

        // Decides from the caller's probe of this file instead of probing again. probe is only called when the
        // runtime is needed, and its result is used whatever the outcome.
        DetectSampleResult IsSample(MovieMetadata movie, string path, Func<MediaInfoProbeResult> probe);
    }

    public class DetectSample : IDetectSample
    {
        private readonly IVideoFileInfoReader _videoFileInfoReader;
        private readonly Logger _logger;

        public DetectSample(IVideoFileInfoReader videoFileInfoReader, Logger logger)
        {
            _videoFileInfoReader = videoFileInfoReader;
            _logger = logger;
        }

        public DetectSampleResult IsSample(MovieMetadata movie, string path)
        {
            return IsSample(movie, path, () => _videoFileInfoReader.GetRunTime(path), LogLevel.Error);
        }

        // The reader has already logged why the caller's probe failed, so a missing runtime is only logged at Debug.
        public DetectSampleResult IsSample(MovieMetadata movie, string path, Func<MediaInfoProbeResult> probe)
        {
            return IsSample(movie, path, () => probe()?.MediaInfo?.RunTime, LogLevel.Debug);
        }

        private DetectSampleResult IsSample(MovieMetadata movie, string path, Func<TimeSpan?> getRunTime, LogLevel missingRunTimeLevel)
        {
            var extension = Path.GetExtension(path);

            if (extension != null)
            {
                if (extension.Equals(".flv", StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.Debug("Skipping sample check for .flv file");
                    return DetectSampleResult.NotSample;
                }

                if (extension.Equals(".strm", StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.Debug("Skipping sample check for .strm file");
                    return DetectSampleResult.NotSample;
                }

                if (new string[] { ".iso", ".img", ".m2ts" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.Debug($"Skipping sample check for DVD/BR image file '{path}'");
                    return DetectSampleResult.NotSample;
                }
            }

            var runTime = getRunTime();

            if (!runTime.HasValue)
            {
                _logger.Log(missingRunTimeLevel, "Failed to get runtime from the file, make sure ffprobe is available");
                return DetectSampleResult.Indeterminate;
            }

            var minimumRuntime = GetMinimumAllowedRuntime(movie);

            if (runTime.Value.TotalMinutes.Equals(0))
            {
                _logger.Error("[{0}] has a runtime of 0, is it a valid video file?", path);
                return DetectSampleResult.Sample;
            }

            if (runTime.Value.TotalSeconds < minimumRuntime)
            {
                _logger.Debug("[{0}] appears to be a sample. Runtime: {1} seconds. Expected at least: {2} seconds", path, runTime.Value.TotalSeconds, minimumRuntime);
                return DetectSampleResult.Sample;
            }

            _logger.Debug("[{0}] does not appear to be a sample. Runtime {1} seconds is more than minimum of {2} seconds", path, runTime, minimumRuntime);
            return DetectSampleResult.NotSample;
        }

        private int GetMinimumAllowedRuntime(MovieMetadata movie)
        {
            // Anime short - 15 seconds
            if (movie.Runtime <= 3)
            {
                return 15;
            }

            // Webisodes - 90 seconds
            if (movie.Runtime <= 10)
            {
                return 90;
            }

            // 30 minute episodes - 5 minutes
            if (movie.Runtime <= 30)
            {
                return 300;
            }

            // 60 minute episodes - 10 minutes
            return 600;
        }
    }
}

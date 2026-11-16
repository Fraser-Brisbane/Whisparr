using System;
using FFMpegCore;
using NLog;
using NzbDrone.Common.Processes;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public interface IFFProbeSelfCheck
    {
        // True when the ffprobe binary can be started and `ffprobe -version` exits 0.
        bool IsHealthy();
    }

    public class FFProbeSelfCheck : IFFProbeSelfCheck
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        private readonly IProcessProvider _processProvider;
        private readonly Logger _logger;
        private readonly object _lock = new ();

        private bool _healthy;
        private DateTime _checkedAt = DateTime.MinValue;

        public FFProbeSelfCheck(IProcessProvider processProvider, Logger logger)
        {
            _processProvider = processProvider;
            _logger = logger;
        }

        public bool IsHealthy()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _checkedAt < CacheDuration)
                {
                    return _healthy;
                }

                _healthy = Check();
                _checkedAt = DateTime.UtcNow;

                return _healthy;
            }
        }

        private bool Check()
        {
            try
            {
                var output = _processProvider.StartAndCapture(GlobalFFOptions.GetFFProbeBinaryPath(), "-version");

                if (output.ExitCode == 0)
                {
                    return true;
                }

                _logger.Warn("ffprobe self-check failed, 'ffprobe -version' exited with code {0}. Files ffprobe cannot read will not be treated as corrupt.", output.ExitCode);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "ffprobe self-check failed, unable to run ffprobe. Files ffprobe cannot read will not be treated as corrupt.");
            }

            return false;
        }
    }
}

using System;
using NLog;
using NzbDrone.Common.Processes;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public interface IFFProbeSelfCheck
    {
        bool IsHealthy();
    }

    public class FFProbeSelfCheck : IFFProbeSelfCheck
    {
        private readonly IProcessProvider _processProvider;
        private readonly Logger _logger;

        public FFProbeSelfCheck(IProcessProvider processProvider, Logger logger)
        {
            _processProvider = processProvider;
            _logger = logger;
        }

        public bool IsHealthy()
        {
            throw new NotImplementedException();
        }
    }
}

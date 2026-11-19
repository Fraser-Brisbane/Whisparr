using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport.Specifications
{
    public class NotSampleSpecification : IImportDecisionEngineSpecification
    {
        private readonly IDetectSample _detectSample;
        private readonly Logger _logger;

        public NotSampleSpecification(IDetectSample detectSample,
                                      Logger logger)
        {
            _detectSample = detectSample;
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            if (localMovie.ExistingFile)
            {
                _logger.Debug("Existing file, skipping sample check");
                return ImportSpecDecision.Accept();
            }

            // Decide from the probe recorded on the file, whatever its outcome, so a Sample verdict and the probe outcome
            // that makes it permanent (PermanentImportRejection) always describe the same probe and the file is not
            // probed again. Only a file that was never probed is probed here.
            var sample = localMovie.MediaProbeOutcome == MediaProbeOutcome.NotProbed
                ? _detectSample.IsSample(localMovie.Movie.MovieMetadata, localMovie.Path)
                : _detectSample.IsSample(localMovie.Movie.MovieMetadata, localMovie.Path, () => new MediaInfoProbeResult(localMovie.MediaInfo, localMovie.MediaProbeOutcome));

            if (sample == DetectSampleResult.Sample)
            {
                return ImportSpecDecision.Reject(ImportRejectionReason.Sample, "Sample");
            }
            else if (sample == DetectSampleResult.Indeterminate)
            {
                return ImportSpecDecision.Reject(ImportRejectionReason.SampleIndeterminate, "Unable to determine if file is a sample");
            }

            return ImportSpecDecision.Accept();
        }
    }
}

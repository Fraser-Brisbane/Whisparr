using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public interface IUpdateMediaInfo
    {
        bool Update(MovieFile movieFile, Movie movie);
        bool UpdateMediaInfo(MovieFile movieFile, Movie movie);
    }

    public class UpdateMediaInfoService : IUpdateMediaInfo, IHandle<MovieScannedEvent>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IVideoFileInfoReader _videoFileInfoReader;
        private readonly AggregateQuality _augmentQuality;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public UpdateMediaInfoService(IDiskProvider diskProvider,
                                IMediaFileService mediaFileService,
                                IVideoFileInfoReader videoFileInfoReader,
                                AggregateQuality augmentQuality,
                                IConfigService configService,
                                Logger logger)
        {
            _diskProvider = diskProvider;
            _mediaFileService = mediaFileService;
            _videoFileInfoReader = videoFileInfoReader;
            _augmentQuality = augmentQuality;
            _configService = configService;
            _logger = logger;
        }

        public void Handle(MovieScannedEvent message)
        {
            if (!_configService.EnableMediaInfo)
            {
                _logger.Debug("MediaInfo is disabled");
                return;
            }

            var allMediaFiles = _mediaFileService.GetFilesByMovie(message.Movie.Id);
            var filteredMediaFiles = allMediaFiles.Where(c =>
                c.MediaInfo == null ||
                c.MediaInfo.SchemaRevision < VideoFileInfoReader.MINIMUM_MEDIA_INFO_SCHEMA_REVISION).ToList();

            foreach (var mediaFile in filteredMediaFiles)
            {
                UpdateMediaInfo(mediaFile, message.Movie);
            }
        }

        public bool Update(MovieFile movieFile, Movie movie)
        {
            if (!_configService.EnableMediaInfo)
            {
                _logger.Debug("MediaInfo is disabled");
                return false;
            }

            return UpdateMediaInfo(movieFile, movie);
        }

        public bool UpdateMediaInfo(MovieFile movieFile, Movie movie)
        {
            var path = movieFile.Path.IsNotNullOrWhiteSpace() ? movieFile.Path : Path.Combine(movie.Path, movieFile.RelativePath);

            if (!_diskProvider.FileExists(path))
            {
                _logger.Debug("Can't update MediaInfo because '{0}' does not exist", path);
                return false;
            }

            _logger.Trace("GetMediaInfo path='{0}', movieFile.Path='{1}', movieFile.RelativePath='{2}'", path, movieFile.Path, movieFile.RelativePath);
            var probe = _videoFileInfoReader.ProbeMediaInfo(path);

            // A failed or corrupt probe carries no real media info (with corrupt-file detection on it is the -1min
            // sentinel), so keep what is already stored for the file.
            if (probe?.Outcome != MediaProbeOutcome.Succeeded)
            {
                return false;
            }

            var updatedMediaInfo = probe.MediaInfo;

            movieFile.MediaInfo = updatedMediaInfo;

            var localMovie = new LocalMovie();
            localMovie.Path = movieFile.Path;
            localMovie.Movie = movie;

            // Try re-check for Quality rather than defaulting to null/Unknown
            localMovie.Quality = QualityParser.ParseQuality(movieFile.RelativePath);
            localMovie.MediaInfo = updatedMediaInfo;

            localMovie = _augmentQuality.Aggregate(localMovie, null);

            if (localMovie?.Quality?.ResolutionDetectionSource != null && localMovie.Quality.ResolutionDetectionSource == Qualities.QualityDetectionSource.MediaInfo)
            {
                movieFile.Quality = localMovie.Quality;
            }

            _mediaFileService.Update(movieFile);
            _logger.Debug("Updated MediaInfo for '{0}'", path);

            return true;
        }
    }
}

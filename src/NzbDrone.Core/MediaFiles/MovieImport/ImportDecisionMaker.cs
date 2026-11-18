using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport
{
    public interface IMakeImportDecision
    {
        List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie);
        (List<Movie> MoviesToAdd, List<MovieFile> UnmappedFiles, List<ImportDecision> Decisions) GetSceneImportDecisions(List<string> videoFiles);
        List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, bool filterExistingFiles);
        List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, DownloadClientItem downloadClientItem, ParsedMovieInfo folderInfo, bool sceneSource);
        List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, DownloadClientItem downloadClientItem, ParsedMovieInfo folderInfo, bool sceneSource, bool filterExistingFiles);
        ImportDecision GetDecision(LocalMovie localMovie, DownloadClientItem downloadClientItem);
    }

    public class ImportDecisionMaker : IMakeImportDecision
    {
        private readonly IEnumerable<IImportDecisionEngineSpecification> _specifications;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMovieService _movieService;
        private readonly IAggregationService _aggregationService;
        private readonly ISceneIdentificationService _identificationService;
        private readonly IDiskProvider _diskProvider;
        private readonly IDetectSample _detectSample;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IVideoFileInfoReader _videoFileInfoReader;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public ImportDecisionMaker(IEnumerable<IImportDecisionEngineSpecification> specifications,
                                   IMediaFileService mediaFileService,
                                   IMovieService movieService,
                                   IAggregationService aggregationService,
                                   ISceneIdentificationService identificationService,
                                   IDiskProvider diskProvider,
                                   IDetectSample detectSample,
                                   ITrackedDownloadService trackedDownloadService,
                                   ICustomFormatCalculationService formatCalculator,
                                   IVideoFileInfoReader videoFileInfoReader,
                                   IConfigService configService,
                                   Logger logger)
        {
            _specifications = specifications;
            _mediaFileService = mediaFileService;
            _movieService = movieService;
            _aggregationService = aggregationService;
            _diskProvider = diskProvider;
            _detectSample = detectSample;
            _identificationService = identificationService;
            _trackedDownloadService = trackedDownloadService;
            _formatCalculator = formatCalculator;
            _videoFileInfoReader = videoFileInfoReader;
            _configService = configService;
            _logger = logger;
        }

        public List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie)
        {
            return GetImportDecisions(videoFiles, movie, null, null, false);
        }

        public (List<Movie> MoviesToAdd, List<MovieFile> UnmappedFiles, List<ImportDecision> Decisions) GetSceneImportDecisions(List<string> videoFiles)
        {
            return GetSceneImportDecisions(videoFiles, null, null, false, true);
        }

        public List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, bool filterExistingFiles)
        {
            return GetImportDecisions(videoFiles, movie, null, null, false, filterExistingFiles);
        }

        public List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, DownloadClientItem downloadClientItem, ParsedMovieInfo folderInfo, bool sceneSource)
        {
            return GetImportDecisions(videoFiles, movie, downloadClientItem, folderInfo, sceneSource, true);
        }

        public List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, DownloadClientItem downloadClientItem, ParsedMovieInfo folderInfo, bool sceneSource, bool filterExistingFiles)
        {
            var newFiles = filterExistingFiles ? _mediaFileService.FilterExistingFiles(videoFiles.ToList(), movie) : videoFiles.ToList();

            _logger.Debug("Analyzing {0}/{1} files.", newFiles.Count, videoFiles.Count);

            ParsedMovieInfo downloadClientItemInfo = null;

            if (downloadClientItem != null)
            {
                downloadClientItemInfo = Parser.Parser.ParseMovieTitle(downloadClientItem.Title);
            }

            var nonSampleVideoFileCount = GetNonSampleVideoFileCount(newFiles, movie.MovieMetadata);

            var decisions = new List<ImportDecision>();

            foreach (var file in newFiles)
            {
                var localMovie = new LocalMovie
                {
                    Movie = movie,
                    DownloadClientMovieInfo = downloadClientItemInfo,
                    DownloadItem = downloadClientItem,
                    FolderMovieInfo = folderInfo,
                    Path = file,
                    SceneSource = sceneSource,
                    ExistingFile = movie.Path.IsParentPath(file),
                    OtherVideoFiles = nonSampleVideoFileCount > 1
                };

                decisions.AddIfNotNull(GetDecision(localMovie, downloadClientItem, nonSampleVideoFileCount > 1));
            }

            return decisions;
        }

        public (List<Movie> MoviesToAdd, List<MovieFile> UnmappedFiles, List<ImportDecision> Decisions) GetSceneImportDecisions(List<string> videoFiles, DownloadClientItem downloadClientItem, ParsedMovieInfo folderInfo, bool sceneSource, bool filterExistingFiles)
        {
            var newFiles = filterExistingFiles ? _mediaFileService.FilterExistingFiles(videoFiles.ToList()) : videoFiles.ToList();

            _logger.Debug("Analyzing {0}/{1} files.", newFiles.Count, videoFiles.Count);

            ParsedMovieInfo downloadClientItemInfo = null;

            if (downloadClientItem != null)
            {
                downloadClientItemInfo = Parser.Parser.ParseMovieTitle(downloadClientItem.Title);
            }

            var moviesToAdd = new List<Movie>();
            var unmappedFiles = new List<MovieFile>();
            var decisions = new List<ImportDecision>();

            var i = 0;
            foreach (var file in newFiles)
            {
                i++;
                _logger.ProgressInfo($"Identifying scene {i}/{newFiles.Count}");
                _logger.Debug($"Identifying scene - {file}");
                var movie = GetRemoteMovie(file.ToString());

                var fileMovieInfo = Parser.Parser.ParseMoviePath(file) ?? new ParsedMovieInfo();
                var localMovie = new LocalMovie
                {
                    Movie = movie,
                    DownloadClientMovieInfo = downloadClientItemInfo,
                    DownloadItem = downloadClientItem,
                    FolderMovieInfo = folderInfo,
                    FileMovieInfo = fileMovieInfo,
                    Path = movie.Path,
                    SceneSource = sceneSource,
                };

                if (movie.ForeignId == null)
                {
                    // Add the movieFile if no match to a movie
                    var unmappedFile = new MovieFile();

                    unmappedFile.DateAdded = DateTime.UtcNow;
                    unmappedFile.Languages = new List<Languages.Language>();
                    unmappedFile.MediaInfo = GetSuccessfulMediaInfo(file.ToString());
                    unmappedFile.OriginalFilePath = file.ToString();
                    unmappedFile.Size = _diskProvider.GetFileSize(file.ToString());

                    localMovie.Movie.MovieFile = unmappedFile;
                    localMovie.Path = file.ToString();

                    // Augment movie file so imported files have all additional information an automatic import would
                    localMovie = _aggregationService.Augment(localMovie, null);
                    unmappedFile.Quality = localMovie.Quality;

                    unmappedFiles.Add(unmappedFile);
                    continue;
                }

                if (movie.Id == 0)
                {
                    moviesToAdd.Add(movie);
                    continue;
                }

                decisions.AddIfNotNull(GetDecision(localMovie, downloadClientItem, false));
            }

            return (moviesToAdd, unmappedFiles, decisions);
        }

        public ImportDecision GetDecision(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            var reasons = _specifications.Select(c => EvaluateSpec(c, localMovie, downloadClientItem))
                                         .Where(c => c != null);

            return new ImportDecision(localMovie, reasons.ToArray());
        }

        // Only a successful probe is real media info; a failed or corrupt one may be the -1min sentinel.
        private MediaInfoModel GetSuccessfulMediaInfo(string file)
        {
            var probe = _videoFileInfoReader.ProbeMediaInfo(file);

            return probe?.Outcome == MediaProbeOutcome.Succeeded ? probe.MediaInfo : null;
        }

        private Movie GetRemoteMovie(string file)
        {
            var scene = new Movie();
            var fileMovieInfo = Parser.Parser.ParseMoviePath(file);

            if (fileMovieInfo == null)
            {
                return scene;
            }

            // Try to get match by searching for the scene
            scene = _identificationService.Identify(file, fileMovieInfo);

            return scene;
        }

        private ImportDecision GetDecision(LocalMovie localMovie, DownloadClientItem downloadClientItem, bool otherFiles)
        {
            ImportDecision decision = null;

            try
            {
                var fileMovieInfo = Parser.Parser.ParseMoviePath(localMovie.Path);

                localMovie.FileMovieInfo = fileMovieInfo;
                localMovie.Size = _diskProvider.GetFileSize(localMovie.Path);

                _aggregationService.Augment(localMovie, downloadClientItem);

                if (localMovie.Movie.MovieMetadata.Value.ItemType == ItemType.Movie)
                {
                    if (localMovie.Movie == null)
                    {
                        decision = new ImportDecision(localMovie, new ImportRejection(ImportRejectionReason.InvalidMovie, "Invalid movie"));
                    }
                    else
                    {
                        localMovie.CustomFormats = _formatCalculator.ParseCustomFormat(localMovie);
                        localMovie.CustomFormatScore = localMovie.Movie.QualityProfile?.CalculateCustomFormatScore(localMovie.CustomFormats) ?? 0;

                        decision = GetDecision(localMovie, downloadClientItem);
                    }
                }
                else
                {
                    if (downloadClientItem?.DownloadId.IsNotNullOrWhiteSpace() == true)
                    {
                        var trackedDownload = _trackedDownloadService.Find(downloadClientItem.DownloadId);

                        if (trackedDownload?.RemoteMovie?.Release?.IndexerFlags != null)
                        {
                            localMovie.IndexerFlags = trackedDownload.RemoteMovie.Release.IndexerFlags;
                        }
                    }

                    var matchedMovie = new Movie();

                    if (localMovie.Movie != null)
                    {
                        matchedMovie = _movieService.GetMovie(localMovie.Movie.Id);

                        // Switch the movie so that the QualityProfile is populated
                        localMovie.Movie = matchedMovie;
                    }
                    else
                    {
                        matchedMovie = _movieService.FindScene(fileMovieInfo);
                        if (matchedMovie.Title != localMovie.Movie.Title)
                        {
                            decision = new ImportDecision(localMovie, new ImportRejection(ImportRejectionReason.UnknownMovie, "Movie title does not match identified scene title"));
                        }
                    }

                    localMovie.CustomFormats = _formatCalculator.ParseCustomFormat(localMovie);
                    localMovie.CustomFormatScore = localMovie.Movie.QualityProfile?.CalculateCustomFormatScore(localMovie.CustomFormats) ?? 0;

                    decision = GetDecision(localMovie, downloadClientItem);
                }

                if (localMovie.MediaProbeOutcome == MediaProbeOutcome.Corrupt)
                {
                    decision = GetCorruptFileDecision(localMovie, decision);
                }
                else if (IsRuntimeMismatch(localMovie, out var runtime))
                {
                    var rejection = $"Runtime of {localMovie.Movie.MovieMetadata.Value.Runtime} expected but {runtime} Found";
                    if (runtime == -1)
                    {
                        rejection = "Corrupt file detected as Media Information was not able to be extracted.";
                    }

                    _logger.Warn($"{rejection} for {localMovie.Movie.ToString()}");
                    if (_configService.WhisparrValidateRuntime)
                    {
                        decision = new ImportDecision(localMovie, new ImportRejection(ImportRejectionReason.Error, $"Runtime of {localMovie.Movie.MovieMetadata.Value.Runtime} expected but {runtime} Found"));
                    }
                }
            }
            catch (AugmentingFailedException)
            {
                decision = new ImportDecision(localMovie, new ImportRejection(ImportRejectionReason.UnableToParse, "Unable to parse file"));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Couldn't import file. {0}", localMovie.Path);

                decision = new ImportDecision(localMovie, new ImportRejection(ImportRejectionReason.Error, "Unexpected error processing file"));
            }

            if (decision == null)
            {
                _logger.Error("Unable to make a decision on {0}", localMovie.Path);
            }
            else if (decision.Rejections.Any())
            {
                _logger.Debug("File rejected for the following reasons: {0}", string.Join(", ", decision.Rejections));
            }
            else
            {
                _logger.Debug("File accepted");
            }

            return decision;
        }

        private bool IsRuntimeMismatch(LocalMovie localMovie, out double runtime)
        {
            runtime = 0;

            if (localMovie.MediaInfo?.RunTime == null || localMovie.MediaInfo.RunTime.TotalMinutes == 0 || localMovie.Movie.MovieMetadata.Value.Runtime <= 0)
            {
                return false;
            }

            runtime = localMovie.MediaInfo.RunTime.TotalMinutes;
            var limit = _configService.WhisparrValidateRuntimeLimit;
            var expected = localMovie.Movie.MovieMetadata.Value.Runtime;

            return runtime < expected - limit || runtime > expected + limit;
        }

        // ffprobe ran, is healthy, and rejected a readable file. The rejections that only exist because the probe failed
        // (Sample / SampleIndeterminate, and the runtime-mismatch Error raised by the -1min sentinel) are replaced by a
        // single CorruptFile rejection, so the reason no longer depends on WhisparrCorruptFileDetection. Other rejections
        // are kept, and a file that would otherwise be approved is not newly rejected.
        private ImportDecision GetCorruptFileDecision(LocalMovie localMovie, ImportDecision decision)
        {
            var rejections = decision?.Rejections.ToList() ?? new List<ImportRejection>();
            var runtimeRejected = _configService.WhisparrValidateRuntime && IsRuntimeMismatch(localMovie, out _);

            if (rejections.Empty() && !runtimeRejected)
            {
                return decision;
            }

            _logger.Warn("Corrupt file detected, ffprobe was not able to read media information from {0}", localMovie.Path);

            var remaining = rejections
                .Where(r => r.Reason != ImportRejectionReason.Sample && r.Reason != ImportRejectionReason.SampleIndeterminate)
                .Append(new ImportRejection(ImportRejectionReason.CorruptFile, "Corrupt file, ffprobe was not able to read media information"))
                .ToArray();

            return new ImportDecision(localMovie, remaining);
        }

        private ImportRejection EvaluateSpec(IImportDecisionEngineSpecification spec, LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            try
            {
                var result = spec.IsSatisfiedBy(localMovie, downloadClientItem);

                if (!result.Accepted)
                {
                    return new ImportRejection(result.Reason, result.Message);
                }
            }
            catch (NotImplementedException e)
            {
                _logger.Warn(e, "Spec " + spec.ToString() + " currently does not implement evaluation for movies.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Couldn't evaluate decision on {0}", localMovie.Path);
                return new ImportRejection(ImportRejectionReason.DecisionError, $"{spec.GetType().Name}: {ex.Message}");
            }

            return null;
        }

        private int GetNonSampleVideoFileCount(List<string> videoFiles, MovieMetadata movie)
        {
            return videoFiles.Count(file =>
            {
                var sample = _detectSample.IsSample(movie, file);

                if (sample == DetectSampleResult.Sample)
                {
                    return false;
                }

                return true;
            });
        }
    }
}

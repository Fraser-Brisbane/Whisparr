using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Aggregation
{
    [TestFixture]
    public class AggregationServiceProbeOutcomeFixture : CoreTest<AggregationService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IEnumerable<IAggregateLocalMovie>>(new List<IAggregateLocalMovie>());
        }

        [TestCase(MediaProbeOutcome.Succeeded)]
        [TestCase(MediaProbeOutcome.Corrupt)]
        [TestCase(MediaProbeOutcome.Failed)]
        public void TestR2_augment_records_the_probe_outcome_and_media_info(MediaProbeOutcome outcome)
        {
            var mediaInfo = new MediaInfoModel { RunTime = TimeSpan.FromMinutes(-1) };
            var path = @"C:\Test\Unsorted\The.Movie.2018.DVDRip.XviD-OSiTV.avi".AsOsAgnostic();

            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.ProbeMediaInfo(path))
                  .Returns(new MediaInfoProbeResult(mediaInfo, outcome));

            var localMovie = new LocalMovie { Path = path, Movie = new Movie(), FileMovieInfo = new ParsedMovieInfo() };

            Subject.Augment(localMovie, null);

            localMovie.MediaInfo.Should().BeSameAs(mediaInfo);
            localMovie.MediaProbeOutcome.Should().Be(outcome);
        }
    }
}

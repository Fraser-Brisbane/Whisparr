using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.IndexerTests
{
    [TestFixture]
    public class FailDownloadsFixture
    {
        [Test]
        public void TestR1_fail_downloads_has_samples_and_corrupt_options_with_stable_values()
        {
            // Values are persisted as ints in indexer settings JSON, so they must never be renumbered.
            ((int)FailDownloads.Executables).Should().Be(0);
            ((int)FailDownloads.PotentiallyDangerous).Should().Be(1);
            ((int)FailDownloads.Samples).Should().Be(2);
            ((int)FailDownloads.CorruptOrUnreadable).Should().Be(3);

            Enum.GetValues<FailDownloads>().Should().HaveCount(4);
        }

        [Test]
        public void TestR1_new_indexer_settings_default_to_no_fail_downloads()
        {
            new Indexers.Newznab.NewznabSettings().FailDownloads.Should().BeEmpty();
            new Indexers.Torznab.TorznabSettings().FailDownloads.Should().BeEmpty();
            new Indexers.TorrentRss.TorrentRssIndexerSettings().FailDownloads.Should().BeEmpty();
            new Indexers.FileList.FileListSettings().FailDownloads.Should().BeEmpty();
            new Indexers.HDBits.HDBitsSettings().FailDownloads.Should().BeEmpty();
            new Indexers.IPTorrents.IPTorrentsSettings().FailDownloads.Should().BeEmpty();
        }

        [Test]
        public void TestR9_fail_downloads_fields_carry_localized_help_text_and_warning()
        {
            var settingsTypes = new[]
            {
                typeof(Indexers.Newznab.NewznabSettings),
                typeof(Indexers.TorrentRss.TorrentRssIndexerSettings),
                typeof(Indexers.FileList.FileListSettings),
                typeof(Indexers.HDBits.HDBitsSettings),
                typeof(Indexers.IPTorrents.IPTorrentsSettings)
            };

            foreach (var type in settingsTypes)
            {
                var attribute = type.GetProperty("FailDownloads")
                    .GetCustomAttributes(typeof(Annotations.FieldDefinitionAttribute), true)
                    .Cast<Annotations.FieldDefinitionAttribute>()
                    .Single();

                attribute.HelpText.Should().Be("IndexerSettingsFailDownloadsHelpText", type.Name);
                attribute.HelpTextWarning.Should().Be("IndexerSettingsFailDownloadsHelpTextWarning", type.Name);
            }
        }
    }
}

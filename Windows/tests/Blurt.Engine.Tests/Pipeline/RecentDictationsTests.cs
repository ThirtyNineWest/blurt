using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class RecentDictationsTests
{
    private static readonly DateTimeOffset Epoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void RecordsNewestFirst()
    {
        var recent = new RecentDictations();

        recent.Record("one", null, null, Epoch);
        recent.Record("two", null, null, Epoch.AddSeconds(1));

        recent.Entries.Select(e => e.Text).Should().Equal("two", "one");
    }

    [Test]
    public void CapsAtCapacityDroppingTheOldest()
    {
        var recent = new RecentDictations();
        var texts = Enumerable.Range(1, RecentDictations.Capacity + 1).Select(i => $"u{i}").ToList();

        for (var offset = 0; offset < texts.Count; offset++)
        {
            recent.Record(texts[offset], null, null, Epoch.AddSeconds(offset));
        }

        recent.Entries.Should().HaveCount(RecentDictations.Capacity);
        recent.Entries[0].Text.Should().Be(texts[^1]);
        recent.Entries[^1].Text.Should().Be("u2");
    }

    [Test]
    public void ShowsOnlyDisplayCapacityRowsOutOfAMuchDeeperHistory()
    {
        var recent = new RecentDictations();
        string[] texts = ["a", "b", "c", "d", "e"];

        for (var offset = 0; offset < texts.Length; offset++)
        {
            recent.Record(texts[offset], null, null, Epoch.AddSeconds(offset));
        }

        RecentDictations.DisplayCapacity.Should().BeLessThan(RecentDictations.Capacity);
        recent.Entries.Should().HaveCount(5);
        recent.Displayed.Select(e => e.Text).Should().Equal("e", "d", "c");
    }

    [Test]
    public void ProjectsTheSpokenHistoryOldestFirstForTheRequestContext()
    {
        var recent = new RecentDictations();
        string[] texts = ["a", "b", "c"];

        for (var offset = 0; offset < texts.Length; offset++)
        {
            recent.Record(texts[offset], null, null, Epoch.AddSeconds(offset));
        }

        recent.SpokenOldestFirst.Should().Equal("a", "b", "c");
    }

    [Test]
    public void AnEntryKeepsTheSpokenTextBesideThePastedOne()
    {
        var recent = new RecentDictations();

        recent.Record("Mail me@example.com.", "Mail personal email.", null, Epoch);

        recent.Entries[0].Text.Should().Be("Mail me@example.com.");
        recent.Entries[0].Spoken.Should().Be("Mail personal email.");
        recent.SpokenOldestFirst.Should().Equal("Mail personal email.");
    }

    [Test]
    public void EntriesKeepAStableUniqueIdAsNewerOnesPushIn()
    {
        var recent = new RecentDictations();
        recent.Record("first", null, null, Epoch);
        var firstId = recent.Entries[0].Id;

        recent.Record("second", null, null, Epoch.AddSeconds(1));

        recent.Entries.Single(e => e.Text == "first").Id.Should().Be(firstId);
        recent.Entries[0].Id.Should().NotBe(firstId);
        recent.Entries.Select(e => e.Id).Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void PreservesTheTimestampItWasRecordedWith()
    {
        var recent = new RecentDictations();
        var when = Epoch.AddSeconds(12345);

        recent.Record("hi", null, null, when);

        recent.Entries[0].Timestamp.Should().Be(when);
    }

    [Test]
    public void PreservesTheStyleItWasRecordedWithIncludingNone()
    {
        var recent = new RecentDictations();

        recent.Record("styled", null, "Casual", Epoch);
        recent.Record("unstyled", null, null, Epoch.AddSeconds(1));

        recent.Entries[0].Style.Should().BeNull();
        recent.Entries[1].Style.Should().Be("Casual");
    }

    [Test]
    public void ReservedHeightCountsSeparatorsBetweenRowsNotAfterEveryRow()
    {
        RecentDictations.ReservedHeight(28, 1).Should().Be(86);
        RecentDictations.ReservedHeight(10, 0).Should().Be(30);
        RecentDictations.ReservedHeight(0, 2).Should().Be(4);
    }

    [Test]
    public void SnapshotIsIndependentOfLaterRecords()
    {
        var recent = new RecentDictations();
        recent.Record("one", null, null, Epoch);

        var snapshot = recent.Snapshot();
        recent.Record("two", null, null, Epoch.AddSeconds(1));

        snapshot.Entries.Select(e => e.Text).Should().Equal("one");
        recent.Entries.Select(e => e.Text).Should().Equal("two", "one");
    }

    [Test]
    public void SnapshotCopiesEveryEntry()
    {
        var recent = new RecentDictations();
        recent.Record("one", null, "Casual", Epoch);
        recent.Record("two", null, null, Epoch.AddSeconds(1));

        var snapshot = recent.Snapshot();

        snapshot.Entries.Should().Equal(recent.Entries);
    }
}

public sealed class RecentDictationsRelativeLabelTests
{
    private static readonly DateTimeOffset Recorded = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RecentDictations.Entry Entry()
    {
        var recent = new RecentDictations();
        recent.Record("hi", null, null, Recorded);
        return recent.Entries[0];
    }

    [Test]
    public void TheFirstMinuteReadsAsJustNow()
    {
        Entry().RelativeLabel(Recorded.AddSeconds(59)).Should().Be("just now");
    }

    [Test]
    public void SmallClockSkewWithTheEntryInTheFutureStillReadsAsJustNow()
    {
        Entry().RelativeLabel(Recorded.AddSeconds(-5)).Should().Be("just now");
    }

    [TestCase(60, "1 minute ago")]
    [TestCase(120, "2 minutes ago")]
    [TestCase(3599, "59 minutes ago")]
    [TestCase(3600, "1 hour ago")]
    [TestCase(7 * 3600 + 1800, "7 hours ago")]
    [TestCase(86400, "1 day ago")]
    [TestCase(3 * 86400 + 5, "3 days ago")]
    public void FromOneMinuteOnTheFullRelativePhrasingTakesOver(int secondsLater, string expected)
    {
        Entry().RelativeLabel(Recorded.AddSeconds(secondsLater)).Should().Be(expected);
    }

    [Test]
    public void TheJustNowThresholdIsOneMinute()
    {
        RecentDictations.Entry.JustNowThresholdSeconds.Should().Be(60);
    }
}

namespace AniSprinkles.UnitTests;

public class BrowseMediaItemTests
{
    private static BrowseMediaItem OnListItem() => new()
    {
        Node = new RelatedMedia
        {
            Id = 21,
            Type = "ANIME",
            Format = "TV",
            Status = "RELEASING",
            Episodes = 1100,
            Title = new MediaTitle { Romaji = "ONE PIECE" },
            ListEntryId = 555,
            ListStatus = MediaListStatus.Current,
            ListProgress = 800,
            ListScore = 8.5,
        },
    };

    [Test]
    public void ToListEntry_MapsListSnapshotAndMedia()
    {
        var entry = OnListItem().ToListEntry();

        Assert.That(entry.Id, Is.EqualTo(555));
        Assert.That(entry.MediaId, Is.EqualTo(21));
        Assert.That(entry.Status, Is.EqualTo(MediaListStatus.Current));
        Assert.That(entry.Progress, Is.EqualTo(800));
        Assert.That(entry.Score, Is.EqualTo(8.5));
        // Media carries what the popups need: title for headers, episodes for the progress cap.
        Assert.That(entry.Media!.Id, Is.EqualTo(21));
        Assert.That(entry.Media.Episodes, Is.EqualTo(1100));
        Assert.That(entry.Media.DisplayTitle, Is.EqualTo("ONE PIECE"));
    }

    [Test]
    public void ToListEntry_NotOnList_HasZeroIdForCreatingSave()
    {
        var item = new BrowseMediaItem { Node = new RelatedMedia { Id = 99, Type = "ANIME" } };

        var entry = item.ToListEntry();

        Assert.That(entry.Id, Is.EqualTo(0));
        Assert.That(entry.Status, Is.Null);
    }

    [Test]
    public void ApplyListEntry_CopiesSavedStateAndKeepsKnownIdWhenServerOmitsIt()
    {
        var item = OnListItem();
        var saved = new MediaListEntry { Id = 0, MediaId = 21, Status = MediaListStatus.Paused, Progress = 810, Score = 9 };

        item.ApplyListEntry(saved);

        Assert.That(item.Node!.ListStatus, Is.EqualTo(MediaListStatus.Paused));
        Assert.That(item.Node.ListProgress, Is.EqualTo(810));
        Assert.That(item.Node.ListScore, Is.EqualTo(9));
        Assert.That(item.Node.ListEntryId, Is.EqualTo(555)); // Id 0 must not clobber the known entry id
        Assert.That(item.HasListStatus, Is.True);
    }

    [Test]
    public void ApplyListEntry_AdoptsServerAssignedIdAfterCreate()
    {
        var item = new BrowseMediaItem { Node = new RelatedMedia { Id = 99, Type = "ANIME" } };

        item.ApplyListEntry(new MediaListEntry { Id = 777, MediaId = 99, Status = MediaListStatus.Planning });

        Assert.That(item.Node!.ListEntryId, Is.EqualTo(777));
        Assert.That(item.Node.ListStatus, Is.EqualTo(MediaListStatus.Planning));
    }

    [Test]
    public void ClearListEntry_RemovesSnapshotAndHidesChip()
    {
        var item = OnListItem();

        item.ClearListEntry();

        Assert.That(item.Node!.ListEntryId, Is.Null);
        Assert.That(item.Node.ListStatus, Is.Null);
        Assert.That(item.Node.ListProgress, Is.Null);
        Assert.That(item.Node.ListScore, Is.Null);
        Assert.That(item.HasListStatus, Is.False);
    }

    [Test]
    public void ListEntryChanges_RaiseChipPropertyNotifications()
    {
        var item = OnListItem();
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.ClearListEntry();

        Assert.That(changed, Does.Contain(nameof(BrowseMediaItem.HasListStatus)));
        Assert.That(changed, Does.Contain(nameof(BrowseMediaItem.ListStatusDisplay)));
        Assert.That(changed, Does.Contain(nameof(BrowseMediaItem.ListStatusColor)));
    }

    [TestCase(0, false, "")]
    [TestCase(1, true, "#1")]
    [TestCase(42, true, "#42")]
    public void Rank_ZeroMeansHidden(int rank, bool expectedHasRank, string expectedDisplay)
    {
        var item = new BrowseMediaItem { Rank = rank };

        Assert.That(item.HasRank, Is.EqualTo(expectedHasRank));
        Assert.That(item.RankDisplay, Is.EqualTo(expectedDisplay));
    }
}

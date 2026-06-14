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

    [Fact]
    public void ToListEntry_MapsListSnapshotAndMedia()
    {
        var entry = OnListItem().ToListEntry();

        Assert.Equal(555, entry.Id);
        Assert.Equal(21, entry.MediaId);
        Assert.Equal(MediaListStatus.Current, entry.Status);
        Assert.Equal(800, entry.Progress);
        Assert.Equal(8.5, entry.Score);
        // Media carries what the popups need: title for headers, episodes for the progress cap.
        Assert.Equal(21, entry.Media!.Id);
        Assert.Equal(1100, entry.Media.Episodes);
        Assert.Equal("ONE PIECE", entry.Media.DisplayTitle);
    }

    [Fact]
    public void ToListEntry_NotOnList_HasZeroIdForCreatingSave()
    {
        var item = new BrowseMediaItem { Node = new RelatedMedia { Id = 99, Type = "ANIME" } };

        var entry = item.ToListEntry();

        Assert.Equal(0, entry.Id);
        Assert.Null(entry.Status);
    }

    [Fact]
    public void ApplyListEntry_CopiesSavedStateAndKeepsKnownIdWhenServerOmitsIt()
    {
        var item = OnListItem();
        var saved = new MediaListEntry { Id = 0, MediaId = 21, Status = MediaListStatus.Paused, Progress = 810, Score = 9 };

        item.ApplyListEntry(saved);

        Assert.Equal(MediaListStatus.Paused, item.Node!.ListStatus);
        Assert.Equal(810, item.Node.ListProgress);
        Assert.Equal(9, item.Node.ListScore);
        Assert.Equal(555, item.Node.ListEntryId); // Id 0 must not clobber the known entry id
        Assert.True(item.HasListStatus);
    }

    [Fact]
    public void ApplyListEntry_AdoptsServerAssignedIdAfterCreate()
    {
        var item = new BrowseMediaItem { Node = new RelatedMedia { Id = 99, Type = "ANIME" } };

        item.ApplyListEntry(new MediaListEntry { Id = 777, MediaId = 99, Status = MediaListStatus.Planning });

        Assert.Equal(777, item.Node!.ListEntryId);
        Assert.Equal(MediaListStatus.Planning, item.Node.ListStatus);
    }

    [Fact]
    public void ClearListEntry_RemovesSnapshotAndHidesChip()
    {
        var item = OnListItem();

        item.ClearListEntry();

        Assert.Null(item.Node!.ListEntryId);
        Assert.Null(item.Node.ListStatus);
        Assert.Null(item.Node.ListProgress);
        Assert.Null(item.Node.ListScore);
        Assert.False(item.HasListStatus);
    }

    [Fact]
    public void ListEntryChanges_RaiseChipPropertyNotifications()
    {
        var item = OnListItem();
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.ClearListEntry();

        Assert.Contains(nameof(BrowseMediaItem.HasListStatus), changed);
        Assert.Contains(nameof(BrowseMediaItem.ListStatusDisplay), changed);
        Assert.Contains(nameof(BrowseMediaItem.ListStatusColor), changed);
    }

    [Theory]
    [InlineData(0, false, "")]
    [InlineData(1, true, "#1")]
    [InlineData(42, true, "#42")]
    public void Rank_ZeroMeansHidden(int rank, bool expectedHasRank, string expectedDisplay)
    {
        var item = new BrowseMediaItem { Rank = rank };

        Assert.Equal(expectedHasRank, item.HasRank);
        Assert.Equal(expectedDisplay, item.RankDisplay);
    }
}

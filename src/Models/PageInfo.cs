namespace AniSprinkles.Models;

public class PageInfo
{
    public bool HasNextPage { get; set; }
    public int CurrentPage { get; set; }
    public int LastPage { get; set; }

    /// <summary>Total items across all pages, when the query requests it (AniList <c>pageInfo.total</c>). Null if not fetched.</summary>
    public int? Total { get; set; }
}

namespace AniSprinkles.Models
{
    public class Media
    {
        public int Id { get; set; }
        public MediaTitle? Title { get; set; }
        public MediaCoverImage? CoverImage { get; set; }
        public string? BannerImage { get; set; }
        public string? Description { get; set; }
        public string? Format { get; set; }
        public string? Status { get; set; }
        public int? Episodes { get; set; }
        public int? Duration { get; set; }
        public string? Season { get; set; }
        public int? SeasonYear { get; set; }
        public int? AverageScore { get; set; }
        public int? MeanScore { get; set; }
        public int? Popularity { get; set; }
        public int? Favourites { get; set; }
        public List<string> Genres { get; set; } = [];
        public List<MediaTag> Tags { get; set; } = [];
        public List<Studio> Studios { get; set; } = [];

        public string DisplayTitle =>
            Title?.English ?? Title?.Romaji ?? Title?.Native ?? "Unknown Title";
    }
}

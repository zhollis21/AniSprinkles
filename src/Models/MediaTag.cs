namespace AniSprinkles.Models
{
    public class MediaTag
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int? Rank { get; set; }
        public bool? IsSpoiler { get; set; }
        public bool? IsAdult { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
    }
}

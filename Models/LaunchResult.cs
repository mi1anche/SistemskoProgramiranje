namespace SpaceXServer.Models
{
    public class LaunchResult
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? DateUtc { get; set; }
        public bool? Success { get; set; }
        public bool Upcoming { get; set; }
        public string? Details { get; set; }
        public int FlightNumber { get; set; }
        public string? RocketId { get; set; }
        public string? LaunchpadId { get; set; }
        public string? WebcastUrl { get; set; }
        public string? ArticleUrl { get; set; }
        public string? WikipediaUrl { get; set; }
    }
}

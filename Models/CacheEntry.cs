namespace SpaceXServer.Models
{
    public class CacheEntry
    {
        public List<LaunchResult> Results { get; set; }
        public bool IsLoading { get; set; }

        public CacheEntry(List<LaunchResult> results)
        {
            Results = results;
            IsLoading = false;
        }

        public CacheEntry()
        {
            Results = new List<LaunchResult>();
            IsLoading = true;
        }
    }
}

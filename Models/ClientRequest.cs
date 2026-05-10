namespace SpaceXServer.Models
{
    public class ClientRequest
    {
        public string RequestId { get; set; }
        public string Query { get; set; }
        public Dictionary<string, string> Filters { get; set; }
        public DateTime ReceivedAt { get; set; }
        public TaskCompletionSource<(int statusCode, string body)> ResponseSource { get; set; }

        public ClientRequest(string requestId, string query, Dictionary<string, string> filters)
        {
            RequestId = requestId;
            Query = query;
            Filters = filters;
            ReceivedAt = DateTime.UtcNow;
            ResponseSource = new TaskCompletionSource<(int, string)>();
        }
    }
}

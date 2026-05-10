using Newtonsoft.Json;
using SpaceXServer.Models;
using SpaceXServer.Services;
using SpaceXServer.Utils;

namespace SpaceXServer.Services
{

    // ovo je mozak svega servera
    // uzima zahteve iz reda, proverava kes, poziva api ako treba i salje odgovor klijentu
    public class RequestProcessor
    {
        private readonly RequestQueue _queue;
        private readonly LaunchCache _cache;
        private readonly SpaceXApiService _apiService;
        private readonly int _radneNiti;

        private int _processedCount = 0;
        private int _errorCount = 0;

        public RequestProcessor(RequestQueue queue, LaunchCache cache,
                                SpaceXApiService apiService, int radneNiti = 4)
        {
            _queue = queue;
            _cache = cache;
            _apiService = apiService;
            _radneNiti = radneNiti;
        }


        // pokretanje worker niti, default 4
        // uzimamo postojecu nit iz poola
        // stavljamo ovaj posao u red, ThreadPool izvrsava kad ima slobodnu nit
        // kada parametar postoji ali ga ne koristimo, zovemo _ da pokazemo da je namerno ignorisan
        public void Start()
        {
            Logger.Info($"[Proces] Pokretanje {_radneNiti} radnih niti iz ThreadPool-a...");

            for (int i = 0; i < _radneNiti; i++)
            {
                int workerId = i + 1;
                ThreadPool.QueueUserWorkItem(_ => WorkerLoop(workerId));
            }
        }


        // svaka nit vrti ovu petlju, ceka, uzme zahtev, obradi ga, ceka opet
        // kada Dequeue vrati null nit zavrsava
        private void WorkerLoop(int workerId)
        {
            Logger.Info($"[Proces] Radna nit #{workerId} pokrenuta (Thread {Thread.CurrentThread.ManagedThreadId})");

            while (true)
            {
                // Blokira se ovde dok ne stigne zahtev ili dok se ne ugasi server
                ClientRequest? request = _queue.Dequeue();

                if (request == null)
                {
                    Logger.Info($"[Proces] Radna nit #{workerId} se gasi.");
                    break;
                }

                ProcessRequest(workerId, request);
            }
        }

        private void ProcessRequest(int workerId, ClientRequest request)
        {
            Logger.Req($"[Proces #{workerId}] Obrada zahteva {request.RequestId} | query='{request.Query}'");

            try
            {
                string cacheKey = BuildCacheKey(request.Filters);

                var cached = _cache.TryGet(cacheKey);
                if (cached != null)
                {
                    Logger.Req($"[Proces #{workerId}] Zahtev {request.RequestId} opsluzem iz kesa ({cached.Count} letova)");
                    string cachedJson = SerializeResults(cached);
                    request.ResponseSource.SetResult((200, cachedJson));
                    Interlocked.Increment(ref _processedCount);
                    return;
                }

                _cache.ReservePlaceholder(cacheKey);

                try
                {
                    var results = _apiService.FetchAndFilter(request.Filters);

                    _cache.Set(cacheKey, results);

                    string json = SerializeResults(results);
                    request.ResponseSource.SetResult((200, json));
                    Interlocked.Increment(ref _processedCount);

                    Logger.Req($"[Proces #{workerId}] Zahtev {request.RequestId} uspesno obradjeni ({results.Count} letova)");
                }
                catch (Exception ex)
                {
                    _cache.RemovePlaceholder(cacheKey);
                    throw new Exception($"Greska pri pozivu SpaceX API-a: {ex.Message}", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                Logger.Error($"[Proces #{workerId}] HTTP greska za zahtev {request.RequestId}: {ex.Message}");
                Interlocked.Increment(ref _errorCount);
                request.ResponseSource.SetResult((502, $"{{\"error\": \"SpaceX API nije dostupan: {EscapeJson(ex.Message)}\"}}"));
            }
            catch (Exception ex)
            {
                Logger.Error($"[Proces #{workerId}] Greska pri obradi zahteva {request.RequestId}: {ex.Message}");
                Interlocked.Increment(ref _errorCount);
                request.ResponseSource.SetResult((500, $"{{\"error\": \"Interna greska servera: {EscapeJson(ex.Message)}\"}}"));
            }
        }

        private string BuildCacheKey(Dictionary<string, string> filters)
        {
            if (filters.Count == 0)
                return "ALL";

            var sorted = filters.OrderBy(kvp => kvp.Key)
                                .Select(kvp => $"{kvp.Key}={kvp.Value.ToLower()}");
            return string.Join("&", sorted);
        }

        private string SerializeResults(List<LaunchResult> results)
        {
            if (results.Count == 0)
                return "{\"message\": \"Nisu pronadjeni letovi koji odgovaraju zadatim filterima.\", \"count\": 0, \"results\": []}";

            return JsonConvert.SerializeObject(new
            {
                count = results.Count,
                results
            }, Formatting.Indented);
        }

        private string EscapeJson(string s) => s.Replace("\"", "\\\"").Replace("\n", " ");

        public void PrintStats()
        {
            Logger.Info($"Statistike -> obradjena: {_processedCount} | greske: {_errorCount}");
            _cache.PrintStats();
        }
    }
}

using Newtonsoft.Json.Linq;
using SpaceXServer.Models;
using SpaceXServer.Utils;

namespace SpaceXServer.Services
{
    public class SpaceXApiService
    {
        // klasa za slanje http zahteva
        // adresa API-a odakle povlacimo podatke

        private readonly HttpClient _client;

        private const string BaseUrl = "https://api.spacexdata.com/v5/launches/past";

        public SpaceXApiService(HttpClient client)
        {
            _client = client;
        }

        //salje se zahtev API-u i nit se blokira dok ne stigne odgovor(.Result to radi)
        // EnsureSuccessStatusCode baca exception ako je status kod 400 ili 500
        // odgovor api-a se cita i parsira u JArray(lista JSON objekata), i smesta u niz letova.
        // i onda svaki let pretovori u LaunchResult objekat provberi filtere i doda u listu ako prolazi
        public List<LaunchResult> FetchAndFilter(Dictionary<string, string> filters)
        {
            Logger.Info($"Saljem zahtev ka SpaceX API-u...");
            HttpResponseMessage response = _client.GetAsync(BaseUrl).Result;
            response.EnsureSuccessStatusCode();

            string body = response.Content.ReadAsStringAsync().Result;
            var launches = JArray.Parse(body);

            Logger.Info($"Primljeno {launches.Count} letova sa API-a, primenjujem filtere...");

            var results = new List<LaunchResult>();

            foreach (var launch in launches)
            {
                var mapped = MapLaunch(launch);
                if (mapped != null && MatchesFilters(mapped, filters))
                    results.Add(mapped);
            }

            Logger.Info($"[API] Nakon filtriranja: {results.Count} letova.");
            return results;
        }

        // uzima JSON objekat i pretvara u LaunchResult objekat.(tacnije parsira)
        // ako polje ne postoji rez je null.
        private LaunchResult? MapLaunch(JToken token)
        {
            try
            {
                return new LaunchResult
                {
                    Id          = token["id"]?.ToString(),
                    Name        = token["name"]?.ToString(),
                    DateUtc     = token["date_utc"]?.ToString(),
                    Success     = token["success"]?.ToObject<bool?>(),
                    Upcoming    = token["upcoming"]?.ToObject<bool>() ?? false,
                    Details     = token["details"]?.ToString(),
                    FlightNumber= token["flight_number"]?.ToObject<int>() ?? 0,
                    RocketId    = token["rocket"]?.ToString(),
                    LaunchpadId = token["launchpad"]?.ToString(),
                    WebcastUrl  = token["links"]?["webcast"]?.ToString(),
                    ArticleUrl  = token["links"]?["article"]?.ToString(),
                    WikipediaUrl= token["links"]?["wikipedia"]?.ToString()
                };
            }
            catch
            {
                return null;
            }
        }

        // prolazi kroz filtere koje je klijent poslao i proverava svaki
        // nije case sensitive
        // tryParse pokusava da pretvori string u broj, ako ne uspe samo ignorise taj filter
        private bool MatchesFilters(LaunchResult launch, Dictionary<string, string> filters)
        {
            foreach (var filter in filters)
            {
                switch (filter.Key.ToLower())
                {
                    case "name":
                        if (launch.Name == null ||
                            !launch.Name.Contains(filter.Value, StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;

                    case "success":
                        if (bool.TryParse(filter.Value, out bool successVal))
                        {
                            if (launch.Success != successVal)
                                return false;
                        }
                        break;

                    case "flight_number":
                        if (int.TryParse(filter.Value, out int flightNum))
                        {
                            if (launch.FlightNumber != flightNum)
                                return false;
                        }
                        break;

                    case "year":
                        if (int.TryParse(filter.Value, out int year))
                        {
                            if (!launch.DateUtc?.StartsWith(year.ToString()) ?? true)
                                return false;
                        }
                        break;
                }
            }
            return true;
        }
    }
}

using System.Net;
using System.Web;
using SpaceXServer.Models;
using SpaceXServer.Services;
using SpaceXServer.Utils;

namespace SpaceXServer.Services
{
    // ulazna tacka servera, prima http zazhteve iz browsera stavlja u red,ceka odgovor od worker niti u salje ga nazad klijentu
    public class WebServer
    {
        private readonly HttpListener _listener;
        private readonly RequestQueue _queue;
        private readonly RequestProcessor _processor;
        private bool _isRunning = false;
        private int _requestCounter = 0; //brojanje zahteva

        public WebServer(string prefix, RequestQueue queue, RequestProcessor processor)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _queue = queue;
            _processor = processor;
        }

        //pokretanje servera
        // kljucno je sto se prvo pokrecu worker niti, pa tek onda pocnu uzimati zahtevi
        public void Start()
        {
            _listener.Start();
            _isRunning = true;
            Logger.Info($"Server pokrenut. Slusa na: {string.Join(", ", _listener.Prefixes)}");
            Logger.Info("Dostupne rute:");
            Logger.Info("GET /launches           -> svi letovi");
            Logger.Info("GET /launches?name=...  -> filtriranje po imenu");
            Logger.Info("GET /launches?success=true/false");
            Logger.Info("GET /launches?year=2022");
            Logger.Info("GET /launches?flight_number=5");
            Logger.Info("GET /status             -> statistike servera");

            _processor.Start();
            AcceptLoop();
        }

        //petlja za prijem zahteva
        // getContext blokira glavnu nit dok ne stigne http zahtev, i zahtev se obradi na novoj niti pomocu thread poola
        private void AcceptLoop()
        {
            while (_isRunning)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleContext(context));
                }
                catch (HttpListenerException) when (!_isRunning)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Greska pri prijemu zahteva: {ex.Message}");
                }
            }
        }

        // metoda za obradu jednog zahteva NAJVAZNIJA!
        // prvo postavlja headers i dozvoljava browserimma sa bilo kog domena da pozovu API
        private void HandleContext(HttpListenerContext context)
        {
            var req = context.Request;
            var resp = context.Response;

            Logger.Req($"{req.HttpMethod} {req.Url?.PathAndQuery}");

            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            resp.Headers.Add("Access-Control-Allow-Origin", "*");

            try
            {
                string path = req.Url?.AbsolutePath ?? "/";

                // proverava http metodu i dozvoljava samo GET metodu
                // i postoje samo 2 moguce rute a to su: /status i /launches
                if (req.HttpMethod != "GET")
                {
                    SendResponse(resp, 405, "{\"error\": \"Samo GET metoda je podrzana.\"}");
                    return;
                }

                if (path == "/status")
                {
                    _processor.PrintStats();
                    SendResponse(resp, 200, "{\"status\": \"running\", \"message\": \"Statistike su ispisane u konzolu.\"}");
                    return;
                }

                if (path != "/launches")
                {
                    SendResponse(resp, 404, "{\"error\": \"Ruta nije pronadjena. Koristite /launches ili /status\"}");
                    return;
                }

                // generise jedinstveni id, pravi clientRequest i stavlja u red, ako je red pun vraca gresku 503
                // ceka 30 sekundi da worker nit postavi rezultat, ako ne postovi za 30sec vraca error 504 = Timeout
                var filters = ParseFilters(req.Url?.Query ?? "");
                string requestId = $"REQ-{Interlocked.Increment(ref _requestCounter):D5}";
                string query = req.Url?.Query ?? "";

                var clientRequest = new ClientRequest(requestId, query, filters);
                bool enqueued = _queue.Enqueue(clientRequest);
                if (!enqueued)
                {
                    SendResponse(resp, 503, "{\"error\": \"Server preopterecen. Pokusajte ponovo.\"}");
                    return;
                }
                bool completed = clientRequest.ResponseSource.Task.Wait(TimeSpan.FromSeconds(30));
                if (!completed)
                {
                    SendResponse(resp, 504, "{\"error\": \"Zahtev nije obradjen na vreme (timeout).\"}");
                    return;
                }

                var (statusCode, body) = clientRequest.ResponseSource.Task.Result;
                SendResponse(resp, statusCode, body);
            }
            catch (Exception ex)
            {
                Logger.Error($"Greska pri obradi zahteva: {ex.Message}");
                SendResponse(resp, 500, $"{{\"error\": \"Interna greska: {ex.Message.Replace("\"", "'")}\"}}");
            }
        }

        // pretvara url string u dicrionary
        private Dictionary<string, string> ParseFilters(string queryString)
        {
            var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(queryString))
                return filters;

            var qs = queryString.TrimStart('?');
            var parts = qs.Split('&', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2)
                {
                    string key = Uri.UnescapeDataString(kv[0]).Trim();
                    string value = Uri.UnescapeDataString(kv[1]).Trim();
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                        filters[key] = value;
                }
            }

            if (filters.Count > 0)
                Logger.Info($"Filteri: {string.Join(", ", filters.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

            return filters;
        }

        // fja koja samo salje odgovore, s tim sto pretvara stirng u byteove zato sto http prenosi bytove a ne stringove

        private void SendResponse(HttpListenerResponse resp, int statusCode, string body)
        {
            try
            {
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(body);
                resp.StatusCode = statusCode;
                resp.ContentLength64 = buffer.Length;
                resp.OutputStream.Write(buffer, 0, buffer.Length);
                resp.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Logger.Error($"Greska pri slanju odgovora: {ex.Message}");
            }
        }


        // gasi server
        public void Stop()
        {
            _isRunning = false;
            _listener.Stop();
            _queue.Shutdown();
            Logger.Info("Server ugasen.");
        }
    }
}

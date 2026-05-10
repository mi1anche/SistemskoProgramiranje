using SpaceXServer.Models;
using SpaceXServer.Utils;

namespace SpaceXServer.Services
{
    public class RequestQueue
    {
        private readonly Queue<ClientRequest> _queue = new Queue<ClientRequest>();
        private readonly object _queueLock = new object();
        private bool _isRunning = true;

        private const int MaxQueueSize = 100;

        // ovde dodajemo zahtev u red cekanja
        // ako je pun vraca se log i false, odnosno odbija se zahtev
        // ako nije pun dodaje se zahtev i budi se jedna nit
        // vraca bool da se zna da li je zahtev primljen/odbijen
        public bool Enqueue(ClientRequest request)
        {
            lock (_queueLock)
            {
                if (_queue.Count >= MaxQueueSize)
                {
                    Logger.Warn($"Red je pun ({MaxQueueSize}), zahtev {request.RequestId} odbijen!");
                    return false;
                }

                _queue.Enqueue(request);
                Logger.Info($"Zahtev {request.RequestId} dodat u red. Velicina: {_queue.Count}");

                Monitor.Pulse(_queueLock);
                return true;
            }
        }

        // ovde se preuzima zahtev iz reda
        // dok god je prazan i server je aktivan on ceka
        // ako je red prazan i server je ugasen vraca null

        public ClientRequest? Dequeue()
        {
            lock (_queueLock)
            {
                while (_queue.Count == 0 && _isRunning)
                {
                    Logger.Info($"Radna nit ceka na zahtev...");
                    Monitor.Wait(_queueLock);
                }

                if (!_isRunning && _queue.Count == 0)
                    return null;

                var request = _queue.Dequeue();
                Logger.Info($"Zahtev {request.RequestId} preuzet iz reda. Preostalo: {_queue.Count}");
                return request;
            }
        }

        // budi sve niti, svaka nit proverava red, vidi da je prazan i postavlja _isRunning na false, odnosno zavrsava rad
        public void Shutdown()
        {
            lock (_queueLock)
            {
                _isRunning = false;
                Monitor.PulseAll(_queueLock);
                Logger.Info("Red zahteva je ugasen.");
            }
        }

        public int Count
        {
            get { lock (_queueLock) { return _queue.Count; } }
        }
    }
}

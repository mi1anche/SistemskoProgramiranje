using SpaceXServer.Models;
using SpaceXServer.Utils;

namespace SpaceXServer.Services
{

    // imamo glavni kes _cache sa value CacheEntry sa podacima
    // imamo redosled unosa podataka koji je potreban za fifo
    public class LaunchCache
    {
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();

        private readonly LinkedList<string> _insertionOrder = new LinkedList<string>();

        private readonly int _maxSize;
        private readonly object _cacheLock = new object();

        private int _hits = 0;
        private int _misses = 0;
        private int _evictions = 0;

        public LaunchCache(int maxSize = 10)
        {
            _maxSize = maxSize;
            Logger.Cache($"Inicijalizovan sa max velicinom: {_maxSize} unosa");
        }


        // TryGet trazi podatke iz kesa
        // ako ne kljuc postojivraca null vrednost
        // ako postoji ali je isLoading=true, ceka dok druga nit ne zavrsi ucitavanje
        // Monitor.Wait privremeno oslobadja lock da druge niti mogu da rade
        // podaci su gotovi, broji hit i vraca rezultate

        public List<LaunchResult>? TryGet(string key)
        {
            lock (_cacheLock)
            {
                if (!_cache.TryGetValue(key, out CacheEntry? entry))
                    return null;

                while (entry.IsLoading)
                {
                    Logger.Cache($"Nit ceka na ucitavanje kljuca '{key}' (cache stampede zastita)");
                    Monitor.Wait(_cacheLock);

                    if (!_cache.TryGetValue(key, out entry!))
                        return null;
                }

                Interlocked.Increment(ref _hits);
                Logger.Cache($"HIT za '{key}' | velicina={_cache.Count}/{_maxSize} | hits={_hits} misses={_misses}");
                return entry.Results;
            }
        }


        // rezervise mesto pre api poziva
        // ako postoji key u kesu, ne radi nista i izlazi
        // ako je kes pun izbaci najstariji unos
        // zatim zauzima mesto sa praznim CacheEntry
        public void ReservePlaceholder(string key)
        {
            lock (_cacheLock)
            {
                if (_cache.ContainsKey(key))
                    return;

                if (_cache.Count >= _maxSize)
                    EvictOldest();

                _cache[key] = new CacheEntry();
                _insertionOrder.AddLast(key);

                Interlocked.Increment(ref _misses);
                Logger.Cache($"MISS za '{key}', placeholder rezervisan | velicina={_cache.Count}/{_maxSize}");
            }
        }

        // ovde se upisuju podaci
        // bude se sve niti koje cekaju u redu da mogu da procitaju rezultate
        public void Set(string key, List<LaunchResult> results)
        {
            lock (_cacheLock)
            {
                _cache[key] = new CacheEntry(results);

                Logger.Cache($"Sacuvano {results.Count} letova za '{key}' | velicina={_cache.Count}/{_maxSize}");
                Monitor.PulseAll(_cacheLock);
            }
        }


        // ciscenje u slucaju greske
        // ako api poziv nije uspeo uklanja placeholder i budi cekajuce niti
        public void RemovePlaceholder(string key)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var entry) && entry.IsLoading)
                {
                    _cache.Remove(key);
                    _insertionOrder.Remove(key);
                    Logger.Cache($"Placeholder uklonjen za '{key}' zbog greske.");
                }
                Monitor.PulseAll(_cacheLock);
            }
        }


        // fifo izbacivanje

        private void EvictOldest()
        {
            var node = _insertionOrder.First;
            while (node != null)
            {
                string candidate = node.Value;
                if (_cache.TryGetValue(candidate, out var e) && !e.IsLoading)
                {
                    _insertionOrder.Remove(node);
                    _cache.Remove(candidate);
                    Interlocked.Increment(ref _evictions);
                    Logger.Cache($"EVICTION (FIFO): uklonjen '{candidate}' | ukupno evictions={_evictions}");
                    return;
                }
                node = node.Next;
            }

            Logger.Warn("Eviction nije moguca - svi unosi su u stanju ucitavanja.");
        }


        // ovde je samo ispis
        public void PrintStats()
        {
            lock (_cacheLock)
            {
                Logger.Cache($"Statistike -> velicina: {_cache.Count}/{_maxSize} | hits: {_hits} | misses: {_misses} | evictions: {_evictions}");
                Logger.Cache("Redosled unosa (najstariji -> najnoviji):");
                foreach (var key in _insertionOrder)
                {
                    bool loading = _cache.TryGetValue(key, out var e) && e.IsLoading;
                    Logger.Cache($"  -> '{key}'{(loading ? " [ucitava se...]" : "")}");
                }
            }
        }
    }
}

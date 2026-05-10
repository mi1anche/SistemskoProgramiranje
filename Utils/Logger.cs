namespace SpaceXServer.Utils
{

    // u sustini ovde se desava samo ispis logova
    public static class Logger
    {
        private static readonly object _logLock = new object();
        private static StreamWriter? _fileWriter;
        private static readonly string _logPath;

        // ovde se pravi putanja root foldera prvo
        // posle se pravi putanja za log folder
        // u log folderu se kreiraju fajlovi kao server_datetime.log
        // i nakon toga se otvara fajl za pisanje
        static Logger()
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            string logsDir = Path.Combine(projectRoot, "logs");
            Directory.CreateDirectory(logsDir);
            _logPath = Path.Combine(logsDir, $"server_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _fileWriter = new StreamWriter(_logPath, append: true) { AutoFlush = true };
        }


        // ovde se prave linije tako sto se stavlja datum, level poruke poravnat levo za 5 mesta i thread id poravnat desno za 3 mesta, i poruka
        private static void Write(string level, string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level,-5}] [{Thread.CurrentThread.ManagedThreadId,3}] {message}";

            lock (_logLock)
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = level switch
                {
                    "INFO"  => ConsoleColor.Cyan,
                    "WARN"  => ConsoleColor.Yellow,
                    "ERROR" => ConsoleColor.Red,
                    "CACHE" => ConsoleColor.Green,
                    "REQ"   => ConsoleColor.Magenta,
                    _       => ConsoleColor.White
                };
                Console.WriteLine(line);
                Console.ForegroundColor = prev;
                _fileWriter?.WriteLine(line);
            }
        }


        // metode za ispisivanje poruke
        public static void Info(string msg)  => Write("INFO",  msg);
        public static void Warn(string msg)  => Write("WARN",  msg);
        public static void Error(string msg) => Write("ERROR", msg);
        public static void Cache(string msg) => Write("CACHE", msg);
        public static void Req(string msg)   => Write("REQ",   msg);
    }
}

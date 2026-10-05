namespace AgentLoop;

/// <summary>Presentacion del host. No modifica eventos, datos persistidos ni salidas JSON.</summary>
internal static class ConsoleUi
{
    private static readonly object Gate = new();
    private static bool _plain;
    private static bool _noColor;
    public static void Configure(string[] args)
    {
        _plain = args.Contains("--plain-ui");
        _noColor = _plain || args.Contains("--no-color") || Environment.GetEnvironmentVariable("NO_COLOR") is not null;
    }
    private static bool ColorEnabled => !_noColor && !Console.IsOutputRedirected;
    private static int Width
    {
        get
        {
            if (Console.IsOutputRedirected) return 96;
            try { var columns = Console.WindowWidth; return columns > 0 ? Math.Clamp(columns - 5, 16, 96) : 96; }
            catch (Exception error) when (error is IOException or PlatformNotSupportedException) { return 96; }
        }
    }
    public static void Styled(string text, ConsoleColor color, ConsoleColor? background = null)
    {
        lock (Gate)
        {
            if (!ColorEnabled) { Console.Write(text); return; }
            var previousForeground = Console.ForegroundColor;
            var previousBackground = Console.BackgroundColor;
            try
            {
                Console.ForegroundColor = color;
                if (background is {} shade) Console.BackgroundColor = shade;
                Console.Write(text);
            }
            finally { Console.ForegroundColor = previousForeground; Console.BackgroundColor = previousBackground; }
        }
    }
    public static void Text(string? text = null, ConsoleColor color = ConsoleColor.Gray, int indent = 0)
    {
        lock (Gate)
        {
            var padding = new string(' ', _plain ? 0 : 2 + indent);
            var available = Math.Max(8, Width - indent);
            foreach (var line in DataGuard.Display(text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                var rest = line;
                while (rest.Length > available)
                {
                    var split = rest.LastIndexOf(' ', available, available);
                    if (split < available / 2) split = available;
                    if (split > 0 && char.IsHighSurrogate(rest[split-1])) split--;
                    Styled(padding + rest[..split] + Environment.NewLine, color);
                    rest = rest[split..].TrimStart();
                }
                Styled(padding + rest + Environment.NewLine, color);
            }
        }
    }
    public static void Section(string title)
    {
        lock (Gate)
        {
            Console.WriteLine();
            if (_plain) { Text(title); return; }
            var width = Width;
            Styled("  " + new string('-', width) + Environment.NewLine, ConsoleColor.DarkCyan);
            var safe = DataGuard.Display(title);
            foreach (var line in safe.Chunk(Math.Max(1,width-2)))
                Styled("  " + (" " + new string(line)).PadRight(width) + Environment.NewLine, ConsoleColor.White, ConsoleColor.DarkBlue);
            Styled("  " + new string('-', width) + Environment.NewLine, ConsoleColor.DarkCyan);
        }
    }
    public static void Field(string label, object? value)
    {
        Text($"{label,-12} : {value}", ConsoleColor.Gray, 2);
    }
    public static void Status(string text, bool success) => Text(text, success ? ConsoleColor.Green : ConsoleColor.Yellow);
    public static void Log(string text, ConsoleColor? color = null)
    {
        var clean = DataGuard.Display(text).Trim();
        var tone = color ?? (clean.Contains("Rechaz") || clean.Contains("Fallo") || clean.Contains("SIN CERRAR") ? ConsoleColor.Yellow
            : clean.Contains("Tarjeta") || clean.Contains("CUMPLIDO") ? ConsoleColor.Green
            : clean.Contains("Modelo") ? ConsoleColor.Cyan : ConsoleColor.DarkGray);
        Text(clean, tone, 2);
    }
    public static string Margin => _plain ? "" : "  ";
    public static string StreamMargin => _plain ? "" : "    ";
    public static void Prompt(int exchanges)
    {
        Styled($"\n{Margin}agentloop [{exchanges}] > ", ConsoleColor.Cyan);
    }
}

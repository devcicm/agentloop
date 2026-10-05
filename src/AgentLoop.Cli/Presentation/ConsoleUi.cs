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
    public static void Styled(string text, ConsoleColor color, ConsoleColor? background = null, bool error = false)
    {
        lock (Gate)
        {
            var writer = error ? Console.Error : Console.Out;
            var useColor = error ? !_noColor && !Console.IsErrorRedirected : ColorEnabled;
            if (!useColor) { writer.Write(text); return; }
            var previousForeground = Console.ForegroundColor;
            var previousBackground = Console.BackgroundColor;
            try
            {
                Console.ForegroundColor = color;
                if (background is {} shade) Console.BackgroundColor = shade;
                writer.Write(text);
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
    public static void Error(string text)
    {
        lock (Gate)
            foreach (var line in DataGuard.Display(text).Replace("\r\n", "\n").Split('\n'))
                Styled(Margin + line + Environment.NewLine, ConsoleColor.Red, error: true);
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
    public static void Approval(AgentEvent item)
    {
        if (item.Kind != AgentEventKind.AprobacionSolicitada || item.Data is null) return;
        using var document = System.Text.Json.JsonDocument.Parse(item.Data);
        var data = document.RootElement;
        Section("APROBACION  /  Efecto pendiente");
        foreach (var field in new[] { "approval_id", "shell", "cwd", "risk", "reason", "comando" })
            if (data.TryGetProperty(field, out var value)) Field(field, value.ToString());
        var continuous = data.TryGetProperty("continuous_allowed", out var allowed) && allowed.ValueKind == System.Text.Json.JsonValueKind.True;
        Text(continuous ? "aprobar [id] continuo: permiso para este interprete y directorio durante la sesion."
            : "Destructivo u opaco: solo aprobacion puntual, incluso con permisos continuos.", ConsoleColor.Yellow);
    }
    public static void ToolResult(string name, string output, bool success)
    {
        if (!success || name is not ("code_view" or "edit_text_file")) return;
        using var document = System.Text.Json.JsonDocument.Parse(output);
        var data = document.RootElement;
        Section(name == "code_view" ? "CODE VIEW  /  Documento leido" : "EDICION  /  Resultado");
        if (data.TryGetProperty("path", out var path)) Field("Archivo", path.GetString());
        if (name == "code_view")
        {
            if (data.TryGetProperty("code_view", out var code))
                foreach (var line in DataGuard.Display(code.GetString() ?? "").Replace("\r\n", "\n").Split('\n'))
                    Styled(Margin + line + Environment.NewLine, ConsoleColor.Gray);
            foreach (var field in new[] { "start_line", "next_line", "next_column", "truncated" })
                if (data.TryGetProperty(field, out var value)) Field(field, value);
        }
        else
            foreach (var field in new[] { "preview", "written", "changes", "before_excerpt", "after_excerpt" })
                if (data.TryGetProperty(field, out var value)) Field(field, value);
    }
    public static string StreamMargin => _plain ? "" : "    ";
    public static void Prompt(int exchanges)
    {
        Styled($"\n{Margin}agentloop [{exchanges}] > ", ConsoleColor.Cyan);
    }
}

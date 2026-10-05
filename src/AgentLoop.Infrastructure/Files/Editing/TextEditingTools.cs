using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace AgentLoop.Tools;

/// <summary>Edicion vinculada a lectura y version. Cada registro tiene sus propios recibos en memoria.</summary>
public sealed class TextEditingTools
{
    private readonly TextEditingOptions _options;
    private readonly Dictionary<string,TextReadReceipt> _reads = new(StringComparer.Ordinal);
    private readonly Queue<string> _issued = new();
    private readonly SemaphoreSlim _gate = new(1,1);
    public TextEditingTools(TextEditingOptions? options = null)
    {
        _options = options ?? new();
        if (_options.MaxFileBytes < 1 || _options.MaxViewCharacters < 1 || _options.DefaultPageLines < 1
            || _options.MaxPageLines < _options.DefaultPageLines || _options.MaxReadReceipts < 1
            || _options.MaxResponseCharacters < 1024 || _options.MaxResponseCharacters > DataLimits.PromptOutputCharacters-512)
            throw new ArgumentException("Presupuestos del editor invalidos.");
    }
    public ToolRegistry Registry() => new([ReadTool(false),ReadTool(true),EditTool(),WriteTool()]);
    private DelegateTool ReadTool(bool numbered) => new(numbered ? "code_view" : "read_text_file",
        "Lee texto antes de editar. Devuelve hash, read_receipt y rango visible. code_view agrega numeros de linea. "
        + "Si truncated=true sigue con next_line/next_column; el recibo solo autoriza el rango devuelto.",
        """{"type":"object","properties":{"path":{"type":"string"},"start_line":{"type":"integer"},"start_column":{"type":"integer"},"line_count":{"type":"integer"},"max_characters":{"type":"integer"}},"required":["path"]}""",
        (args,token) => ReadAsync(args,numbered,token));
    private DelegateTool EditTool() => new("edit_text_file",
        "Reemplaza texto literal, palabras completas, lineas o parrafos. Primero lee con code_view/read_text_file y aporta read_receipt. "
        + "Rechaza version cambiada, rango no leido y coincidencias ambiguas. preview=true muestra propuesta sin escribir; despues de aplicar vuelve a leer.",
        """{"type":"object","properties":{"path":{"type":"string"},"read_receipt":{"type":"string"},"mode":{"type":"string","enum":["text","word","lines","paragraph"]},"old_text":{"type":"string"},"new_text":{"type":"string"},"start_line":{"type":"integer"},"end_line":{"type":"integer"},"expected_matches":{"type":"integer"},"preview":{"type":"boolean"}},"required":["path","read_receipt","mode","new_text"]}""",
        EditAsync);
    private DelegateTool WriteTool() => new("write_text_file",
        "Crea un archivo UTF-8. Para sobrescribir uno existente primero lee TODO el documento y aporta read_receipt. Para cambios parciales usa edit_text_file.",
        """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"},"read_receipt":{"type":"string"}},"required":["path","content"]}""",
        WriteAsync);
    private static string PathFrom(JsonElement args)
    {
        var path = ToolArguments.ResolvePath(args,"path");
        ProjectContractAdmission.CheckResource(path);
        return path;
    }
    private async Task<byte[]> ReadBytesAsync(Stream stream,CancellationToken token)
    {
        if (stream.Length > _options.MaxFileBytes) throw new InvalidOperationException("Documento excede presupuesto de bytes del editor.");
        using var buffer = new MemoryStream();
        var chunk = new byte[Math.Min(_options.MaxFileBytes,8192)];
        int count;
        while ((count = await stream.ReadAsync(chunk,token)) != 0)
        {
            if (buffer.Length+count > _options.MaxFileBytes) throw new InvalidOperationException("Documento crecio fuera del presupuesto.");
            buffer.Write(chunk,0,count);
        }
        return buffer.ToArray();
    }
    private static (int Line,int Column) Location(int offset,int[] starts)
    {
        var index = Array.BinarySearch(starts,offset);
        if (index < 0) index = ~index-1;
        return (index+1,offset-starts[index]+1);
    }
    private async ValueTask<string> ReadAsync(JsonElement args,bool numbered,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var path = PathFrom(args);
            await using var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            var doc = TextDocumentSnapshot.Decode(path,await ReadBytesAsync(stream,token));
            var starts = doc.LineStarts;
            var line = ToolArguments.ReadInt(args,"start_line",1);
            var column = ToolArguments.ReadInt(args,"start_column",1);
            var count = ToolArguments.ReadInt(args,"line_count",numbered ? _options.DefaultPageLines : starts.Length);
            if (line < 1 || line > starts.Length || column < 1 || count < 1) throw new ArgumentException("Rango de lectura invalido; indices desde 1.");
            var lineEnd = line < starts.Length ? starts[line] : doc.Text.Length;
            while (lineEnd > starts[line-1] && doc.Text[lineEnd-1] is '\r' or '\n') lineEnd--;
            var requestedStart = (long)starts[line-1]+column-1;
            if (requestedStart > lineEnd) throw new ArgumentException("Columna fuera de la linea.");
            var start = (int)requestedStart;
            if (start < doc.Text.Length && char.IsLowSurrogate(doc.Text[start])) throw new ArgumentException("La columna divide un caracter Unicode; ajusta el inicio.");
            var lastLine = Math.Min(starts.Length,(int)Math.Min((long)starts.Length,(long)line-1+Math.Min(count,_options.MaxPageLines)));
            var rangeEnd = lastLine < starts.Length ? starts[lastLine] : doc.Text.Length;
            var requested = ToolArguments.ReadInt(args,"max_characters",_options.MaxViewCharacters);
            if (requested < 1) throw new ArgumentException("max_characters debe ser positivo.");
            var length = Math.Min(rangeEnd-start,Math.Min(requested,_options.MaxViewCharacters));
            var id = Guid.NewGuid().ToString("N");
            var readStart=start;var readEnd=start+length;
            string output;
            while (true)
            {
                if (length > 0 && start+length < doc.Text.Length && (char.IsHighSurrogate(doc.Text[start+length-1])
                    || doc.Text[start+length-1] == '\r' && doc.Text[start+length] == '\n')) length--;
                var content = doc.Text.Substring(start,length);
                var end = start+length;
                var next = Location(end,starts);
                var display = numbered ? string.Join("\n",content.Replace("\r\n","\n").Replace('\r','\n').Split('\n').Select((text,index) => $"{line+index,6} | {text}")) : null;
                readStart=start;readEnd=end;
                // Merge only contiguous ranges of the exact same byte version already returned to the agent.
                bool merged;
                do
                {
                    merged=false;
                    foreach (var previous in _reads.Values.Where(r => string.Equals(r.Path,path,OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && r.Hash == doc.Hash))
                        if (previous.Start <= readEnd && previous.End >= readStart)
                        {
                            var from=Math.Min(previous.Start,readStart);var until=Math.Max(previous.End,readEnd);
                            merged |= from!=readStart || until!=readEnd;readStart=from;readEnd=until;
                        }
                } while (merged);
                output = JsonSerializer.Serialize(new { path,content,lines=starts.Length,hash=doc.Hash,read_receipt=id,
                    read_scope_start=readStart,read_scope_end=readEnd,document_characters=doc.Text.Length,
                    start_line=line,start_column=column,end_line=next.Line,end_column=next.Column,
                    next_line=next.Line,next_column=next.Column,truncated=end < doc.Text.Length,code_view=display,
                    encoding=doc.Encoding.WebName,bom=doc.Preamble.Length>0 });
                if (output.Length <= _options.MaxResponseCharacters) break;
                if (length <= 1) throw new InvalidOperationException("Metadatos o linea exceden el presupuesto de respuesta.");
                length /= 2;
            }
            if (length == 0 && start < doc.Text.Length) throw new InvalidOperationException("La pagina no contiene caracteres legibles; ajusta el rango.");
            _reads[id] = new(id,path,doc.Hash,readStart,readEnd);
            _issued.Enqueue(id);
            while (_issued.Count > _options.MaxReadReceipts) _reads.Remove(_issued.Dequeue());
            return output;
        }
        finally { _gate.Release(); }
    }
    private TextReadReceipt RequireRead(JsonElement args,TextDocumentSnapshot doc,int? start = null,int? end = null)
    {
        var id = ToolArguments.ReadRequiredString(args,"read_receipt");
        if (!_reads.TryGetValue(id,out var read) || !string.Equals(read.Path,doc.Path,OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Lee este documento primero: read_receipt ausente o de otro archivo/registro.");
        if (read.Hash != doc.Hash) { Invalidate(doc.Path); throw new InvalidOperationException("El documento cambio despues de leerlo. Vuelve a leer antes de editar."); }
        if (start is {} from && from < read.Start || end is {} until && until > read.End) throw new InvalidOperationException("El rango de edicion no fue leido. Usa code_view para leerlo completo.");
        return read;
    }
    private void Invalidate(string path)
    {
        foreach (var id in _reads.Where(p => string.Equals(p.Value.Path,path,OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).Select(p => p.Key).ToArray()) _reads.Remove(id);
    }
    private static bool ParagraphBoundary(string text,int start,int end) =>
        (start == 0 || Regex.IsMatch(text[..start],@"(?:\r\n|\n|\r)[ \t]*(?:\r\n|\n|\r)$",RegexOptions.None,TimeSpan.FromMilliseconds(100)))
        && (end == text.Length || Regex.IsMatch(text[end..],@"^(?:\r\n|\n|\r)[ \t]*(?:\r\n|\n|\r)",RegexOptions.None,TimeSpan.FromMilliseconds(100)));
    private async ValueTask<string> EditAsync(JsonElement args,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        var effectStarted=false;
        try
        {
            var path = PathFrom(args);
            var preview=args.TryGetProperty("preview",out var previewNode) && previewNode.ValueKind == JsonValueKind.True;
            // Verificacion y modificacion bajo el mismo handle exclusivo; no se escribe una version distinta de la leida.
            await using var file = new FileStream(path,FileMode.Open,preview ? FileAccess.Read : FileAccess.ReadWrite,FileShare.None);
            var doc = TextDocumentSnapshot.Decode(path,await ReadBytesAsync(file,token));
            var read = RequireRead(args,doc); // Validate identity/version; range is checked below.
            var mode = ToolArguments.ReadRequiredString(args,"mode");
            var replacement = doc.NormalizeNewLines(ToolArguments.ReadString(args,"new_text") ?? throw new ArgumentException("Falta new_text."));
            if (replacement.Contains('\0')) throw new ArgumentException("NUL no admitido en texto.");
            var ranges = new List<(int Start,int End)>();
            if (mode == "lines")
            {
                var starts = doc.LineStarts;
                var first = ToolArguments.ReadInt(args,"start_line",0);
                var last = ToolArguments.ReadInt(args,"end_line",first);
                if (first < 1 || last < first || last > starts.Length) throw new ArgumentException("Rango de lineas invalido; indices inclusivos desde 1.");
                var start=starts[first-1];var end=last < starts.Length ? starts[last] : doc.Text.Length;
                RequireRead(args,doc,start,end);
                if (replacement.Length > 0 && end > start && doc.Text[end-1] is '\r' or '\n' && !replacement.EndsWith('\r') && !replacement.EndsWith('\n')) replacement+=doc.NewLine;
                if (ToolArguments.ReadString(args,"old_text") is {} expected && doc.NormalizeNewLines(expected).TrimEnd('\r','\n') != doc.Text[start..end].TrimEnd('\r','\n'))
                    throw new InvalidOperationException("old_text no coincide con las lineas leidas.");
                ranges.Add((start,end));
            }
            else if (mode is "text" or "word" or "paragraph")
            {
                var old = doc.NormalizeNewLines(ToolArguments.ReadRequiredString(args,"old_text"));
                if (mode == "word" && old.Any(char.IsWhiteSpace)) throw new ArgumentException("word requiere una palabra sin espacios.");
                var pattern = mode == "word" ? @"(?<![\p{L}\p{N}\p{M}_])"+Regex.Escape(old)+@"(?![\p{L}\p{N}\p{M}_])" : Regex.Escape(old);
                foreach (Match match in Regex.Matches(doc.Text,pattern,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))
                {
                    var start=match.Index;var end=start+match.Length;
                    if (start < read.Start || end > read.End) continue;
                    if (mode != "paragraph" || ParagraphBoundary(doc.Text,start,end)) ranges.Add((start,end));
                }
                var expected=ToolArguments.ReadInt(args,"expected_matches",1);
                if (expected < 1 || ranges.Count != expected) throw new InvalidOperationException($"Coincidencias: {ranges.Count}; se requieren exactamente {expected}. Precisa el texto/rango antes de editar.");
            }
            else throw new ArgumentException("Modo de edicion no permitido.");
            var result = new StringBuilder(doc.Text.Length);
            var cursor=0;
            foreach (var range in ranges) { result.Append(doc.Text.AsSpan(cursor,range.Start-cursor));result.Append(replacement);cursor=range.End; }
            result.Append(doc.Text.AsSpan(cursor));
            var updated=result.ToString();
            var bytes=doc.Encode(updated);
            if (bytes.Length > _options.MaxFileBytes) throw new InvalidOperationException("Edicion excede presupuesto de bytes.");
            if (!preview)
            {
                effectStarted=true;
                await ReplaceLockedAsync(file,doc.Bytes,bytes,token);
                Invalidate(path);
            }
            return JsonSerializer.Serialize(new {path,written=!preview,preview,changes=ranges.Count,old_hash=doc.Hash,new_hash=Convert.ToHexString(SHA256.HashData(bytes)),
                encoding=doc.Encoding.WebName,bom=doc.Preamble.Length>0,bytes=bytes.Length,
                before_excerpt=TextSanitizer.Truncate(doc.Text[ranges[0].Start..ranges[0].End],512),after_excerpt=TextSanitizer.Truncate(replacement,512),
                next="Vuelve a leer con code_view para comprobar el resultado antes de otro cambio."});
        }
        catch (Exception error) when (!effectStarted && error is not OperationCanceledException)
        { return JsonSerializer.Serialize(new { error=DataGuard.Display(error.Message),effects_started=false }); }
        finally { _gate.Release(); }
    }
    private static async Task ReplaceLockedAsync(FileStream file,byte[] original,byte[] updated,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            file.Position=0;
            await file.WriteAsync(updated,token);
            file.SetLength(updated.Length);
            await file.FlushAsync(token);
            file.Flush(flushToDisk:true);
        }
        catch
        {
            // Best-effort rollback while the exclusive handle remains owned. Failure is never reported as success.
            file.Position=0;
            await file.WriteAsync(original,CancellationToken.None);
            file.SetLength(original.Length);
            await file.FlushAsync(CancellationToken.None);
            file.Flush(flushToDisk:true);
            throw;
        }
    }
    private async ValueTask<string> WriteAsync(JsonElement args,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        var effectStarted=false;
        try
        {
            var path=PathFrom(args);
            var content=ToolArguments.ReadString(args,"content") ?? throw new ArgumentException("Falta content.");
            var parent=System.IO.Path.GetDirectoryName(path)!;
            if (content.Contains('\0')) throw new ArgumentException("NUL no admitido en texto.");
            if (File.Exists(path))
            {
                await using var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
                var doc=TextDocumentSnapshot.Decode(path,await ReadBytesAsync(file,token));
                RequireRead(args,doc,0,doc.Text.Length);
                var updated=doc.Encode(doc.NormalizeNewLines(content));
                if (updated.Length>_options.MaxFileBytes) throw new InvalidOperationException("Escritura excede presupuesto de bytes.");
                effectStarted=true;
                await ReplaceLockedAsync(file,doc.Bytes,updated,token);
                Invalidate(path);
                return JsonSerializer.Serialize(new {path,written=true,bytes=updated.Length,old_hash=doc.Hash,new_hash=Convert.ToHexString(SHA256.HashData(updated))});
            }
            var bytes=new UTF8Encoding(false,true).GetBytes(content);
            if (bytes.Length>_options.MaxFileBytes) throw new InvalidOperationException("Escritura excede presupuesto de bytes.");
            token.ThrowIfCancellationRequested();
            effectStarted=true;
            Directory.CreateDirectory(parent);
            var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary,bytes,token);
                token.ThrowIfCancellationRequested();
                // Create only: a competing creation cannot be overwritten without a read receipt.
                File.Move(temporary,path,overwrite:false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return JsonSerializer.Serialize(new {path,written=true,bytes=bytes.Length,new_hash=Convert.ToHexString(SHA256.HashData(bytes))});
        }
        catch (Exception error) when (!effectStarted && error is not OperationCanceledException)
        { return JsonSerializer.Serialize(new { error=DataGuard.Display(error.Message),effects_started=false }); }
        finally { _gate.Release(); }
    }
}

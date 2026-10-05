using System.Security.Cryptography;
using System.Text;
namespace AgentLoop.Tools;
/// <summary>Version de bytes y texto; conserva codificacion, BOM y terminadores del documento.</summary>
internal sealed record TextDocumentSnapshot(string Path, byte[] Bytes, string Text, Encoding Encoding, byte[] Preamble)
{
    public string Hash => Convert.ToHexString(SHA256.HashData(Bytes));
    public string NewLine => Text.Contains("\r\n") ? "\r\n" : Text.Contains('\r') ? "\r" : "\n";
    public string NormalizeNewLines(string value) => value.Replace("\r\n", "\n").Replace('\r','\n').Replace("\n",NewLine);
    public byte[] Encode(string value) => [..Preamble, ..Encoding.GetBytes(value)];
    public int[] LineStarts
    {
        get
        {
            var starts = new List<int> { 0 };
            for (var index = 0; index < Text.Length; index++)
            {
                if (Text[index] == '\r' && index+1 < Text.Length && Text[index+1] == '\n') index++;
                if (Text[index] is '\r' or '\n') starts.Add(index+1);
            }
            return starts.ToArray();
        }
    }
    public static TextDocumentSnapshot Decode(string path, byte[] bytes)
    {
        Encoding encoding = new UTF8Encoding(false,true);
        byte[] preamble = [];
        if (bytes.AsSpan().StartsWith(new byte[] {0xff,0xfe,0,0}) || bytes.AsSpan().StartsWith(new byte[] {0,0,0xfe,0xff}))
            throw new InvalidOperationException("UTF-32 no admitido; usa UTF-8 o UTF-16.");
        if (bytes.AsSpan().StartsWith(new byte[] {0xef,0xbb,0xbf})) preamble = [0xef,0xbb,0xbf];
        else if (bytes.AsSpan().StartsWith(new byte[] {0xff,0xfe})) { encoding = new UnicodeEncoding(false,false,true); preamble=[0xff,0xfe]; }
        else if (bytes.AsSpan().StartsWith(new byte[] {0xfe,0xff})) { encoding = new UnicodeEncoding(true,false,true); preamble=[0xfe,0xff]; }
        var text = encoding.GetString(bytes.AsSpan(preamble.Length));
        if (text.Contains('\0')) throw new InvalidOperationException("Contenido binario no admitido como documento de texto.");
        return new(path,bytes,text,encoding,preamble);
    }
}

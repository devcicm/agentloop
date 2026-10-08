namespace AgentLoop.Tools;

/// <summary>Lectura acotada incluso si el archivo cambia de tamaÃƒÂ±o; no asigna el documento completo antes de comprobar.</summary>
public static class BoundedFile
{
    public static string ReadText(string path,int maximumBytes) => TextDocumentSnapshot.Decode(path,ReadBytes(path,maximumBytes)).Text;

    public static byte[] ReadBytes(string path,int maximumBytes)
    {
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if (file.Length > maximumBytes) throw new InvalidOperationException("Archivo fuera de presupuesto.");
        using var buffer=new MemoryStream(); var bytes=new byte[Math.Min(8192,maximumBytes)]; int read;
        while ((read=file.Read(bytes,0,bytes.Length)) != 0)
        {
            if (buffer.Length+read > maximumBytes) throw new InvalidOperationException("Archivo crecio fuera de presupuesto.");
            buffer.Write(bytes,0,read);
        }
        return buffer.ToArray();
    }
}

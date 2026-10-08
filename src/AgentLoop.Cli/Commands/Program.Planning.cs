using System.Text.Json;

namespace AgentLoop;

internal static partial class Program
{
    private static int ShowSkills(string[] args)
    {
        var services=OperationServiceFactory.Create();
        var intent=args.Length>1 ? ParseIntent(args[1]) : AgentLoop.Operations.OperationIntent.General;
        var skills=services.Skills?.List(intent.ToString()) ?? [];
        ConsoleUi.Section("SKILLS / Procedimientos del operador");
        foreach (var skill in skills) ConsoleUi.Field(skill.Id,$"{skill.Version}: {skill.Purpose} | max calls={skill.MaxCalls} | tools={string.Join(", ",skill.RequiredTools)}");
        if (skills.Count==0) ConsoleUi.Text("Sin skills instaladas para esta intencion.");
        return 0;
    }
    private static async Task<int> InspectRepositoryAsync(string[] args,CancellationToken token)
    {
        var root=StringOption(args,"--root") ?? Environment.CurrentDirectory;
        var tool=new RepositoryReviewTools().Registry();
        var arguments=JsonSerializer.SerializeToElement(new { root,focus=StringOption(args,"--focus") ?? "", max_files=IntOption(args,"--max-files",64) });
        var result=await tool.ExecuteAsync(new ParsedToolCall("operator-review","code_review",arguments,arguments.GetRawText(),false),token);
        Console.WriteLine(DataGuard.Redact(result.Output)); return result.Failed ? 1 : 0;
    }
    private static async Task<int> RestoreEditAsync(string[] args,CancellationToken token)
    {
        if (args.Length<4 || args[1]!="restaurar") return Fail("Uso: ediciones restaurar <id> --hash-current <hash observado>.");
        var hash=StringOption(args,"--hash-current");
        if (hash is null) return Fail("Lee/inspecciona la version actual y aporta --hash-current.");
        if (!Guid.TryParseExact(args[2],"N",out _)) return Fail("Id de recuperacion invalido.");
        if (Console.IsInputRedirected) return Fail("Restaurar requiere aprobacion puntual en consola interactiva.");
        var journal=Path.Combine(SecretStore.ResolveRepositoryRoot(),".agentloop","edit-recovery",args[2]+".json");
        using var proposal=JsonDocument.Parse(BoundedFile.ReadText(journal,64000));
        ConsoleUi.Field("Archivo",proposal.RootElement.GetProperty("path").GetString());
        ConsoleUi.Field("Version",hash);
        ConsoleUi.Text("Restauracion destructiva del original para id "+args[2]+". Escribe RESTAURAR "+args[2]+" para autorizar esta accion.");
        if (Console.ReadLine()!="RESTAURAR "+args[2]) return Fail("Restauracion rechazada; sin cambios.");
        await EditRecoveryStore.RestoreAsync(args[2],hash,token);
        ConsoleUi.Status("Original restaurado. Relee el archivo y reconcilia su operacion antes de repetir acciones.",true);
        return 0;
    }
    private static int InspectEdits(string[] args)
    {
        var directory=Path.Combine(SecretStore.ResolveRepositoryRoot(),".agentloop","edit-recovery");
        if (!Directory.Exists(directory)) { ConsoleUi.Text("Sin registros de edicion."); return 0; }
        if (args.Length!=1) return Fail("Uso: ediciones. La restauracion de originales requiere revision explicita del operador.");
        foreach (var file in Directory.EnumerateFiles(directory,"*.json").Take(256))
        {
            using var data=JsonDocument.Parse(BoundedFile.ReadText(file,64000));
            var root=data.RootElement; var path=root.GetProperty("path").GetString()!;
            ProjectContractAdmission.CheckResource(path);
            string? actual=null;
            if (File.Exists(path) && new FileInfo(path).Length <= 8000000)
            { using var content=File.OpenRead(path); actual=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)); }
            var state=actual==root.GetProperty("oldHash").GetString() ? "original observado"
                : actual==root.GetProperty("newHash").GetString() ? "cambio observado" : "incierto: revisar antes de restaurar/repetir";
            ConsoleUi.Field(root.GetProperty("id").GetString()!,state+" | current_hash="+(actual ?? "desconocido")+" | "+path);
        }
        return 0;
    }
}

using System.Text;
using System.Text.Json;
namespace AgentLoop.Verification;

/// <summary>Regresion local de distribucion: permisos, transporte multilinea y editor con lectura previa.</summary>
public static class ReleaseReadinessSelfTest
{
    public static async Task<bool> RunAsync(CancellationToken token)
    {
        var checks = new List<(string Name, bool Ok)>();
        void Check(string name, bool ok) { checks.Add((name,ok)); Console.WriteLine((ok ? "OK " : "FAIL ")+name); }
        var temp = Path.GetFullPath(Path.Combine(Path.GetTempPath(),"agentloop-release-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(temp);
        try
        {
            var prompts=0;
            var policy=new ExecutionPolicy { Mode=PolicyMode.Confirmar,WorkingDirectory=temp,
                RiskClassifier=new ShellCommandRiskClassifier(),AsyncConfirm=(_,_) => { prompts++; return ValueTask.FromResult(false); } };
            var safe=new ShellCommand(ShellKind.PowerShell,"Write-Output 'dato observado'",temp);
            Check("confirmar rechaza sin consentimiento",!(await policy.EvaluateAsync(safe,token)).Allowed && prompts==1);
            Check("permiso continuo ordinario concedido",policy.GrantContinuous(safe));
            Check("continuo evita nueva pregunta ordinaria",(await policy.EvaluateAsync(safe,token)).Allowed && prompts==1);
            Check("continuo acotado a interprete",!policy.HasContinuousPermission(safe with { Shell=ShellKind.Cmd }));
            Check("continuo acotado a directorio",!policy.HasContinuousPermission(safe with { WorkingDirectory=Path.Combine(temp,"otro") }));
            var destructive=safe with {Command="Remove-Item -LiteralPath 'dato.txt'"};
            Check("destructivo exige permiso puntual",!(await policy.EvaluateAsync(destructive,token)).Allowed && prompts==2);
            Check("destructivo no concede continuo",!policy.GrantContinuous(destructive));
            Check("codigo opaco exige permiso puntual",policy.Assess(safe with {Command="Invoke-Expression $script"}).RequiresFreshApproval);
            policy.RevokeContinuousPermissions();
            Check("revocacion elimina continuo",!policy.HasContinuousPermission(safe) && policy.ContinuousPermissions.Count==0);
            var auto=new ExecutionPolicy { RiskClassifier=new ShellCommandRiskClassifier(),AsyncConfirm=(_,_) => ValueTask.FromResult(false) };
            Check("auto permite ordinario",(await auto.EvaluateAsync(safe,token)).Allowed);
            Check("auto no omite riesgo destructivo",!(await auto.EvaluateAsync(destructive,token)).Allowed);
            Check("sin clasificador se rechaza",!new ExecutionPolicy().Evaluate(safe).Allowed);
            var script="$texto = @'\nprimera linea\nsegunda linea\n'@\nWrite-Output $texto";
            var command=safe with {Command=script};
            var args=command.ToStartInfo(temp).ArgumentList.ToArray();
            Check("transporte conserva script exacto",args.Contains("-EncodedCommand") && Encoding.Unicode.GetString(Convert.FromBase64String(args[^1]))==script);
            var bus=new EventBus(); using var supervisor=new ProcessSupervisor(bus,new ExecutionPolicy {WorkingDirectory=temp});
            var observed=await supervisor.RunAsync(command,TimeSpan.FromSeconds(10),token);
            Check("here-string real produce salida",observed.ExitCode==0 && string.Join("\n",observed.Output).Contains("segunda linea"));
            var registry=BuiltInTools.DefaultRegistry().WithScope(Scope.Minimal(new ExecutionPolicy {WorkingDirectory=temp}));
            async Task<ToolExecution> Call(string name,object arguments)
            {
                var raw=JsonSerializer.Serialize(arguments);
                return await registry.ExecuteAsync(new(Guid.NewGuid().ToString("N"),name,JsonSerializer.SerializeToElement(arguments),raw,false),token);
            }
            async Task<string> Read(string path) { var result=await Call("code_view",new {path,line_count=200}); using var doc=JsonDocument.Parse(result.Output); return doc.RootElement.GetProperty("read_receipt").GetString()!; }
            var path=Path.Combine(temp,"documento con espacios.txt");
            var original="uno dos\r\ntercera linea\r\n\r\nParrafo original.\r\n\r\nFinal.\r\n";
            await File.WriteAllTextAsync(path,original,new UTF8Encoding(false,true),token);
            Check("sin lectura no modifica",(await Call("edit_text_file",new {path,read_receipt="inventado",mode="word",old_text="uno",new_text="UNO"})).Failed && await File.ReadAllTextAsync(path,token)==original);
            var receipt=await Read(path);
            Check("preview no escribe",!(await Call("edit_text_file",new {path,read_receipt=receipt,mode="word",old_text="uno",new_text="UNO",preview=true})).Failed && await File.ReadAllTextAsync(path,token)==original);
            Check("palabra completa se aplica",!(await Call("edit_text_file",new {path,read_receipt=receipt,mode="word",old_text="uno",new_text="UNO"})).Failed && (await File.ReadAllTextAsync(path,token)).StartsWith("UNO dos"));
            Check("aplicar invalida recibo",(await Call("edit_text_file",new {path,read_receipt=receipt,mode="text",old_text="dos",new_text="DOS"})).Failed);
            receipt=await Read(path);
            Check("lineas preservan CRLF",!(await Call("edit_text_file",new {path,read_receipt=receipt,mode="lines",start_line=2,end_line=2,old_text="tercera linea",new_text="otra linea"})).Failed && (await File.ReadAllTextAsync(path,token)).Contains("otra linea\r\n\r\n"));
            receipt=await Read(path);
            Check("parrafo se aplica",!(await Call("edit_text_file",new {path,read_receipt=receipt,mode="paragraph",old_text="Parrafo original.",new_text="Parrafo actualizado."})).Failed);
            receipt=await Read(path);
            Check("texto literal se aplica",!(await Call("edit_text_file",new {path,read_receipt=receipt,mode="text",old_text="Final.",new_text="Fin."})).Failed);
            receipt=await Read(path);
            Check("ambiguedad rechaza sin incertidumbre",(await Call("edit_text_file",new {path,read_receipt=receipt,mode="text",old_text="a",new_text="A",expected_matches=99})) is {Failed:true,EffectsMayHaveOccurred:false});
            await File.AppendAllTextAsync(path,"cambio externo",token);
            Check("hash cambiado rechaza",(await Call("edit_text_file",new {path,read_receipt=receipt,mode="text",old_text="Fin.",new_text="Final."})).Failed);
            var partial=await Call("code_view",new {path,start_line=2,line_count=1});
            using(var d=JsonDocument.Parse(partial.Output))
                Check("sobrescribir exige lectura completa",(await Call("write_text_file",new {path,read_receipt=d.RootElement.GetProperty("read_receipt").GetString(),content="nuevo"})).Failed);
            var paginated=BuiltInTools.DefaultRegistry().WithScope(Scope.Minimal(new ExecutionPolicy {WorkingDirectory=temp}));
            var line=1;var column=1;var pages=0;string? lastReceipt=null;
            while(pages++<100)
            {
                var a=new {path,start_line=line,start_column=column,max_characters=11};var raw=JsonSerializer.Serialize(a);
                var page=await paginated.ExecuteAsync(new(Guid.NewGuid().ToString("N"),"code_view",JsonSerializer.SerializeToElement(a),raw,false),token);
                using var d=JsonDocument.Parse(page.Output);lastReceipt=d.RootElement.GetProperty("read_receipt").GetString();
                if(!d.RootElement.GetProperty("truncated").GetBoolean()) break;
                line=d.RootElement.GetProperty("next_line").GetInt32();column=d.RootElement.GetProperty("next_column").GetInt32();
            }
            var overwrite=new {path,read_receipt=lastReceipt,content="contenido tras lectura paginada"};
            var written=await paginated.ExecuteAsync(new(Guid.NewGuid().ToString("N"),"write_text_file",JsonSerializer.SerializeToElement(overwrite),JsonSerializer.Serialize(overwrite),false),token);
            Check("paginas contiguas permiten sobrescritura",pages<100 && !written.Failed);
            var utf16=Path.Combine(temp,"utf16.txt");await File.WriteAllTextAsync(utf16,"hola mundo\r\n",Encoding.Unicode,token);
            receipt=await Read(utf16);
            var edited=await Call("edit_text_file",new {path=utf16,read_receipt=receipt,mode="word",old_text="hola",new_text="adios"});
            var bytes=await File.ReadAllBytesAsync(utf16,token);
            Check("UTF16 y BOM se conservan",!edited.Failed && bytes[0]==255 && bytes[1]==254 && Encoding.Unicode.GetString(bytes[2..]).StartsWith("adios"));
            var binary=Path.Combine(temp,"binary.txt");await File.WriteAllBytesAsync(binary,[65,0,66],token);
            Check("binario falla sin efectos",(await Call("code_view",new {path=binary})) is {Failed:true,EffectsMayHaveOccurred:false});
        }
        finally
        {
            var boundary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!temp.StartsWith(boundary,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)) throw new InvalidOperationException("Temporal fuera de alcance.");
            Directory.Delete(temp,recursive:true);
        }
        Console.WriteLine($"Release readiness: {checks.Count(c=>c.Ok)}/{checks.Count}");
        return checks.All(c=>c.Ok);
    }
}

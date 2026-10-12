using System.Text.Json;
namespace AgentLoop.Verification;
public static class SessionPersistenceSelfTest
{
    public static bool Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "agentloop-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ok = true;
        void Check(string label,bool value) { Console.WriteLine($"  [{(value ? "ok" : "FALLO")}] {label}"); ok &= value; }
        try
        {
            var store = new FileConversationSessionStore();
            var session = new ConversationSession { SessionStore = store };
            session.UseSystemTemplate(PromptTemplate.Parse("Sistema de prueba."));
            session.SetIntent(AgentLoop.Operations.OperationIntent.Review);
            session.BindObjective("revisar el repositorio");
            session.AddConstraint("citar archivos observados");
            var context = session.StartTurn("hola");
            context.Messages.Add(ChatMessage.FromAssistant("listo"));
            session.CommitTurn(context);
            var path = Path.Combine(root,"sesion con espacios.json");
            session.SaveSession(path);
            var restored = new ConversationSession { SessionStore = store };
            restored.LoadSession(path);
            Check("sesion persiste y recupera mensajes con adaptador instalado", restored.Messages.Count == 2 && restored.Messages[1].Content == "listo");
            Check("sesion conserva intencion objetivo y restricciones", restored.TaskState.Intent == AgentLoop.Operations.OperationIntent.Review
                && restored.TaskState.Objective == "revisar el repositorio" && restored.TaskState.Constraints.SequenceEqual(["citar archivos observados"]));
            var memory = new FileConversationMemory(root);
            memory.Remember("idioma=espanol");
            Check("memoria explicita persiste independiente del historial",new FileConversationMemory(root).Notes["idioma"] == "espanol");
            var closed = false;
            try { new ConversationSession().SaveSession(path); } catch (InvalidOperationException) { closed = true; }
            Check("harness sin adaptador de almacenamiento falla cerrado", closed);
            Check("payload conserva dialecto thinking fuera del Kernel", ChatPayloadSerializer.Serialize(new ChatRequest { Model="fixture", Messages=[ChatMessage.FromUser("hola")], Thinking=ThinkingOptions.Enabled(256) }).Contains("budget_tokens"));
            Check("contrato neutral no define nombres JSON del proveedor", JsonSerializer.Serialize(ThinkingOptions.Enabled(256)).Contains("Mode") && !JsonSerializer.Serialize(ThinkingOptions.Enabled(256)).Contains("budget_tokens"));
            Check("secreto se descubre desde binario bajo src",SecretStore.ResolveRepositoryRoot() == Path.GetFullPath(SecretStore.ResolveRepositoryRoot()) && Directory.Exists(Path.Combine(SecretStore.ResolveRepositoryRoot(),"src")));
        }
        finally { Directory.Delete(root, recursive:true); }
        return ok;
    }
}


namespace AgentLoop.Provider;


/// <summary>Que hacer con el thinking cuando no se dice explicitamente por modelo.</summary>
public enum ThinkingPreference
{
    /// <summary>Activo salvo en los modelos de los que ya consta que no lo soportan.</summary>
    Auto,

    /// <summary>Activo siempre, aunque el modelo lo haya rechazado antes.</summary>
    Always,

    /// <summary>Nunca se manda el parametro.</summary>
    Never,
}

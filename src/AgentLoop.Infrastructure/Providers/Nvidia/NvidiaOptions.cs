
namespace AgentLoop.Provider;


/// <summary>Configuracion del cliente HTTP contra el gateway.</summary>
public sealed class NvidiaOptions : TransportOptions
{
    public const string DefaultBaseUrl = "https://integrate.api.nvidia.com/v1";

    /// <summary>
    /// Modelo NVIDIA predeterminado; el catalogo describe los modelos configurados.
    /// La configuracion no garantiza acceso ni disponibilidad remota.
    /// discover puede producir un cache local de capacidades.
    /// </summary>
    public const string FallbackModelId = NvidiaModelCatalog.Glm53;

    public const string KimiK3ModelId = NvidiaModelCatalog.KimiK3;

    public static IReadOnlyList<string> SupportedModels { get; } =
        NvidiaModelCatalog.Models;

    public NvidiaOptions() { BaseUrl = DefaultBaseUrl; }
}

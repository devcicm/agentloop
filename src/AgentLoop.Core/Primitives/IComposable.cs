
namespace AgentLoop.Harness.Functional;


/// <summary>
/// Una funcion pura que se puede componer con otras: dada una entrada,
/// produce una salida. Es el primer pilar de la programacion funcional
/// expresado como tipo: las funciones son valores que se pueden pasar,
/// guardar y combinar.
///
/// No es obligatorio: una funcion anonima o un delegado valen. Pero si
/// quieres que el codigo explicite "esto se compone con aquello", la
/// interfaz lo deja claro en la firma.
/// </summary>
public interface IComposable<TIn, TOut>
{
    TOut Apply(TIn input);
}

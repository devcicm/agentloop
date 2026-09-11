using System.Collections.Immutable;

namespace AgentLoop.Harness.Functional;

/// <summary>
/// Helpers de inmutabilidad: convertir listas mutables en inmutables,
/// crear records a partir de otros con campos cambiados. El objetivo no
/// es substituir a la keyword <c>with</c> de C# (que ya funciona en
/// records), sino formalizar el contrato: cualquier valor que cruza un
/// limite (hilo, scope, async) pasa por aqui y se vuelve inmutable de
/// verdad, no solo nominal.
/// </summary>
public static class Immutable
{
    /// <summary>Congela una lista en un ImmutableArray equivalente. Es O(n) y copia los elementos.</summary>
    public static ImmutableArray<T> Freeze<T>(IEnumerable<T> source)
        => source is null ? [] : ImmutableArray.CreateRange(source);

    /// <summary>Congela un diccionario en un ImmutableDictionary equivalente.</summary>
    public static ImmutableDictionary<TKey, TValue> Freeze<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> source)
        where TKey : notnull
        => source is null
            ? ImmutableDictionary<TKey, TValue>.Empty
            : ImmutableDictionary.CreateRange(source);
}

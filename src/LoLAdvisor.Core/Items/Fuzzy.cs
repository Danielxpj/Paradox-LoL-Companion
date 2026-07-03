namespace LoLAdvisor.Core.Items;

/// <summary>
/// Núcleo de lógica difusa, sin dependencias: funciones de pertenencia y combinadores,
/// todos con salida en [0,1]. Reemplaza los umbrales duros del scoring por transiciones
/// graduales — el umbral de la v2 se preserva como el cruce μ=0.5 de la rampa.
/// </summary>
public static class Fuzzy
{
    /// <summary>Recorta a [0,1].</summary>
    public static double Clamp01(double x) => Math.Clamp(x, 0.0, 1.0);

    /// <summary>
    /// Rampa lineal: μ=0 en <paramref name="foot"/>, μ=1 en <paramref name="shoulder"/>,
    /// lineal en medio. Si <paramref name="foot"/> &gt; <paramref name="shoulder"/> la
    /// rampa es decreciente (μ=1 por debajo, μ=0 por encima). Si son iguales, es un escalón.
    /// </summary>
    public static double Ramp(double x, double foot, double shoulder)
    {
        if (foot == shoulder)
            return x >= foot ? 1.0 : 0.0;
        return Clamp01((x - foot) / (shoulder - foot));
    }

    /// <summary>Triángulo: sube de <paramref name="foot"/> a <paramref name="peak"/> (μ=1) y baja hasta <paramref name="shoulder"/>.</summary>
    public static double Triangle(double x, double foot, double peak, double shoulder) =>
        Math.Min(Ramp(x, foot, peak), Ramp(x, shoulder, peak));

    /// <summary>Conjunción prudente (mínimo).</summary>
    public static double And(params double[] degrees) => degrees.Select(Clamp01).Min();

    /// <summary>Disyunción (máximo).</summary>
    public static double Or(params double[] degrees) => degrees.Select(Clamp01).Max();

    /// <summary>Complemento (1 − μ).</summary>
    public static double Not(double a) => 1.0 - Clamp01(a);

    /// <summary>Conjunción suave (producto): el grado débil arrastra al fuerte.</summary>
    public static double AndProduct(double a, double b) => Clamp01(a) * Clamp01(b);
}

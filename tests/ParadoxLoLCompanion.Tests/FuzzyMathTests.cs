using ParadoxLoLCompanion.Core.Items;

namespace ParadoxLoLCompanion.Tests;

/// <summary>El núcleo difuso: funciones de pertenencia y combinadores, en [0,1].</summary>
public class FuzzyMathTests
{
    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.3, 0.3)]
    [InlineData(2.0, 1.0)]
    public void Clamp01_KeepsValueInUnitInterval(double x, double expected) =>
        Assert.Equal(expected, Fuzzy.Clamp01(x), precision: 6);

    [Fact]
    public void RampUp_IsZeroBelowFoot_OneAboveShoulder_LinearBetween()
    {
        Assert.Equal(0.0, Fuzzy.Ramp(0.4, foot: 0.5, shoulder: 0.7), precision: 6);
        Assert.Equal(0.0, Fuzzy.Ramp(0.5, foot: 0.5, shoulder: 0.7), precision: 6);
        Assert.Equal(0.5, Fuzzy.Ramp(0.6, foot: 0.5, shoulder: 0.7), precision: 6); // punto medio
        Assert.Equal(1.0, Fuzzy.Ramp(0.7, foot: 0.5, shoulder: 0.7), precision: 6);
        Assert.Equal(1.0, Fuzzy.Ramp(0.9, foot: 0.5, shoulder: 0.7), precision: 6);
    }

    [Fact]
    public void RampUp_CrossesHalfAtOldThreshold()
    {
        // La regla de diseño: el umbral duro de la v2 es el cruce μ=0.5 de la rampa.
        // Con foot y shoulder simétricos alrededor de 0.62, a 0.62 la pertenencia es 0.5.
        Assert.Equal(0.5, Fuzzy.Ramp(0.62, foot: 0.52, shoulder: 0.72), precision: 6);
    }

    [Fact]
    public void RampDown_WhenFootAboveShoulder_IsOneBelow_ZeroAbove()
    {
        // foot > shoulder ⇒ decreciente: μ=1 por debajo del shoulder, μ=0 por encima del foot.
        Assert.Equal(1.0, Fuzzy.Ramp(0.2, foot: 0.5, shoulder: 0.3), precision: 6);
        Assert.Equal(1.0, Fuzzy.Ramp(0.3, foot: 0.5, shoulder: 0.3), precision: 6);
        Assert.Equal(0.5, Fuzzy.Ramp(0.4, foot: 0.5, shoulder: 0.3), precision: 6);
        Assert.Equal(0.0, Fuzzy.Ramp(0.5, foot: 0.5, shoulder: 0.3), precision: 6);
        Assert.Equal(0.0, Fuzzy.Ramp(0.9, foot: 0.5, shoulder: 0.3), precision: 6);
    }

    [Fact]
    public void Ramp_Degenerate_FootEqualsShoulder_IsAStep()
    {
        Assert.Equal(0.0, Fuzzy.Ramp(0.49, foot: 0.5, shoulder: 0.5), precision: 6);
        Assert.Equal(1.0, Fuzzy.Ramp(0.5, foot: 0.5, shoulder: 0.5), precision: 6);
        Assert.Equal(1.0, Fuzzy.Ramp(0.51, foot: 0.5, shoulder: 0.5), precision: 6);
    }

    [Fact]
    public void Ramp_IsMonotonicIncreasing_WhenShoulderAboveFoot()
    {
        double prev = -1;
        for (var x = 0.0; x <= 1.0; x += 0.05)
        {
            var mu = Fuzzy.Ramp(x, foot: 0.2, shoulder: 0.8);
            Assert.True(mu >= prev, $"no monotónica en x={x}: {mu} < {prev}");
            prev = mu;
        }
    }

    [Fact]
    public void Triangle_PeaksAtOne_AndIsZeroAtFeet()
    {
        Assert.Equal(0.0, Fuzzy.Triangle(0.0, foot: 0.2, peak: 0.5, shoulder: 0.8), precision: 6);
        Assert.Equal(0.0, Fuzzy.Triangle(0.2, foot: 0.2, peak: 0.5, shoulder: 0.8), precision: 6);
        Assert.Equal(0.5, Fuzzy.Triangle(0.35, foot: 0.2, peak: 0.5, shoulder: 0.8), precision: 6);
        Assert.Equal(1.0, Fuzzy.Triangle(0.5, foot: 0.2, peak: 0.5, shoulder: 0.8), precision: 6);
        Assert.Equal(0.5, Fuzzy.Triangle(0.65, foot: 0.2, peak: 0.5, shoulder: 0.8), precision: 6);
        Assert.Equal(0.0, Fuzzy.Triangle(0.8, foot: 0.2, peak: 0.5, shoulder: 0.8), precision: 6);
    }

    [Fact]
    public void And_IsTheMinimum() =>
        Assert.Equal(0.3, Fuzzy.And(0.7, 0.3, 0.9), precision: 6);

    [Fact]
    public void Or_IsTheMaximum() =>
        Assert.Equal(0.9, Fuzzy.Or(0.7, 0.3, 0.9), precision: 6);

    [Fact]
    public void Not_IsTheComplement() =>
        Assert.Equal(0.25, Fuzzy.Not(0.75), precision: 6);

    [Fact]
    public void AndProduct_MultipliesDegrees()
    {
        // Conjunción suave: el débil arrastra al fuerte (0.5·0.5 = 0.25 < min = 0.5).
        Assert.Equal(0.25, Fuzzy.AndProduct(0.5, 0.5), precision: 6);
        Assert.Equal(0.0, Fuzzy.AndProduct(0.0, 1.0), precision: 6);
    }

    [Fact]
    public void Combinators_ClampTheirInputs()
    {
        Assert.Equal(1.0, Fuzzy.Or(2.0, -1.0), precision: 6);   // 2.0 → 1.0
        Assert.Equal(0.0, Fuzzy.And(2.0, -1.0), precision: 6);  // -1.0 → 0.0
        Assert.Equal(1.0, Fuzzy.Not(-0.5), precision: 6);       // -0.5 → 0.0 → 1
    }
}

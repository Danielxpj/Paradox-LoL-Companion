using ParadoxLoLCompanion.Core.Items;

namespace ParadoxLoLCompanion.Tests;

public class BuildPathPlannerTests
{
    private static readonly ParadoxLoLCompanion.Core.DataDragon.DataDragonCatalog Data = TestCatalog.Catalog();

    // Filo Infinito (3031, 3.400) = Espada B. F. (1038, 1.300) + Capa de agilidad (1018, 600) + receta.

    [Fact]
    public void FullGold_FinishesTargetNow()
    {
        var plan = BuildPathPlanner.Plan(Data, Data.ItemById(3031)!, new int[0], gold: 3500);
        Assert.True(plan.CanFinishNow);
        Assert.Equal(3400, plan.RemainingCost);
    }

    [Fact]
    public void OwnedComponent_ReducesRemainingCost()
    {
        var plan = BuildPathPlanner.Plan(Data, Data.ItemById(3031)!, new[] { 1038 }, gold: 0);
        Assert.Equal(3400 - 1300, plan.RemainingCost);
    }

    [Fact]
    public void PartialGold_SuggestsBiggestAffordableComponent()
    {
        // 1.400 de oro: alcanza la B. F. (1.300) y la Capa (600); debe sugerir la más cara.
        var plan = BuildPathPlanner.Plan(Data, Data.ItemById(3031)!, new int[0], gold: 1400);
        Assert.False(plan.CanFinishNow);
        Assert.Equal(1038, plan.NextComponent!.Id);
    }

    [Fact]
    public void OwnedComponent_IsNotSuggestedAgain()
    {
        // Ya tengo la B. F.: con 700 de oro la siguiente compra es la Capa.
        var plan = BuildPathPlanner.Plan(Data, Data.ItemById(3031)!, new[] { 1038 }, gold: 700);
        Assert.Equal(1018, plan.NextComponent!.Id);
    }

    [Fact]
    public void NotEnoughForAnyComponent_ReturnsNull()
    {
        var plan = BuildPathPlanner.Plan(Data, Data.ItemById(3031)!, new int[0], gold: 500);
        Assert.Null(plan.NextComponent);
        Assert.Equal(3400, plan.RemainingCost);
    }

    [Fact]
    public void PicksBiggestAffordablePiece_AcrossNesting()
    {
        // Recordatorio Mortal (3033) ← Llamado del ejecutor (3123, 800): con 900 alcanza
        // el Llamado completo; es la compra más grande posible.
        var plan = BuildPathPlanner.Plan(Data, Data.ItemById(3033)!, new int[0], gold: 900);
        Assert.Equal(3123, plan.NextComponent!.Id);
    }

    [Fact]
    public void RecursesIntoNestedComponents_WhenIntermediateUnaffordable()
    {
        // Con 500 no alcanza el Llamado (800), pero sí su Espada larga (350).
        var plan = BuildPathPlanner.Plan(Data, Data.ItemById(3033)!, new int[0], gold: 500);
        Assert.Equal(1036, plan.NextComponent!.Id);
    }

    [Fact]
    public void AllComponentsOwned_OnlyRecipeMissing()
    {
        var plan = BuildPathPlanner.Plan(Data, Data.ItemById(3031)!, new[] { 1038, 1018 }, gold: 1600);
        Assert.True(plan.CanFinishNow);
        Assert.Equal(3400 - 1300 - 600, plan.RemainingCost);
    }

    [Fact]
    public void DuplicateOwnedIds_OnlyCountOnce()
    {
        // Rabadon (3089) = Vara (1058) + Tomo (1052): una sola Vara no descuenta dos veces.
        var plan = BuildPathPlanner.Plan(Data, Data.ItemById(3089)!, new[] { 1058 }, gold: 0);
        Assert.Equal(3600 - 1250, plan.RemainingCost);
    }

    [Fact]
    public void NextComponent_ComparesRemainingCost_NotStickerPrice()
    {
        // 9300 = [9301 (1000, básico), 9302 (3000, ← 9303 (1600, ← B.F. 1300))].
        // Con B.F. Sword comprada y 1000 de oro: 9301 banquea 1000 reales; para 9303 solo
        // faltan 300 (sticker 1600). "Menos oro muerto" debe elegir 9301; el bug elegía
        // 9303 porque comparaba su precio de lista (1600) contra el faltante de 9301 (1000).
        var target = Data.ItemById(9300)!;

        var plan = BuildPathPlanner.Plan(Data, target, new[] { 1038 }, gold: 1000);

        Assert.Equal(9301, plan.NextComponent!.Id);
    }
}

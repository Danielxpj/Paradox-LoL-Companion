# op.gg-First Boots Priority Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Las botas de op.gg (meta) mandan siempre que haya datos; la sugerencia por amenaza (Mercs/Steelcaps) pasa a ser una nota dentro del texto del consejo.

**Architecture:** Cambio único en `ItemAdvisor.BootsFor`: la sugerencia por amenaza se calcula como candidata local en vez de retornar de inmediato; si op.gg trae botas, gana la primera del orden de op.gg disponible en el mapa, con la amenaza como nota en `Reason` cuando difiere. Sin datos de op.gg el comportamiento actual (amenaza → arquetipo) queda intacto.

**Tech Stack:** C# / .NET (WPF app), xUnit.

**Spec:** `docs/superpowers/specs/2026-07-08-opgg-boots-priority-design.md`

---

### Task 1: Tests que fijan el nuevo comportamiento

**Files:**
- Modify: `tests/ParadoxLoLCompanion.Tests/ItemAdvisorStatsTests.cs` (agregar al final de la clase)

Los tests usan `TestCatalog` (mismo fixture que los tests de botas existentes en `ItemAdvisorTests`), que tiene las 5 botas: 3111 Mercury's Treads, 3047 Plated Steelcaps, 3006 Berserker's Greaves, 3020 Sorcerer's Shoes, 3158 Ionian Boots of Lucidity. La composición enemiga Malzahar+Leona+Amumu dispara la sugerencia de Mercs (misma que `Boots_MercsAgainstCcAndMagic`).

- [ ] **Step 1: Escribir los tests que fallan**

Agregar dentro de la clase `ItemAdvisorStatsTests` (antes de la llave de cierre final):

```csharp
    // --- Botas: la meta (op.gg) manda; la amenaza queda como nota ---

    private static readonly int[] NoItems = Array.Empty<int>();

    private static ChampionBuildStats BootsStats(params int[] bootIds) => new()
    {
        ChampionKey = "Jinx",
        GameMode = "ranked",
        Position = "adc",
        Boots = new ItemSetStats(bootIds, PickRate: 0.62, Play: 9000, Win: 4700),
    };

    [Fact]
    public void Boots_OpggMetaWins_ThreatBecomesNote()
    {
        // Misma amenaza que Boots_MercsAgainstCcAndMagic (CC pesado → Mercs),
        // pero op.gg dice Sorcerer's: la meta manda y la amenaza queda como nota.
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, NoItems),
            ("Malzahar", "CHAOS", 0, NoItems),
            ("Leona", "CHAOS", 0, NoItems),
            ("Amumu", "CHAOS", 0, NoItems));
        var advisor = new ItemAdvisor(TestCatalog.Catalog());

        var plan = advisor.Advise(state, stats: BootsStats(3020))!;

        Assert.NotNull(plan.Boots);
        Assert.Equal(3020, plan.Boots!.Boots.Id);
        Assert.Contains("pick rate", plan.Boots.Reason);
        Assert.Contains("consider Mercury's Treads", plan.Boots.Reason);
    }

    [Fact]
    public void Boots_OpggMeta_NoNote_WhenThreatAgrees_OrNoSkew()
    {
        // Sin sesgo de amenaza: op.gg decide y la razón no lleva nota.
        // Además op.gg trae dos botas y gana la primera de SU orden (3158),
        // no la primera del orden del catálogo.
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, NoItems),
            ("Zed", "CHAOS", 2, NoItems),
            ("Ahri", "CHAOS", 2, NoItems));
        var advisor = new ItemAdvisor(TestCatalog.Catalog());

        var plan = advisor.Advise(state, stats: BootsStats(3158, 3006))!;

        Assert.NotNull(plan.Boots);
        Assert.Equal(3158, plan.Boots!.Boots.Id);
        Assert.Contains("pick rate", plan.Boots.Reason);
        Assert.DoesNotContain("consider", plan.Boots.Reason);
    }

    [Fact]
    public void Boots_ThreatFallback_WhenOpggBootsNotInCatalog()
    {
        // op.gg trae un id que no existe en el mapa: fallback a la amenaza (Mercs).
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, NoItems),
            ("Malzahar", "CHAOS", 0, NoItems),
            ("Leona", "CHAOS", 0, NoItems),
            ("Amumu", "CHAOS", 0, NoItems));
        var advisor = new ItemAdvisor(TestCatalog.Catalog());

        var plan = advisor.Advise(state, stats: BootsStats(99999))!;

        Assert.NotNull(plan.Boots);
        Assert.Equal(3111, plan.Boots!.Boots.Id);
        Assert.Contains("CC", plan.Boots.Reason);
    }
```

- [ ] **Step 2: Correr los tests nuevos y verificar que fallan**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests --filter "FullyQualifiedName~ItemAdvisorStatsTests.Boots_"`
Expected: FAIL — `Boots_OpggMetaWins_ThreatBecomesNote` recomienda 3111 (Mercs) en vez de 3020, porque hoy la amenaza retorna antes de mirar op.gg. `Boots_ThreatFallback_WhenOpggBootsNotInCatalog` puede pasar ya (cubre comportamiento existente); está bien.

### Task 2: Reordenar `BootsFor` — op.gg primero, amenaza como nota

**Files:**
- Modify: `src/ParadoxLoLCompanion.Core/Items/ItemAdvisor.cs:841-866` (cuerpo de `BootsFor`)

- [ ] **Step 1: Reemplazar los bloques de amenaza y op.gg**

Reemplazar este código actual dentro de `BootsFor`:

```csharp
        if (threat.HeavyCcCount >= _config.CcCountForMercs
            || threat.MagicalShare >= _config.SkewedDamageShare)
        {
            var mercs = ByTag(candidates, "SpellBlock");
            if (mercs is not null)
                return Advice(mercs, threat.HeavyCcCount >= _config.CcCountForMercs
                    ? $"the enemy has {threat.HeavyCcCount} heavy-CC champions"
                    : $"{Pct(threat.MagicalShare)} of enemy damage is magic");
        }

        if (threat.PhysicalShare >= _config.SkewedDamageShare && threat.AutoAttackShare >= 0.35)
        {
            var steelcaps = ByTag(candidates, "Armor");
            if (steelcaps is not null)
                return Advice(steelcaps,
                    $"heavy physical auto-attack damage ({threat.TopPhysicalName})");
        }

        // Sin amenaza que decida: las botas que más compran los jugadores de tu campeón.
        if (stats?.Boots is { } statBoots)
        {
            var popular = candidates.FirstOrDefault(c => statBoots.ItemIds.Contains(c.Id));
            if (popular is not null)
                return Advice(popular,
                    $"most common boots on your champion ({Pct(statBoots.PickRate)} pick rate)");
        }
```

por este:

```csharp
        // La amenaza ya no decide sola: queda como candidata, y si la meta
        // (op.gg) recomienda otra bota, acompaña como nota en la razón.
        (StaticItem Boots, string Reason)? threatPick = null;
        if (threat.HeavyCcCount >= _config.CcCountForMercs
            || threat.MagicalShare >= _config.SkewedDamageShare)
        {
            var mercs = ByTag(candidates, "SpellBlock");
            if (mercs is not null)
                threatPick = (mercs, threat.HeavyCcCount >= _config.CcCountForMercs
                    ? $"the enemy has {threat.HeavyCcCount} heavy-CC champions"
                    : $"{Pct(threat.MagicalShare)} of enemy damage is magic");
        }

        if (threatPick is null
            && threat.PhysicalShare >= _config.SkewedDamageShare && threat.AutoAttackShare >= 0.35)
        {
            var steelcaps = ByTag(candidates, "Armor");
            if (steelcaps is not null)
                threatPick = (steelcaps,
                    $"heavy physical auto-attack damage ({threat.TopPhysicalName})");
        }

        // La meta manda: las botas que más compran los jugadores de tu campeón,
        // en el orden de op.gg (popularidad), no en el orden del catálogo.
        if (stats?.Boots is { } statBoots)
        {
            var popular = statBoots.ItemIds
                .Select(id => candidates.FirstOrDefault(c => c.Id == id))
                .FirstOrDefault(c => c is not null);
            if (popular is not null)
            {
                var reason =
                    $"most common boots on your champion ({Pct(statBoots.PickRate)} pick rate)";
                if (threatPick is { } alt && alt.Boots.Id != popular.Id)
                    reason += $" — but {alt.Reason}: consider {alt.Boots.Name}";
                return Advice(popular, reason);
            }
        }

        // Sin datos de op.gg: la amenaza decide como hasta ahora.
        if (threatPick is { } picked)
            return Advice(picked.Boots, picked.Reason);
```

El fallback por arquetipo que sigue (`var byArchetype = profile.Archetype switch ...`) queda sin cambios.

- [ ] **Step 2: Correr los tests nuevos y verificar que pasan**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests --filter "FullyQualifiedName~ItemAdvisorStatsTests.Boots_"`
Expected: PASS (3 tests)

- [ ] **Step 3: Correr la suite completa**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests`
Expected: PASS — en particular `Boots_MercsAgainstCcAndMagic`, `Boots_SteelcapsAgainstPhysicalAutoAttackers` y `Boots_ArchetypeDefault_WhenNoSkew` (no pasan stats: cubren el fallback sin op.gg) siguen verdes.

- [ ] **Step 4: Commit**

```bash
git add src/ParadoxLoLCompanion.Core/Items/ItemAdvisor.cs tests/ParadoxLoLCompanion.Tests/ItemAdvisorStatsTests.cs
git commit -m "feat: op.gg meta boots take priority; threat suggestion becomes a note"
```

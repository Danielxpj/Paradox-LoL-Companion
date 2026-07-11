# Mejoras al Algoritmo de Recomendación (v4) — Plan Maestro

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Elevar la calidad de las recomendaciones de items en ARAM: corregir 6 bugs verificados que hoy degradan el top-3 en silencio, hacer que el modelo de amenaza lea la partida REAL (builds enemigas, eventos, stats vivos) en vez de solo el draft, recalibrar el scoring difuso contra una vara de medición offline, y conectar datos ya parseados que nunca llegan al scoring.

**Architecture:** Mismos límites que la v3: todo en `ParadoxLoLCompanion.Core` (sin UI), `ItemAdvicePlan` inmutable por tick, listas curadas en config como override de cualquier señal derivada. Cinco frentes ordenados por dependencia: **Fase 0** (bugs, ejecutable tal cual desde este documento) → **Fase 1** (harness de calibración: sin vara de medición, toda recalibración es a ciegas) → **Fase 2** (ThreatAnalyzer/TeamThreat: entradas veraces) → **Fase 3** (ItemAdvisor.ScoreItem: matemática coherente sobre entradas ya veraces) → **Fase 4** (datos nuevos: op.gg por slot, GW temprano, kit flags, tabla ARAM).

**Tech Stack:** C# / .NET 10 (WPF app), xUnit, Data Dragon, Live Client API, op.gg vía MCP.

**Estado:** ✅ **IMPLEMENTADO (2026-07-11), release v2.0.** Fases 0–4 implementadas por TDD sobre la base verde de 267 tests → **309 tests verdes**, la app WPF compila limpia. Tres pasadas de revisión adversarial (Fase 0, Fase 2, Fases 3–4) encontraron y se corrigieron: NPE por summonerSpells null, over-count de sustain/anti-heal, falsos positivos de keywords de kit (D5), unidad de HealthRegen, fuga de histéresis entre partidas (S6), NPE de config null (D6), zona muerta de botas (D4) y varios cruces difusos.

**Diferido con justificación (no bloqueante para v2.0):**
- **S5 — Priority desde puntaje sin boost:** las recomendaciones se muestran en orden de puntaje CON boost, así que las prioridades deben seguir ese orden (una prioridad basada en el puntaje sin boost sería no-monótona). Solo se implementó la parte de Category desde componentes limpios.
- **S7 parte 2 — fit por valor (statFit) en vez de conteo de tags:** "el mayor reordenamiento del plan"; necesita la vara de medición H2 con corpus real antes de recalibrar a ciegas. Se implementó la parte 1 (valuación de maná/MS/lifesteal en la eficiencia).
- **H1 — corpus de partidas reales** y las métricas agreement@3 / churn de H2: requieren grabar partidas ARAM reales (no fabricables autónomamente). Se implementó la métrica counter-responsiveness (barridos sintéticos, sin corpus).
- **D6 — lado "taken" (durabilidad propia):** solo se implementó el lado "dealt" (amenaza enemiga).
- **Fase 5 (G1–G3):** ángulos de descubrimiento, fuera del alcance de implementación (requieren sus propias sesiones de diseño).

---

## Cómo se produjo este plan (y cuánto confiar en él)

Sesión de descubrimiento multi-agente (2026-07-10): 5 lectores mapearon los subsistemas (scoring, amenaza/perfiles, flujo Live Client, op.gg, config/tests/Mayhem); 6 lentes independientes propusieron mejoras (matemática del scoring, estado de partida, fuentes de datos, expertise ARAM, corrección, ideas diferidas en specs previas); un merge dedujo 29 propuestas distintas; un crítico de completitud detectó 3 ángulos no cubiertos.

**Verificación:** la flota adversarial de verificación murió por límite de sesión, así que las 29 propuestas se verificaron manualmente contra el código en la misma sesión: **27 confirmadas** con evidencia `archivo:línea` releída, **2 confirmadas con matices** (anotados en su brief). Los 3 ángulos del crítico (Fase 5) quedaron **sin explorar en profundidad** — son direcciones de descubrimiento, no diseños.

Matices encontrados al verificar:
- «Bramble/Executioner's excluidos dos veces» — Bramble Vest sí tiene componente (`from`); lo que lo excluye del pool es el piso de oro (`MinCompletedItemGold = 1100`). La conclusión (el tier de 800 g de GW es irrecomendable) se sostiene igual.
- «Listas curadas vacías» — `PercentHpTrueDamageChampions` y `HardEngageChampions` NO están en el JSON compartido pero sí tienen defaults en código (`AdvisorConfig.cs:168-180`). El reclamo real es que las listas están incompletas/desactualizadas (faltan Taric, Milio, Renata, Ivern en healer/shield), no vacías.

---

## Resumen priorizado

| # | Mejora | Impacto | Esfuerzo | Fase |
|---|--------|:---:|:---:|:---:|
| F1 | Grupo excluyente envenenado por item GW salteado | 4 | 1 | 0 |
| F2 | Fallback por summonerName case-sensitive anula todo el asesor | 3 | 1 | 0 |
| F3 | El feed llama a Advise SIN stats de op.gg (botas meta no rigen en el feed) | 4 | 2 | 0 |
| F4 | BuildPathPlanner compara sticker vs. faltante real (unidades mezcladas) | 3 | 2 | 0 |
| F5 | Shop alert cita precio de lista, no lo que falta pagar | 2 | 1 | 0 |
| F6 | Un fetch fallido de op.gg silencia el prior TODA la partida + caché sin TTL | 4 | 2 | 0 |
| H1 | Corpus de partidas reales (dump de allgamedata) | 4 | 2 | 1 |
| H2 | Harness offline: agreement@3, churn, monotonía de counters | 4 | 3 | 1 |
| T1 | Sustain graduado por oro invertido (starters/botas fuera) + gate GW aliado suave | 5 | 2 | 2 |
| T2 | Mix de daño enemigo inferido de sus compras (blend con el kit) | 5 | 3 | 2 |
| T3 | Burst también para magos (hoy solo tag Assassin) | 4 | 2 | 2 |
| T4 | Eventos ChampionKill → pesar más a quien TE mata | 4 | 3 | 2 |
| T5 | Peso de fed-ness relativo al promedio (no se comprime en late) | 3 | 2 | 2 |
| T6 | Summoner spells: Ignite→EnemyAntiHeal, Heal→Sustain | 3 | 2 | 2 |
| T7 | Grado CcThreat + flag de tenacidad en items | 4 | 3 | 2 |
| T8 | Stats vivos propios (Armor/MR/HP) → necesidad defensiva real | 4 | 3 | 2 |
| T9 | Modulación ahead/behind con el peso propio vs. promedio enemigo | 4 | 2 | 2 |
| S1 | Re-anclar TODAS las rampas a μ(umbral)=0.5 (hoy 0 / 0.33 / 0.375 / 0.48) | 4 | 2 | 3 |
| S2 | Doble conteo defensivo: fusionar burst+engage, gate del CritThreat, tope | 4 | 3 | 3 |
| S3 | Boost de afford multiplicativo (×1.25) → empujón acotado y continuo en oro | 4 | 2 | 3 |
| S4 | Fuzzificar Cleanse (3.0 plano) y ShieldBreak con peso de fed-ness | 3 | 2 | 3 |
| S5 | Category/Priority desde componentes limpios (sin boost ni penalidades) | 2 | 1 | 3 |
| S6 | Histéresis entre ticks (diferida explícitamente en v3) | 4 | 2 | 3 |
| S7 | Fit consciente de magnitud (stats faltantes de ddragon + valor-oro) | 5 | 4 | 3 |
| D1 | Camino dedicado para el tier GW de 800 g (Bramble/Executioner's/Orb) | 5 | 3 | 4 |
| D2 | Prior op.gg consciente del slot (4.º/5.º/6.º item) | 3 | 2 | 4 |
| D3 | Primera compra ARAM: set de op.gg + presupuesto de 1400 g | 3 | 2 | 4 |
| D4 | Gate de confianza (Play) para las botas meta | 2 | 1 | 4 |
| D5 | championFull.json → flags de kit (unión con listas curadas) | 4 | 4 | 4 |
| D6 | Tabla curada de balance ARAM (daño hecho/recibido por campeón) | 3 | 3 | 4 |
| G1 | Contexto del equipo ALIADO en el scoring | ? | ? | 5 |
| G2 | Timing y entrega: ventana de muerte, oro proyectado, item set vivo en LCU | ? | ? | 5 |
| G3 | Telemetría de adopción y personalización entre partidas | ? | ? | 5 |

---

## Fase 0 — Correcciones de bugs verificados (ejecutable desde este documento)

Seis defectos confirmados leyendo el código; ninguno requiere calibración. Cada tarea es independiente y se puede ejecutar en cualquier orden. Correr `dotnet test` desde la raíz del repo.

### Task F1: Un item GW salteado no debe envenenar su grupo excluyente

El loop de selección marca el grupo "límite de 1" ANTES de la puerta de Heridas Graves: si Mortal Reminder (GW + grupo Last Whisper) se saltea porque el cupo GW ya está tomado, deja marcado el grupo LW y bloquea a Lord Dominik's/Serylda's — la respuesta correcta a la armadura apilada desaparece del top-3.

**Files:**
- Modify: `src/ParadoxLoLCompanion.Core/Items/ItemAdvisor.cs:242-266`
- Test: `tests/ParadoxLoLCompanion.Tests/ItemAdvisorTests.cs` (agregar al final de la clase)

- [ ] **Step 1: Escribir el test que falla**

```csharp
    [Fact]
    public void ExclusiveGroup_NotPoisoned_ByGwSkippedItem()
    {
        // Tanque vs comp física con sustain alto y armadura apilada: Thornmail (GW)
        // es top y toma el cupo de Heridas Graves; Mortal Reminder (GW + grupo Last
        // Whisper) se saltea por gwTaken — y NO debe bloquear a Lord Dominik's.
        var vests = new[] { 1029, 1029 };
        var state = TestCatalog.State(20000,
            ("Leona", "ORDER", 0, Array.Empty<int>()),
            ("Warwick", "CHAOS", 0, vests),
            ("Aatrox", "CHAOS", 0, vests),
            ("Zed", "CHAOS", 0, vests));
        var advisor = new ItemAdvisor(TestCatalog.Catalog(),
            new ItemsConfig { MaxRecommendations = 10 });

        var plan = advisor.Advise(state)!;

        var names = plan.Recommendations.Select(r => r.Item.Name).ToList();
        Assert.Contains("Thornmail", names);                 // el GW que sí entró
        Assert.DoesNotContain("Mortal Reminder", names);     // cupo GW ya tomado
        Assert.Contains("Lord Dominik's Regards", names);    // NO bloqueado por el grupo
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test --filter ExclusiveGroup_NotPoisoned_ByGwSkippedItem`
Expected: FAIL — `Lord Dominik's Regards` ausente (el grupo quedó marcado por el Mortal Reminder salteado).

- [ ] **Step 3: Hacer los chequeos puros y comitear el estado solo si el item pasa TODAS las puertas**

Reemplazar el cuerpo del `foreach` de `ranked` (ItemAdvisor.cs:242-266, hasta `takenPassives.UnionWith(...)` inclusive) por:

```csharp
        foreach (var (item, score, reasons, category, plan) in ranked)
        {
            if (recommendations.Count >= _config.MaxRecommendations)
                break;
            // Chequeos puros primero; el estado (nombre/grupo/pasivas) se marca recién
            // cuando el item pasa TODAS las puertas — un item salteado no debe
            // envenenar a los que vienen detrás.
            if (recommendedNames.Contains(item.Name))
                continue;
            if (item.PassiveNames.Count > 0 && item.PassiveNames.Overlaps(takenPassives))
                continue;
            var rankedGroup = ExclusiveGroupOf(item);
            if (rankedGroup >= 0 && takenExclusiveGroups.Contains(rankedGroup))
                continue;
            if (item.AppliesGrievousWounds)
            {
                // Un solo item de Heridas Graves: el efecto no se acumula (aunque las
                // pasivas tengan nombres distintos: Hackshorn, Thorns…).
                if (gwTaken)
                    continue;
                gwTaken = true;
            }
            recommendedNames.Add(item.Name);
            if (rankedGroup >= 0)
                takenExclusiveGroups.Add(rankedGroup);
            takenPassives.UnionWith(item.PassiveNames);
```

(El resto del cuerpo del loop — `reasons`, `missing`, `topScore`, `finalCategory`, `recommendations.Add` — no cambia.)

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test --filter "FullyQualifiedName~ItemAdvisor"`
Expected: PASS (el nuevo test y todos los existentes de dedup por nombre/pasiva/grupo).

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.Core/Items/ItemAdvisor.cs tests/ParadoxLoLCompanion.Tests/ItemAdvisorTests.cs
git commit -m "fix: exclusive group no longer poisoned by GW-skipped item in ranked loop"
```

### Task F2: Fallback por summonerName case-insensitive

`ActivePlayerEntry` matchea riotId con `OrdinalIgnoreCase` pero el fallback por summonerName usa `Ordinal` (GameState.cs:38). Con riotId vacío (payloads viejos/parciales) y casing distinto, el jugador activo no se resuelve → cero recomendaciones toda la partida, sin error visible.

**Files:**
- Modify: `src/ParadoxLoLCompanion.Core/Models/GameState.cs:38`
- Test: Create `tests/ParadoxLoLCompanion.Tests/GameStateTests.cs`

- [ ] **Step 1: Escribir el test que falla**

```csharp
using ParadoxLoLCompanion.Core.Models;
using Xunit;

namespace ParadoxLoLCompanion.Tests;

public class GameStateTests
{
    [Fact]
    public void ActivePlayerEntry_MatchesSummonerName_CaseInsensitive()
    {
        // riotId vacío (payload viejo) + casing distinto: el fallback por
        // summonerName debe ser case-insensitive, como el camino por riotId.
        var state = new GameState
        {
            ActivePlayer = new ActivePlayer { SummonerName = "XxJinxxX" },
            AllPlayers = { new Player { SummonerName = "xxjinxxx", ChampionName = "Jinx" } },
        };

        Assert.NotNull(state.ActivePlayerEntry);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test --filter ActivePlayerEntry_MatchesSummonerName_CaseInsensitive`
Expected: FAIL — `ActivePlayerEntry` es null.

- [ ] **Step 3: Cambiar la comparación**

En `GameState.cs:38`, `StringComparison.Ordinal` → `StringComparison.OrdinalIgnoreCase`.

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test --filter "FullyQualifiedName~GameState"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.Core/Models/GameState.cs tests/ParadoxLoLCompanion.Tests/GameStateTests.cs
git commit -m "fix: case-insensitive summonerName fallback in ActivePlayerEntry"
```

### Task F3: Pasar las stats de op.gg al feed (ItemRecommendationRule)

El feed llama `_advisor.Advise(state, _forcedArchetype?.Invoke())` sin stats (ItemRecommendationRule.cs:38), mientras el panel pasa `_championStats` (MainViewModel.cs:687). El feed pierde hasta 3.125 puntos de prior por item (puede cambiar el top) y — peor — la rama "botas meta de op.gg mandan" solo corre con `stats != null` (ItemAdvisor.cs:865-878): la regla de proyecto «op.gg meta boots always win» NO rige en el feed, y feed y panel pueden nombrar items y botas distintos en el mismo tick.

**Files:**
- Modify: `src/ParadoxLoLCompanion.Core/Advice/Rules/ItemRecommendationRule.cs:17-38`
- Modify: `src/ParadoxLoLCompanion.Core/Advice/AdviceEngine.cs:28-34`
- Modify: `src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs:369`
- Test: `tests/ParadoxLoLCompanion.Tests/ItemRecommendationTests.cs` (agregar al final de la clase)

- [ ] **Step 1: Escribir el test que falla**

```csharp
    [Fact]
    public void Feed_UsesOpggStats_MetaBootsRuleInFeed()
    {
        // Amenaza que pide Mercs (Malzahar+Leona+Amumu) pero op.gg dice Sorcerer's:
        // el feed debe decir lo mismo que el panel — la meta manda.
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, Array.Empty<int>()),
            ("Malzahar", "CHAOS", 0, Array.Empty<int>()),
            ("Leona", "CHAOS", 0, Array.Empty<int>()),
            ("Amumu", "CHAOS", 0, Array.Empty<int>()));
        var stats = new ChampionBuildStats
        {
            ChampionKey = "Jinx",
            GameMode = "ranked",
            Position = "adc",
            Boots = new ItemSetStats(new[] { 3020 }, PickRate: 0.62, Play: 9000, Win: 4700),
        };
        var rule = new ItemRecommendationRule(TestCatalog.Catalog(),
            statsProvider: () => stats);

        var advice = rule.Evaluate(state).ToList();

        var boots = advice.Single(a => a.Key == "item-boots");
        Assert.Contains("Sorcerer's Shoes", boots.Message);
        Assert.Contains("pick rate", boots.Message);
    }
```

(Requiere `using ParadoxLoLCompanion.Core.Stats;` en el archivo de tests si no está.)

- [ ] **Step 2: Correr el test y verificar que no compila / falla**

Run: `dotnet test --filter Feed_UsesOpggStats_MetaBootsRuleInFeed`
Expected: error de compilación (no existe `statsProvider`) — es el "falla" de esta tarea.

- [ ] **Step 3: Plumbing del provider (mismo patrón que `forcedArchetype`)**

`ItemRecommendationRule.cs` (agregar `using ParadoxLoLCompanion.Core.Stats;`):

```csharp
    private readonly ItemAdvisor _advisor;
    private readonly Func<BuildArchetype?>? _forcedArchetype;
    private readonly Func<ChampionBuildStats?>? _statsProvider;

    public ItemRecommendationRule(IStaticData data, ItemsConfig? config = null,
        Func<BuildArchetype?>? forcedArchetype = null,
        Func<ChampionBuildStats?>? statsProvider = null)
        : this(new ItemAdvisor(data, config), forcedArchetype, statsProvider)
    {
    }

    /// <param name="forcedArchetype">
    /// Provider del arquetipo forzado en la UI (se lee en cada tick): el feed debe
    /// decir lo mismo que el panel del asesor cuando el jugador elige build a mano.
    /// </param>
    /// <param name="statsProvider">
    /// Provider de las stats de op.gg cacheadas (se lee en cada tick): sin esto el
    /// feed pierde el prior estadístico y la regla "las botas meta mandan".
    /// </param>
    public ItemRecommendationRule(ItemAdvisor advisor,
        Func<BuildArchetype?>? forcedArchetype = null,
        Func<ChampionBuildStats?>? statsProvider = null)
    {
        _advisor = advisor;
        _forcedArchetype = forcedArchetype;
        _statsProvider = statsProvider;
    }
```

y en `Evaluate`:

```csharp
        var plan = _advisor.Advise(state, _forcedArchetype?.Invoke(), _statsProvider?.Invoke());
```

`AdviceEngine.cs` (agregar `using ParadoxLoLCompanion.Core.Stats;`):

```csharp
    public static AdviceEngine CreateWith(IStaticData staticData, AdvisorConfig? config = null,
        Func<BuildArchetype?>? forcedArchetype = null,
        Func<ChampionBuildStats?>? statsProvider = null)
    {
        config ??= AdvisorConfig.Default;
        return new(DefaultRules(config)
            .Append(new ItemRecommendationRule(staticData, config.Items, forcedArchetype, statsProvider)));
    }
```

`MainViewModel.cs:369`:

```csharp
        _engine = AdviceEngine.CreateWith(catalog, _config, () => _forcedArchetype, () => _championStats);
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test`
Expected: PASS completo. Si algún test del feed fija strings de botas por amenaza, revisar que siga pasando `stats == null` (el fallback es idéntico al comportamiento de hoy).

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.Core/Advice src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs tests/ParadoxLoLCompanion.Tests/ItemRecommendationTests.cs
git commit -m "fix: feed advises with op.gg stats - meta boots rule now holds in the feed"
```

### Task F4: BuildPathPlanner — comparar faltante real, no precio de lista

`BestAffordableComponent` compara `candidate.GoldTotal` para candidatos profundos y el costo restante para los directos (BuildPathPlanner.cs:77): unidades mezcladas. Con subárboles parcialmente comprados, el "Buy now" elige gastar poco en vez de banquear más oro en la build — lo contrario de la heurística declarada («la compra que menos oro deja muerto»).

**Files:**
- Modify: `src/ParadoxLoLCompanion.Core/Items/BuildPathPlanner.cs:54-87`
- Modify: `tests/ParadoxLoLCompanion.Tests/TestCatalog.cs` (items de test nuevos)
- Test: `tests/ParadoxLoLCompanion.Tests/BuildPathPlannerTests.cs` (agregar al final de la clase)

- [ ] **Step 1: Agregar al `Items` JSON de TestCatalog un árbol que exponga el bug**

Dentro del bloque `Items` (antes de las botas), agregar:

```json
        "9301": { "name":"Test Banked Component","gold":{"total":1000,"purchasable":true},"tags":["Health"],"maps":{"11":true,"12":true},"into":["9300"],"stats":{"FlatHPPoolMod":300} },
        "9303": { "name":"Test Deep Sub","gold":{"total":1600,"purchasable":true},"tags":["Damage"],"maps":{"11":true,"12":true},"into":["9302"],"from":["1038"],"depth":2,"stats":{"FlatPhysicalDamageMod":45} },
        "9302": { "name":"Test Expensive Branch","gold":{"total":3000,"purchasable":true},"tags":["Damage"],"maps":{"11":true,"12":true},"into":["9300"],"from":["9303"],"depth":3,"stats":{"FlatPhysicalDamageMod":60} },
        "9300": { "name":"Test Twin Target","gold":{"total":4500,"purchasable":true},"tags":["Damage","Health"],"maps":{"11":true,"12":true},"from":["9301","9302"],"depth":4,"stats":{"FlatPhysicalDamageMod":70,"FlatHPPoolMod":400} },
```

- [ ] **Step 2: Escribir el test que falla**

```csharp
    [Fact]
    public void NextComponent_ComparesRemainingCost_NotStickerPrice()
    {
        // 9300 = [9301 (1000, básico), 9302 (3000, from 9303 (1600, from B.F. 1300))].
        // Con B.F. Sword comprado y 1000 de oro: 9301 banquea 1000 reales; 9303
        // cuesta 300 reales (sticker 1600). "Menos oro muerto" debe elegir 9301;
        // el bug elige 9303 porque compara su sticker contra el faltante de 9301.
        var data = TestCatalog.Catalog();
        var target = data.ItemById(9300)!;

        var plan = BuildPathPlanner.Plan(data, target, new[] { 1038 }, gold: 1000);

        Assert.Equal(9301, plan.NextComponent!.Id);
    }
```

Run: `dotnet test --filter NextComponent_ComparesRemainingCost_NotStickerPrice`
Expected: FAIL — NextComponent es 9303.

- [ ] **Step 3: Propagar el costo real por la recursión**

Reemplazar `BestAffordableComponent` (BuildPathPlanner.cs:54-87) por:

```csharp
    private static StaticItem? BestAffordableComponent(
        IStaticData data, StaticItem item, Dictionary<int, int> owned, double gold) =>
        BestAffordable(data, item, owned, gold)?.Item;

    /// <summary>
    /// El componente faltante que más oro real banquea en la build y que el oro
    /// actual permite completar. El costo comparado es SIEMPRE el faltante
    /// (descontando componentes poseídos), nunca el precio de lista — las ramas
    /// profundas y las directas compiten en la misma unidad.
    /// </summary>
    private static (StaticItem Item, int Cost)? BestAffordable(
        IStaticData data, StaticItem item, Dictionary<int, int> owned, double gold)
    {
        (StaticItem Item, int Cost)? best = null;

        foreach (var component in Components(data, item))
        {
            // Copia para sondear sin consumir: cada rama evalúa su propio faltante.
            var probe = new Dictionary<int, int>(owned);
            var cost = RemainingCost(data, component, probe);
            if (cost == 0)
            {
                Consume(owned, component.Id); // ya está en el inventario: descartarlo de otras ramas
                continue;
            }

            var candidate = cost <= gold
                ? (component, cost)
                : BestAffordable(data, component, new Dictionary<int, int>(owned), gold);

            if (candidate is { } c && (best is null || c.Cost > best.Value.Cost))
                best = c;
        }

        return best;
    }
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test --filter "FullyQualifiedName~BuildPathPlanner"`
Expected: PASS (el nuevo y los existentes: los casos sin subárboles poseídos no cambian de resultado).

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.Core/Items/BuildPathPlanner.cs tests/ParadoxLoLCompanion.Tests
git commit -m "fix: BuildPathPlanner compares true remaining cost across recursion depths"
```

### Task F5: El shop alert cita el oro que falta pagar, no el precio de lista

`ShopAlertFor` imprime `top.Item.GoldTotal` (ItemAdvisor.cs:392) justo en la ventana de muerte donde el usuario actúa; con componentes comprados el número real es `RemainingCost`, a menudo mucho menor. Ídem la línea "Buy now" del feed (ItemRecommendationRule.cs:52) con el componente.

**Files:**
- Modify: `src/ParadoxLoLCompanion.Core/Items/BuildPathPlanner.cs:9-12` (campo nuevo en el record)
- Modify: `src/ParadoxLoLCompanion.Core/Items/ItemAdvisor.cs:386-396`
- Modify: `src/ParadoxLoLCompanion.Core/Advice/Rules/ItemRecommendationRule.cs:50-52`
- Test: `tests/ParadoxLoLCompanion.Tests/ItemAdvisorTests.cs`

- [ ] **Step 1: Escribir el test que falla**

```csharp
    [Fact]
    public void ShopAlert_QuotesRemainingCost_WhenComponentsOwned()
    {
        // Jinx muerta con componentes ya comprados: el alert debe citar lo que
        // FALTA pagar del top, no su precio de lista.
        var state = TestCatalog.AramState(3000,
            ("Jinx", "ORDER", 0, new[] { 1018, 1036, 1053 }),
            ("Malzahar", "CHAOS", 0, Array.Empty<int>()));
        state.AllPlayers[0].IsDead = true;
        var advisor = new ItemAdvisor(TestCatalog.Catalog());

        var plan = advisor.Advise(state)!;
        var top = plan.Recommendations[0];

        Assert.NotNull(plan.ShopAlert);
        Assert.True(top.Purchase.RemainingCost < top.Item.GoldTotal,
            "el escenario debe tener descuento por componentes; si no, ajustar los items poseídos");
        Assert.Contains(top.Purchase.RemainingCost.ToString("N0",
            System.Globalization.CultureInfo.InvariantCulture), plan.ShopAlert);
    }
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter ShopAlert_QuotesRemainingCost_WhenComponentsOwned`
Expected: FAIL — el alert contiene el GoldTotal del top.

- [ ] **Step 3: Llevar el costo real al plan y a los textos**

`PurchasePlan` (BuildPathPlanner.cs:9): agregar el costo del próximo componente —

```csharp
public sealed record PurchasePlan(StaticItem Target, int RemainingCost,
    StaticItem? NextComponent, int NextComponentCost = 0)
{
    public bool CanFinishNow => NextComponent is not null && NextComponent.Id == Target.Id;
}
```

En `Plan(...)`, llenar el campo con el costo ya computado (tras F4, `BestAffordable` devuelve `(Item, Cost)`):

```csharp
        var remaining = RemainingCost(data, target, new Dictionary<int, int>(owned));
        if (remaining <= gold)
            return new PurchasePlan(target, remaining, target, remaining);

        var next = BestAffordable(data, target, owned, gold);
        return new PurchasePlan(target, remaining, next?.Item, next?.Cost ?? 0);
```

`ItemAdvisor.ShopAlertFor` (:388-395):

```csharp
        if (top.Purchase.CanFinishNow)
            return $"Shop open while dead — finish {top.Item.Name} now ({GoldFmt(top.Purchase.RemainingCost)}).";
        if (top.Purchase.NextComponent is { } component)
            return $"Shop open while dead — buy {component.Name} ({GoldFmt(top.Purchase.NextComponentCost)}) toward {top.Item.Name}.";
```

`ItemRecommendationRule.cs:51-52` (la línea "Buy now"):

```csharp
            if (!top.Affordable && top.Purchase.NextComponent is { } component)
                line += $" Buy now: {component.Name} ({top.Purchase.NextComponentCost.ToString("N0", CultureInfo.InvariantCulture)}).";
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test`
Expected: PASS. Tests que fijen los strings viejos ("Buy now: X (precio-de-lista)") se actualizan al faltante real — es el comportamiento correcto nuevo.

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.Core tests/ParadoxLoLCompanion.Tests
git commit -m "fix: shop alert and Buy-now quote payable cost, not sticker price"
```

### Task F6: Reintentar fetches fallidos de op.gg + TTL de la caché

`_statsFetchKey` se fija ANTES del fetch y nunca se limpia en fallo (MainViewModel.cs:535, :556-558): un timeout de 10 s deja TODA la partida sin prior, sin botas meta, sin item sets ni runas. Además `StatsCache` solo invalida por parche: datos ruidosos del día 1 quedan clavados semanas.

**Files:**
- Modify: `src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs` (:533-538, :554-561, :936-947)
- Modify: `src/ParadoxLoLCompanion.Core/Stats/StatsCache.cs:20-36`
- Test: `tests/ParadoxLoLCompanion.Tests/StatsCacheTests.cs`

- [ ] **Step 1: Test del TTL que falla**

```csharp
    [Fact]
    public void TryRead_MissesEntriesOlderThanMaxAge()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var cache = new StatsCache(dir) { MaxAge = TimeSpan.FromHours(48) };
        cache.Write("16.13.1", "Jinx", "aram", "mid", new ChampionBuildStats { ChampionKey = "Jinx" });
        var file = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).Single();
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-3));

        var hit = cache.TryRead("16.13.1", "Jinx", "aram", "mid", out _);

        Assert.False(hit);
        Directory.Delete(dir, recursive: true);
    }
```

Run: `dotnet test --filter TryRead_MissesEntriesOlderThanMaxAge` → Expected: FAIL (no existe `MaxAge`; con el envejecido, hoy devolvería hit).

- [ ] **Step 2: TTL por timestamp del archivo (tolera cachés viejas, sin cambiar el formato)**

`StatsCache.cs`:

```csharp
    /// <summary>
    /// Antigüedad máxima de una entrada dentro del mismo parche: las stats del día 1
    /// de un parche son ruidosas y no deben quedar clavadas semanas.
    /// </summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromHours(48);
```

y en `TryRead`, tras el `File.Exists`:

```csharp
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > MaxAge)
                return false;
```

Run: `dotnet test --filter "FullyQualifiedName~StatsCache"` → Expected: PASS.

- [ ] **Step 3: Retry con backoff en el ViewModel (ambos caminos: en vivo y champ select)**

`MainViewModel.cs` — estado nuevo junto a `_statsFetchKey` (:61):

```csharp
    private readonly Dictionary<string, (int Attempts, DateTime NextRetryUtc)> _statsRetry = new();
    private const int StatsMaxAttempts = 3;
```

En `RequestStatsIfNeeded` (:533) y en el camino de champ select (:936), antes de fijar la key:

```csharp
        if (key == _statsFetchKey)
            return;
        if (_statsRetry.TryGetValue(key, out var retry)
            && (retry.Attempts >= StatsMaxAttempts || DateTime.UtcNow < retry.NextRetryUtc))
            return;
```

En el callback de `FetchStatsAsync` (:554-561), tras el guard de llegada tardía:

```csharp
            if (_statsFetchKey != key)
                return;   // llegó tarde: el contexto ya cambió
            _championStats = stats;
            if (stats is null)
            {
                // Falló: liberar la key para que el tick de 1 s reintente tras el
                // backoff (45 s, 90 s, luego se rinde) — hoy un timeout silencia
                // el prior, las botas meta y las runas por TODA la partida.
                var attempts = _statsRetry.TryGetValue(key, out var r) ? r.Attempts + 1 : 1;
                _statsRetry[key] = (attempts, DateTime.UtcNow.AddSeconds(45 * Math.Pow(2, attempts - 1)));
                _statsFetchKey = null;
            }
            else
                _statsRetry.Remove(key);
```

- [ ] **Step 4: Verificación**

Run: `dotnet test` → Expected: PASS.
Manual (el VM no tiene test harness): desconectar la red, abrir una partida, reconectar — el log `[stats]` debe mostrar el reintento y las stats llegando al panel.

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs src/ParadoxLoLCompanion.Core/Stats/StatsCache.cs tests/ParadoxLoLCompanion.Tests/StatsCacheTests.cs
git commit -m "fix: retry failed op.gg fetches with backoff + 48h stats cache TTL"
```

---

## Fase 1 — Vara de medición (antes de recalibrar nada)

Toda constante del scorer (13 magnitudes, 9 pares de anclas, la tabla oro-por-stat) fue calibrada a mano para preservar selecciones v2; los tests fijan casos, no calidad. Sin una métrica, las Fases 2–3 son cambios a ciegas. **Convertir en plan ejecutable propio al implementar.**

### H1 — Corpus de partidas reales

- **Problema:** el único fixture es un snapshot curado a mano (`sample-allgamedata-aram.json`); no hay datos de partidas reales para evaluar nada.
- **Cambio:** `LiveClientConnector` ya expone `rawJson` por tick. Agregar un toggle de debug en la app («grabar partida») que persista snapshots cada 30 s a `%LocalAppData%/ParadoxLoLCompanion/corpus/<gameId>/<t>.json` junto con la versión del catálogo. Comitear al repo un corpus inicial de 5–10 partidas ARAM reales anonimizadas (los payloads no tienen datos sensibles más allá de los nombres — reemplazarlos por `Player1..10` al exportar).
- **Tests:** el toggle no necesita test; el formato sí — un test que deserializa cada snapshot del corpus con `LiveGameParser` sin excepción.
- **Riesgos:** el corpus rota con los parches → guardar la versión de catálogo por snapshot y re-grabar por parche mayor.

### H2 — Harness offline con tres métricas

- **Cambio:** `tests/ParadoxLoLCompanion.Tests/CalibrationHarnessTests.cs`, opt-in como `RealCatalogSmokeTests`, que reproduce cada snapshot por `ItemAdvisor.Advise` y afirma bandas de tolerancia sobre:
  1. **agreement@3**: intersección del top-3 con los sets core+late de op.gg **evaluado con `stats: null`** (si el prior entra, la métrica se infla trivialmente) — mide si fit+amenaza solos encuentran items de meta. Es un piso, no un target: los counters correctos legítimamente se apartan de la meta.
  2. **churn**: distancia Jaccard media del top-3 entre snapshots consecutivos de la misma partida — techo fijado; mide la estabilidad que v3 prometió.
  3. **counter-responsiveness**: sobre barridos sintéticos de cada uno de los 11 grados (subir `ArmorStack` de 0→1 con todo lo demás fijo), el rank del item counter correspondiente es monótono no-decreciente.
- **Riesgos:** re-fijar los techos tras cada recalibración intencional es parte del flujo (el harness detecta regresiones, no verdades absolutas).

---

## Fase 2 — El modelo de amenaza lee la partida real

Todas las mejoras van en `ThreatAnalyzer`/`TeamThreat` salvo indicación. El orden interno sugerido es el listado (T1–T3 primero: máximo impacto, mínimo riesgo). **Convertir en plan ejecutable propio al implementar.**

### T1 — Sustain graduado por oro invertido + gate GW aliado suave *(imp 5, esf 2)*

- **Problema (verificado contra el catálogo real cacheado):** `HasSustain` es binario por enemigo — CUALQUIER item con tag de sustain acredita el peso completo (ThreatAnalyzer.cs:200-209, :85-89). Los starters estándar de ARAM (Doran's Blade/Bow, Guardian's Hammer) y las Gluttonous Greaves llevan tags de sustain → en el minuto 0 con 2–4 enemigos con starter, `sustainScore ≥ 0.4` satura la rampa ARAM (foot 0.15, shoulder 0.255) → μ=1.0 y el feed emite el warning de Grievous Wounds **contra cero curación real, en el modo insignia, la mayoría de las partidas**. Además un solo Oblivion Orb aliado anula TODO el bono anti-heal (`!teamHasGw`, ItemAdvisor.cs:537): contra Mundo+Soraka nunca se sugiere la segunda fuente GW — jugada estándar de alto nivel.
- **Cambio:** grado por enemigo en vez de booleano — healers curados mantienen crédito 1.0; sustain por items = `Fuzzy.Ramp(oroEnItemsConSustain, 800, 3000)` **excluyendo** `item.HasTag("Lane")` y `item.IsBoots`; acumular `sustain += w * grado` y `topSustain` por `w * grado`. En ItemAdvisor:537, reemplazar el gate duro por amortiguación: `teamHasGw ? Fuzzy.Ramp(threat.Sustain, 0.55, 0.9) : 1.0`.
- **Tests:** enemigo con solo Doran's → μ≈0; enemigo con BT+Riftmaker → μ alto; comp Mundo+Soraka con GW aliado → el bono anti-heal sobrevive parcialmente. Actualizar los tests que fijan sustain binario (`Aram_TriggersAntiHealEarlier` y afines).
- **Riesgos:** los pies de rampa deben validarse con H2 para que el timing GW mid-game no se retrase; interactúa con el dedup máx-1-GW.

### T2 — Mix de daño enemigo inferido de sus compras *(imp 5, esf 3)*

- **Problema:** el split físico/mágico por enemigo es del kit y cuantizado a 1.0/0.0/0.5 TODA la partida (ThreatAnalyzer.cs:67-74); el loop de items enemigos solo lee Armor/MR/HP/Crit/GW aunque `StaticItem` ya trae AD/AP/AS. Un «Mixed» que compró 3 items full-AP sigue contando 50/50 → PhysicalSkew/MagicalSkew/MixedDamage — los grados que deciden muro Armor vs MR, Mercs vs Steelcaps y HP crudo — están sistemáticamente mal contra cualquier build off-meta.
- **Cambio:** en el loop de items enemigos acumular `adGold = AD*35 + AS%*2500 + Crit*4000`, `apGold = AP*21.75` (constantes de `Efficiency`); `observedPhys = adGold/(adGold+apGold)`; `phys = Lerp(kitPhys, observedPhys, Fuzzy.Ramp(adGold+apGold, 1500, 4500))`, blend pleno para Mixed y tope 0.5 del factor para kits sesgados. Usar el mismo blend para `burstDamage` del top burst (ThreatAnalyzer.cs:107-112). Todo lo downstream se actualiza solo.
- **Tests:** Mixed con 3 items AP → MagicalSkew sube; sin items → idéntico a hoy; estabilidad tick a tick (H2 churn).
- **Riesgos:** híbridos on-hit pueden desviar el ratio (ponderar por valor-oro, no por tags); fixtures existentes fijan skews de kit puro → actualizar con inventarios.

### T3 — Burst para magos, no solo tag Assassin *(imp 4, esf 2)*

- **Problema:** `topBurst` solo trackea `champ.HasTag("Assassin")` (ThreatAnalyzer.cs:107-112). El burst dominante de ARAM es un mago fed (Veigar, Syndra, Lux) → `Burst = 0` y la regla anti-burst (Zhonya/GA/Banshee, AntiBurstMag 2.0) **nunca dispara en el modo insignia**: un Veigar 12/2 no produce recomendación de supervivencia; un Zed 0/5 sí.
- **Cambio:** candidatos a burst = Assassin a peso 1.0; Mage (o Magical con tag secundario Assassin/Mage) a ~0.8; lista config `SustainedDpsMages` (Cassiopeia, Karthus…) excluida para evitar falsos positivos. `burstDamage` sigue saliendo del perfil (o de T2). Sin cambios en ItemAdvisor.
- **Tests:** Veigar fed → Burst > 0 y Banshee/Zhonya suben para un squishy; Cassiopeia fed → sin cambio.
- **Riesgos:** sobre-recomendar Zhonya vs magos de poke; el factor 0.8 se valida con H2.

### T4 — Eventos ChampionKill: pesar a quien TE mata *(imp 4, esf 3)*

- **Problema:** `GameState.Events` se parsea entero (KillerName/VictimName/EventTime) y su único consumidor es un log de debug (MainViewModel.cs:475, verificado por grep). Un enemigo que te mató 3–4 veces pesa igual que otro con el mismo scoreboard que nunca te tocó: los counters personales (anti-burst, muro del tipo correcto, QSS) no apuntan al asesino real.
- **Cambio:** en `Analyze`, escanear `ChampionKill` con `VictimName` = jugador activo y `KillerName` = enemigo (matchear contra SummonerName Y prefijo de RiotId, OrdinalIgnoreCase — evitar el pitfall de F2); multiplicar el `Weight` de ese enemigo por `1 + 0.4 * min(killsOnMe, 3)` con decaimiento por recencia (`exp(-(gameTime-EventTime)/300)`) y tope total ~2.2×. `Label()` puede sumar «killed you 3x» a las razones.
- **Tests:** fixture con eventos (el sample ya trae ChampionKill); matcheo fallido → comportamiento actual intacto.
- **Riesgos:** formatos de nombre varían por parche (summonerName vacío en recientes) — degradar limpio; el escaneo por tick debe ser acotado (lista acumulativa).

### T5 — Peso de fed-ness relativo a la línea base del juego *(imp 3, esf 2)*

- **Problema:** `Weight = 1 + 2.5K + 0.8A + 0.04CS + 0.5·level − 1.2D` crece con nivel/CS compartidos → en late todos los pesos se inflan y las razones (`burstScore = topW/avgW`) se comprimen justo cuando los asesinos spikean; el gap topW−minW que nombra al «biggest threat» queda dominado por crecimiento común.
- **Cambio:** `weight = max(0.5, 1 + 2.5K + 0.8A − 1.2D + 0.5·(level − avgLevel) + 0.04·(cs − avgCs))` con los promedios del equipo enemigo; mantener el overload estático actual para el reuso de T9.
- **Tests:** early game ≈ idéntico; sintético late-game: burstScore ya no tiende a 1. Amplias actualizaciones mecánicas de tests que fijan pesos.
- **Riesgos:** las muertes pegan más sin el colchón de nivel → re-validar el floor 0.5 con H2.

### T6 — Summoner spells: Ignite y Heal *(imp 3, esf 2)*

- **Problema:** la API manda `summonerSpells` por jugador y el modelo no lo tiene (Players.cs:35-52, verificado) — v2 lo difirió y quedó. Un Ignite enemigo aplica 40 % GW desde el minuto 0 (comunísimo en ARAM) y `EnemyAntiHeal` no lo ve → el robo de vida propio se sobrevalora temprano; los Heal enemigos tampoco suman a Sustain.
- **Cambio:** agregar `SummonerSpells { SummonerSpellOne/Two { DisplayName, RawDisplayName } }` a `Player` (el deserializador case-insensitive lo levanta solo). En `Analyze`: `RawDisplayName` conteniendo `SummonerDot` → `gwHolderW += 0.5 * w`; `SummonerHeal`/`SummonerBoostedHeal` → sustain a peso reducido. Extender el fixture con el campo (hoy no lo trae — verificado).
- **Riesgos:** verificar las raw keys contra un payload real ANTES de codificar (fixture-first); en ARAM el Mark reemplaza muchos Ignites.

### T7 — Grado CcThreat + tenacidad *(imp 4, esf 3)*

- **Problema:** contra un wombo de 3–4 CC pesados la única reacción son las botas Mercs (`HeavyCcCount` no tiene otro consumidor — grep verificado). No existe el concepto tenacidad en el scoring; `RemovesCc` solo puntúa vía la lista de 4 supresores y la regla HardEngage.
- **Cambio:** (1) acumular `heavyCcW += w` y exponer `TeamThreat.CcThreat = Ratio(heavyCcW, totalW, 0.2, 0.6)`; (2) flag `GrantsTenacity` por keyword localizado (patrón de `CritReductionKeywords`), validado contra el catálogo real en `RealCatalogSmokeTests`; (3) en `ScoreItem`: `offense += CcCounterMag(~2.0) * CcThreat` para `RemovesCc || GrantsTenacity`, sin gate de arquetipo.
- **Riesgos:** keywords es_MX/en_US frágiles (validar contra Mercurial y Silvermere reales); rot de `HeavyCcChampions` acota el recall (D5 lo mitiga).

### T8 — Necesidad defensiva desde stats vivos *(imp 4, esf 3)*

- **Problema:** `ChampionStats` parsea 10 campos y el único consumidor del motor es `CritChance` (grep verificado). Los bonos de muro/anti-burst dependen solo de grados enemigos × `DefenseFactor` por arquetipo: el top-3 apila un tercer item de armadura sin retornos decrecientes, y un glass cannon con 2 defensivos ya comprados sigue recibiendo muros.
- **Cambio:** pasar `state.ActivePlayer?.ChampionStats` a `ScoreItem`; factores de necesidad `needArmor = 1 − Fuzzy.Ramp(live.Armor, 60+6·level, 180+8·level)` (ídem MR; HP contra una rampa por gameTime); multiplicar muro Armor/SpellBlock/HP-crudo y anti-burst por el factor del tipo correspondiente; ampliar el gate `IsSquishy` a `Fuzzy.Or(IsSquishy, déficitEhp)`. Fallback limpio al camino actual si `ChampionStats` falta (mismo patrón que `liveCrit`).
- **Riesgos:** armor/MR vivos incluyen runas y buffs → umbrales generosos y relativos a nivel o los tanques quedan sub-servidos; calibrar con H2.

### T9 — Modulación ahead/behind *(imp 4, esf 2)*

- **Problema:** el propio desempeño solo gatea Mejai's y ventas; un 0/6 y un 8/0 puntúan idéntico — «behind ⇒ durable y eficiente, ahead ⇒ greed» es heurística nuclear de ARAM y no existe.
- **Cambio:** exponer `TeamThreat.AvgEnemyWeight` (ya computado como `avgW`); `myWeight = ThreatAnalyzer.Weight(me)`; `behind = Fuzzy.Ramp(avgW/myWeight, 1.15, 1.9)`, `ahead` simétrico; multiplicar canal defensa por `(1 + 0.35·behind)` y counters ofensivos por `(1 + 0.15·ahead)`; volver `snowballing` continuo con `ahead`.
- **Riesgos:** se apila con DefenseFactor y Burst — acotar el multiplicador combinado o las categorías saltan (H2 churn lo vigila).

---

## Fase 3 — Matemática del scoring coherente

Todo en `ItemAdvisor` salvo indicación. Requiere H2 activo; conviene después de T1–T3 (recalibrar contra entradas ya veraces). **Convertir en plan ejecutable propio al implementar.**

### S1 — Re-anclar rampas a μ(umbral) = 0.5 *(imp 4, esf 2)*

- **Problema (verificado por cálculo directo):** la regla v3 «el umbral v2 es el cruce μ≈0.5» solo se cumple en los skews (μ(0.62)=0.48). Hoy: `Sustain` μ(umbral)=**0.0** (foot EN el umbral — el comentario lo admite), `ArmorStack/MrStack` μ(umbral)=**0.375** (0.4×→2×), `Burst` μ(umbral)=**0.333** (0.75×→1.5×). En el umbral, pen aporta 3.0×0.375=1.125 y anti-heal 2.0×0=0 → el orden entre counters con amenazas igual de críticas es arbitrario, y el GW llega sistemáticamente más tarde que en v2.
- **Cambio:** rampas simétricas alrededor del umbral: `Sustain: Ramp(x, 0.6·thr, 1.4·thr)`, `ArmorStack/MrStack: Ramp(x, 0.5·thr, 1.5·thr)`, `Burst: Ramp(x, 0.75·thr, 1.25·thr)`; skews quedan. Test-teoría en `ThreatAnalyzerTests`: para todo grado con umbral de config, μ(umbral) ∈ [0.45, 0.55] — el invariante impide drift futuro.
- **Riesgos:** selecciones fijadas pueden flippear (el GW temprano de ARAM sobre todo) → un canal por vez, re-tunear magnitudes solo si un test de selección flippea, H2 antes/después.

### S2 — Desarmar el doble conteo defensivo *(imp 4, esf 3)*

- **Problema:** contra una comp AD fed, un item de armadura cobra hasta 4 bonos correlacionados: muro (2.0×PhysicalSkew) + anti-burst (2.0×Burst) + hard-engage (1.5×HardEngage, mismo predicado IsSquishy+OffensiveMatch que anti-burst) + anti-crit (2.5×CritThreat, cuyo OR incluye la MISMA share de marksmen que ya alimenta PhysicalSkew). Total defensivo ~5.5–8.0 vs. counter ofensivo máximo 3.0 → un Zhonya/Randuin aspira todo el presupuesto del top-3 y desplaza pen/anti-heal que la misma comp exige.
- **Cambio:** (1) fusionar anti-burst y hard-engage: `defense += SurvivalMag(2.0) × Fuzzy.Or(Burst, HardEngage)` (comparten guardas; la razón del grado mayor); (2) la rama marksman de CritThreat exige crit confirmado: `Or(critRamp, AndProduct(autoAttackRamp, Ramp(critSum, 0.05, 0.4)))`; (3) tope `DefenseCap = 4.0` a la suma defensiva por item. Test de coherencia: vs comp AD de amenaza máxima, defensa por item ≤ cap y un item de pen puede superar a un muro puro con ArmorStack alto.
- **Riesgos:** sub-proteger squishies vs dive reales; on-hit marksmen sin crit dejan de disparar anti-crit (correcto, pero puede pinchar tests).

### S3 — Empujón de affordability acotado y continuo *(imp 4, esf 2)*

- **Problema:** `CanFinishNow ⇒ score × 1.25` (ARAM) es un salto de +2–3 puntos sobre un score típico — mayor que casi cualquier counter — que además flippea cada vez que el oro pasivo cruza un umbral de compra: la mayor discontinuidad restante en un scorer cuyo objetivo declarado v3 es estabilidad tick a tick. Un stat stick alcanzable le pasa por encima al counter urgente al que le faltan 300 g.
- **Cambio:** en la proyección de ranking (:226-234), reemplazar por término aditivo suave: `score + AffordMag × Fuzzy.Ramp(gold, 0.5·RemainingCost, RemainingCost)` con `AffordMag ≈ 0.8` ARAM / 0.5 resto (resemantizar `AramAffordabilityBoost` en config). `Spike` sigue derivando de `CanFinishNow`. Test de propiedad: si dos candidatos difieren en más de `AffordMag` sin boost, el orden es invariante a `CurrentGold`.
- **Riesgos:** debilita a propósito el sesgo «compra ALGO ya» de ARAM — validar la constante con partidas reales (H1) y revisar que el ShopAlert no apunte tan seguido a un top inalcanzable.

### S4 — Fuzzificar Cleanse y ShieldBreak *(imp 3, esf 2)*

- **Problema:** Cleanse es el bono situacional más grande (3.0) y es plano sobre `HasSuppression` — el primer match de una lista de 4 champs, sin peso: un Malzahar 0/9 fuerza QSS al top-3 toda la partida igual que uno 12/0. ShieldBreak ídem (1.0 sobre bool de lista de 8). El HardEngage, estructuralmente idéntico, ya es difuso vía `Ratio()`.
- **Cambio:** acumular `suppressorW`/`shieldW` (patrón de pctHpTrueW); `TeamThreat.Suppression = Ratio(suppressorW, totalW, 0.03, 0.35)` (pie generoso: un supresor de peso promedio ⇒ μ≥0.6 — la supresión es letal aun atrás), `ShieldThreat = Ratio(shieldW, totalW, 0.15, 0.5)`; en `ScoreItem` los adders crisp pasan a `Mag × grado`. Los booleanos y nombres se conservan para ventas y mensajes.
- **Riesgos:** QSS puede salir del top-3 vs un supresor muy atrás — el pie del ramp es la perilla; tests de selección de QSS a actualizar.

### S5 — Category y Priority desde componentes limpios *(imp 2, esf 1)*

- **Problema:** la categoría se deriva de `situational < 0.2 × score` donde score incluye statBonus y resta penalidades → una penalidad grande achica el denominador y flippea a Defense/Counter sin cambio situacional; un statBonus grande disfraza de Core a un counter. Priority se computa DESPUÉS del boost no uniforme → los porcentajes saltan al cruzar oro.
- **Cambio:** `ScoreItem` devuelve los componentes; categoría desde `situational / (core + situational)`; en el loop, ordenar por score con boost pero `Priority = Clamp01(unboosted / unboostedTop)`. Tests: Priority invariante a CurrentGold con comp fija; item penalizado conserva Core.
- **Riesgos:** mínimos (badges de UI se corren un poco).

### S6 — Histéresis entre ticks *(imp 4, esf 2)*

- **Problema:** `Advise` es 100 % stateless y se recomputa cada 1 s; la v3 difirió la histéresis confiando en las rampas, pero las rampas suavizan el score, no el argmax: candidatos casi empatados (Δ<5 %) intercambian rank con cualquier twitch de input — la «lista que tiembla» que la v3 diagnosticó sigue viva en el borde.
- **Cambio:** parámetro opcional `IReadOnlyList<int>? previousTopIds` en `Advise`; en la proyección de ranking, `score += HysteresisMag (~0.3)` si el item estaba en el top anterior. Los dos callers (panel y feed — tras F3 pueden compartir el plan) guardan el último top y lo devuelven; reset al empezar partida y al cambiar el override de arquetipo. Un retador debe superar al incumbente por >0.3 reales: mata la oscilación, y cualquier cambio de amenaza real (magnitudes 2–3) desplaza en 1–2 ticks.
- **Tests:** `previousTopIds=null` ⇒ todos los tests actuales intactos; test de propiedad: dos candidatos alternando ±0.1 por tick producen un #1 estable. H2-churn debe bajar de forma medible.
- **Riesgos:** estado compartido entre partidas si el reset falla; mantener `HysteresisMag` muy por debajo de las magnitudes de counter.

### S7 — Fit consciente de magnitud *(imp 5, esf 4 — el más grande; dividir en 2 entregas)*

- **Problema:** `fit = Σ tags` es binario — un item de 15 AD y uno de 60 AD puntúan igual, y los multi-tag (muchos stats chicos) le ganan a los enfocados. El único corrector, `Efficiency`, (a) modula solo ×[0.8, 1.28] y (b) valora exactamente 7 stats — items cuyo precio es mayormente maná, MS, lifesteal o regen (la mayoría de mago/enchanter/assassin) leen como ineficientes y quedan clavados al piso 0.8: un spread de ~35 % del score base no relacionado con el poder real. El costo tampoco entra al término base.
- **Cambio (entrega 1, valor solo):** extraer de ddragon `FlatMPPoolMod`, `FlatMovementSpeedMod`/`PercentMovementSpeedMod`, `PercentLifeStealMod`, `FlatHPRegenMod` a `StaticItem`; extender `Efficiency` con valores oro estándar (maná 1.4/pt, MS plano 12/pt, lifesteal 37.5/1 %, regen 3/pt) — en `AdvisorConfig`, tuneable sin recompilar; clamp a 1.0 neutral para items cuyo oro-en-stats < 50 % del precio (pasiva/haste-pesados: valor no modelado no se castiga). **(Entrega 2, fit por valor):** `statFit = Σ weights[tagDelStat] × valorOro(stat) / 3000` (normalización por 3000 g: densidad de valor, no valor bruto) y `fit = max(statFit, 0.5 × tagFit)` como piso para items de pasiva. Mantener `3·√fit` y el descuento de CritWaste.
- **Tests:** los targets son selecciones (v3 lo permite); H2 antes/después es obligatorio — es el cambio que más reordena.
- **Riesgos:** el mayor reordenamiento del plan; constantes oro-por-stat rotan con parches; el maná puede sobre-premiar Lost Chapter para usuarios de maná. La entrega 1 es shippeable sola y de riesgo bajo.

---

## Fase 4 — Datos que faltan conectar o traer

Independientes entre sí; D1 es el de mayor impacto de todo el plan junto a T1/T2/S7. **Convertir en plan ejecutable propio al implementar.**

### D1 — El tier GW de 800 g debe poder recomendarse *(imp 5, esf 3)*

- **Problema (verificado; matiz Bramble anotado arriba):** el pool puntuado exige `GoldTotal ≥ 1100` y componentes → Bramble Vest/Executioner's Calling/Oblivion Orb (800 g, el 100 % del efecto GW) son irrecomendables. La jugada de nivel alto — comprar la pieza de 800 en la primera muerte contra Soraka/Mundo y completarla al final — no se puede expresar; el consejo GW solo aparece si un item TERMINADO de GW entra al top-3, y nombra el item de 2200–3300 g.
- **Cambio:** camino dedicado en `Advise`: si `threat.Sustain ≥ ~0.4`, sin GW propio ni aliado (post-T1: gate suave), escanear el catálogo completo (no `CompletedItemsFor`) por `AppliesGrievousWounds && BuildsIntoSomething && Purchasable && OnAram`, elegir por perfil de daño (Bramble tanque / Executioner's AD / Orb AP) y emitir como campo nuevo de `ItemAdvicePlan` (`UrgentPickup`), republicado por el feed con «buy the 800g piece now, finish it later». Escalar además `AntiHealMag` con la intensidad: `2.0 + 1.0·Ramp(Sustain, 0.5, 0.9)`.
- **Tests:** comp doble-sustain ⇒ UrgentPickup = pieza de 800 del perfil correcto; dedup contra un GW terminado ya en top-3 y contra el upgrade path (no sugerir Bramble si Thornmail es el top).
- **Riesgos:** interacción con máx-1-GW; tests de dedup anti-heal a actualizar.

### D2 — Prior op.gg consciente del slot *(imp 3, esf 2)*

- **Problema:** `ItemPriorFor` escanea core y luego `LateItems` aplanado (4.º+5.º+6.º concatenados) y devuelve el primer match; `PriorFor` no sabe cuántos items completos tenés. Un item presente en 4.º y 6.º hereda siempre las stats del 4.º; el piso core (0.6) sigue empujando items core con el core ya terminado; las listas por slot se parsean y solo las consume `ItemSetBuilder` (grep verificado).
- **Cambio:** `ItemPriorFor(int itemId, int completedCount = -1)`: con ≥3 completos, mirar primero la lista del slot que corresponde, después las otras, después core, con fallback al aplanado (cachés viejas). `completedCount` desde `ownedIds` (`!BuildsIntoSomething && !IsBoots && GoldTotal ≥ MinCompletedItemGold`); amortiguar `StatCoreFloor` cuando `completedCount ≥ CoreItems.Count`. Opcional: centrar el factor WR del prior en el `WinRate` global del campeón (parseado y sin consumidor — verificado).
- **Riesgos:** conteo de completados vs evoluciones (Muramana); compat con cachés que solo traen `LateItems`.

### D3 — Primera compra ARAM: set completo + presupuesto *(imp 3, esf 2)*

- **Problema:** `StarterFor` solo puede recomendar UN item del pool con tag `Lane`; el starter de op.gg se reduce a una sort key binaria sobre ese pool (aperturas Tear/Dark Seal/botas+pots inexpresables) y nunca lee el oro actual (los ~450 g sobrantes tras un Guardian's quedan sin consejo).
- **Cambio:** con `stats?.Starter` presente, resolver `Starter.ItemIds` directo del catálogo (`Purchasable && OnAram`) y recomendar el SET mientras `Σ GoldTotal ≤ CurrentGold`; `StarterAdvice` pasa a llevar `IReadOnlyList<StaticItem>`. Fallback: heurística actual + completar con la compra complementaria más barata (botas 1001/pots) mientras el sobrante alcance.
- **Riesgos:** sets de op.gg con consumibles/ids duplicados a filtrar; el record `StarterAdvice` toca bindings de UI y el test del starter.

### D4 — Gate de confianza para botas meta *(imp 2, esf 1)*

- **Problema:** el override de botas op.gg gana incondicionalmente leyendo solo `ItemIds`/`PickRate`; `Boots.Play/Win` se parsean y nadie los consume. Una entrada día-1 con un puñado de partidas pisa un señalazo de amenaza (4 CC pesados → Mercs) todo el parche (la caché no expira — mitigado por F6).
- **Cambio:** `bootsConfidence = Fuzzy.Ramp(statBoots.Play, StatPlayFoot, StatPlayShoulder)`; op.gg gana solo con `bootsConfidence > MuGate` (Play ≥ ~300 — cualquier dato real lo pasa); si no, cae al threatPick. Sumar WR a la razón: «(62 % pick rate, 52 % WR)». **La regla «op.gg boots always win» se mantiene para cualquier muestra real** — esto solo cierra el hueco de muestra trivial, igual que el ramp de confianza que los priors de items ya tienen.
- **Tests:** los existentes usan Play=9000 → siguen verdes; agregar Play<300 ⇒ fallback a amenaza.

### D5 — championFull.json → flags de kit (unión con curadas) *(imp 4, esf 4)*

- **Problema:** la v3 difirió parsear spells («poco confiable») y las listas curadas rotan en silencio: faltan Taric/Milio en healers, Ivern/Milio/Renata en shields (verificado contra `AdvisorConfig.cs:150-180`); `Contains` exacto ⇒ un campeón ausente aporta exactamente 0 a Sustain/PercentHpTrue/HardEngage/Suppression/Shield — anti-heal y Mercs nunca disparan contra ellos.
- **Cambio:** bajar `championFull.json` una vez por parche (misma caché que item.json); derivar en el catálogo `HealsAllies/GrantsShields/HasSuppression/DealsPercentHpOrTrue` escaneando spells+passive con la maquinaria de keywords localizados ya probada en items (`MentionsAny`); en `ThreatAnalyzer`, `champ.Flag || _config.XChampions.Contains(...)` — **las curadas pasan a ser overrides, nunca la única fuente, y nunca se quita conocimiento curado**. Validar en `RealCatalogSmokeTests` (Soraka cura, Malzahar suprime, Vayne es %HP, Filo Infinito NO flaggea).
- **Riesgos:** la razón original del deferral sigue viva — falsos positivos (self-heals, daño verdadero condicional): keywords conservadores, blocklist en config, smoke tests reales. championFull es ~10× más grande (descarga única por parche).

### D6 — Tabla curada de balance ARAM *(imp 3, esf 3)*

- **Problema:** Riot aplica modificadores ARAM por campeón (±10–20 % daño hecho/recibido, curación) que no existen en ddragon: el análisis pesa igual a un campeón nerfeado al 85 % que a uno buffeado al 115 % — error sistemático en TODOS los grados, en el modo insignia.
- **Cambio:** sección `AramBalance` en `ItemsConfig` + JSON compartido: `Key → { dealt, taken }`, default 1.0, curada del wiki comunitario (~50–80 entradas no neutrales por parche, mismo flujo de mantenimiento que las listas existentes). En `Analyze` con `isAram`: contribución de daño × `dealt`; `1/taken` en el término de EnemyTankiness; el `taken` propio escala el lado de durabilidad.
- **Riesgos:** rot por parche (defaults 1.0 degradan con gracia; versionar la tabla); aplicar ANTES de defuzzificar o el efecto se pierde en las rampas.

---

## Fase 5 — Ángulos sin explorar (descubrimiento pendiente)

Detectados por el crítico de completitud; los agentes que debían desarrollarlos murieron por límite de sesión. Son direcciones con evidencia inicial verificable, **no diseños**: cada uno requiere su propia sesión de brainstorming/diseño.

- **G1 — Contexto del equipo aliado.** Todo grado difuso sale de los 5 ENEMIGOS; la única señal aliada del pipeline es `teamHasGw`. Falta: valor predictivo de pen cuando mi equipo es full-AD (los enemigos VAN a apilar armadura), peso defensivo según si soy el único frontline, redundancia de auras únicas (dos Lockets), y `TeamBalanceAdvisor` ya demuestra el concepto en champ select sin aplicarse in-game.
- **G2 — Timing y entrega en la ventana de muerte.** El shop de ARAM solo se usa muerto, pero la affordability se evalúa contra `CurrentGold` cada tick con la tienda cerrada; nada proyecta el oro del próximo respawn (`RespawnTimer` + oro pasivo, ambos disponibles); el overlay Ctrl+X es manual y no se auto-muestra al morir; `LcuItemSetWriter` ya escribe páginas de items al cliente pero solo las estáticas de op.gg — el top-3 vivo podría aparecer DENTRO del shop del juego.
- **G3 — Telemetría de adopción y personalización.** La app es amnésica entre partidas: nada registra qué se recomendó, qué compró el jugador ni el resultado. Un journal por partida (diff de inventario por tick para atribuir compras) daría métricas de adopción, priors personales por campeón — y **construiría automáticamente el corpus que H1 necesita** (sinergia directa: si G3 se hace primero, H1 sale casi gratis).

---

## Dependencias y secuencia

```
Fase 0 (F1–F6)  — sin dependencias; cualquier orden; hoy mismo.
Fase 1 (H1–H2)  — antes de CUALQUIER recalibración (S*, y deseable antes de T*).
Fase 2 (T1–T9)  — T1/T2/T3 primero (máx. impacto). T9 depende de T5 (misma escala de pesos).
Fase 3 (S1–S7)  — después de T1–T3 (calibrar sobre entradas veraces). S1 antes que S2
                   (anclas primero, luego magnitudes). S6 después de S3 (el afford-cliff
                   es la mayor fuente de oscilación; matarlo primero hace la histéresis
                   más chica). S7 al final (mayor reordenamiento; entrega 1 puede adelantarse).
Fase 4 (D1–D6)  — independientes; D1 tras T1 (usa el gate GW suave). D4 tras F6 (TTL).
Fase 5 (G1–G3)  — sesiones de diseño propias; G3 alimenta a H1.
```

**Impacto en tests (inventario):** los tests actuales afirman SELECCIONES, no scores (decisión v3) — hay libertad de magnitudes mientras las selecciones pinneadas se sostengan o se cambien a consciencia. Los que seguro se tocan: sustain binario (T1), skews de kit puro (T2), pesos de fed-ness (T5), selección de QSS (S4), timing GW ARAM (S1/D1), strings del feed (F3/F5), dedup anti-heal (D1). El resto debe quedar verde tal cual.

## Fuera de alcance (decidido, no olvidado)

- Nada de Summoner's Rift (jungla/objetivos: removidos a propósito el 2026-07-08).
- Revertir «las botas meta de op.gg mandan» (D4 solo agrega un piso de muestra mínima).
- Parsear texto de spells como ÚNICA fuente (D5 lo usa en unión con curadas, nunca en reemplazo).
- Runas/orden de habilidades más allá de lo ya integrado; augments de Mayhem por API (no expuestos).

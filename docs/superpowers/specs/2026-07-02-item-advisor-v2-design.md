# LoL Advisor — Asesor de Items v2 (diseño)

**Fecha:** 2026-07-02
**Estado:** Diseñado y ejecutado de forma autónoma bajo directiva `/goal`
("make the best item advisor possible for league of legends"). Sustituye la
recomendación de items "fina" de la v1 por un motor completo.

## Objetivo

Convertir la regla de items de la v1 (tags AD/AP + "el item más caro que alcanzas")
en un **asesor de items real**: entiende quién te está ganando la partida, qué tipo de
daño te va a matar, qué contrarresta a cada amenaza, qué te conviene comprar *ahora
mismo* con el oro que tienes, y **explica cada recomendación** con razones legibles.

## Qué mejora respecto a la v1

| v1 | v2 |
|----|----|
| AD/AP por tags de clase (Mage/Marksman…) | Perfil de daño por `info.attack/magic` de Data Dragon + overrides configurables |
| Un solo consejo de resistencia | Análisis de amenaza por equipo: split físico/mágico ponderado por lo "fed" de cada enemigo |
| Anti-curación genérica | Anti-curación **acorde a tu build** (Morello si eres AP, Ejecutor/Recordatorio si AD, Cota si tanque) |
| Nada de build paths | Planificador de compra: el **componente** que te conviene comprar ya, camino al item objetivo |
| Nada situacional | Penetración vs. resistencias apiladas, Zhonya/Ángel vs. burst, Mercurial vs. supresión, rompe-escudos, botas según amenaza |
| Solo feed de texto | Panel dedicado "Asesor de items" con amenaza del equipo enemigo, top-3 con razones y compra inmediata |
| Falla si el cliente está en otro idioma | Resolución de campeones por `rawChampionName` (locale-independiente) |

## Arquitectura

Todo el motor vive en `LoLAdvisor.Core.Items` (sin UI, testeable). La UI consume un
`ItemAdvicePlan` inmutable por tick.

```
GameState ─┐
           ├─ ChampionProfiler ──► perfil mío (DamageProfile + BuildArchetype)
IStaticData┤
           ├─ ThreatAnalyzer ───► TeamThreat (split físico/mágico, sustain, stacking,
           │                       burst, supresión, CC, nombres de amenazas)
           └─ ItemAdvisor ──────► ItemAdvicePlan
                 │                  ├─ Recommendations (top-3 con Score + Reasons)
                 │                  ├─ NextPurchase (BuildPathPlanner: componente ya)
                 │                  └─ Boots (BootsAdvice)
                 └─ BuildPathPlanner
```

### 1. Datos estáticos ampliados (`DataDragon`)

- **StaticItem** gana: `Depth`, `From` (ids de componentes), stats planos
  (`Armor`, `SpellBlock`, `Health`, `AttackDamage`, `AbilityPower`, `AttackSpeedPct`,
  `CritChance`), y flags por keywords de descripción (localizables por config):
  `RemovesCc` (QSS/Mercurial), `BreaksShields` (Colmillo de la Serpiente).
  Los stats permiten estimar cuánta armadura/vida compró el enemigo.
- **StaticChampion** gana: `Key` (id textual de ddragon, p.ej. `MonkeyKing`) e
  `Info` (attack/defense/magic 0–10) para clasificar el daño sin listas curadas.
- **Catálogo**: `ChampionByKey`, `ResolveChampion(name, rawName)` — resuelve por el
  sufijo de `game_character_displayname_X` primero (independiente del idioma del
  cliente) y por nombre localizado después. Query `FinishedBoots()`.

### 2. ChampionProfiler

- **DamageProfile** ∈ {Physical, Magical, Mixed}: `attack − magic ≥ 3` → Physical;
  `magic − attack ≥ 3` → Magical; si no, Mixed. Overrides en config
  (`DamageProfileOverrides`, claves por `Key` de ddragon) para los casos donde
  `info` engaña (p.ej. luchadores de daño mágico).
- **BuildArchetype** ∈ {Marksman, Mage, AdAssassin, AdFighter, ApFighter, Tank,
  Enchanter}: derivado del tag primario de ddragon + perfil de daño, con
  `ArchetypeOverrides` en config. El arquetipo define la bolsa de items "core".

### 3. ThreatAnalyzer → `TeamThreat`

Cada enemigo pesa según lo fed que va:
`w = 1 + kills·2.5 + assists·0.8 + cs·0.04 + level·0.5 − deaths·1.2` (mínimo 0.5).

- **Split de daño**: share físico/mágico = Σ w·perfil / Σ w (Mixed cuenta 0.5 y 0.5).
- **Sustain**: enemigos con items de robo de vida/vamp **o** campeones curadores
  (lista `HealerChampions` por Key), ponderado por w → dispara anti-curación.
- **Stacking de resistencias**: Σ de armadura/MR compradas (stats de sus items).
  Umbral configurable → dispara penetración para tu tipo de daño.
- **Burst**: la mayor w entre asesinos/amenazas fed → dispara Zhonya (AP), Ángel de
  la Guarda (AD) o Fauces/Banshee según el tipo de daño del burst, solo si tu
  arquetipo es squishy.
- **Supresión** (`SuppressionChampions`: Malzahar, Warwick, Urgot, Skarner) →
  Mercurial para carries AD; **CC pesado** (`HeavyCcChampions`) → botas Mercurio.
- Nombres de la amenaza top por categoría para armar mensajes.

### 4. BuildPathPlanner

Dado un item objetivo, el inventario y el oro actual: descuenta componentes ya
comprados (cada id del inventario consume una ocurrencia), calcula el **costo
restante real** y elige el componente más caro que puedas pagar ya (bajando
recursivamente si ninguno directo alcanza). Devuelve `NextComponent` +
`RemainingGold`.

### 5. ItemAdvisor (scoring)

Para cada item completo no poseído, `score = base + situacional`, con razones:

- **Base (fit de arquetipo)**: pesos por tag (p.ej. Marksman: AttackDamage 3,
  CriticalStrike 3, AttackSpeed 2.5…; Tank: Health 3, Armor/SpellBlock 2.5 escalado
  por el split de amenaza). Pesos en código, sobreescribibles por config.
- **Situacional** (cada bono agrega una razón):
  - Anti-curación si sustain alto y tu equipo no tiene Heridas Graves — el bono va
    a los GW *de tu perfil* (AP→Morello, AD→Recordatorio, tanque→Cota).
  - Penetración si el enemigo apila la resistencia que te bloquea (armadura para
    físico / MR para mágico), escalado por cuánto apilan.
  - Defensa según el split (armadura si te matan físico, MR si mágico), escalada
    por la amenaza y tu squishiness; los híbridos ofensivos (Zhonya, Ángel, Fauces,
    Banshee, Sterak) puntúan naturalmente por sumar fit + defensa.
  - Mercurial si hay supresión y eres carry AD; rompe-escudos si abundan escudos.
  - Afinidad de compra: +15 % si te alcanza ya; sin descartar power-spikes caros
    (para eso está el planificador de componentes).
- **Restricciones**: nunca items poseídos; máximo un item de Heridas Graves; tope
  de `MaxRecommendations` (3).
- **Botas** por separado: si sigues en botas t1/sin botas → Mercurio (mágico+CC),
  Acorazadas (físico/autos), o la bota del arquetipo.

### 6. Integración

- `ItemRecommendationRule` queda como adaptador fino: emite al feed el anti-heal
  urgente (Warning), la compra recomendada (Info) y botas (Info). El panel nuevo
  muestra el plan completo.
- **UI**: tarjeta "Asesor de items" en la pestaña Partida: encabezado con el split
  de amenaza ("Daño enemigo: 68 % físico — mayor amenaza: Jinx 7/1/3"), top-3 con
  costo, ✓ alcanzable / "faltan N", razones, línea "Compra ahora: X → hacia Y" y
  línea de botas.

### 7. Config (`ItemsConfig`)

Nuevas claves (defaults embebidos, editables en `advisor-config.json`):
`HealerChampions`, `ShieldChampions`, `HeavyCcChampions`, `SuppressionChampions`
(por Key de ddragon), `DamageProfileOverrides`, `ArchetypeOverrides`,
`CleanseKeywords`, `ShieldBreakerKeywords` (es+en), umbrales
(`ArmorStackThreshold`, `MrStackThreshold`, `SustainThreshold`,
`SkewedDamageShare`), `MaxRecommendations`, y `ArchetypeTagWeights` opcional.

## Manejo de errores

- Catálogo no cargado → el asesor no emite nada (igual que v1).
- Campeón no resuelto → cae a Mixed/AdFighter genérico; nunca lanza.
- Datos parciales del Live Client (scores en 0, items vacíos) → el motor degrada a
  recomendación por arquetipo puro.

## Testing

- Catálogo de prueba realista (~15 campeones con `info`, ~30 items con
  stats/from/depth) en el proyecto de tests.
- Escenarios: clasificación del profiler y overrides; split de amenaza ponderado;
  detección de stacking; anti-heal acorde al perfil; penetración; Zhonya/Ángel vs.
  burst; Mercurial vs. supresión; elección de botas; paso de componente con oro
  parcial; exclusión de poseídos y de segundo GW; resolución por rawChampionName.
- Los tests existentes de `ItemRecommendationRule` se adaptan a las claves nuevas.

## Fuera de alcance (v2)

- Winrates/meta externos (op.gg u otros): sin scraping ni dependencias frágiles.
- Runas, hechizos de invocador, orden de habilidades.
- Overlay in-game.

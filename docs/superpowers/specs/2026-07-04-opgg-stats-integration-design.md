# OP.GG Stats Integration — Design

**Date:** 2026-07-04
**Status:** Approved by user

## Problem

The fuzzy engine scores items from archetype fit plus situational counters, but it cannot
know champion-identity facts that only aggregated match data reveals: Jayce always builds
mana items (Manamune), certain champions have signature cores, etc. The champion's resource
type (`partype`) is also ignored, so mana items can be suggested to manaless champions.

## Solution overview

Two complementary layers:

1. **Statistical prior (online):** per champion+role+patch build statistics from the OP.GG
   MCP server, cached locally, fed into the existing fuzzy engine as one more additive
   fuzzy rule. Also powers a new runes/skill-order panel with an "Apply runes" LCU button.
2. **`partype` rule (offline, always on):** Data Dragon's `partype` field gates mana-item
   weights deterministically — no network needed.

If the stats layer fails (network down, endpoint changed), the app behaves exactly as
today plus the `partype` rule. Stats are an enhancement, never a dependency.

## Components

### `ParadoxLoLCompanion.Core/Stats/`

- **`OpggMcpClient`** — speaks MCP (JSON-RPC over streamable HTTP) to
  `https://mcp-api.op.gg/mcp`: `initialize` handshake, then `tools/call` of
  `lol_get_champion_analysis`. 5 s timeout, no auth. Parses the response into our models.
- **`ChampionBuildStats`** — model for one champion+role+patch:
  - `ItemPriors`: `itemId → (pickRate, winRate)`
  - boots and starter priors
  - rune pages (perk ids: keystone, primary/secondary trees, shards)
  - skill order and summoner spells
- **`StatsCache`** — JSON files at `%LocalAppData%\ParadoxLoLCompanion\stats\{patch}\{champKey}-{role}.json`.
  Invalidated only by patch change (patch version comes from `DataDragonClient`).
- **`StatsProvider`** — orchestrates: cache-first, async fetch on miss, returns `null` on
  any failure. Single entry point: `Task<ChampionBuildStats?> GetAsync(champion, role, mapNumber)`.

### Role resolution

`Player.Position` from the Live Client API (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY). When empty
(ARAM, normals), request the champion's main role. If the OP.GG tool supports an ARAM
mode/queue parameter, use it for map 12; otherwise fall back to ranked data.

## Mixing rule (approved: additive fuzzy rule)

In `ItemAdvisor.ScoreItem`, following the existing magnitude × degree pattern:

```csharp
// μ_core ∈ [0,1] — fuzzy grade of pick rate, lightly modulated by win rate
if (stats?.ItemPrior(item.Id) is { } prior && prior.Mu > MuGate)
{
    offense += StatCoreMag * prior.Mu;   // StatCoreMag ≈ 2.5
    reasons.Add($"core in {prior.PickRate:P0} of {championName} builds");
}
```

- `μ_core = Fuzzy.Grade(pickRate, 0.10, 0.50) × (0.75 + 0.5 × Fuzzy.Grade(winRate, 0.48, 0.54))`:
  pick rate drives the ramp; win rate scales it in [0.75, 1.25] so a popular-but-losing
  item is dampened and a popular-and-winning item is boosted. Exact breakpoints are
  calibration constants, tuned in tests.
- `Advise()` gains an optional `ChampionBuildStats? stats` parameter; the ViewModel fetches
  it asynchronously and passes it in. `stats == null` → behavior identical to today.
- Boots and starter selection use the prior as a tiebreaker.
- Situational counter magnitudes are untouched: a needed counter (QSS vs suppression) can
  still outrank the statistically popular item.

Rejected alternatives: multiplier on the core fit term (crushes unpopular-but-needed
counters, breaks current calibration); dual-ranking blend (two normalization regimes,
opaque reasons).

## `partype` rule

- Parse `partype` (and base `mp`) from `champion.json` into `StaticChampion`.
- If the champion's resource is not Mana (Energy/Fury/None/…), `Mana`/`ManaRegen` tag
  weights become 0 in the fit computation — manaless champions never see mana items.
- This is the negative complement of the statistical prior (which covers the positive
  side: Jayce → Manamune).

## Runes & skills UI + LCU apply

- New panel in `MainWindow`: recommended rune page (keystone + both trees + shards, names
  and icons from Data Dragon `runesReforged.json`), skill order (e.g. `Q > E > W`), and
  summoner spells.
- **Apply runes** button: new `LcuConnector` method that
  1. `GET /lol-perks/v1/pages`, deletes any previous `"ParadoxLoLCompanion: {champ}"` page,
  2. `POST /lol-perks/v1/pages` with the recommended perks,
  3. `PUT /lol-perks/v1/currentpage` to select it.
  Failures surface as a non-fatal UI notice.

## Data flow

Champ select or live game detected → champion + role resolved → `StatsProvider.GetAsync`
→ ViewModel holds `ChampionBuildStats?` → `ItemAdvisor.Advise(state, forcedArchetype, stats)`
→ recommendations + runes panel. Patch rollover naturally refreshes the cache.

## Error handling

- Network/endpoint failure → log, return `null`, engine runs without stats.
- Corrupt cache file → delete and re-fetch.
- LCU rune-page writes → non-fatal notice on error; never block the advice flow.

## Main risk & mitigation

The `lol_get_champion_analysis` response format is undocumented. Implementation starts
with a **spike**: one real call recorded as a fixture; the parser is written against that
fixture (which doubles as its test). If the tool lacks usable rune/skill ids, the item
prior still ships and the runes panel waits for an alternative source.

## Testing

- Unit tests with injected fake stats: prior rule scores and emits reasons; counters still
  win when threat is clear; `null` stats → identical output to today.
- `partype` gating: manaless champion never scores mana items.
- `StatsCache` round-trip and patch invalidation.
- MCP response parser against the recorded fixture.
- LCU rune-page payload builder against known perk ids.

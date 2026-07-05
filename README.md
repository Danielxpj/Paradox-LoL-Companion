# LoL Advisor

Desktop tool (C# / WPF) that **reads League of Legends' local APIs live** and shows a
*Data Studio*-style dashboard, with a full **item advisor** as the main feature plus a
layer of general advice.

## What it does

- **Live Client Data API** (`https://127.0.0.1:2999`, no auth): reads `allgamedata` every ~1s
  → gold, CS, CS/min, KDA, level, game time, player table and events.
- **LCU API** (champ select): reads the champion select session (picks/bans per team).
- **Bench advisor (ARAM)**: in champ select it scores your team composition (physical/magic
  damage mix, frontline, marksman, support, crowd control) and tells you which discarded
  champion on the bench balances the team best — or to keep your current pick — with
  reasons ("adds magic damage (your team is 100% physical)", "adds a tank/frontline").
- **Item advisor (v2)** — the heart of the tool:
  - **Enemy team x-ray**: physical/magic damage split **weighted by how fed each enemy
    is** (not just the draft), healing/sustain, purchased resistances (sums the real
    stats of their items), assassin burst, suppression, heavy CC and shields.
  - **Your profile**: damage type (from Data Dragon data, with configurable overrides)
    plus a build archetype (marksman, mage, assassin, fighter, tank, support).
  - **Build detection from your inventory**: the same champion can go different builds
    (tank Garen vs damage Garen, support Janna vs AP Janna). Every item you actually own
    "votes" (weighted by its gold cost) for the archetype that best explains it; the
    champion's default acts as a configurable prior. The detection is recomputed every
    tick from your current inventory, so selling everything and starting another build
    switches the recommendations automatically. The banner announces it: "Build detected
    from your items: tank".
  - **Top-3 items with reasons**: every recommendation explains itself ("cuts the healing
    of Warwick (6/1/7)", "enemies already bought 160 armor", "to survive the burst from
    Zed"). Anti-heal matched to your build, penetration against stacked resistances,
    defensive hybrids (Zhonya/GA/Maw/Banshee) against burst, Mercurial against
    suppression, shield breakers.
  - **Purchase plan**: if you can't afford the target item, it tells you **which
    component to buy right now** with your current gold (walks the real build tree).
  - **Ownership that understands upgrades**: items you own — including transformed or
    Masterwork upgrades whose id differs from the base item — are never recommended
    again (the whole build tree counts as owned).
  - **Boots for the game you're in**: Mercury's vs CC/magic damage, Steelcaps vs physical
    auto-attackers, or your archetype's standard boots.
- **ARAM-aware**: on the Howling Abyss (map 12) the advisor uses the **ARAM item pool**,
  triggers anti-heal earlier (constant fights), favors immediately-affordable buys
  (buy-on-respawn economy), and Rift-only advice (CS/min, Dragon/Baron timers) shuts off.
- **ARAM: Mayhem support**: the mode is detected via the LCU queue (2400). Since Riot does
  not expose the offered augment choices through any local API, the advisor focuses on
  what *is* knowable: an **augment pick tracker** (picks unlock at start and levels
  7/11/15, and can only be taken while dead — it tells you when a pick is available and
  flags the window while you're dead) plus **guidance tuned to your archetype and the
  live enemy threat** ("as a mage prioritize AP/haste augments", "enemy damage is 89%
  physical — defensive augments against physical are high value", anti-heal/survival hints).
- **General advice**: objective timers (Dragon/Baron), gold milestones, CS/min vs a reference.
- **Raw data console** (bottom): stream of everything being read; pause / clear / save.
- **Replay mode**: plays a recorded snapshot so you can see everything working **without LoL
  open**. A scenario selector next to the checkbox switches between a **Rift** game and an
  **ARAM: Mayhem** game (augment tracker demo), and the Champ Select tab shows a sample
  ARAM bench to demo the bench advisor.

All item and champion names come from Data Dragon in **English** (en_US), regardless of
your game client language (players are matched via locale-independent ids).

## Structure

```
src/
  ParadoxLoLCompanion.Core/   models, parser, connectors (Live + LCU), item advisor, rules  (no UI, testable)
    Items/           ChampionProfiler · ThreatAnalyzer · BuildPathPlanner · ItemAdvisor
    Draft/           TeamBalanceAdvisor (ARAM bench swaps)
    DataDragon/      static catalog (champions + items with stats, build tree, per-map pools)
  ParadoxLoLCompanion.App/    WPF (MVVM), dashboard, "Item advisor" panel, console
tests/
  ParadoxLoLCompanion.Tests/  xUnit: parsing, item engine (99 tests), lockfile, champ select
docs/superpowers/specs/  design documents (v1 and item advisor v2)
```

## Requirements

- .NET 10 SDK (with the WindowsDesktop / WPF runtime). Windows.

## How to run

```powershell
dotnet run --project src/ParadoxLoLCompanion.App
```

- Without LoL open you'll see **"waiting for game"**. Check **"Replay mode"** (top right)
  to see the dashboard and item advisor populated with sample data.
- With a game in progress, the Live Client API starts responding and the dashboard fills
  in on its own. Champ select is detected automatically.
- Static data (Data Dragon) downloads once and is cached per version + language; the app
  notifies you when a new patch is available.

## Configuration

`advisor-config.json` (next to the app, with an editable copy at
`%LocalAppData%\ParadoxLoLCompanion\advisor-config.json`) exposes all the advisor's knowledge:
lists of healer / suppression / heavy-CC / shield champions, per-champion damage-profile
and archetype overrides, threat-analysis thresholds, ARAM tuning, and even the per-tag
weights of each archetype. All adjustable without recompiling.

## Tests

```powershell
dotnet test
```

Includes a smoke test against the **real** cached catalog (validates the assumptions
about Data Dragon tags and descriptions on your machine).

## Notes / limitations

- Objective timers are **estimates** using patch defaults (configurable).
- Champion classification is data-driven (Data Dragon tags + `info`) with overrides for
  the misleading cases; fix any misclassification by editing the config — no recompile.
- The advisor doesn't use external winrates (no scraping): it reasons from live game data.
- See `docs/superpowers/specs/` for the design docs and roadmap.

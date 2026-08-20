# Item Advisor v4 — the meta build as the spine

**Date:** 2026-08-20
**Status:** Implemented

## Problem

The user's report, verbatim: *"the fuzzy logic is sometimes stupid, for example never offers
jayce tear of the goddess and never offerts tanks heartsteel, i think we failed, and we need
to use the meta as main offerings"*.

Both examples are real, and both were reproduced against the real Data Dragon catalog
(16.16.1) and the real OP.GG cache on the developer's machine.

## Why the v3 engine failed

Diagnosed empirically — every number below was computed from the shipped constants against
the 14 cached OP.GG files, not estimated.

1. **The statistical prior was worth zero, not "a little".** For Jayce ARAM the cached core
   set is `{PickRate 0.08, Play 430, WinRate 0.451}`. Substituting into `ItemAdvisor.cs:754-771`:
   `pickMu = 0.6 + 0.4·Ramp(0.08, 0.05, 0.30) = 0.648`; `play = Ramp(430, 300, 2500) = 0.059`;
   `win = 0.75` (the win rate is below `StatWinFoot`, so the ramp clamps to 0). That gives
   `mu = 0.0287`, which fails the `mu > MuGate (0.05)` guard — so no bonus *and* no reason
   were emitted at all. Across the 14 cached champions the prior was exactly 0.000 for three
   of them (Jayce, Amumu at 159 games, Trundle at 137).

2. **`Play`'s confidence ramp was tuned for ranked sample sizes.** `StatPlayFoot/Shoulder =
   300/2500` against an ARAM core-`Play` distribution of min 137 / median 1798. `CoreItems.Play`
   is a *combo* count, not a champion sample size.

3. **The pick-rate ramp compared incompatible units.** `CoreItems.PickRate` is the probability
   of a whole 3–4 item combo (0.06–0.20, and `r(pickRate, setSize) = −0.647` — it mostly measures
   how many items are in the set). `FourthItems.PickRate` is a single-item rate (0.11–0.31).
   They shared one ramp.

4. **The real cause, and the reason no retune could work: `fit` is tag-presence, not value.**
   `ItemAdvisor.cs:611` computes `fit = item.Tags.Sum(t => weights[t])` and `ItemAdvisor.cs:618`
   makes `3·sqrt(fit)·(0.8+0.4·eff)` the base of the score. Over the 22 tank-relevant ARAM
   items, the Spearman correlation between tag-fit rank and gold-weighted stat-magnitude rank
   is **−0.257** — mildly *inverted*. Heartsteel concentrates its whole budget in one stat
   (900 HP, tags `[Health, HealthRegen]`, fit 3.5) and ranked #20 of 22 on tag-fit while
   ranking #2 of 22 on actual magnitude. Locket sprinkles six small stats, scores fit 8.5, and
   wins the slot. The fit spread alone is worth `3·(√8.5 − √3.5) = 3.13` points — larger than
   the entire gap between Heartsteel and the third recommendation slot.

   Counterfactuals confirmed no additive fix exists: raising `StatCoreMag` from 2.5 to 10.0
   left Heartsteel at #4, and replacing the prior with a flat **+8** membership bonus still
   produced a top-3 of `[Unending Despair, Thornmail, Winter's Approach]` — Thornmail beat the
   meta core because it is a `FourthItems` entry whose prior stacked on a higher tag-fit.

   **Conclusion: the meta has to seed the ranking, not be added to it.**

5. **Two component ids are unreachable, but that was the smaller half.** `DataDragonCatalog.cs:60-65`
   builds the candidate pool with `!BuildsIntoSomething && GoldTotal >= 1100 && From.Count > 0`,
   which excludes Tear (3070, 400g) and Serrated Dirk (3134, 1000g). But 42 of the 46 cached
   meta core ids *do* reach the pool, and `ItemEvolutions` already rescues the two
   non-purchasable transforms (Muramana 3042 → Manamune 3004, Fimbulwinter 3121 → Winter's
   Approach 3119). Tear was never a missing *slot*; it was a missing *immediate purchase*.

6. **The buy bar actively recommended against stacking Tear.** `BuildPathPlanner.BestAffordable`
   picks the most expensive affordable component. Walking Manamune's real recipe: at 400–1049
   gold it returns Tear, but at ≥1050 it returns Caulfield's Warhammer. ARAM starts at 1400
   gold, so in practice the card said "buy Caulfield's" — exactly backwards for an item whose
   value is time-stacked.

7. **Legacy mode-variant items were occupying real slots.** `2524 Bandlepipes` and
   `773107 Runic Bulwark` pass every existing gate, have tag-fit in the top 4 of 22 and
   magnitude-fit in the bottom 4, and were the actual #2/#3 recommendations for several tanks.

## Architecture

One new file plus a seeding stage in the existing advisor. The fuzzy engine, `ThreatAnalyzer`,
`Fuzzy` and `ScoreItem` are unchanged in substance.

### `Core/Items/MetaSpine.cs` (new, pure, no state)

- `MetaSpineSlot(StaticItem Item, int Rank, string SlotLabel, double PickRate, double WinRate, int Play)`
- `MetaSpine.Build(data, config, stats, mapNumber, ownedTree?) → IReadOnlyList<MetaSpineSlot>`

Returns the champion's real build as an ordered list of **buyable** targets, with owned slots
already removed so `Rank 0` is always the next meta purchase. Empty when `stats is null`.

### `ItemAdvisor.Advise`

Builds the spine, then adds a rank-decreasing bonus in the ranking projection. Everything
else — the pool gates, the dedupe pass, `Priority`, `Category`, the affordability nudge,
hysteresis — is untouched, so a spine item still has to be legal to be shown.

## The spine

1. Resolve each raw OP.GG id: `ItemEvolutions` bridge first (3042→3004, 3121→3119, 3040→3003),
   then a map-aware catalog lookup (`OnAram` vs `OnSummonersRift` — ids differ per map;
   Hubris is 126697 on ARAM and 6697 on the Rift), then drop non-purchasable, consumable,
   boots, and excluded-tag items.
2. Order: `CoreItems` **in OP.GG's listed order** (that order is the build order and is the
   signal), then `FourthItems`, `FifthItems`, `SixthItems`, each list internally by pick rate
   descending. Pick rate is never compared *across* lists — the units differ (see failure 3).
3. **Absorb components.** If a resolved item's build tree is contained in another resolved
   item, drop the component. Nautilus's core lists Tear *and* Fimbulwinter, and Winter's
   Approach builds from Tear — so the slot is Winter's Approach, and Tear surfaces as its
   immediate purchase instead.
4. Drop any component nothing absorbs: a 400g piece is an opening buy, not one of three cards.
5. Drop owned items (build tree included), so ranks compact as the game goes on.
6. Dedupe by id and by **normalized** name — `NormalizedName` strips a leading "The ", because
   the ARAM catalog contains 33 duplicate-name pairs and one of them ("Black Cleaver" vs
   "The Black Cleaver") defeats the existing raw-name dedupe.

## Reorder and eviction

`ScoreItem` now also returns its clean pre-nudge `situational` component. In the ranking stage:

```
spine member       → bonus = SpineBaseMag + SpineSlotMag · (SpineDepth − rank)
severe counter     → bonus = SpineBaseMag + SpineSlotMag · (SpineDepth − (MaxRecommendations−1))
everything else    → 0
```

- `SpineBaseMag = 20.0` sits above any achievable raw score (~12), so the meta build owns the
  list. `SpineSlotMag = 4.0` is the value of one rank: the fuzzy score still lives inside the
  raw score, so a situational advantage larger than 4.0 **reorders** the spine — which is what
  "fuzzy re-orders the meta" means concretely.
- **Eviction** (`EvictionSituationalGate = 4.0`): a non-meta item whose situational
  contribution clears the gate is promoted to the tier of the *last displayed* slot, where it
  competes on merit with the weakest meta item rather than being appended. The list length is
  unchanged. 4.0 requires a genuine threat — cleansing a suppression is `CleanseMag = 3.0`, a
  full defensive wall is capped at `DefenseCap = 4.0`.
- Expressing the preference as a score rather than a list operation keeps `Priority =
  score/topScore` monotonically non-increasing, which `ItemAdvisorTests.cs:526-545` asserts.
- Spine members bypass the `score > 0` pool drop: a meta item with zero archetype fit is
  exactly the case this design exists to serve.

## Starter and sell

- **Starter:** `StarterFor` already resolved the OP.GG starter set correctly; the UI was
  throwing most of it away. `MainViewModel` now renders every item in `StarterAdvice.Items`
  with the set total, so Jayce reads "Tear of the Goddess + Serrated Dirk (1400)". Starters
  remain ARAM-only — `Starter_Suppressed_WhenOwningItems_OrLate_OrOnRift` pins that on purpose.
- **Sell:** an item the meta build wants is never suggested for sale, checked against the
  spine built *without* the owned filter. The archetype fit table is precisely what misjudges
  meta items, so it must not be the thing that decides to sell one.

## Buy path

`BuildPathPlanner.Plan` takes an optional `stackingComponentIds`. When the target is
unaffordable and its tree contains an unowned stacking component, that component is returned
as `NextComponent` regardless of the most-expensive rule. `ItemsConfig.StackingComponentIds`
defaults to `{3070, 773070}` (Tear and its ARAM twin). Only the item-plan call site passes it;
boots and starter planning are unaffected.

## Fallback

`stats is null` → the spine is empty → byte-identical v3 behaviour. This matters: 130 of the
143 `Advise` call sites in the test suite pass no stats, so ~90% of the assertions exercise
the fallback path, and it stays exercised in production for champions with no cache entry.

## Contract changes

None breaking. `ItemRecommendation`'s positional signature is untouched. The only user-visible
change is the reason string: a meta slot now leads with `"core build for {champ} — 8% of
games, 55% WR"` (or `"4th item for {champ} — …"`), replacing the weaker `"bought in 8% of
{champ} builds"`, which in small ARAM samples was never emitted at all.

## Calibration

Left deliberately alone: `StatCoreMag`, `StatPick*`, `StatPlay*`, `StatWin*` and `MuGate` still
govern the additive prior on the fallback path. With the spine seeding the list, the prior is
no longer load-bearing, and retuning it would have changed `BootsFor`, which shares
`StatPlayFoot` and is out of scope.

## Verification

376 tests green (up from 373). `MetaSpineRealDataTests` closes the loop against real data:
Nautilus is offered Heartsteel with a meta reason; Jayce is offered Manamune with Tear as the
immediate buy; and for 12 cached champions the spine's first slot appears in the list.

Observed before → after on the real cache:

| Champion | v3 | v4 |
|---|---|---|
| Nautilus | Bandlepipes, Jak'Sho, Runic Bulwark | Heartsteel, Winter's Approach, Unending Despair |
| Jayce | Experimental Hexplate, Trinity Force, Bandlepipes | Hubris, Manamune *(buy → Tear)*, The Collector |
| Amumu | Bandlepipes, Jak'Sho, Runic Bulwark | Sunfire Aegis, Winter's Approach, Abyssal Mask |
| Veigar | Iceborn Gauntlet, Banner of Command, Zhonya's | Luden's Echo, Shadowflame, Rabadon's Deathcap |

Amumu's v3 list was byte-identical to that of a champion with no cached stats at all — the
clearest single symptom of the prior collapsing to zero.

## Out of scope

- **Boots** stay a `BootsFor` tiebreaker, per the user's decision. `BootsFor` uses a crisp
  `Play >= StatPlayFoot` gate that ARAM boots data clears comfortably (Jayce 3916 games).
- **`fit` itself.** Tag-presence remains the fallback base term. Rebuilding it from stat
  magnitudes is the obvious next step, but the spine removes its authority in the case that
  matters, and touching it would move ~90 assertions at once.
- **Mana tag weights.** `AdFighter`/`AdAssassin`/`Marksman` have no `Mana` entry, so a Tear
  item scores zero fit for every AD champion — worth +1.11 points to Manamune on Jayce. The
  spine makes it non-blocking; fixing it changes fit for every AD item and belongs in its own
  change.
- **The pure-HP defensive branch** is paid ~1/2.6 of what the armor branch is paid for the same
  `DefenseMag`, and its `MixedDamage` escape hatch measured 0.000 on a real 74/26 comp. It only
  affects reorder *within* the spine now, so it is deferred.

The app records replay snapshots in its local game journals at
`%LocalAppData%\ParadoxLoLCompanion\journal`. Each sampled tick includes the full
game state, configuration, champion build statistics, manual override, previous
recommendation IDs, catalog version, and score breakdowns. Older journal entries
remain readable but do not have enough inputs to replay.

Run from the repository root, using the same catalog version and locale files
used when the match was recorded:

```powershell
dotnet run --project tools/RecommendationReplay -- "C:\path\match.jsonl" "16.13.1" "C:\path\ddragon\16.13.1\en_US"
```

The directory must contain `champion.json` and `item.json`, plus
`championFull.json` if it was available during recording. A different catalog
version is rejected. Keep the original files to reproduce catalog-derived flags.

An optional fourth argument loads an `ItemsConfig` JSON object to compare a
candidate configuration. For example, `SevereAntiHealCounterGate: 4` restores the
general four-point gate for off-meta anti-heal items; the default of 3 only applies
when sustain is at least 0.9. Omitted JSON properties use current defaults, so use
a complete configuration when isolating a single setting.

Standard output contains one JSON comparison per snapshot, including item IDs,
score changes, score channels, reasons and purchase availability. Standard error
reports the number of evaluated snapshots and changed rankings. The comparison
uses the recorded previous recommendations for each frame, isolating each
decision rather than simulating purchases a player might have made differently.

The regression suite checks severe-healing and low-healing scenarios, crit
upgrades, urgent purchases, and exact journal replay. These checks measure
decision behavior; they do not establish a causal improvement in win rate.

## Modes (v6)

```powershell
# One journal, frame by frame (the original behaviour; the mode word is optional for a .jsonl path):
dotnet run --project tools/RecommendationReplay -- replay "C:\path\match.jsonl" "16.13.1" "C:\path\ddragon\16.13.1\en_US" [candidate.json]

# Calibration sweep over every journal in a directory:
dotnet run --project tools/RecommendationReplay -- sweep "%LocalAppData%\ParadoxLoLCompanion\journal" "16.13.1" "C:\path\ddragon\16.13.1\en_US" sweep.json

# Did the early threat materialise? (early snapshot = last tick at or before 300 s):
dotnet run --project tools/RecommendationReplay -- threats "%LocalAppData%\ParadoxLoLCompanion\journal" "16.13.1" "C:\path\ddragon\16.13.1\en_US" [300]
```

`sweep.json` lists values per `ItemsConfig` property; each value becomes one variant on
top of the recorded configuration (one-dimensional sweep, `baseline` first):

```json
{ "spineSituationalWeight": [0.5, 1.0, 1.5], "coverageOverlapPenalty": [0, 0.6, 1.0], "projectionWeight": [0.4, 0.6, 0.8] }
```

For every replayable tick the sweep looks up the next **completed** item the player
bought after that tick (components, boots and consumables are skipped) and counts the
tick as agreed when the replayed top-3 contains it. The table reports games, decisions,
agreed and agreement@3 per variant. This measures agreement with the player, not causal
effect on winning; a player who buys badly lowers the agreement of a good configuration.

`threats` recomputes the fuzzy degrees on the early snapshot (blind, and `+proj` with the
enemy build projection recorded in the journal) and on the last snapshot, then reports
per signal the mean absolute error, false alarms (announced at μ ≥ 0.5 and never
happened) and misses (happened, never announced). Journals recorded before v6 have no
enemy stats, so `+proj` rows equal the blind ones for them.

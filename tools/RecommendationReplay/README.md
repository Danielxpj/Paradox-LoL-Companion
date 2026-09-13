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

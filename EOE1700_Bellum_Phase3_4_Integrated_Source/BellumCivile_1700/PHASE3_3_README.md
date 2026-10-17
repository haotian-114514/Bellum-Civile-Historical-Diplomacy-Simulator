# Phase 3.3 — Evidence-qualified event context (conservative increment)

## Changes
- `EOE1700/EoeHistoricalWorldObserverBehavior.cs` now stores `EOE1700_Chronicle_Context_v2` alongside preserved v1 chronicle save keys.
- Each newly observed event gains a structured evidence record keyed by the existing ten-digit sequence.
- Recorded fields: schema version, sequence, observation day, domain, event kind, affected subject, change detail, old/new state, evidence `observation`, cause `cause_unknown`, and `survey_day_not_exact_time`.
- Leadership transitions distinguish ruler-only, ruling-clan-only and both. A change alone is NOT labelled election, murder, usurpation or dynastic coup.
- The v1 1,000-event retention cap also prunes the matching v2 record.
- Existing saves and v1 records are not rewritten or retroactively annotated.
- No new notifications are subscribed yet: this phase adds quality of evidence, not precise realtime detection.
- No external writes, no change to faction IDs, no war/peace action execution.

## Build and validation
```
$env:GameFolder = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
dotnet restore BellumCivile.csproj -p:GameFolder="$env:GameFolder"
dotnet build BellumCivile.csproj -c Release --no-restore -p:GameFolder="$env:GameFolder"
```
Test on disposable saves: baseline should create no event; owner, war, ruler/clan changes generate correct v1 records and v2 context. Save/reload must not duplicate events. Existing Phase 3.2 saves must load. This archive has not been built against your local TaleWorlds DLLs.

## Next
Attach verified engine event/action metadata in an additive layer after confirming signatures in game version; store *explicit* cause only when the action source proves it.

# EOE1700 Bellum Civile Phase 3.3 — Event callbacks with evidence context

This is the complete Bellum Civile source tree based on Phase 3.2. Changes are confined to `EOE1700/EoeHistoricalWorldObserverBehavior.cs`. Compile on your Windows Bannerlord installation before deploying. This archive is NOT a compiled mod.

## Changes

- Hooks native `WarDeclared`, `MakePeace`, `OnSettlementOwnerChangedEvent`, and `HeroKilledEvent` campaign events in addition to the Phase 3.2 daily survey.
- Records immediate callbacks only if an existing subject was baselined; avoids inventing initial wars/transfers.
- Immediately updates the previous state after an observed callback, so the next daily survey does not double count it.
- Appends evidence fields to the existing journal row: `sequence|day|event-kind|subject|old|new|source|detail|exact_campaign_day`.
- Preserves the Phase 3.2 saved dictionaries and keys; old six-field records remain parseable by readers that accept a minimum of six fields.
- Logs a leader's death report independently from a leadership transition (the transition is still verified by survey).
- Does not infer a political reason from the action detail enum.
- No diplomacy actions or AI Influence file writes.

## Limitations

- Bannerlord event callbacks are not guaranteed for direct state changes made by other mods. Daily surveys remain authoritative fallback.
- Event callbacks with no previous baseline are intentionally not chronicled.
- Observed settlement event owner uses the callback newOwner's clan/kingdom, not assumed peace-treaty motivation.
- Event detail represents Bannerlord's action-detail enum, not a trustworthy natural-language motive.
- The journal stores up to 1,000 records. It is for diagnostics; export to AI Influence comes later.
- Leader death log requires the affected hero still to be the kingdom leader when the callback executes; otherwise the daily leadership transition can still be observed.

## Build
```
$env:GameFolder = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
dotnet restore BellumCivile.csproj -p:GameFolder="$env:GameFolder"
dotnet build BellumCivile.csproj -c Release --no-restore -p:GameFolder="$env:GameFolder"
```

## Runtime checklist

1. Back up previous DLL; use a separate disposable save.
2. Load a save with a baseline. Trigger an actual war or peace. Check `[EOE1700 Chronicle]` has `WarDeclared` / `MakePeace` evidence (if the game fires the callback).
3. Advance one day and check no duplicate of that same transition.
4. Trigger settlement ownership change and verify original/new kingdom IDs.
5. Save, reload and verify sequence continuity and no duplicate entries.
6. Confirm original faction names, succession, and Bellum diplomacy behavior unchanged.

**Unverified:** This environment cannot run the TaleWorlds game DLL build; run the commands above on your system. If CS errors arise, provide full compiler diagnostics so the callback signatures can be adjusted.

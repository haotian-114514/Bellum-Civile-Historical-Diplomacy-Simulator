# Phase 3.2 — Persistent diplomatic event chronicle

Source baseline: Phase 3.1 (unchanged Bellum political decisions).

## New behavior
- Adds a bounded (last 1000) append-only **observation** journal to EoeHistoricalWorldObserverBehavior.
- Logs `[EOE1700 Chronicle] sequence|day|event type|subject|before|after` on each observed change.
- Uses existing `Dictionary<string,string>` save registration plus an integer sequence number.
- Journal records: WAR_STARTED_OBSERVED / WAR_ENDED_OBSERVED, SETTLEMENT_OWNER_CHANGED_OBSERVED, KINGDOM_LEADERSHIP_CHANGED_OBSERVED.
- Existing snapshot keys and latest-change behavior remain unchanged. First snapshots produce NO event.
- Changes are only published to the internal Bellum log and persisted in campaign save, NOT written to AI Influence files.
- A survey failure no longer advances last-successful-survey-day (enables retry).

## Important limitations
- Once-a-day sampling can miss short-lived changes. We must integrate Bannerlord hooks after verifying exact game DLL event signatures.
- Events are not causally classified: war ended != verified peace terms; clan change != confirmed deposition; territory transferred != conquest.
- If the user reverts a save, the restored in-save chronicle is authoritative; no external files are written, avoiding stale AI Influence news.
- Retention is 1000 newest entries; long-term historical aggregation comes later.
- No migration is needed for Phase 3.1 saves. Newly introduced journal fields default empty.

## Windows compile
```powershell
$env:GameFolder = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
dotnet restore BellumCivile.csproj -p:GameFolder="$env:GameFolder"
dotnet build BellumCivile.csproj -c Release --no-restore -p:GameFolder="$env:GameFolder"
```

## Runtime validation on a disposable save
1. Boot with Phase 2.1 XML; confirm no phantom Chronicle entries on baseline.
2. Trigger one real war change, advance a day; look for ONE Chronicle entry.
3. Save/reload, advance a day unchanged; verify no duplicates.
4. Verify an actual territory transfer logs the old/new faction StringIds.
5. Check other Bellum features, including peace and ordinary succession, continue to work.

This package is source only, not a built DLL; compilation against your local Bannerlord DLLs is required.

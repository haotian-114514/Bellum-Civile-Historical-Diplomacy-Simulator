# EOE1700 Bellum Civile — Phase 3.4 direct campaign notifications

Complete source based on the Phase 3.3 integrated source ZIP.

Changes in `EOE1700/EoeHistoricalWorldObserverBehavior.cs`:
* Adds direct listeners using signatures verified from the Bellum Civile source (`WarScoreBehavior`, `ControversyBehavior`).
* WarDeclared and MakePeace can update observed war state immediately, with event evidence and enum detail.
* Settlement owner changes can update observed ownership immediately, retaining transfer-detail enum.
* HeroKilledEvent adds a confirmed death entry, never a speculative assassination or deposition.
* Keeps the pre-existing v1 chronicle and v2-context save keys. Context now distinguishes direct event callbacks from daily observations.
* Daily survey remains the fallback for missed events and will not re-emit a state already observed via a direct callback.

Limitations:
* No explicit accession-event hook is implemented; subsequent ruling-clan changes remain daily-survey observations.
* Handler registration does not prove callbacks will fire in every EoE/Bellum combination.
* Event detail enum is NOT proof of political motivation, and all causes remain `cause_unknown`.
* A direct event that arrives before initial baseline establishes only a baseline for the affected state.
* Hero deaths are captured for heroes in the 22 recognized kingdoms; the chronicle caps at 1,000 entries.
* No files in AI Influence are written. This is a read-only observer, NOT a diplomacy executor.

Build (Windows with installed Bannerlord 1.4.8 references):
```
$env:GameFolder = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
dotnet restore BellumCivile.csproj -p:GameFolder="$env:GameFolder"
dotnet build BellumCivile.csproj -c Release --no-restore -p:GameFolder="$env:GameFolder"
```

Test on disposable campaign: record war start/peace, settlement transfer, death; verify immediate `[EOE1700 Chronicle]` messages and no duplicate next day; save/reload; verify prior 3.3 save opens. If compiler errors occur, report exact errors. Build and runtime NOT verified here (game DLLs absent).

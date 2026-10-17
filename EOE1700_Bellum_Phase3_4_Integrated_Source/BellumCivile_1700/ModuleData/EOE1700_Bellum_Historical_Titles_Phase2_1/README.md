# EOE1700 — Bellum Civile historical naming patches (Phase 2.1)

Prepared from user-uploaded `bellum_realm_names*.xml`, `bellum_feudal_titles*.xml`, title presets and `EriksEoE1701 v1.4.7.1 Free.zip`.

## Scope
- Overrides realm *display roots* and kingdom-specific *title styles* for 22 EoE faction IDs.
- **No changes** to native kingdom IDs, faction names inside EoE, Crown registration, clans, title de jure/de facto ownership, treaty actions or succession mechanics.
- Uses `TitleStyle kingdom=...` and `Rank tier=...`, as specified by Bellum's comments. Both Kingdom and Empire ranks are supplied because the actual legal rank must be checked in game.
- Government titles such as `Grand Pensionary` are cosmetic only; the Dutch Republic remains mechanically a Bellum sovereign until Phase 2 government rules are implemented.
- `prussia` initially uses `Elector`; the EoE file labels it Kingdom of Prussia although its StartingYear is 1701. Bellum does not dynamically upgrade titles with these static patches; future historical event code should handle it.

## Installation on a disposable test save
1. Back up your installed Bellum Civile `ModuleData/bellum_realm_names_patch.xml` and `ModuleData/bellum_feudal_titles_patch.xml` (if present).
2. Copy the two patch XMLs in this folder into `Modules/BellumCivile/ModuleData/` **only if the installed module name and patch load location match** (check your Vortex installed folder). Prefer a separate compatibility submodule later.
3. Restart Bannerlord. The patch comments say later patches in active module order win; disable other conflicting title patches during this isolated test.
4. Load a DISPOSABLE save, inspect France, Ottoman, Dutch, HRE and Prussia labels, then save/reload. If labels do not change, capture Bellum logs and the active MCM terminology preset.
5. Restore backups to roll back.

## Important limitations
- `nativeName` values come directly from EoE's `spkingdoms` XML. Different localized names can supersede these.
- Some titles need careful historical nuance: Heinsius was Grand Pensionary *of Holland*, not the sovereign of the Dutch Republic. The UI may prepend title to hero globally and use a formalized approximation.
- Both Kingdom and Empire style tiers were set to the same selected country-specific ruler term as a presentation-only fallback. This is not an endorsement of empire rank.
- Republican heir/consort text may still inherit generic templates. Fixing that requires separate title-rendering logic, not assumptions that XML modifies succession.
- Changes to game-loaded title styling on existing saves may be cached. Do not assume failure until restarting and checking Bellum's actual loader behavior.
- This is validated as **well-formed XML**, but has NOT been runtime tested under your installed mod stack.

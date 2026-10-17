# EOE1700 + Bellum Civile — Phase 2.1 v2 naming corrections

## What changed
- Exact 22 native realm identities from the `EOE1700_KingdomNames.csv` generated in the game with Bellum OFF. Realm nativeName guard and Realm Identity display root both use the exact EoE name, including `aserai=The Alawi Dynasty` and `vlandia=Kingdom of France`.
- Retains the previous title-style patch for other kingdoms; **replaces incorrect Alawi `Sultanate/Sultan` styles with EoE's `Kingdom/King`**. These styles are presentation only.
- Does not alter kingdom IDs, save data, political rules, or heraldry. Broken family banners are outside scope.

## How to install safely
1. Back up `Modules/BellumCivile/ModuleData/bellum_realm_names_patch.xml` and `bellum_feudal_titles_patch.xml` (adjust actual module path to your installation).
2. Replace those TWO files with their counterparts here. **Do not copy the CSV into ModuleData**; it is only the source record.
3. Fully restart Bannerlord; check Bellum's MCM **Realm Name Display** mode. Use **Native** to preserve exact EoE game names, or test **Realm Identity** with these exact roots. **Sovereign Title** assembles names from title rank and may add prefixes, so it is not suitable when exact starting names are required.
4. Confirm names in diplomacy, kingdom list, and encyclopedia on a disposable save; save/reload. Some displays may use title-generated text rather than the native name; report those separately rather than patching kingdom IDs.
5. Restore backups to uninstall.

## Future scope
- Some historical offices are not hereditary sovereign positions. `dutch` display `Grand Pensionary` is a cosmetic compromise; do not take it as a constitutional change.
- `prussia` currently uses `Elector` in the inherited style patch; date-appropriate rank changes will be addressed with campaign-event logic, not static XML.
- For maximum isolation, move these XML files to a separately loaded EoE compatibility module once packaging is verified.

**Validation:** generated from user CSV; XML parsed and checked for 22 unique IDs and no accidental change of the other realm identities. Runtime test still required.

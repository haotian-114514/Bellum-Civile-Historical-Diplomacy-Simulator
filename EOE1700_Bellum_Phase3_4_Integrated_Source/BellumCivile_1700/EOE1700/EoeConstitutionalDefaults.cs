// EOE1700 Phase 2.2C: conservative initial realm constitutions.
// This is only applied on FIRST creation of a permanent realm's saved law group.
// It does not mutate any existing saved law selection and does not change clan inheritance directly.
using System;

namespace BellumCivile.EOE1700
{
    internal static class EoeConstitutionalDefaults
    {
        internal static bool TryGet(string kingdomId, out SuccessionLawSet laws, out int mandateYears)
        {
            laws = default(SuccessionLawSet);
            mandateYears = 0; // Lifetime, unless otherwise specified.
            EoeGovernmentPolicy profile;
            if (!EoeHistoricalGovernments.TryGet(kingdomId, out profile)) return false;
            // Temporary approximations: Bellum Civile's electorate is clans, not the
            // States General, Sejm, Imperial Electors, College of Cardinals, etc.
            // Those institution-specific voter rules are not implemented in 2.2C.
            switch (profile.Succession)
            {
                case EoeSuccessionKind.Republican:
                    laws = new SuccessionLawSet(GenderSuccessionLaw.MalePreference,
                        HouseSuccessionLaw.ElectiveSeniority);
                    mandateYears = 0; // Lifetime fallback: actual republican offices and terms need custom rules.
                    break;
                case EoeSuccessionKind.Elective:
                    laws = new SuccessionLawSet(GenderSuccessionLaw.MalePreference,
                        HouseSuccessionLaw.ElectiveSeniority);
                    mandateYears = 0;
                    break;
                case EoeSuccessionKind.Dynastic:
                case EoeSuccessionKind.Hereditary:
                    laws = new SuccessionLawSet(GenderSuccessionLaw.MalePreference,
                        HouseSuccessionLaw.Primogeniture);
                    break;
                default:
                    return false;
            }
            return true;
        }
    }
}

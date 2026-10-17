// EOE1700 Phase 2.2D: keep private noble estate inheritance separate
// from the election of an office/crown in elective and republican realms.
// No saved state or changes to existing realm-law records.
using TaleWorlds.CampaignSystem;

namespace BellumCivile.EOE1700
{
    internal static class EoeHouseInheritancePolicy
    {
        internal static bool TryGetHouseLaws(Clan clan, out SuccessionLawSet laws)
        {
            laws = default(SuccessionLawSet);
            if (clan == null || clan.Kingdom == null) return false;

            EoeGovernmentPolicy profile;
            if (!EoeHistoricalGovernments.TryGet(clan.Kingdom.StringId, out profile))
                return false;
            if (profile.Succession != EoeSuccessionKind.Elective
                && profile.Succession != EoeSuccessionKind.Republican)
                return false;

            // Only a first approximation. This applies to private family
            // holdings; the realm itself still elects through Bellum's code.
            // Preserve the culture's gender preference while ensuring the
            // family's property passes hereditarily, not by realm-wide ballot.
            var cultureLaws = SuccessionConfig.Instance.GetDefaultLaws(
                clan.Culture?.StringId, null);
            laws = new SuccessionLawSet(cultureLaws.GenderLaw,
                HouseSuccessionLaw.Primogeniture);
            return true;
        }
    }
}

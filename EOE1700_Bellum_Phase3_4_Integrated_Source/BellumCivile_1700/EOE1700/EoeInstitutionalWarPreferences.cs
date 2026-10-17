using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.EOE1700
{
    /// <summary>
    /// Experimental, bounded clan-level institutional caution in voluntary war votes.
    /// These are proxies (landed holdings and clan rank), not real legislative offices.
    /// No new actors, save records, or action executors are introduced.
    /// </summary>
    internal static class EoeInstitutionalWarPreferences
    {
        internal static float AdjustSupport(Kingdom kingdom, Clan voter, float baseSupport)
        {
            if (kingdom == null || voter == null || voter.Kingdom != kingdom
                || kingdom.RulingClan == Clan.PlayerClan)
                return baseSupport;

            float penalty = 0f;
            int towns = voter.Fiefs == null ? 0 : voter.Fiefs.Count(f => f != null && f.Settlement != null && f.Settlement.IsTown);
            int rankAboveThree = Math.Max(0, voter.Tier - 3);

            switch (kingdom.StringId)
            {
                case "dutch":
                case "venice":
                case "genoa":
                    // Town-holding elites: modest caution over trade disruption.
                    penalty = Math.Min(6f, 2f * towns);
                    break;
                case "britain":
                    // Rough war-finance constraint, not a simulation of MPs.
                    penalty = Math.Min(4f, 1.5f * towns);
                    break;
                case "poland":
                case "germany":
                    // Influential magnates/territorial princes seek stronger justification.
                    penalty = Math.Min(6f, 1.5f * rankAboveThree);
                    break;
                case "swiss":
                    penalty = Math.Min(4f, 1f * towns);
                    break;
                default:
                    return baseSupport;
            }

            return baseSupport - penalty;
        }
    }
}

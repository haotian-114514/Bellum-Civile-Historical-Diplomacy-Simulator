using System;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.EOE1700
{
    /// <summary>
    /// Phase 2.2E: narrow political-authorization rule for AI foreign-war votes.
    /// Does not pretend to implement the historical electoral bodies themselves.
    /// Player-controlled kingdoms and unknown factions retain the existing Bellum logic.
    /// </summary>
    internal static class EoeConstitutionalAuthority
    {
        internal static bool RequiresCouncilWarApproval(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.RulingClan == Clan.PlayerClan)
                return false;

            if (!EoeHistoricalGovernments.TryGet(kingdom.StringId, out EoeGovernmentPolicy government))
                return false;

            // Elective rulers and heads of republican governments do not have
            // the same unilateral foreign-war authority as hereditary monarchs.
            return government.Succession == EoeSuccessionKind.Republican
                || government.Succession == EoeSuccessionKind.Elective;
        }
    }
}

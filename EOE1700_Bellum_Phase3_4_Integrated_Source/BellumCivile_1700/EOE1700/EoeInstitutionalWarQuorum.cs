using System;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.EOE1700
{
    // Limited historical-constitution approximation; not simulated MPs or electors.
    internal static class EoeInstitutionalWarQuorum
    {
        internal static PoliticalInfluenceVoteTally Calculate(Kingdom kingdom, int yay, int nay, int capacity)
        {
            PoliticalInfluenceVoteTally original = PoliticalInfluenceVoteHelper.Calculate(yay, nay, capacity);
            if (kingdom == null || kingdom.RulingClan == Clan.PlayerClan || capacity <= 0)
                return original;
            float factor = GetMultiplier(kingdom.StringId);
            if (factor <= 1f)
                return original;
            int preferred = PoliticalInfluenceVoteHelper.RoundUpToVoteStep((int)Math.Ceiling(original.Quorum * factor));
            int quorum = Math.Max(original.Quorum, Math.Min(capacity, preferred));
            return new PoliticalInfluenceVoteTally(yay, nay, quorum);
        }

        private static float GetMultiplier(string id)
        {
            switch (id)
            {
                case "dutch": case "poland": return 1.30f;
                case "swiss": return 1.25f;
                case "germany": case "venice": case "genoa": return 1.20f;
                case "cossack": case "britain": return 1.15f;
                default: return 1f;
            }
        }
    }
}

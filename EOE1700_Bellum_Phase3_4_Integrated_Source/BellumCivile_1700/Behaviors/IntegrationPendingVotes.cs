using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class ExpulsionDeliberationBehavior
    {
        private Dictionary<string, string> _integrationVoteIds = new Dictionary<string, string>();

        internal string GetIntegrationVoteId(Kingdom realm, Clan target) => realm != null && target != null
            && _integrationVoteIds.TryGetValue(PendingKey(realm, target), out string id) ? id : null;

        internal IEnumerable<PendingVoteSnapshot> GetIntegrationPendingVotes(Kingdom realm)
        {
            string prefix = KingdomPrefix(realm);
            foreach (var pending in _pendingExpelDate.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var target = Clan.All.FirstOrDefault(c => c.StringId == pending.Key.Substring(prefix.Length));
                if (!BellumIntegrationBehavior.IsExpulsionTarget(realm, target)
                    || !_integrationVoteIds.TryGetValue(pending.Key, out string id)) continue;
                _pendingExpelProposer.TryGetValue(pending.Key, out string proposerId);
                yield return new PendingVoteSnapshot(id, PendingVoteKind.Expulsion, PendingVoteStage.Deliberation,
                    realm, Clan.All.FirstOrDefault(c => c.StringId == proposerId), target: target,
                    scheduledDay: pending.Value.ToDays);
            }
        }
    }

    public partial class FiefDeliberationBehavior
    {
        private Dictionary<string, string> _integrationVoteIds = new Dictionary<string, string>();

        internal string GetIntegrationVoteId(Kingdom realm, Settlement settlement) => realm != null && settlement != null
            && _integrationVoteIds.TryGetValue(PendingKey(realm, settlement), out string id) ? id : null;

        internal IEnumerable<PendingVoteSnapshot> GetIntegrationPendingVotes(Kingdom realm)
        {
            string prefix = KingdomPrefix(realm);
            foreach (var pending in _pendingFiefDate.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var settlement = Settlement.Find(pending.Key.Substring(prefix.Length));
                if (settlement?.MapFaction != realm || !_integrationVoteIds.TryGetValue(pending.Key, out string id)) continue;
                _pendingFiefProposer.TryGetValue(pending.Key, out string proposerId);
                _pendingFiefExclude.TryGetValue(pending.Key, out string excludedId);
                yield return CreateIntegrationVote(id, realm, settlement, ResolveClan(proposerId), ResolveClan(excludedId),
                    pending.Value.ToDays, PendingVoteStage.Deliberation);
            }
        }

        internal static PendingVoteSnapshot CreateIntegrationVote(string id, Kingdom realm, Settlement settlement,
            Clan proposer, Clan excluded, double? scheduledDay, PendingVoteStage stage)
        {
            var candidates = FiefNominationHelper.GetEligibleCandidates(realm, settlement, excluded);
            var behavior = Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>();
            var nominees = behavior?.GetRankedNominations(realm, settlement, candidates, 3)
                .Select(r => ResolveClan(r.CandidateId)) ?? Enumerable.Empty<Clan>();
            return new PendingVoteSnapshot(id, PendingVoteKind.FiefAllocation, stage, realm, proposer,
                settlement: settlement, scheduledDay: scheduledDay, candidates: candidates, nominees: nominees);
        }
    }

    public sealed partial class CouncilAppointmentDeliberationBehavior
    {
        private Dictionary<string, string> _integrationVoteIds = new Dictionary<string, string>();

        internal string GetIntegrationVoteId(Kingdom realm, PrivyCouncilOffice office) => realm != null
            && _integrationVoteIds.TryGetValue(BuildPendingKey(realm, office), out string id) ? id : null;

        internal IEnumerable<PendingVoteSnapshot> GetIntegrationPendingVotes(Kingdom realm)
        {
            string prefix = realm.StringId + "|";
            foreach (var pending in _pendingVoteDate.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                if (!int.TryParse(pending.Key.Substring(prefix.Length), out int office)
                    || !Enum.IsDefined(typeof(PrivyCouncilOffice), office)
                    || !_integrationVoteIds.TryGetValue(pending.Key, out string id)) continue;
                _pendingProposerClan.TryGetValue(pending.Key, out string proposerId);
                yield return CreateIntegrationVote(id, realm, (PrivyCouncilOffice)office, ResolveClan(proposerId), pending.Value.ToDays);
            }
        }

        internal static PendingVoteSnapshot CreateIntegrationVote(string id, Kingdom realm, PrivyCouncilOffice office,
            Clan proposer, double? scheduledDay, IEnumerable<Clan> shortlist = null)
        {
            var council = Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>();
            var candidates = council?.GetIntegrationAppointmentCandidates(realm, office) ?? new List<Clan>();
            var nominees = shortlist ?? Current?.BuildStoredRanking(realm, office, candidates).Take(3).Select(r => r.Candidate)
                ?? Enumerable.Empty<Clan>();
            return new PendingVoteSnapshot(id, PendingVoteKind.CouncilAppointment,
                shortlist == null ? PendingVoteStage.Deliberation : PendingVoteStage.Ballot, realm, proposer,
                office: office, scheduledDay: scheduledDay, candidates: candidates,
                nominees: nominees.Where(candidates.Contains));
        }
    }
}

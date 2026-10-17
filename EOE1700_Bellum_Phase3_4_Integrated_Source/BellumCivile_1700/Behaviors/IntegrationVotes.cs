using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile.Behaviors
{
    public sealed partial class BellumIntegrationBehavior
    {
        private const int RecentCouncilAppointmentLimit = 100;
        private List<IntegrationVoteRecord> _votes = new List<IntegrationVoteRecord>();
        private List<CouncilAppointmentResultRecord> _councilResults = new List<CouncilAppointmentResultRecord>();
        private bool _councilDispatchPending;
        internal event Action<CouncilAppointmentResultSnapshot> CouncilAppointmentDecided;

        private void SyncVotes(IDataStore store)
        {
            store.SyncData("BC_Integration_Votes", ref _votes);
            store.SyncData("BC_Integration_CouncilResults", ref _councilResults);
            _votes = (_votes ?? new List<IntegrationVoteRecord>())
                .Where(v => v?.Decision != null && !string.IsNullOrEmpty(v.Id)).ToList();
            _councilResults = (_councilResults ?? new List<CouncilAppointmentResultRecord>())
                .Where(r => r?.Realm != null && r.Winner != null && !string.IsNullOrEmpty(r.VoteId)).ToList();
            _councilDispatchPending = _councilResults.Any(r => !r.Dispatched);
        }

        private void InitializeVotes(CampaignGameStarter starter)
        {
            foreach (var realm in Kingdom.All)
                foreach (var decision in realm.UnresolvedDecisions)
                    TrackVote(decision, false);
            PruneVotes();
        }

        private void TrackVote(KingdomDecision decision, bool isPlayerInvolved)
        {
            if (decision?.Kingdom == null || _votes.Any(v => v.Decision == decision)) return;
            string id;
            if (decision is ExpelClanFromKingdomDecision expulsion)
                id = ExpulsionDeliberationBehavior.Current?.GetIntegrationVoteId(decision.Kingdom, expulsion.ClanToExpel);
            else if (decision is SettlementClaimantDecision fief)
                id = Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>()?.GetIntegrationVoteId(decision.Kingdom, fief.Settlement);
            else if (decision is PrivyCouncilAppointmentDecision council)
                id = CouncilAppointmentDeliberationBehavior.Current?.GetIntegrationVoteId(decision.Kingdom, council.Office);
            else return;
            _votes.Add(new IntegrationVoteRecord { Id = id ?? Guid.NewGuid().ToString("N"), Decision = decision });
        }

        internal string GetTrackedVoteId(KingdomDecision decision) => _votes.FirstOrDefault(v => v.Decision == decision)?.Id;

        internal IReadOnlyList<PendingVoteSnapshot> GetPendingVotes(Kingdom realm)
        {
            var result = new List<PendingVoteSnapshot>();
            if (realm == null || realm.IsEliminated) return result.AsReadOnly();
            var expulsion = ExpulsionDeliberationBehavior.Current;
            var fiefs = Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>();
            var council = CouncilAppointmentDeliberationBehavior.Current;
            if (expulsion != null) result.AddRange(expulsion.GetIntegrationPendingVotes(realm));
            if (fiefs != null) result.AddRange(fiefs.GetIntegrationPendingVotes(realm));
            if (council != null) result.AddRange(council.GetIntegrationPendingVotes(realm));

            foreach (var vote in _votes.Where(v => v.Decision.Kingdom == realm && realm.UnresolvedDecisions.Contains(v.Decision)))
            {
                PendingVoteSnapshot snapshot = null;
                if (vote.Decision is ExpelClanFromKingdomDecision e && IsExpulsionTarget(realm, e.ClanToExpel))
                    snapshot = new PendingVoteSnapshot(vote.Id, PendingVoteKind.Expulsion, PendingVoteStage.Ballot,
                        realm, e.ProposerClan, target: e.ClanToExpel);
                else if (vote.Decision is SettlementClaimantDecision f && f.Settlement?.MapFaction == realm)
                    snapshot = FiefDeliberationBehavior.CreateIntegrationVote(vote.Id, realm, f.Settlement,
                        f.ProposerClan, f.ClanToExclude, null, PendingVoteStage.Ballot);
                else if (vote.Decision is PrivyCouncilAppointmentDecision c && c.AppliedCandidate == null)
                    snapshot = CouncilAppointmentDeliberationBehavior.CreateIntegrationVote(vote.Id, realm,
                        c.Office, c.ProposerClan, null, c.IntegrationShortlist);
                if (snapshot == null) continue;
                // During native handoff both representations can briefly be present. The ballot wins.
                result.RemoveAll(p => p.Id == snapshot.Id);
                result.Add(snapshot);
            }
            return result.OrderBy(p => p.ScheduledDay ?? double.MaxValue).ThenBy(p => p.Kind)
                .ThenBy(p => p.Id, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        internal static bool IsExpulsionTarget(Kingdom realm, Clan clan) => realm != null
            && NobleClanEligibilityHelper.IsLiveNobleClan(clan) && clan.Kingdom == realm && clan != realm.RulingClan;

        internal void RecordCouncilAppointment(PrivyCouncilAppointmentDecision decision, Clan previousHolder)
        {
            if (decision?.AppliedCandidate == null || decision.Kingdom == null) return;
            TrackVote(decision, false);
            string id = _votes.First(v => v.Decision == decision).Id;
            if (_councilResults.Any(r => r.VoteId == id)) return;
            _councilResults.Add(new CouncilAppointmentResultRecord { VoteId = id, Realm = decision.Kingdom,
                Office = decision.Office, Winner = decision.AppliedCandidate, WinnerLeader = decision.AppliedCandidate.Leader,
                PreviousHolder = previousHolder, CompletedDay = CampaignTime.Now.ToDays });
            _councilDispatchPending = true;
        }

        internal IReadOnlyList<CouncilAppointmentResultSnapshot> GetRecentCouncilAppointments() =>
            _councilResults.Select(r => new CouncilAppointmentResultSnapshot(r)).ToList().AsReadOnly();

        private void PruneVotes() => _votes.RemoveAll(v =>
            v.Decision.Kingdom == null || !v.Decision.Kingdom.UnresolvedDecisions.Contains(v.Decision));

        private void DispatchCouncilAppointments(float dt)
        {
            PruneVotes();
            if (!_councilDispatchPending) return;
            _councilDispatchPending = false;
            foreach (var record in _councilResults.Where(r => !r.Dispatched).ToList())
            {
                record.Dispatched = true;
                var snapshot = new CouncilAppointmentResultSnapshot(record);
                foreach (Action<CouncilAppointmentResultSnapshot> subscriber in
                    CouncilAppointmentDecided?.GetInvocationList() ?? Array.Empty<Delegate>())
                {
                    try { subscriber(snapshot); }
                    catch (Exception ex) { BellumCivileLogger.Log("Integration council subscriber failed: " + ex); }
                }
            }
            while (_councilResults.Count > RecentCouncilAppointmentLimit && _councilResults[0].Dispatched)
                _councilResults.RemoveAt(0);
        }
    }
}

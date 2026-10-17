using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public enum PendingVoteKind { Expulsion, FiefAllocation, CouncilAppointment }
    public enum PendingVoteStage { Deliberation, Ballot }

    /// <summary>A copied view of an open vote, not a reservation of eligibility or a promise window.</summary>
    public sealed class PendingVoteSnapshot
    {
        public string Id { get; }
        public PendingVoteKind Kind { get; }
        public PendingVoteStage Stage { get; }
        public Kingdom Realm { get; }
        public Clan Proposer { get; }
        public Clan TargetClan { get; }
        public Settlement Settlement { get; }
        public PrivyCouncilOffice? Office { get; }
        public double? ScheduledDay { get; }
        public IReadOnlyList<Clan> EligibleCandidates { get; }
        /// <summary>Recorded nomination leaders (or the stored council shortlist at ballot stage), not a guaranteed final ballot.</summary>
        public IReadOnlyList<Clan> LeadingNominees { get; }

        internal PendingVoteSnapshot(string id, PendingVoteKind kind, PendingVoteStage stage,
            Kingdom realm, Clan proposer, Clan target = null, Settlement settlement = null,
            PrivyCouncilOffice? office = null, double? scheduledDay = null,
            IEnumerable<Clan> candidates = null, IEnumerable<Clan> nominees = null)
        {
            Id = id; Kind = kind; Stage = stage; Realm = realm; Proposer = proposer;
            TargetClan = target; Settlement = settlement; Office = office; ScheduledDay = scheduledDay;
            EligibleCandidates = (candidates ?? Enumerable.Empty<Clan>()).Where(c => c != null).Distinct().ToList().AsReadOnly();
            LeadingNominees = (nominees ?? Enumerable.Empty<Clan>()).Where(c => c != null).Distinct().ToList().AsReadOnly();
        }
    }

    /// <summary>A successfully applied council ballot, including confirmation of an incumbent.</summary>
    public sealed class CouncilAppointmentResultSnapshot
    {
        public string VoteId { get; }
        public Kingdom Realm { get; }
        public PrivyCouncilOffice Office { get; }
        public Clan Winner { get; }
        public Hero WinnerLeader { get; }
        public Clan PreviousHolder { get; }
        public bool IncumbentRetained { get; }
        public double CompletedDay { get; }

        internal CouncilAppointmentResultSnapshot(CouncilAppointmentResultRecord record)
        {
            VoteId = record.VoteId; Realm = record.Realm; Office = record.Office;
            Winner = record.Winner; WinnerLeader = record.WinnerLeader; PreviousHolder = record.PreviousHolder;
            IncumbentRetained = record.Winner == record.PreviousHolder; CompletedDay = record.CompletedDay;
        }
    }

    internal sealed class IntegrationVoteRecord
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public KingdomDecision Decision;
    }

    internal sealed class CouncilAppointmentResultRecord
    {
        [SaveableField(1)] public string VoteId;
        [SaveableField(2)] public Kingdom Realm;
        [SaveableField(3)] public PrivyCouncilOffice Office;
        [SaveableField(4)] public Clan Winner;
        [SaveableField(5)] public Hero WinnerLeader;
        [SaveableField(6)] public Clan PreviousHolder;
        [SaveableField(7)] public double CompletedDay;
        [SaveableField(8)] public bool Dispatched;
    }

    internal static class IntegrationVoteIds
    {
        internal static string Ensure(Dictionary<string, string> ids, string key)
        {
            if (!ids.TryGetValue(key, out string id) || string.IsNullOrEmpty(id))
                ids[key] = id = Guid.NewGuid().ToString("N");
            return id;
        }

        internal static void Sync(IDataStore store, string name, ref Dictionary<string, string> ids,
            IEnumerable<string> pendingKeys)
        {
            store.SyncData(name, ref ids);
            ids = ids ?? new Dictionary<string, string>();
            if (!store.IsLoading) return;
            var live = new HashSet<string>(pendingKeys);
            foreach (string key in ids.Keys.Where(k => !live.Contains(k)).ToList()) ids.Remove(key);
            foreach (string key in live) Ensure(ids, key);
        }
    }
}

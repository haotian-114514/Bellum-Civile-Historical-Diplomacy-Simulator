using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class IntegrationVoteTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) => Items.Add(b);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) => Items.AddRange(b);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) Data[key] = Copy(data);
            else data = Data.TryGetValue(key, out object value) ? (T)Copy(value) : default;
            return true;
        }
        private static object Copy(object value)
        {
            if (value is IDictionary dictionary)
            {
                var clone = (IDictionary)Activator.CreateInstance(value.GetType());
                foreach (DictionaryEntry entry in dictionary) clone.Add(entry.Key, Copy(entry.Value));
                return clone;
            }
            if (value is IList list)
            {
                var clone = (IList)Activator.CreateInstance(value.GetType());
                foreach (object item in list) clone.Add(Copy(item));
                return clone;
            }
            if (value != null && value.GetType().Assembly == typeof(BellumIntegration).Assembly)
                return typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, null);
            return value;
        }
    }

    private static readonly Assembly Mod = typeof(BellumIntegration).Assembly;
    private static readonly List<Clan> Clans = new List<Clan>();
    private static readonly List<Kingdom> Realms = new List<Kingdom>();
    private static readonly List<KingdomDecision> Decisions = new List<KingdomDecision>();
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static Kingdom _realm, _foreignRealm;
    private static Clan _ruler, _target, _candidate, _foreign, _merc, _minor, _holder;
    private static Hero _captive;
    private static Settlement _fief;
    private static bool _apply, _verify;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Field(object o, string name) => AccessTools.Field(o.GetType(), name).GetValue(o);
    private static void Set(object o, string name, object value) => AccessTools.Field(o.GetType(), name).SetValue(o, value);
    private static object Call(object o, string name, params object[] args) => AccessTools.Method(o.GetType(), name).Invoke(o, args);
    private static Dictionary<string, T> Map<T>(object o, string name) => (Dictionary<string, T>)Field(o, name);
    private static bool Skip() => false;
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Guard() => throw new Exception("A read-only vote query invoked a campaign mutation.");
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Clans); return false; }
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Realms); return false; }
    private static bool RealmClans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Clans.Where(c => c.Kingdom == __instance).ToList()); return false; }
    private static bool LiveDecisions(Kingdom __instance, ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(Decisions.Where(d => d.Kingdom == __instance).ToList()); return false; }
    private static bool Player(ref Clan __result) { __result = _ruler; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { __result = __instance == _realm ? _ruler : _foreign; return false; }
    private static bool ClanRealm(Clan __instance, ref Kingdom __result) { __result = __instance == _foreign ? _foreignRealm : _realm; return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { __result = Leaders[__instance]; return false; }
    private static bool Merc(Clan __instance, ref bool __result) { __result = __instance == _merc; return false; }
    private static bool Minor(Clan __instance, ref bool __result) { __result = __instance == _minor; return false; }
    private static bool Captive(Hero __instance, ref bool __result) { __result = __instance == _captive; return false; }
    private static bool Fiefs(ref MBReadOnlyList<Town> __result) { __result = new MBReadOnlyList<Town>(new List<Town>()); return false; }
    private static bool Desired(ref int __result) { __result = 1; return false; }
    private static bool Rank(ref FeudalTitleType? __result) { __result = null; return false; }
    private static bool FindFief(string idString, ref Settlement __result) { __result = idString == _fief.StringId ? _fief : null; return false; }
    private static bool FiefRealm(ref IFaction __result) { __result = _realm; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(12); return false; }
    private static bool Appoint(Clan candidate, ref bool __result) { if (_apply && _verify) _holder = candidate; __result = _apply; return false; }
    private static bool Holder(ref Clan __result) { __result = _holder; return false; }

    private static T Decision<T>() where T : KingdomDecision
    {
        var d = Blank<T>(); Set(d, "_kingdom", _realm); Set(d, "<ProposerClan>k__BackingField", _ruler); return d;
    }
    private static PrivyCouncilAppointmentDecision CouncilDecision(Clan previous)
    {
        var d = Decision<PrivyCouncilAppointmentDecision>();
        Set(d, "_office", PrivyCouncilOffice.Marshal); Set(d, "_incumbentClan", previous);
        Set(d, "_shortlistedClans", new List<Clan> { _target, _candidate }); return d;
    }
    private static void Tick(BellumIntegrationBehavior b) => Call(b, "DispatchCouncilAppointments", 0f);

    internal static void Run(Action<bool, string> check)
    {
        var previous = Campaign.Current;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null);
        var harmony = new Harmony("bellum.tests.integration.votes");
        void Patch(MethodBase m, string prefix) => harmony.Patch(m, prefix: new HarmonyMethod(typeof(IntegrationVoteTests), prefix));
        void Getter(Type t, string name, string prefix) => Patch(AccessTools.PropertyGetter(t, name), prefix);
        var manager = new Manager();
        var integration = new BellumIntegrationBehavior();
        var expulsion = new ExpulsionDeliberationBehavior();
        var fiefs = new FiefDeliberationBehavior();
        var council = new CouncilAppointmentDeliberationBehavior();
        var offices = new PrivyCouncilBehavior();
        manager.Items.AddRange(new CampaignBehaviorBase[] { integration, expulsion, fiefs, council, offices });
        try
        {
            Clans.Clear(); Realms.Clear(); Decisions.Clear(); Leaders.Clear(); _captive = null;
            ticks.SetValue(null, 1000L);
            _realm = Blank<Kingdom>(); _realm.StringId = "realm";
            _foreignRealm = Blank<Kingdom>(); _foreignRealm.StringId = "other";
            Realms.AddRange(new[] { _realm, _foreignRealm });
            Clan House(string id)
            {
                var c = Blank<Clan>(); c.StringId = id; Clans.Add(c);
                var h = Blank<Hero>(); h.StringId = id + "_leader"; Set(h, "_clan", c); Leaders[c] = h; return c;
            }
            _ruler = House("ruler"); _target = House("target"); _candidate = House("candidate");
            _foreign = House("foreign"); _merc = House("merc"); _minor = House("minor");
            _fief = Blank<Settlement>(); _fief.StringId = "castle";
            var campaign = Blank<Campaign>();
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Getter(typeof(Clan), "All", nameof(AllClans)); Getter(typeof(Kingdom), "All", nameof(AllRealms));
            Getter(typeof(Kingdom), "Clans", nameof(RealmClans)); Getter(typeof(Kingdom), "UnresolvedDecisions", nameof(LiveDecisions));
            Getter(typeof(Kingdom), "IsEliminated", nameof(No)); Getter(typeof(Kingdom), "RulingClan", nameof(Ruler));
            Getter(typeof(Clan), "PlayerClan", nameof(Player)); Getter(typeof(Clan), "Kingdom", nameof(ClanRealm));
            Getter(typeof(Clan), "Leader", nameof(Leader)); Getter(typeof(Clan), "IsEliminated", nameof(No));
            Getter(typeof(Clan), "IsUnderMercenaryService", nameof(Merc)); Getter(typeof(Clan), "IsMinorFaction", nameof(Minor));
            Getter(typeof(Clan), "Fiefs", nameof(Fiefs)); Getter(typeof(Hero), "IsDead", nameof(No));
            Getter(typeof(Hero), "IsPrisoner", nameof(Captive)); Getter(typeof(CampaignTime), "Now", nameof(Now));
            Getter(typeof(Settlement), "MapFaction", nameof(FiefRealm));
            Patch(AccessTools.Method(typeof(Settlement), "Find"), nameof(FindFief));
            Patch(AccessTools.Method(typeof(FactionObject), "CalculateDesiredFiefs"), nameof(Desired));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.FeudalPoliticalWeightHelper"), "GetHighestHeldTitleRank"), nameof(Rank));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "IsOfficeUnlocked"), nameof(Yes));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "EnsureCouncilForKingdom"), nameof(Guard));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "ValidateCouncilProceeding"), nameof(Guard));
            Patch(AccessTools.Method(typeof(FiefDeliberationBehavior), "RefreshPreliminaryNominationsForVote"), nameof(Guard));
            Patch(AccessTools.Constructor(typeof(KingdomElection), new[] { typeof(KingdomDecision) }), nameof(Guard));

            string expelKey = "realm|target", fiefKey = "realm|castle", councilKey = "realm|" + (int)PrivyCouncilOffice.Marshal;
            Map<CampaignTime>(expulsion, "_pendingExpelDate")[expelKey] = CampaignTime.Days(20);
            Map<string>(expulsion, "_pendingExpelProposer")[expelKey] = _ruler.StringId;
            Map<CampaignTime>(fiefs, "_pendingFiefDate")[fiefKey] = CampaignTime.Days(21);
            Map<string>(fiefs, "_pendingFiefProposer")[fiefKey] = _ruler.StringId;
            Map<string>(fiefs, "_pendingFiefExclude")[fiefKey] = _ruler.StringId;
            Map<string>(fiefs, "_fiefNomineeByVoter")[fiefKey + "|ruler"] = _candidate.StringId;
            Map<string>(fiefs, "_fiefNomineeByVoter")[fiefKey + "|target"] = _candidate.StringId;
            Map<string>(fiefs, "_fiefNomineeByVoter")[fiefKey + "|candidate"] = _foreign.StringId;
            Map<CampaignTime>(council, "_pendingVoteDate")[councilKey] = CampaignTime.Days(22);
            Map<string>(council, "_pendingProposerClan")[councilKey] = _ruler.StringId;
            Map<string>(council, "_nomineeByVoter")[councilKey + "|ruler"] = _target.StringId;
            var legacy = new Store(); expulsion.SyncData(legacy); fiefs.SyncData(legacy); council.SyncData(legacy);
            legacy.Data.Remove("BC_Expulsion_IntegrationVoteIds"); legacy.Data.Remove("BC_Fief_IntegrationVoteIds"); legacy.Data.Remove("BC_Council_IntegrationVoteIds");
            legacy.IsLoading = true; expulsion.SyncData(legacy); fiefs.SyncData(legacy); council.SyncData(legacy);
            integration.SyncData(new Store { IsLoading = true });
            check(BellumIntegration.GetPendingVotes(null).Count == 0 && BellumIntegration.GetPendingVotes(_foreignRealm).Count == 0,
                "Null and unrelated realms have no pending votes");
            var pending = BellumIntegration.GetPendingVotes(_realm);
            check(pending.Count == 3 && pending.All(p => p.Stage == PendingVoteStage.Deliberation && !string.IsNullOrEmpty(p.Id))
                && pending.Select(p => p.Id).Distinct().Count() == 3, "Legacy queues receive distinct saved vote IDs");
            var fv = pending.Single(p => p.Kind == PendingVoteKind.FiefAllocation);
            check(fv.Settlement == _fief && fv.Proposer == _ruler && fv.ScheduledDay == 21
                && fv.EligibleCandidates.Count == 2 && fv.EligibleCandidates.Contains(_target) && fv.EligibleCandidates.Contains(_candidate),
                "Fief snapshot exposes subject, date and eligible houses, excluding foreign, mercenary, minor and excluded houses");
            check(fv.LeadingNominees.SequenceEqual(new[] { _candidate }), "Only eligible recorded nominations are exposed; no ranking is synthesized");
            check(pending.Single(p => p.Kind == PendingVoteKind.Expulsion).TargetClan == _target
                && pending.Single(p => p.Kind == PendingVoteKind.CouncilAppointment).Office == PrivyCouncilOffice.Marshal,
                "Expulsion and council snapshots identify their subjects");
            var queueSave = new Store(); expulsion.SyncData(queueSave); fiefs.SyncData(queueSave); council.SyncData(queueSave);
            queueSave.IsLoading = true; expulsion.SyncData(queueSave); fiefs.SyncData(queueSave); council.SyncData(queueSave);
            Map<CampaignTime>(expulsion, "_pendingExpelDate")[expelKey] = CampaignTime.Days(25);
            check(BellumIntegration.GetPendingVotes(_realm).Select(p => p.Id).OrderBy(x => x)
                .SequenceEqual(pending.Select(p => p.Id).OrderBy(x => x)), "Queue save/load and rescheduling preserve vote identity");
            check(pending.Single(p => p.Kind == PendingVoteKind.Expulsion).ScheduledDay == 20
                && BellumIntegration.GetPendingVotes(_realm).Single(p => p.Kind == PendingVoteKind.Expulsion).ScheduledDay == 25,
                "Rescheduling changes only the new snapshot's date");
            bool immutable = false;
            try { ((IList<Clan>)fv.EligibleCandidates).Clear(); } catch (NotSupportedException) { immutable = true; }
            check(immutable, "Consumers cannot modify candidate snapshots");
            Map<string>(fiefs, "_fiefNomineeByVoter").Clear();
            check(fv.LeadingNominees.Count == 1 && BellumIntegration.GetPendingVotes(_realm)
                .Single(p => p.Kind == PendingVoteKind.FiefAllocation).LeadingNominees.Count == 0,
                "Earlier snapshots are detached from later nomination changes");
            check(((IList)Field(offices, "_officeRecords")).Count == 0, "Polling does not initialize absent council offices");
            var record = new PrivyCouncilOfficeRecord(_realm.StringId, PrivyCouncilOffice.Marshal, 0);
            record.Initialize(_target.StringId, 0); ((IList)Field(offices, "_officeRecords")).Add(record);
            _captive = Leaders[_target];
            check(BellumIntegration.GetPendingVotes(_realm).Single(p => p.Kind == PendingVoteKind.CouncilAppointment)
                .EligibleCandidates.Contains(_target), "A captive incumbent remains an eligible council candidate");
            _captive = Leaders[_candidate];
            check(!BellumIntegration.GetPendingVotes(_realm).Single(p => p.Kind == PendingVoteKind.CouncilAppointment)
                .EligibleCandidates.Contains(_candidate), "A captive challenger is not eligible");
            _captive = null;

            string councilId = pending.Single(p => p.Kind == PendingVoteKind.CouncilAppointment).Id;
            var e = Decision<ExpelClanFromKingdomDecision>(); Set(e, "ClanToExpel", _target);
            var f = Decision<SettlementClaimantDecision>(); Set(f, "Settlement", _fief); Set(f, "ClanToExclude", _ruler);
            var c = CouncilDecision(_target);
            foreach (var d in new KingdomDecision[] { e, f, c }) { Call(integration, "TrackVote", d, true); Decisions.Add(d); }
            var ballot = BellumIntegration.GetPendingVotes(_realm);
            check(ballot.Count == 3 && ballot.All(p => p.Stage == PendingVoteStage.Ballot)
                && ballot.Select(p => p.Id).OrderBy(x => x).SequenceEqual(pending.Select(p => p.Id).OrderBy(x => x)),
                "Native handoff is deduplicated and preserves all three vote IDs");
            Map<CampaignTime>(fiefs, "_openedFiefVoteDates")[fiefKey] = CampaignTime.Days(21);
            Call(fiefs, "FinishFiefHandoff", fiefKey);
            check(!Map<CampaignTime>(fiefs, "_pendingFiefDate").ContainsKey(fiefKey)
                && Map<string>(fiefs, "_integrationVoteIds")[fiefKey] == fv.Id,
                "Opened fief recovery retains the original ID after removing deliberation");
            var recoverySave = new Store(); fiefs.SyncData(recoverySave); recoverySave.IsLoading = true; fiefs.SyncData(recoverySave);
            check(Map<string>(fiefs, "_integrationVoteIds")[fiefKey] == fv.Id,
                "Recovery receipt preserves its vote identity through save/load without a queue entry");
            Call(expulsion, "RemovePendingKey", expelKey); Call(fiefs, "RemovePendingKey", fiefKey); Call(council, "RemovePendingLifecycle", councilKey);
            check(BellumIntegration.GetPendingVotes(_realm).Count == 3, "Active ballots remain visible after their queue entries are removed");
            var saved = new Store(); integration.SyncData(saved); saved.IsLoading = true;
            var loaded = new BellumIntegrationBehavior(); loaded.SyncData(saved);
            manager.Items.Remove(integration); manager.Items.Insert(0, loaded); integration = loaded;
            Call(integration, "InitializeVotes", new object[] { null });
            check(BellumIntegration.GetPendingVotes(_realm).Select(p => p.Id).OrderBy(x => x)
                .SequenceEqual(pending.Select(p => p.Id).OrderBy(x => x)), "Ballot IDs survive save/load and session initialization");
            Decisions.Remove(e); Decisions.Remove(f);
            check(BellumIntegration.GetPendingVotes(_realm).Count == 1, "Canceled native votes disappear immediately without waiting for cleanup");
            check(BellumIntegration.GetRecentCouncilAppointments().Count == 0, "Canceled votes and old saves create no historical council outcomes");
            Map<CampaignTime>(expulsion, "_pendingExpelDate")[expelKey] = CampaignTime.Days(30);
            Call(expulsion, "InitializePendingLifecycle", expelKey, true);
            check(BellumIntegration.GetPendingVotes(_realm).Single(p => p.Kind == PendingVoteKind.Expulsion).Id
                != pending.Single(p => p.Kind == PendingVoteKind.Expulsion).Id, "A later proposal against the same house gets a new ID");
            Call(expulsion, "RemovePendingKey", expelKey);
            var oldBallot = Decision<SettlementClaimantDecision>(); Set(oldBallot, "Settlement", _fief);
            Decisions.Add(oldBallot); Call(integration, "InitializeVotes", new object[] { null });
            check(!string.IsNullOrEmpty(BellumIntegration.GetPendingVotes(_realm).Single(p => p.Kind == PendingVoteKind.FiefAllocation).Id),
                "A pre-update native ballot without a queue receives an ID at session launch");
            Decisions.Remove(oldBallot);

            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "TryAppointOffice"), nameof(Appoint));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "GetOfficeHolder"), nameof(Holder));
            _apply = false; _verify = false; _holder = _target;
            var failed = CouncilDecision(_target);
            failed.ApplyChosenOutcome(new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_candidate));
            check(BellumIntegration.GetRecentCouncilAppointments().Count == 0, "Rejected appointments do not publish a result");
            _apply = true;
            var unverified = CouncilDecision(_target);
            unverified.ApplyChosenOutcome(new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_candidate));
            check(BellumIntegration.GetRecentCouncilAppointments().Count == 0, "A success return without the actual office change does not publish a result");
            _verify = true;
            int delivered = 0;
            Action<CouncilAppointmentResultSnapshot> handler = r => { delivered++; check(r.VoteId == councilId, "Deferred result links to the pending vote ID"); };
            BellumIntegration.CouncilAppointmentDecided += handler;
            var winnerLeader = Leaders[_candidate];
            c.ApplyChosenOutcome(new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_candidate));
            c.ApplyChosenOutcome(new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_candidate));
            var result = BellumIntegration.GetRecentCouncilAppointments().Single();
            check(delivered == 0 && result.VoteId == councilId && result.Realm == _realm && result.Winner == _candidate
                && result.WinnerLeader == winnerLeader && result.PreviousHolder == _target && !result.IncumbentRetained
                && result.CompletedDay == 12 && result.Office == PrivyCouncilOffice.Marshal,
                "Successful appointment is captured once with historical leader, predecessor and date, before deferred delivery");
            Leaders[_candidate] = Blank<Hero>();
            check(BellumIntegration.GetRecentCouncilAppointments().Single().WinnerLeader == winnerLeader,
                "Leader changes after resolution do not rewrite the captured winner");
            Leaders[_candidate] = winnerLeader;
            Decisions.Remove(c);
            var queuedSave = new Store(); integration.SyncData(queuedSave); queuedSave.IsLoading = true;
            var resumed = new BellumIntegrationBehavior(); resumed.SyncData(queuedSave);
            manager.Items.Remove(integration); manager.Items.Insert(0, resumed); integration = resumed;
            Action<CouncilAppointmentResultSnapshot> bad = r => throw new InvalidOperationException("test subscriber");
            BellumIntegration.CouncilAppointmentDecided += bad;
            BellumIntegration.CouncilAppointmentDecided += handler;
            Tick(integration); Tick(integration);
            check(delivered == 1, "Queued results survive reload; a throwing subscriber does not block later listeners or cause replay");
            var dispatchedSave = new Store(); integration.SyncData(dispatchedSave); dispatchedSave.IsLoading = true;
            resumed = new BellumIntegrationBehavior(); resumed.SyncData(dispatchedSave);
            manager.Items.Remove(integration); manager.Items.Insert(0, resumed); integration = resumed;
            BellumIntegration.CouncilAppointmentDecided += handler; Tick(integration);
            check(delivered == 1 && BellumIntegration.GetRecentCouncilAppointments().Count == 1,
                "Delivered results remain in history but are not replayed on reload");
            BellumIntegration.CouncilAppointmentDecided -= handler;
            var retained = CouncilDecision(_candidate);
            retained.ApplyChosenOutcome(new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_candidate));
            check(BellumIntegration.GetRecentCouncilAppointments().Last().IncumbentRetained
                && BellumIntegration.GetRecentCouncilAppointments().Last().VoteId != councilId,
                "A repeated contest of the same office records incumbent retention with a distinct ID");
            var changedHolder = CouncilDecision(_target);
            changedHolder.ApplyChosenOutcome(new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_candidate));
            check(BellumIntegration.GetRecentCouncilAppointments().Last().PreviousHolder == _candidate,
                "Result captures the holder at resolution rather than the incumbent when the motion was filed");
            Tick(integration);
            CouncilDecision(_candidate).ApplyChosenOutcome(new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_candidate));
            int reentrant = 0;
            Action<CouncilAppointmentResultSnapshot> addDuringDelivery = r =>
            {
                if (reentrant++ == 0) CouncilDecision(_candidate).ApplyChosenOutcome(
                    new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_candidate));
            };
            BellumIntegration.CouncilAppointmentDecided += addDuringDelivery;
            Tick(integration); check(reentrant == 1, "Results added by a listener are deferred to the next tick");
            Tick(integration); check(reentrant == 2, "Reentrant result is delivered exactly once on the next tick");
            BellumIntegration.CouncilAppointmentDecided -= addDuringDelivery;
            for (int i = 0; i < 105; i++) CouncilDecision(_candidate).ApplyChosenOutcome(
                new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_candidate));
            Tick(integration);
            check(BellumIntegration.GetRecentCouncilAppointments().Count == 100, "Delivered council history is bounded to the latest 100 results");
            bool offThread = Task.Run(() => { try { BellumIntegration.GetPendingVotes(_realm); return false; } catch (InvalidOperationException) { return true; } }).Result;
            check(offThread, "Pending-vote API rejects background access");
            offThread = Task.Run(() => { try { BellumIntegration.CouncilAppointmentDecided += handler; return false; } catch (InvalidOperationException) { return true; } }).Result;
            check(offThread, "Council event subscriptions require the campaign thread");
            check(Map<string>(council, "_nomineeByVoter").Count == 1 && ((IList)Field(offices, "_officeRecords")).Count == 1,
                "API polling never mutates the stored nominations or office records");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); ticks.SetValue(null, oldTicks);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
        }
    }
}

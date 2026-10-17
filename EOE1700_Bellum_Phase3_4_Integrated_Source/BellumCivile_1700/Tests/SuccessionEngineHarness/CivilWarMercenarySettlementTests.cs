using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class CivilWarMercenarySettlementTests
{
    private sealed class Behaviors : ICampaignBehaviorManager
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

    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly HashSet<Clan> Mercenaries = new HashSet<Clan>();
    private static readonly HashSet<Clan> MinorClans = new HashSet<Clan>();
    private static readonly List<string> Events = new List<string>();
    private static Clan _player, _failJoin, _failDismiss, _throwDismiss;
    private static Action<Clan> _afterJoin, _afterDismiss;
    private static int _captures, _restores;
    private static int _settlementWrites;
    private static List<FactionObject> _activeFactions;
    private static List<Clan> _tribunalClans;
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public bool IsSaving { get; set; } = true;
        public bool IsLoading => !IsSaving;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) Values[key] = data;
            else if (Values.TryGetValue(key, out var value)) data = (T)value;
            return true;
        }
    }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object instance, string method, params object[] args) =>
        AccessTools.Method(typeof(CivilWarResolutionBehavior), method).Invoke(instance, args);
    private static bool Skip() => false;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool SetRuler(Kingdom __instance, Clan value) { Rulers[__instance] = value; return false; }
    private static bool Head(Clan __instance, ref Hero __result) { Heads.TryGetValue(__instance, out __result); return false; }
    private static bool Monarch(Kingdom __instance, ref Hero __result)
    { __result = Rulers.TryGetValue(__instance, out var clan) ? Heads[clan] : null; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Mercenary(Clan __instance, ref bool __result) { __result = Mercenaries.Contains(__instance); return false; }
    private static bool Minor(Clan __instance, ref bool __result) { __result = MinorClans.Contains(__instance); return false; }
    private static bool Clans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Realms.Where(p => p.Value == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool Capture() { _captures++; return false; }
    private static bool Restore() { _restores++; return false; }
    private static bool NoTitle(ref FeudalTitleRecord __result) { __result = null; return false; }
    private static bool Strongholds(ref int __result) { __result = 1; return false; }
    private static bool Tracked(Kingdom preferredKingdom, ref Kingdom __result) { __result = preferredKingdom; return false; }
    private static bool Write() { _settlementWrites++; return false; }
    private static bool Remove(FactionObject faction) { _settlementWrites++; _activeFactions.Remove(faction); return false; }
    private static bool Drain(ref bool __result) { _settlementWrites++; __result = true; return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(new List<KingdomDecision>()); return false; }
    private static bool Tribunal(List<Clan> losingClans, out bool anyExecuted, out bool anyForgiven, out bool anyExiled, out bool oldKingExecuted)
    {
        _tribunalClans = losingClans.ToList();
        _settlementWrites++;
        anyExecuted = anyForgiven = anyExiled = oldKingExecuted = false;
        return false;
    }

    private static bool Dismiss(Clan mercenaryClan)
    {
        var clan = mercenaryClan;
        Events.Add("dismiss:" + clan.StringId);
        if (clan == _throwDismiss) throw new InvalidOperationException("Injected dismissal exception");
        if (clan == _failDismiss) return false;
        Mercenaries.Remove(clan);
        Realms[clan] = null;
        _afterDismiss?.Invoke(clan);
        return false;
    }

    private static bool Join(Clan clan, Kingdom targetKingdom)
    {
        if (clan == _failJoin) throw new InvalidOperationException("Injected transfer interruption");
        Events.Add("join:" + clan.StringId);
        // Native vassal joining ends mercenary service; detecting a bad call must not rely on the flag afterward.
        if (Mercenaries.Remove(clan)) Events.Add("CONVERTED:" + clan.StringId);
        Realms[clan] = targetKingdom;
        _afterJoin?.Invoke(clan);
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var previous = Campaign.Current;
        var harmony = new Harmony("bellum.tests.civil_war_mercenary_settlement");
        void Patch(MethodBase method, string name) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(CivilWarMercenarySettlementTests), name));
        var campaign = Blank<Campaign>();
        var behaviors = new Behaviors();
        var resolver = new CivilWarResolutionBehavior();
        behaviors.AddBehavior(resolver);
        behaviors.AddBehavior(new SuccessionChallengeBehavior());
        behaviors.AddBehavior(Blank<FeudalTitleBehavior>());
        campaign.AddCampaignBehaviorManager(behaviors);
        var mod = typeof(FactionObject).Assembly;

        Kingdom KingdomOf(string id) { var k = Blank<Kingdom>(); k.StringId = id; return k; }
        Clan House(string id, Kingdom realm, bool mercenary = false, bool minor = false)
        {
            var c = Blank<Clan>(); c.StringId = id; Realms[c] = realm; Heads[c] = Blank<Hero>();
            if (mercenary) Mercenaries.Add(c);
            if (minor) MinorClans.Add(c);
            return c;
        }
        FactionObject Faction(Clan leader, Kingdom parent)
        {
            var f = Blank<FactionObject>();
            AccessTools.Field(typeof(FactionObject), "_leader").SetValue(f, leader);
            AccessTools.Field(typeof(FactionObject), "_parentKingdom").SetValue(f, parent);
            return f;
        }
        bool Move(Clan clan, Kingdom source, Kingdom target, FactionObject faction = null) =>
            (bool)Call(null, "TransferCivilWarClan", clan, source, target, faction);
        void Reset()
        {
            Realms.Clear(); Rulers.Clear(); Heads.Clear(); Mercenaries.Clear(); MinorClans.Clear(); Events.Clear();
            _player = _failJoin = _failDismiss = _throwDismiss = null; _afterJoin = _afterDismiss = null; _captures = _restores = 0;
        }
        SuccessionChallengeRecord Challenge(Kingdom shell, Kingdom crown, Clan prince, Clan monarch) =>
            new SuccessionChallengeRecord
            {
                Id = "challenge", WarShell = shell, Realm = crown, OutcomeRealm = crown,
                WarFaction = Faction(prince, crown), OutcomeVictor = prince,
                Challenger = Heads[prince], Sovereign = Heads[monarch],
                WarOutcome = SuccessionChallengeOutcome.Victory, Phase = SuccessionChallengePhase.ResolvingWar
            };

        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(Mercenary));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsMinorFaction"), nameof(Minor));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertySetter(typeof(Kingdom), "RulingClan"), nameof(SetRuler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Leader"), nameof(Monarch));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Yes));
            Patch(AccessTools.Method(typeof(ChangeKingdomAction), "ApplyByLeaveKingdomAsMercenary"), nameof(Dismiss));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.KingdomVisualHelper", true), "ApplyJoinToKingdomPreservingCustomBanner"), nameof(Join));
            Patch(AccessTools.Method(typeof(FactionObject), "CaptureCivilWarStartInfluence", new[] { typeof(Clan) }), nameof(Capture));
            Patch(AccessTools.Method(typeof(FactionObject), "RestoreCivilWarInfluenceSnapshot"), nameof(Restore));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "EnsureValidRulingClan"), nameof(Skip));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ClearSuccessionStateForKingdom"), nameof(Skip));
            Patch(AccessTools.Method(typeof(SuccessionChallengeBehavior), "PrepareWarOutcome"), nameof(Yes));
            // Stop after the real household stage: property and tribunal settlement are separate concerns.
            Patch(AccessTools.Method(typeof(SuccessionChallengeBehavior), "LegalizeChallengeVictory"), nameof(No));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "TrySetKingdomTitleRuler"), nameof(Yes));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "RegisterRestoredRealmMantle"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetIndependentRealmSovereignTitle"), nameof(NoTitle));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumCivileLogger", true), "Log", new[] { typeof(string) }), nameof(Skip));

            Reset();
            var source = KingdomOf("rebel"); var target = KingdomOf("crown"); var other = KingdomOf("other");
            _player = House("player", source, mercenary: true);
            var npc = House("npc_company", source, mercenary: true, minor: true);
            var noble = House("noble", source);
            var faction = Faction(noble, target);
            faction.MoveClanToKingdomPreservingCivilWarInfluence(_player, target);
            faction.MoveClanToKingdomPreservingCivilWarInfluence(npc, target);
            check(Events.Count == 0 && _captures == 0, "Direct faction transfers cannot convert player or NPC mercenaries");
            check(!Move(_player, source, target) && !Move(npc, source, target)
                && Realms[_player] == null && Realms[npc] == null, "Settlement transfers release mercenary contracts instead of granting vassalage");
            check(!Move(_player, source, target) && Events.Count == 2,
                "A dismissed player in a stale roster is not reabsorbed after the mercenary flag clears");
            Realms[noble] = other;
            check(!Move(noble, source, target) && Realms[noble] == other, "A clan that changed realms during callbacks is not taken from its new realm");
            Realms[noble] = source; MinorClans.Add(noble); _player = noble;
            check(Move(noble, source, target, faction) && _captures == 1 && _restores == 1,
                "A genuine player vassal moves with influence preservation even when marked as a minor clan");
            check(!Move(noble, source, target, faction) && _captures == 1, "Repeated transfer does not repeat influence restoration");
            check(!Move(noble, target, target) && !Move(noble, null, target), "Same-realm and missing-source transfers are harmless");

            Reset();
            noble = House("first_noble", source);
            var departed = House("departed_noble", source);
            var hired = House("new_company", source);
            _player = House("player", source, mercenary: true);
            _afterJoin = clan => { if (clan == noble) { Realms[departed] = other; Mercenaries.Add(hired); } };
            Call(null, "TransferClansToKingdom", source, target, noble);
            check(Realms[_player] == null && Events[0] == "dismiss:player", "Bulk reunification dismisses mercenaries before moving the preferred ruler");
            check(Realms[departed] == other && Realms[hired] == null && !Events.Any(e => e.StartsWith("CONVERTED:")),
                "Bulk transfers recheck membership and mercenary service after earlier transfers fire callbacks");
            int count = Events.Count;
            Call(null, "TransferClansToKingdom", source, target, noble);
            check(Events.Count == count, "Bulk transfer recovery does not repeat completed moves or contract dismissals");

            Reset();
            _player = House("player", source, mercenary: true);
            npc = House("npc_company", source, mercenary: true, minor: true);
            _afterDismiss = clan => { if (clan == _player) Realms[npc] = other; };
            Call(null, "ReleaseMercenariesFromKingdom", source);
            check(Realms[npc] == other && Mercenaries.Contains(npc) && Events.Count == 1,
                "Contract dismissal rechecks the employer instead of ending a newly changed contract");

            Reset();
            var monarch = House("queen", target); var prince = House("prince", source);
            noble = House("supporter", source); Rulers[target] = monarch;
            _player = House("player", source, mercenary: true);
            npc = House("npc_company", source, mercenary: true, minor: true);
            var record = Challenge(source, target, prince, monarch);
            Call(resolver, "ResumeChallengeOutcome", record);
            check(record.WarCrownTransferred && Rulers[target] == prince && Realms[prince] == target && Realms[noble] == target,
                "Succession victory recovery still installs the prince and reunifies noble supporters");
            check(Realms[_player] == null && Realms[npc] == null && !Mercenaries.Contains(_player)
                && !Events.Any(e => e.StartsWith("CONVERTED:")), "Recovered prince victory leaves player and NPC mercenaries independent");
            check(Events.Take(2).All(e => e.StartsWith("dismiss:")), "Recovered victory releases all mercenaries before its first noble transfer");
            count = Events.Count;
            Call(resolver, "ResumeChallengeOutcome", record);
            check(Events.Count == count, "A repeated victory recovery does not rehire or transfer dismissed mercenaries");

            Reset();
            monarch = House("queen", target); prince = House("prince", source); Rulers[target] = monarch;
            noble = House("supporter", source); _player = House("player", source, mercenary: true);
            record = Challenge(source, target, prince, monarch);
            _failJoin = noble;
            Call(resolver, "ResumeChallengeOutcome", record);
            check(!record.WarCrownTransferred && record.Failure == "Injected transfer interruption" && Realms[_player] == null,
                "An interrupted victory leaves dismissed mercenaries independent and the Crown stage retryable");
            _failJoin = null;
            Call(resolver, "ResumeChallengeOutcome", record);
            check(record.WarCrownTransferred && Realms[noble] == target && Events.Count(e => e == "dismiss:player") == 1,
                "Retry completes remaining noble transfers without reabsorbing the former mercenary");

            Reset();
            monarch = House("queen", target); prince = House("prince", source); Rulers[target] = monarch;
            _player = House("player", source, mercenary: true); _failDismiss = _player;
            record = Challenge(source, target, prince, monarch);
            Call(resolver, "ResumeChallengeOutcome", record);
            check(!record.WarCrownTransferred && Realms[prince] == source && record.Failure == "Mercenary dismissal pending",
                "A failed contract dismissal defers recovery before any noble transfer");
            _failDismiss = null;
            Call(resolver, "ResumeChallengeOutcome", record);
            check(record.WarCrownTransferred && Realms[_player] == null, "Recovery can retry after a previously blocked contract dismissal");

            Reset();
            prince = House("prince", source); monarch = House("queen", target);
            _player = House("rebel_player", source, mercenary: true);
            npc = House("crown_company", target, mercenary: true, minor: true);
            var collapse = new CivilWarCollapseRecord { Stage = 2, WinnerRealm = source, Parent = target,
                Successor = other, WinnerHouse = prince, Winner = Faction(prince, target) };
            Call(resolver, "ResumeCollapse", collapse);
            check(Realms[prince] == other && Realms[monarch] == other, "Collapse recovery reunifies noble clans from both sides into the successor");
            check(Realms[_player] == null && Realms[npc] == null && Events.Take(2).All(e => e.StartsWith("dismiss:"))
                && !Events.Any(e => e.StartsWith("CONVERTED:")), "Collapse recovery dismisses both rebel and Crown mercenaries before either realm transfers");
            count = Events.Count;
            Call(resolver, "ResumeCollapse", collapse);
            check(Events.Count == count, "Deferred collapse recovery never reabsorbs either side's dismissed mercenaries");

            // Exercise the public entry points too: the previous tests only covered journals.
            var manager = new FactionManagerBehavior();
            behaviors.AddBehavior(manager);
            _activeFactions = (List<FactionObject>)AccessTools.Field(typeof(FactionManagerBehavior), "_activeFactions").GetValue(manager);
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ResolveTrackedRebelKingdom"), nameof(Tracked));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "CountStrongholds"), nameof(Strongholds));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "TrySettleSatisfiedCivilWarDemand"), nameof(No));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "CompleteCivilWarTracker"), nameof(Write));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ApplyCivilWarPeaceIfNeeded"), nameof(Write));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "PacifyIdeologies"), nameof(Write));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "CaptureCurrentCivilWarFiefSnapshot"), nameof(Skip));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "DrainAndDestroyRebelKingdom"), nameof(Drain));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ApplyPacifiedCooldowns"), nameof(Write));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ActivateCompatibilityRepairWindow"), nameof(Skip));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ApplyChallengeAwareVictoryRewards"), nameof(Write));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "FlushTribunalReactions"), nameof(Skip));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "TryQueueChallengeTribunal"), nameof(No));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ApplyPostWarConsequences"), nameof(Tribunal));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "RemoveFaction"), nameof(Remove));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.Method(typeof(SuccessionChallengeBehavior), "CanWinChallenge"), nameof(Yes));
            Patch(AccessTools.Method(typeof(SuccessionChallengeBehavior), "CompleteWarOutcome"), nameof(Skip));
            var pendingField = AccessTools.Field(typeof(CivilWarResolutionBehavior), "_pendingMercenarySettlements");
            var oldSave = new Store { IsSaving = false };
            oldSave.Values["BC_PendingMercenarySettlements"] = null;
            var oldLoaded = new CivilWarResolutionBehavior(); oldLoaded.SyncData(oldSave);
            check(((List<CivilWarMercenarySettlementRecord>)pendingField.GetValue(oldLoaded)).Count == 0,
                "Older saves without pending mercenary settlements load with an empty retry list");
            foreach (int outcome in new[] { 1, 2, 3 })
            {
                Reset(); _settlementWrites = 0; _tribunalClans = null; _activeFactions.Clear();
                resolver = new CivilWarResolutionBehavior();
                behaviors.Items.RemoveAll(b => b is CivilWarResolutionBehavior); behaviors.AddBehavior(resolver);
                prince = House("prince", source); monarch = House("queen", target);
                Rulers[source] = prince; Rulers[target] = monarch;
                _player = House("player", source, mercenary: true);
                npc = House("company", source, mercenary: true, minor: true);
                faction = Faction(prince, target);
                AccessTools.Field(typeof(FactionObject), "_type").SetValue(faction, FactionType.Glory);
                _activeFactions.Add(faction);
                _failDismiss = npc;
                var cause = new TextObject("{=BC_Result_Landless}Both sides have lost their last strongholds. Neither has been able to enforce its demands.");
                void Resolve()
                {
                    if (outcome == 1) resolver.ResolveRebelVictory(faction, source);
                    else if (outcome == 2) resolver.ResolveLiegeVictory(faction, source);
                    else Call(resolver, "ResolveWhitePeace", faction, source, true, cause);
                }
                Resolve();
                check(_settlementWrites == 0 && _activeFactions.Contains(faction) && Realms[prince] == source,
                    "Blocked initial settlement preserves tracker, peace, tribunals and noble membership: " + outcome);
                var pending = (List<CivilWarMercenarySettlementRecord>)pendingField.GetValue(resolver);
                check(pending.Count == 1 && pending[0].Outcome == outcome && Realms[_player] == null,
                    "A partially dismissed roster retains exactly one pending outcome: " + outcome);
                Resolve();
                check(pending.Count == 1 && Events.Count(e => e == "dismiss:player") == 1,
                    "Repeated blocked settlement does not repeat a successful dismissal: " + outcome);
                var store = new Store(); resolver.SyncData(store); store.IsSaving = false;
                var loaded = new CivilWarResolutionBehavior(); loaded.SyncData(store);
                check(((List<CivilWarMercenarySettlementRecord>)pendingField.GetValue(loaded)).Single().Outcome == outcome
                    && (outcome != 3 || pending[0].DemandSatisfied && pending[0].Cause == cause),
                    "SyncData preserves pending outcome and white-peace context: " + outcome);
                behaviors.Items.RemoveAll(b => b is CivilWarResolutionBehavior); behaviors.AddBehavior(loaded);
                _failDismiss = null;
                Call(loaded, "ResumePendingMercenarySettlements");
                check(_settlementWrites > 0 && !_activeFactions.Contains(faction)
                    && Realms[_player] == null && Realms[npc] == null
                    && !Events.Any(e => e.StartsWith("CONVERTED:")) && pending.Count == 0,
                    "Loaded retry completes initial settlement without absorbing mercenaries: " + outcome);
                if (outcome == 2)
                    check(_tribunalClans != null && !_tribunalClans.Contains(_player) && !_tribunalClans.Contains(npc),
                        "Dismissed mercenaries never enter the loyalist tribunal after their service flags clear");
                count = _settlementWrites;
                Call(loaded, "ResumePendingMercenarySettlements");
                check(count == _settlementWrites, "Completed pending settlement does not replay rewards or bookkeeping: " + outcome);
            }

            Reset();
            _player = House("blocked_player", source, mercenary: true); _failDismiss = _player;
            check(!Move(_player, source, target) && Realms[_player] == source && !Events.Any(e => e.StartsWith("CONVERTED:")),
                "A no-op dismissal cannot fall through to noble transfer");
            _failDismiss = null; _throwDismiss = _player;
            check(!Move(_player, source, target) && Realms[_player] == source,
                "A departure exception is contained without converting the mercenary");

            _throwDismiss = null; _failDismiss = _player;
            harmony.Unpatch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "DrainAndDestroyRebelKingdom"),
                HarmonyPatchType.Prefix, harmony.Id);
            check(!(bool)Call(resolver, "DrainAndDestroyRebelKingdom", source, target, true, true)
                && Realms[_player] == source
                && ((List<string>)AccessTools.Field(typeof(CivilWarResolutionBehavior), "_pendingRebelCleanupOrder")
                    .GetValue(resolver)).Contains(source.StringId),
                "Blocked final cleanup retains the occupied shell and queues a later drain");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
            Reset();
        }
    }
}

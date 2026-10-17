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
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static partial class DomesticEstateTests
{
    private static readonly Assembly Mod = typeof(CrossClanEstateRecord).Assembly;
    private static readonly Type Policy = Mod.GetType("BellumCivile.EstateInheritancePolicy", true);
    private static readonly Type Laws = Mod.GetType("BellumCivile.SuccessionLawHelper", true);
    private static World _world;
    private static int _preparations;

    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase behavior) => Items.Add(behavior);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> behaviors) => Items.AddRange(behaviors);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private sealed class World
    {
        internal readonly List<Clan> Houses = new List<Clan>();
        internal readonly List<Kingdom> Realms = new List<Kingdom>();
        internal readonly Dictionary<Clan, Kingdom> Allegiance = new Dictionary<Clan, Kingdom>();
        internal readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
        internal readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
        internal readonly Dictionary<Hero, float> Ages = new Dictionary<Hero, float>();
        internal readonly Dictionary<string, Town> Towns = new Dictionary<string, Town>();
        internal readonly Dictionary<Kingdom, FactionObject> Rebels = new Dictionary<Kingdom, FactionObject>();
        internal readonly Dictionary<Kingdom, Kingdom> Feuds = new Dictionary<Kingdom, Kingdom>();
        internal readonly HashSet<Clan> EliminatedHouses = new HashSet<Clan>();
        internal readonly HashSet<Kingdom> EliminatedRealms = new HashSet<Kingdom>();
        internal readonly HashSet<Clan> Mercenaries = new HashSet<Clan>();
        internal readonly HashSet<Clan> MinorHouses = new HashSet<Clan>();
        internal readonly HashSet<Clan> Bandits = new HashSet<Clan>();
        internal readonly HashSet<string> Allocation = new HashSet<string>();
        internal readonly Dictionary<Kingdom, FeudalTitleRecord> Crowns = new Dictionary<Kingdom, FeudalTitleRecord>();
        internal readonly FeudalTitleBehavior Titles = new FeudalTitleBehavior();
        internal readonly PartitionSuccessionBehavior Partition = new PartitionSuccessionBehavior();
        internal readonly CrownAccessionBehavior Accessions = new CrownAccessionBehavior();
        internal readonly List<FeudalTitleRecord> Records = new List<FeudalTitleRecord>();
        internal SuccessionLawSet Law = new SuccessionLawSet(GenderSuccessionLaw.Equal, HouseSuccessionLaw.Primogeniture);
        internal Kingdom Realm, Foreign;
        internal Clan Source, Domestic, Abroad, Crown, Player;
        internal Hero Donor, Monarch;
        internal bool PartitionEnabled = true;
        internal int PersonalReserve = 1;

        internal void Initialize()
        {
            var campaign = Blank<Campaign>();
            var manager = new Manager();
            manager.Items.AddRange(new CampaignBehaviorBase[] { Titles, Partition, Accessions,
                Blank<FactionManagerBehavior>(), Blank<ClaimFeudWarBehavior>() });
            campaign.AddCampaignBehaviorManager(manager);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Realm = RealmObject("domestic"); Foreign = RealmObject("foreign");
            Source = House("source", Realm); Domestic = House("domestic_house", Realm);
            Abroad = House("foreign_house", Foreign); Crown = House("crown_house", Realm);
            Donor = Person("donor", Source, 65); Monarch = Person("monarch", Crown, 55);
            Rulers[Realm] = Crown; Rulers[Foreign] = Abroad;
        }

        internal Kingdom RealmObject(string id)
        {
            var realm = Blank<Kingdom>(); realm.StringId = id; Realms.Add(realm); return realm;
        }

        internal Clan House(string id, Kingdom realm)
        {
            var house = Blank<Clan>(); house.StringId = id;
            Houses.Add(house); Allegiance[house] = realm; return house;
        }

        internal Hero Person(string id, Clan house, float age, Hero parent = null, bool female = false)
        {
            var hero = Blank<Hero>(); hero.StringId = id;
            Set(hero, "_children", new MBList<Hero>());
            Set(hero, "_heroState", Hero.CharacterStates.Active);
            Set(hero, "_clan", house);
            AccessTools.Property(typeof(Hero), "IsFemale").SetValue(hero, female);
            if (parent != null)
            {
                if (parent.IsFemale) hero.Mother = parent; else hero.Father = parent;
            }
            Ages[hero] = age;
            if (!Heads.ContainsKey(house)) Heads[house] = hero;
            return hero;
        }

        internal Town Land(string id, float prosperity = 100)
        {
            var settlement = Blank<Settlement>(); settlement.StringId = id;
            var party = Blank<PartyBase>();
            AccessTools.Property(typeof(PartyBase), "Settlement").SetValue(party, settlement);
            var town = Blank<Town>(); town.StringId = "town_component_" + id;
            Set(town, "_owner", party); Set(town, "_ownerClan", Source); Set(town, "_prosperity", prosperity);
            Set(settlement, "Town", town);
            Towns.Add(id, town); return town;
        }

        internal FeudalTitleRecord Title(string id, FeudalTitleType rank, string parent = "", string land = "")
        {
            var title = new FeudalTitleRecord(id, id, rank, Source.StringId, Source.StringId,
                parent, land, Realm.StringId, 0, 0);
            Records.Add(title); IndexTitles(); return title;
        }

        internal void IndexTitles()
        {
            Set(Titles, "_titlesById", Records.ToDictionary(t => t.TitleId));
            Set(Titles, "_childrenByParentTitleId", Records.Where(t => t.ParentTitleId != "")
                .GroupBy(t => t.ParentTitleId).ToDictionary(g => g.Key, g => g.Select(t => t.TitleId).ToList()));
            Set(Titles, "_childrenByDeFactoParentTitleId", Records.Where(t => t.DeFactoParentTitleId != "")
                .GroupBy(t => t.DeFactoParentTitleId).ToDictionary(g => g.Key, g => g.Select(t => t.TitleId).ToList()));
            Set(Titles, "_titlesByDeJureHolder", Records.GroupBy(t => t.DeJureHolderClanId)
                .ToDictionary(g => g.Key, g => g.Select(t => t.TitleId).ToList()));
            Set(Titles, "_titlesByDeFactoHolder", Records.GroupBy(t => t.DeFactoHolderClanId)
                .ToDictionary(g => g.Key, g => g.Select(t => t.TitleId).ToList()));
            Set(Titles, "_baronyTitleBySettlementId", Records.Where(t => t.TitleType == FeudalTitleType.Barony)
                .ToDictionary(t => t.CapitalSettlementId, t => t.TitleId));
        }

        internal CrossClanEstateRecord Project() => (CrossClanEstateRecord)Call(Policy, "BuildProjection", null, Source, Donor);
        internal List<CrossClanEstateRecord> Captured => (List<CrossClanEstateRecord>)
            AccessTools.Field(typeof(PartitionSuccessionBehavior), "_crossClanEstates").GetValue(Partition);
        internal List<PendingPartitionSuccessionRecord> PendingPartitions => (List<PendingPartitionSuccessionRecord>)
            AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").GetValue(Partition);
        internal List<CrossClanEstateShare> CrownExecutorShares(IReadOnlyList<Hero> heirs, string primaryId)
        {
            var planner = Mod.GetType("BellumCivile.FeudalInheritancePlanner");
            var secondary = PartitionEnabled ? heirs.Skip(1).ToList() : new List<Hero>();
            var plan = AccessTools.GetDeclaredMethods(planner).Single(m => m.Name == "BuildPlan" && m.GetParameters().Length == 7)
                .Invoke(null, new object[] { Source, Donor, Source.Fiefs.ToList(), secondary,
                    Titles.GetTitlesHeldByClan(Source, deJure: true), PersonalReserve, primaryId });
            if (PartitionEnabled && secondary.Count > 0)
            {
                var args = new object[] { plan, Titles, Records, PersonalReserve, primaryId, null };
                if (!(bool)Call(planner, "TryBuildCrownFirstTitlePackages", null, args))
                    throw new InvalidOperationException("Crown-first fixture rejected: " + args[5]);
            }
            var capture = new object[] { plan, heirs[0], secondary, null, null };
            if (!(bool)Call(Mod.GetType("BellumCivile.PartitionEstateManifest"), "TryCapture", null, capture))
                throw new InvalidOperationException("Executor estate manifest rejected: " + capture[4]);
            return (List<CrossClanEstateShare>)capture[3];
        }
        internal bool Prepare(CrossClanEstateRecord estate, CrossClanEstateShare share) =>
            (bool)Call(typeof(PartitionSuccessionBehavior), "PrepareDomesticEstateShare", Partition, estate, share);
        internal bool Committed(CrossClanEstateRecord estate, CrossClanEstateShare share) =>
            (bool)Call(typeof(PartitionSuccessionBehavior), "HasCommittedEstateShare", null, estate, share);
        internal bool CanStart(CrossClanEstateRecord estate) =>
            (bool)Call(typeof(PartitionSuccessionBehavior), "CanStartDomesticEstateDelivery", Partition, estate, Titles);
        internal CrossClanEstateRecord Pending(params Hero[] heirs) => new CrossClanEstateRecord
        {
            Source = Source, Realm = Realm, Deceased = Donor, PolicyVersion = 1,
            DomesticHeirs = heirs.ToList(), GenderLaw = Law.GenderLaw, SuccessionLaw = Law.SuccessionLaw
        };
        internal CrownAccessionRecord Advance(Hero heir)
        {
            var record = new CrownAccessionRecord { Realm = Foreign, IncomingSourceHead = Donor, Heir = heir,
                IncomingHousePrepared = true, HasLivingEndowment = true, CreatesCadet = true,
                EndowmentSettled = true, DeliveredGold = 25, Completed = true };
            ((List<CrownAccessionRecord>)AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions")
                .GetValue(Accessions)).Add(record);
            return record;
        }
    }

    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    private static object Call(Type type, string method, object target, params object[] args) => AccessTools.Method(type, method).Invoke(target, args);
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(_world.Houses); return false; }
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(_world.Realms); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { _world.Allegiance.TryGetValue(__instance, out __result); return false; }
    private static bool Head(Clan __instance, ref Hero __result) { _world.Heads.TryGetValue(__instance, out __result); return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { _world.Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool Player(ref Clan __result) { __result = _world.Player; return false; }
    private static bool Heroes(Clan __instance, ref MBReadOnlyList<Hero> __result)
    { __result = new MBReadOnlyList<Hero>(_world.Ages.Keys.Where(h => h.Clan == __instance).ToList()); return false; }
    private static bool Fiefs(Clan __instance, ref MBReadOnlyList<Town> __result)
    { __result = new MBReadOnlyList<Town>(_world.Towns.Values.Where(t => t.OwnerClan == __instance).ToList()); return false; }
    private static bool Age(Hero __instance, ref float __result) { __result = _world.Ages[__instance]; return false; }
    private static bool HouseGone(Clan __instance, ref bool __result) { __result = _world.EliminatedHouses.Contains(__instance); return false; }
    private static bool RealmGone(Kingdom __instance, ref bool __result) { __result = _world.EliminatedRealms.Contains(__instance); return false; }
    private static bool Mercenary(Clan __instance, ref bool __result) { __result = _world.Mercenaries.Contains(__instance); return false; }
    private static bool Minor(Clan __instance, ref bool __result) { __result = _world.MinorHouses.Contains(__instance); return false; }
    private static bool Bandit(Clan __instance, ref bool __result) { __result = _world.Bandits.Contains(__instance); return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Skip() => false;
    private static bool NoRegency(ref RegencyBehavior __result) { __result = null; return false; }
    private static bool Adult(ref int __result) { __result = 18; return false; }
    private static bool PartitionEnabled(ref bool __result) { __result = _world.PartitionEnabled; return false; }
    private static bool Reserve(ref int __result) { __result = _world.PersonalReserve; return false; }
    private static bool Now(ref CampaignTime __result) { __result = default(CampaignTime); return false; }
    private static bool Law(object[] __args, MethodBase __originalMethod, ref SuccessionLawSet __result)
    {
        __result = _world.Law;
        if (__args.Length > 1) __args[1] = Activator.CreateInstance(__originalMethod.GetParameters()[1].ParameterType.GetElementType());
        return false;
    }
    private static bool CrownTitle(Kingdom __0, ref FeudalTitleRecord __result) { _world.Crowns.TryGetValue(__0, out __result); return false; }
    private static bool SettlementById(string __0, ref Settlement __result)
    { __result = _world.Towns.TryGetValue(__0, out var town) ? town.Settlement : null; return false; }
    private static bool Owner(Settlement __instance, ref Clan __result)
    { __result = _world.Towns.TryGetValue(__instance.StringId, out var town) ? town.OwnerClan : null; return false; }
    private static bool AwaitingAllocation(Settlement __0, ref bool __result)
    { __result = __0 != null && _world.Allocation.Contains(__0.StringId); return false; }
    private static bool Rebel(Kingdom __0, ref FactionObject __result) { _world.Rebels.TryGetValue(__0, out __result); return false; }
    private static bool TrackedRebel(string __0, ref FactionObject __result)
    { __result = _world.Rebels.FirstOrDefault(p => p.Key.StringId == __0).Value; return false; }
    private static bool Feud(Kingdom __0, Kingdom __1, ref bool __result)
    { __result = _world.Feuds.TryGetValue(__0, out var parent) && parent == __1; return false; }
    private static bool CountPreparation() { _preparations++; return true; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Estate fixture"); return false; }
    private static bool Legalize(Clan __0, FeudalTitleRecord __1, bool __3)
    { __1.SetDeJureHolder(__0.StringId); if (!__3) __1.SetDeFactoHolder(__0.StringId); return false; }
    private static bool EmptyGold(CrownAccessionRecord __0, ref bool __result)
    {
        if (_mixedBalances != null && _world.PendingPartitions.Any(p => p.MixedEstate != null))
        {
            if (!__0.GoldDebited)
            {
                __0.DeliveredGold = Math.Min(__0.EndowmentGold, _mixedBalances[__0.EndowmentDonor]);
                _mixedBalances[__0.EndowmentDonor] -= __0.DeliveredGold; __0.GoldDebited = true;
            }
            if (!__0.GoldCredited)
            {
                _mixedBalances[__0.GoldRecipient] += __0.DeliveredGold; __0.GoldCredited = true;
            }
            __result = true; return false;
        }
        if (__0.EndowmentGold != 0) throw new InvalidOperationException("Escheat must not invent a gold award");
        __0.GoldDebited = __0.GoldCredited = true; __result = true; return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var oldCampaign = Campaign.Current;
        var harmony = new Harmony("bellum.tests.domestic_estates");
        var failures = new List<string>();
        void Patch(MethodBase method, string prefix)
        {
            if (method == null) throw new MissingMethodException("Domestic estate fixture target for " + prefix);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(DomesticEstateTests), prefix));
        }
        void Getter(Type type, string property, string prefix) => Patch(AccessTools.PropertyGetter(type, property), prefix);
        void Case(string name, Action<Action<bool, string>> body)
        {
            try
            {
                _world = new World(); _world.Initialize(); _preparations = 0;
                body((condition, detail) => check(condition, "Domestic estate: " + name + ": " + detail));
            }
            catch (Exception ex)
            {
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                failures.Add(name + ": " + ex.Message);
                Console.Error.WriteLine("FAIL: Domestic estate: " + name + ": " + ex);
            }
        }
        try
        {
            Getter(typeof(Clan), "All", nameof(AllClans)); Getter(typeof(Kingdom), "All", nameof(AllRealms));
            Getter(typeof(Clan), "Kingdom", nameof(Realm)); Getter(typeof(Clan), "Leader", nameof(Head));
            Getter(typeof(Kingdom), "RulingClan", nameof(Ruler)); Getter(typeof(Clan), "PlayerClan", nameof(Player));
            Getter(typeof(Clan), "Heroes", nameof(Heroes)); Getter(typeof(Clan), "Fiefs", nameof(Fiefs));
            Getter(typeof(Clan), "IsEliminated", nameof(HouseGone)); Getter(typeof(Kingdom), "IsEliminated", nameof(RealmGone));
            Getter(typeof(Clan), "IsUnderMercenaryService", nameof(Mercenary)); Getter(typeof(Clan), "IsMinorFaction", nameof(Minor));
            Getter(typeof(Clan), "IsClanTypeMercenary", nameof(No)); Getter(typeof(CampaignTime), "Now", nameof(Now));
            Getter(typeof(CampaignTime), "IsFuture", nameof(No));
            Getter(typeof(Clan), "IsBanditFaction", nameof(Bandit)); Getter(typeof(Hero), "Age", nameof(Age));
            Getter(typeof(Hero), "IsWanderer", nameof(No)); Getter(typeof(Hero), "IsNotable", nameof(No));
            Getter(typeof(RegencyBehavior), "Instance", nameof(NoRegency));
            Patch(AccessTools.Method(Laws, "GetAgeOfMajority"), nameof(Adult));
            foreach (var method in Laws.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "GetLawsForClan" || m.Name == "GetLawsForKingdom")) Patch(method, nameof(Law));
            Getter(Mod.GetType("BellumCivile.BellumCivileOptions"), "EnablePartitionSuccession", nameof(PartitionEnabled));
            Getter(Mod.GetType("BellumCivile.BellumCivileOptions"), "PartitionSuccessionMainHeirReservedFiefs", nameof(Reserve));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetKingdomPoliticalTitle"), nameof(CrownTitle));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetRealmSovereignTitle", new[] { typeof(Kingdom), typeof(FeudalHierarchyMode) }), nameof(CrownTitle));
            Patch(AccessTools.Method(typeof(Settlement), "Find"), nameof(SettlementById));
            Getter(typeof(Settlement), "OwnerClan", nameof(Owner));
            Patch(AccessTools.Method(typeof(FiefDeliberationBehavior), "IsAwaitingAllocation"), nameof(AwaitingAllocation));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "GetFactionByRebelKingdom"), nameof(Rebel));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "GetFactionByTrackedRebelKingdomId"), nameof(TrackedRebel));
            Patch(AccessTools.Method(typeof(ClaimFeudWarBehavior), "IsTemporaryFeudKingdomForParent"), nameof(Feud));
            Patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "PrepareDomesticEstateShare"), nameof(CountPreparation));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.BellumCivileLogger"), "Log", new[] { typeof(string) }), nameof(Skip));
            Getter(typeof(Hero), "Name", nameof(Name)); Getter(typeof(Clan), "Name", nameof(Name));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "LegalizeTitleInheritance",
                new[] { typeof(Clan), typeof(FeudalTitleRecord), typeof(string), typeof(bool) }), nameof(Legalize));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "RegisterEstateReversionClaim"), nameof(Skip));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "DeliverAbdicationGold"), nameof(EmptyGold));
            foreach (var method in Mod.GetType("BellumCivile.BellumCivileNotifications").GetMethods()
                .Where(m => m.Name == "Show")) Patch(method, nameof(Skip));
            ConfigureMixedFixture(Patch, Getter);

            Case("domestic blood, foreign exclusion and minors", c =>
            {
                var w = _world;
                var foreign = w.Person("eldest_abroad", w.Abroad, 35, w.Donor);
                var infant = w.Person("domestic_infant", w.Domestic, 1, w.Donor);
                var unrelated = w.Person("unrelated_house_member", w.Source, 45);
                var heirs = (List<Hero>)Call(Policy, "OrderDomesticHeirs", null, w.Source, w.Donor, w.Realm, w.Law);
                c(heirs.SequenceEqual(new[] { infant }), "infant in another domestic house is the sole heir, not foreign elder or unrelated adult");
                Set(infant, "_heroState", Hero.CharacterStates.Prisoner);
                heirs = (List<Hero>)Call(Policy, "OrderDomesticHeirs", null, w.Source, w.Donor, w.Realm, w.Law);
                c(heirs.Contains(infant), "captivity does not extinguish domestic blood");
                w.EliminatedHouses.Add(w.Domestic);
                c(((List<Hero>)Call(Policy, "OrderDomesticHeirs", null, w.Source, w.Donor, w.Realm, w.Law)).Count == 0,
                    "eliminated domestic house does not make foreign blood eligible");
            });

            Case("temporary realm attachment", c =>
            {
                var w = _world;
                var rebel = w.RealmObject("domestic_rebels_house");
                var feud = w.RealmObject("bc_feud_domestic");
                var faction = Blank<FactionObject>(); Set(faction, "_parentKingdom", w.Realm); w.Rebels[rebel] = faction;
                w.Feuds[feud] = w.Realm;
                foreach (var realm in new[] { w.Realm, rebel, feud })
                {
                    w.Allegiance[w.Domestic] = realm;
                    c((bool)Call(Policy, "IsDomesticHouse", null, w.Domestic, w.Realm), realm.StringId + " stays attached");
                    c(Call(Policy, "ResolvePermanentRealm", null, w.Domestic) == w.Realm, realm.StringId + " resolves original realm");
                }
                w.Allegiance[w.Domestic] = w.Foreign;
                c(!(bool)Call(Policy, "IsDomesticHouse", null, w.Domestic, w.Realm), "permanent foreign realm is not attached");
                w.Allegiance[w.Domestic] = null;
                c(!(bool)Call(Policy, "IsDomesticHouse", null, w.Domestic, w.Realm), "unaffiliated house is not domestic");
            });

            Case("noble house eligibility", c =>
            {
                var w = _world;
                foreach (var exclusion in new[] { w.Mercenaries, w.Bandits, w.MinorHouses, w.EliminatedHouses })
                {
                    exclusion.Add(w.Domestic);
                    c(!(bool)Call(Policy, "IsDomesticHouse", null, w.Domestic, w.Realm), "excluded house cannot inherit ordinary property");
                    exclusion.Clear();
                }
                w.MinorHouses.Add(w.Domestic); w.Player = w.Domestic;
                c((bool)Call(Policy, "IsDomesticHouse", null, w.Domestic, w.Realm), "player minor-faction exception survives");
            });

            foreach (bool femaleDonor in new[] { false, true })
            foreach (var gender in new[] { GenderSuccessionLaw.Equal, GenderSuccessionLaw.MaleOnly, GenderSuccessionLaw.FemaleOnly })
                Case("donor " + (femaleDonor ? "mother" : "father") + " / " + gender, c =>
                {
                    var w = _world;
                    AccessTools.Property(typeof(Hero), "IsFemale").SetValue(w.Donor, femaleDonor);
                    w.Law = new SuccessionLawSet(gender, HouseSuccessionLaw.Primogeniture);
                    var son = w.Person("son", w.Domestic, 10, w.Donor);
                    var daughter = w.Person("daughter", w.Domestic, 20, w.Donor, true);
                    w.Person("foreign_son", w.Abroad, 30, w.Donor);
                    w.Land("ordinary"); w.Title("ordinary_barony", FeudalTitleType.Barony, land: "ordinary");
                    var estate = w.Project();
                    var expected = gender == GenderSuccessionLaw.MaleOnly ? son : daughter;
                    c(estate.Shares.Single().Heir == expected && estate.Shares[0].DomesticOnly, "domestic gender order allocates ordinary property");
                    c(estate.GenderLaw == gender && estate.SuccessionLaw == w.Law.SuccessionLaw && estate.PolicyVersion == 1,
                        "projection snapshots donor law and policy");
                });

            foreach (bool partition in new[] { false, true })
                Case("foreign-only ordinary estate / partition " + partition, c =>
                {
                    var w = _world; w.PartitionEnabled = partition;
                    w.Person("foreign_child", w.Abroad, 30, w.Donor);
                    w.Person("unrelated_native_successor", w.Source, 40);
                    w.Land("ordinary"); w.Title("ordinary_barony", FeudalTitleType.Barony, land: "ordinary");
                    var share = w.Project().Shares.Single();
                    c(share.IsEscheat && share.DomesticOnly && !share.ReceivesGold && share.Heir == w.Monarch,
                        "no domestic blood reverts the exact ordinary estate to Crown without gold");
                });

            Case("genuine Crown root classification", c =>
            {
                var w = _world;
                bool Root(FeudalTitleRecord title, string political = "", bool hereditary = true) =>
                    (bool)Call(Policy, "IsSovereignEstateRoot", null, w.Titles, title, w.Source, political, hereditary);
                var county = w.Title("county_crown", FeudalTitleType.County);
                c(Root(county, county.TitleId), "low-rank political hereditary Crown is sovereign");
                c(!Root(county, county.TitleId, false) && !Root(county), "elective Crown or ordinary county is not a blood-property exception");
                var kingdom = w.Title("kingdom", FeudalTitleType.Kingdom);
                c(Root(kingdom), "fully controlled additional independent Crown is sovereign");
                var empire = w.Title("empire", FeudalTitleType.Empire);
                kingdom.SetDeFactoParentTitle(empire.TitleId);
                c(!Root(kingdom), "high-rank kingdom under active empire is ordinary");
                empire.SetActive(false);
                c(Root(kingdom), "inactive superior does not establish present subordination");
                kingdom.SetDeFactoParentTitle(kingdom.TitleId);
                c(Root(kingdom), "cycle does not bypass existing sovereign executor safeguard");
                kingdom.SetDeFactoHolder(w.Abroad.StringId);
                c(!Root(kingdom), "legal-only Crown is ordinary");
                kingdom.SetDeFactoHolder(w.Source.StringId); kingdom.SetDeJureHolder(w.Abroad.StringId);
                c(!Root(kingdom), "occupation without legal ownership is not inherited Crown authority");
                kingdom.SetDeJureHolder(w.Source.StringId); kingdom.SetActive(false);
                c(!Root(kingdom), "inactive Crown is not sovereign");
            });

            Case("landless historical Crown", c =>
            {
                var w = _world;
                var title = w.Title("historical", FeudalTitleType.Kingdom);
                title.SetAssociatedKingdom(w.Foreign.StringId); w.EliminatedRealms.Add(w.Foreign);
                c(!(bool)Call(Policy, "IsSovereignEstateRoot", null, w.Titles, title, w.Source, "", true),
                    "destroyed realm's unlanded dignity remains ordinary property");
            });

            foreach (bool partition in new[] { false, true })
                Case("primary Crown isolates loose property / partition " + partition, c =>
                {
                    var w = _world; w.PartitionEnabled = partition; w.Rulers[w.Realm] = w.Source;
                    var foreign = w.Person("royal_eldest", w.Abroad, 35, w.Donor);
                    var domestic = w.Person("domestic_younger", w.Domestic, 20, w.Donor);
                    w.Land("royal_home", 100); w.Land("loose", 900);
                    var crown = w.Title("county_crown", FeudalTitleType.County, land: "royal_home");
                    w.Crowns[w.Realm] = crown;
                    w.Title("royal_barony", FeudalTitleType.Barony, crown.TitleId, "royal_home");
                    w.Title("loose_barony", FeudalTitleType.Barony, land: "loose");
                    var estate = w.Project();
                    var royal = estate.Shares.Single(s => s.Titles.Contains(crown.TitleId));
                    var ordinary = estate.Shares.Single(s => s.Fiefs.Contains("loose"));
                    c(royal.Heir == foreign && !royal.DomesticOnly && royal.Fiefs.SequenceEqual(new[] { "royal_home" }),
                        "foreign hereditary Crown receives only its footprint");
                    c(ordinary.Heir == domestic && ordinary.DomesticOnly && !ordinary.Titles.Contains(crown.TitleId),
                        "loose ordinary property is independently assigned domestically");
                    c(estate.Shares.SelectMany(s => s.Titles).Distinct().Count() == 3
                        && estate.Shares.Sum(s => s.Titles.Count) == 3 && estate.Shares.Sum(s => s.Fiefs.Count) == 2,
                        "assets are conserved without overlap");
                });

            Case("secondary foreign Crown package", c =>
            {
                var w = _world; w.Rulers[w.Realm] = w.Source;
                var domestic = w.Person("eldest", w.Domestic, 35, w.Donor);
                var foreign = w.Person("younger_foreign", w.Abroad, 20, w.Donor);
                w.Land("first", 100); w.Land("second", 80); w.Land("loose", 10);
                var first = w.Title("first_crown", FeudalTitleType.Kingdom, land: "first"); w.Crowns[w.Realm] = first;
                w.Title("first_barony", FeudalTitleType.Barony, first.TitleId, "first");
                var second = w.Title("second_crown", FeudalTitleType.Kingdom, land: "second");
                w.Title("second_barony", FeudalTitleType.Barony, second.TitleId, "second");
                w.Title("loose_barony", FeudalTitleType.Barony, land: "loose");
                var estate = w.Project();
                var share = estate.Shares.Single(s => s.Titles.Contains(second.TitleId));
                c(share.Heir == foreign && !share.DomesticOnly && share.Fiefs.SequenceEqual(new[] { "second" }),
                    "additional sovereign package keeps unrestricted original beneficiary");
                c(estate.Shares.Single(s => s.Fiefs.Contains("loose")).Heir == domestic,
                    "additional foreign Crown does not absorb loose ordinary estate");
                c(estate.Shares.Where(s => s.Heir == domestic).Count(s => s.ReceivesGold) == 1,
                    "one heir with Crown and ordinary shares is paid once");
            });

            Case("Crown plus ordinary escheat", c =>
            {
                var w = _world; w.Rulers[w.Realm] = w.Source;
                var foreign = w.Person("sole_foreign", w.Abroad, 30, w.Donor);
                w.Land("royal"); w.Land("loose");
                var crown = w.Title("crown", FeudalTitleType.Kingdom, land: "royal"); w.Crowns[w.Realm] = crown;
                w.Title("royal_barony", FeudalTitleType.Barony, crown.TitleId, "royal");
                w.Title("loose_barony", FeudalTitleType.Barony, land: "loose");
                var estate = w.Project(); var escheat = estate.Shares.Single(s => s.IsEscheat);
                c(estate.Shares.Single(s => !s.IsEscheat).Heir == foreign && escheat.Fiefs.SequenceEqual(new[] { "loose" }),
                    "foreign Crown survives while only ordinary remainder escheats");
                c(!(bool)Call(typeof(PartitionSuccessionBehavior), "NeedsLandedSettlement", null, escheat, escheat.Heir),
                    "Crown reversion does not block accession waiting for its own outcome");
            });

            Case("title-only estate and allocation custody", c =>
            {
                var w = _world; var child = w.Person("child", w.Domestic, 8, w.Donor);
                w.Title("unlanded_duchy", FeudalTitleType.Duchy);
                var titleOnly = w.Project();
                c(titleOnly.Shares.Single().Heir == child && titleOnly.Shares[0].Titles.Contains("unlanded_duchy"),
                    "no personal fief does not lose legal-only ordinary titles");
                w.Land("custody"); w.Title("custody_barony", FeudalTitleType.Barony, land: "custody"); w.Allocation.Add("custody");
                var estate = w.Project();
                c(!estate.Shares.SelectMany(s => s.Fiefs).Contains("custody")
                    && !estate.Shares.SelectMany(s => s.Titles).Contains("custody_barony"), "unallocated Crown custody stays outside inheritance");
            });

            foreach (bool partition in new[] { false, true })
            foreach (int heirCount in new[] { 1, 2, 3 })
            foreach (int reserve in new[] { 1, 99 })
                Case("local Crown-first executor parity / partition " + partition + " / heirs " + heirCount + " / reserve " + reserve, c =>
                {
                    var w = _world; w.Rulers[w.Realm] = w.Source; w.PartitionEnabled = partition; w.PersonalReserve = reserve;
                    var heirs = Enumerable.Range(0, heirCount)
                        .Select(i => w.Person("local_child_" + i, w.Source, 35 - i * 5, w.Donor)).ToList();
                    w.Land("first", 100); w.Land("second", 80); w.Land("loose", 10);
                    var first = w.Title("first_crown", FeudalTitleType.Kingdom, land: "first"); w.Crowns[w.Realm] = first;
                    w.Title("first_barony", FeudalTitleType.Barony, first.TitleId, "first");
                    var second = w.Title("second_crown", FeudalTitleType.Kingdom, land: "second");
                    w.Title("second_barony", FeudalTitleType.Barony, second.TitleId, "second");
                    w.Title("loose_barony", FeudalTitleType.Barony, land: "loose");
                    var expected = w.CrownExecutorShares(heirs, first.TitleId);
                    var estate = w.Project();
                    c(estate.Shares.Count == expected.Count, "projection retains Crown-first executor beneficiary count");
                    foreach (var before in expected)
                    {
                        var after = estate.Shares.Single(s => s.Heir == before.Heir);
                        c(after.Primary == before.Primary && after.RootTitleId == before.RootTitleId
                            && (before.Primary || (after.PrimaryFiefId ?? "") == (before.PrimaryFiefId ?? ""))
                            && new HashSet<string>(after.Titles).SetEquals(before.Titles)
                            && new HashSet<string>(after.Fiefs).SetEquals(before.Fiefs),
                            "asset allocation matches Crown-first executor manifest for " + before.Heir.StringId);
                    }
                    c(estate.Shares.SelectMany(s => s.Titles).Count() == 5
                        && estate.Shares.SelectMany(s => s.Titles).Distinct().Count() == 5
                        && estate.Shares.SelectMany(s => s.Fiefs).Count() == 3
                        && estate.Shares.SelectMany(s => s.Fiefs).Distinct().Count() == 3,
                        "local branch has exact disjoint full-estate coverage");
                    w.Partition.CaptureCrossClanEstate(w.Donor);
                    c(w.Captured.Count == 0, "all-in-house preview keeps the existing partition executor");
                });

            Case("vassal-only coequal Crown executor parity", c =>
            {
                var w = _world; w.Rulers[w.Realm] = w.Source; w.PersonalReserve = 99;
                var primary = w.Person("primary", w.Source, 35, w.Donor);
                var secondary = w.Person("secondary", w.Source, 25, w.Donor);
                w.Land("first"); var vassalLand = w.Land("vassal"); Set(vassalLand, "_ownerClan", w.Domestic);
                var first = w.Title("first_crown", FeudalTitleType.Kingdom, land: "first"); w.Crowns[w.Realm] = first;
                w.Title("first_barony", FeudalTitleType.Barony, first.TitleId, "first");
                var second = w.Title("second_crown", FeudalTitleType.Kingdom, land: "vassal");
                var vassalTitle = w.Title("vassal_barony", FeudalTitleType.Barony, second.TitleId, "vassal");
                vassalTitle.SetDeJureHolder(w.Domestic.StringId); vassalTitle.SetDeFactoHolder(w.Domestic.StringId); w.IndexTitles();
                var expected = w.CrownExecutorShares(new[] { primary, secondary }, first.TitleId);
                var estate = w.Project();
                c(estate.Shares.Count == expected.Count && estate.Shares.Single(s => s.Heir == secondary).Titles.Contains(second.TitleId),
                    "secondary Crown does not require royal personal land");
                foreach (var before in expected)
                {
                    var after = estate.Shares.Single(s => s.Heir == before.Heir);
                    c(new HashSet<string>(after.Titles).SetEquals(before.Titles) && new HashSet<string>(after.Fiefs).SetEquals(before.Fiefs),
                        "vassal-only package matches actual executor for " + before.Heir.StringId);
                }
                c(!estate.Shares.SelectMany(s => s.Fiefs).Contains("vassal")
                    && !estate.Shares.SelectMany(s => s.Titles).Contains(vassalTitle.TitleId), "vassal assets are not the ruler's personal estate");
            });

            foreach (int reserve in new[] { 1, 99 })
            Case("excluded foreign ordinary heir keeps domestic coequal Crown executor / reserve " + reserve, c =>
            {
                var w = _world; w.Rulers[w.Realm] = w.Source; w.PersonalReserve = reserve;
                var primary = w.Person("domestic_primary", w.Source, 40, w.Donor);
                var secondary = w.Person("domestic_secondary", w.Source, 30, w.Donor);
                var excluded = w.Person("foreign_youngest", w.Abroad, 20, w.Donor);
                w.Land("first", 100); w.Land("second", 80); w.Land("loose", 10);
                var first = w.Title("first_crown", FeudalTitleType.Kingdom, land: "first"); w.Crowns[w.Realm] = first;
                w.Title("first_barony", FeudalTitleType.Barony, first.TitleId, "first");
                var second = w.Title("second_crown", FeudalTitleType.Kingdom, land: "second");
                w.Title("second_barony", FeudalTitleType.Barony, second.TitleId, "second");
                w.Title("loose_barony", FeudalTitleType.Barony, land: "loose");
                var expected = w.CrownExecutorShares(new[] { primary, secondary }, first.TitleId);
                var estate = w.Project();
                foreach (var before in expected)
                {
                    var actual = estate.Shares.Where(s => s.Heir == before.Heir).ToList();
                    c(actual.Count > 0 && new HashSet<string>(actual.SelectMany(s => s.Titles)).SetEquals(before.Titles)
                        && new HashSet<string>(actual.SelectMany(s => s.Fiefs)).SetEquals(before.Fiefs),
                        "mixed projection matches Crown-first executor assets for " + before.Heir.StringId);
                }
                c(estate.Shares.All(s => s.Heir.Clan == w.Source) && !estate.Shares.Any(s => s.Heir == excluded),
                    "foreign ordinary candidate receives no actual allocation");
                c(estate.Shares.Single(s => s.Titles.Contains(second.TitleId)).Heir == secondary,
                    "domestic second heir retains the coequal Crown package");
                w.Partition.CaptureCrossClanEstate(w.Donor);
                c(w.Captured.Count == 0, "excluded foreign candidate does not divert domestic Crown promotion into cross-clan settlement");
                Set(w.Donor, "_heroState", Hero.CharacterStates.Dead); w.Heads[w.Source] = primary;
                ((Dictionary<string, string>)AccessTools.Field(typeof(PartitionSuccessionBehavior), "_knownClanLeaderIds")
                    .GetValue(w.Partition))[w.Source.StringId] = w.Donor.StringId;
                Call(typeof(PartitionSuccessionBehavior), "OnHeroKilled", w.Partition,
                    w.Donor, null, KillCharacterAction.KillCharacterActionDetail.None, false);
                c(w.PendingPartitions.Count == 1 && w.PendingPartitions[0].CrownRoutingRequested,
                    "death still queues the established coequal-Crown executor");
            });

            Case("retained additional Crown preflight and executor", c =>
            {
                var w = _world; w.Rulers[w.Realm] = w.Source;
                var heir = w.Person("retaining_head", w.Source, 35, w.Donor); w.Heads[w.Source] = heir;
                var crown = w.Title("retained_crown", FeudalTitleType.Kingdom);
                var estate = w.Pending(heir);
                var share = new CrossClanEstateShare { Heir = heir, Primary = true, Titles = new List<string> { crown.TitleId } };
                estate.Shares.Add(share);
                bool Retains() => (bool)Call(typeof(PartitionSuccessionBehavior), "RetainsSovereignEstateInSource", null,
                    estate, share, crown);
                c(Retains() && w.CanStart(estate), "living legal head may retain a fully held Crown in its ruling house");
                share.Heir = w.Person("secondary", w.Source, 25, w.Donor);
                c(!Retains() && !w.CanStart(estate), "secondary in-house heir requires a separate sovereign executor");
                share.Heir = heir; share.CadetPlan = new CrownAccessionRecord();
                c(!Retains() && !w.CanStart(estate), "a cadet plan cannot masquerade as source-house retention");
                share.CadetPlan = null; w.Rulers[w.Realm] = w.Crown;
                c(!Retains() && !w.CanStart(estate), "non-ruling source cannot use the retention exception");
                w.Rulers[w.Realm] = w.Source;
                var pending = new CrownAccessionRecord { Realm = w.Realm };
                ((List<CrownAccessionRecord>)AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions")
                    .GetValue(w.Accessions)).Add(pending);
                c(!Retains() && !w.CanStart(estate), "pending political Crown blocks source retention");
                pending.Completed = true; crown.SetDeFactoHolder(w.Domestic.StringId);
                c(!Retains(), "legal-only Crown is not a fully held retained sovereign package");
                crown.SetDeFactoHolder(w.Source.StringId); share.Recipient = w.Domestic;
                c(!Retains() && !w.CanStart(estate), "an external recipient cannot use source retention");
                share.Recipient = null; Set(heir, "_heroState", Hero.CharacterStates.Dead);
                c(!Retains() && !w.CanStart(estate), "dead source head cannot retain sovereign authority");
                Set(heir, "_heroState", Hero.CharacterStates.Active);
                Call(typeof(PartitionSuccessionBehavior), "SettleCrossClanShare", w.Partition, estate, share, w.Titles, null);
                c(share.Completed && share.Recipient == w.Source && share.DeliveredTitles.SequenceEqual(new[] { crown.TitleId })
                    && share.SovereignTransfers.Count == 0 && crown.DeJureHolderClanId == w.Source.StringId
                    && crown.DeFactoHolderClanId == w.Source.StringId,
                    "executor completes retained Crown receipts without transfer, cadet creation or foreign integration");
            });

            foreach (bool secondaryInSource in new[] { false, true })
            Case("untouched mixed estate preflight / secondary in source " + secondaryInSource, c =>
            {
                var w = _world; w.Rulers[w.Realm] = w.Source;
                var primary = w.Person("primary", w.Source, 40, w.Donor); w.Heads[w.Source] = primary;
                var secondary = w.Person("secondary", secondaryInSource ? w.Source : w.Domestic, 30, w.Donor);
                var ordinaryHeir = w.Person("ordinary_heir", w.Domestic, 25, w.Donor);
                var land = w.Land("ordinary_land"); var ordinaryTitle = w.Title("ordinary_title", FeudalTitleType.Duchy);
                var crown = w.Title("additional_crown", FeudalTitleType.Kingdom);
                var estate = w.Pending(primary, secondary, ordinaryHeir);
                var ordinary = new CrossClanEstateShare { Heir = ordinaryHeir, DomesticOnly = true, ReceivesGold = true,
                    Titles = new List<string> { ordinaryTitle.TitleId }, Fiefs = new List<string> { "ordinary_land" } };
                var sovereign = new CrossClanEstateShare { Heir = secondary, Titles = new List<string> { crown.TitleId } };
                estate.Shares.Add(ordinary); estate.Shares.Add(sovereign);
                c(!w.CanStart(estate), "unsupported domestic Crown promotion blocks the entire untouched journal");
                for (int retry = 0; retry < 2; retry++)
                    Call(typeof(PartitionSuccessionBehavior), "TrySettleCrossClanEstate", w.Partition, estate, null);
                c(!estate.GoldPrepared && estate.GoldDonor == null && estate.GoldPerShare == 0 && !estate.Completed
                    && !string.IsNullOrEmpty(estate.Failure),
                    "invalid Crown package defers before treasury capture or native delivery, including retries");
                c(ordinary.Recipient == null && ordinary.CadetPlan == null && ordinary.GoldPayment == null
                    && !ordinary.Completed && !ordinary.LandedSettled && ordinary.DeliveredFiefs.Count == 0
                    && ordinary.DeliveredTitles.Count == 0 && ordinary.SupersededFiefs.Count == 0 && ordinary.SupersededTitles.Count == 0
                    && land.OwnerClan == w.Source && ordinaryTitle.DeJureHolderClanId == w.Source.StringId
                    && ordinaryTitle.DeFactoHolderClanId == w.Source.StringId && sovereign.GoldPayment == null
                    && sovereign.Recipient == null && sovereign.CadetPlan == null && sovereign.DeliveredTitles.Count == 0,
                    "ordinary share preceding the blocked Crown has no possession, title, gold, cadet or receipt mutations");
                estate.PolicyVersion = 0;
                c(w.CanStart(estate), "legacy pending allocation bypasses the new whole-estate preflight");
                estate.PolicyVersion = 1;
                ordinary.DeliveredTitles.Add(ordinaryTitle.TitleId);
                c(w.CanStart(estate), "recorded delivery bypasses preflight for interrupted recovery");
                ordinary.DeliveredTitles.Clear(); ordinaryTitle.SetDeJureHolder(w.Domestic.StringId);
                c(w.CanStart(estate), "actual legal delivery without its receipt also bypasses preflight");
                ordinaryTitle.SetDeJureHolder(w.Source.StringId); Set(land, "_ownerClan", w.Domestic);
                c(w.CanStart(estate), "actual possession without its receipt also bypasses preflight");
                Set(land, "_ownerClan", w.Source);
                ordinary.GoldPayment = new CrownAccessionRecord { GoldDebited = true, DeliveredGold = 70 };
                c(w.CanStart(estate) && ordinary.GoldPayment.DeliveredGold == 70,
                    "gold commitment bypasses preflight without rewriting the payment");
                ordinary.GoldPayment = null;
                c(!w.CanStart(estate), "no commitment leaves the unsupported mixed journal pending");
            });

            Case("unassigned sovereign Crown reverts domestically", c =>
            {
                var w = _world;
                var crown = w.Title("additional_crown", FeudalTitleType.Kingdom);
                var estate = w.Project();
                var share = estate.Shares.Single();
                c(share.IsEscheat && share.DomesticOnly && share.Titles.SequenceEqual(new[] { crown.TitleId }),
                    "heirless additional Crown has an explicit original-realm reversion");
                Call(typeof(PartitionSuccessionBehavior), "SettleCrossClanShare", w.Partition, estate, share, w.Titles, null);
                c(share.Completed && share.Recipient == w.Crown && crown.DeJureHolderClanId == w.Crown.StringId
                    && share.SovereignTransfers.Count == 0 && !share.ReceivesGold,
                    "domestic escheat completes without entering foreign sovereign integration");
                Call(typeof(PartitionSuccessionBehavior), "SettleCrossClanShare", w.Partition, estate, share, w.Titles, null);
                c(share.DeliveredTitles.Count == 1 && share.GoldPayment.EndowmentGold == 0,
                    "repeated Crown reversion does not duplicate title or gold delivery");
            });

            Case("foreign collateral political Crown stays separate from property", c =>
            {
                var w = _world; w.Rulers[w.Realm] = w.Source;
                var ancestor = w.Person("ancestor", w.Source, 95);
                w.Donor.Father = ancestor;
                var foreign = w.Person("foreign_brother", w.Abroad, 50, ancestor);
                var local = w.Person("local_brother", w.Source, 40, ancestor);
                Set(ancestor, "_heroState", Hero.CharacterStates.Dead);
                w.Land("royal"); var crown = w.Title("crown", FeudalTitleType.Kingdom, land: "royal");
                w.Crowns[w.Realm] = crown; w.Title("royal_barony", FeudalTitleType.Barony, crown.TitleId, "royal");
                var line = (List<Hero>)Call(Mod.GetType("BellumCivile.HereditaryRealmSuccession"), "GetLine", null,
                    w.Realm, w.Donor, null);
                c(line.First() == foreign, "actual Crown line selects the foreign collateral relative");
                var unrestricted = (List<Hero>)Call(typeof(PartitionSuccessionBehavior), "OrderEstateHeirs", null,
                    w.Source.Heroes, w.Donor, w.Law);
                c(unrestricted.All(h => h.Clan == w.Source), "household estate enumeration alone looks entirely in-house");
                var estate = w.Project();
                c(estate.Shares.All(s => s.Heir == local) && estate.Shares.Single(s => s.Fiefs.Contains("royal")).Heir == local
                    && !estate.Shares.Any(s => s.Heir == foreign && s.ReceivesGold),
                    "political Crown selection does not replace the lawful personal property heir or create a gold award");
                w.Partition.CaptureCrossClanEstate(w.Donor);
                c(w.Captured.Count == 0, "entirely in-house personal estate requires no cross-clan journal");
                var receipt = new CrossClanEstateShare { Heir = local, Titles = new List<string> { crown.TitleId },
                    GoldPayment = new CrownAccessionRecord { GoldCredited = true } };
                Call(typeof(PartitionSuccessionBehavior), "SettleCrossClanShare", w.Partition, estate, receipt, w.Titles, null);
                c(receipt.SupersededTitles.Contains(crown.TitleId) && receipt.DeliveredTitles.Count == 0
                    && crown.DeJureHolderClanId == w.Source.StringId,
                    "property executor leaves the active political Crown to accession");
            });

            foreach (bool fallbackInSource in new[] { false, true })
            foreach (bool partition in new[] { false, true })
                Case("advanced Crown heir / local fallback " + fallbackInSource + " / partition " + partition, c =>
                {
                    var w = _world; w.Rulers[w.Realm] = w.Source; w.PartitionEnabled = partition;
                    var advanced = w.Person("advanced_foreign_eldest", w.Abroad, 35, w.Donor);
                    var local = w.Person("unadvanced_local", fallbackInSource ? w.Source : w.Domestic, 20, w.Donor);
                    var receipt = w.Advance(advanced);
                    w.Land("royal", 100); w.Land("ordinary", 50);
                    var crown = w.Title("crown", FeudalTitleType.Kingdom, land: "royal"); w.Crowns[w.Realm] = crown;
                    w.Title("royal_barony", FeudalTitleType.Barony, crown.TitleId, "royal");
                    w.Title("ordinary_barony", FeudalTitleType.Barony, land: "ordinary");
                    c(w.Accessions.HasInheritanceAdvance(w.Donor, advanced), "saved living-endowment receipt counts as an advance");
                    var line = (List<Hero>)Call(Mod.GetType("BellumCivile.HereditaryRealmSuccession"), "GetLine", null,
                        w.Realm, w.Donor, null);
                    c(line.First() == advanced, "ordinary advance does not disinherit the hereditary Crown");
                    var estate = w.Project();
                    c(estate.Shares.All(s => s.Heir == local) && !estate.DomesticHeirs.Contains(advanced),
                        "advanced political Crown heir is never reinstated into any personal estate package");
                    c(estate.Shares.Single(s => s.Fiefs.Contains("royal")).Heir == local
                        && estate.Shares.Single(s => s.Fiefs.Contains("ordinary")).Heir == local
                        && estate.Shares.Count(s => s.ReceivesGold) == 1,
                        "eligible domestic fallback receives all property and one gold entitlement");
                    c(estate.Shares.SelectMany(s => s.Fiefs).Count() == 2
                        && estate.Shares.SelectMany(s => s.Fiefs).Distinct().Count() == 2
                        && receipt.DeliveredGold == 25 && receipt.EndowmentSettled,
                        "planner filtering does not duplicate assets or mutate previous endowment receipts");
                    w.Partition.CaptureCrossClanEstate(w.Donor);
                    w.Partition.CaptureCrossClanEstate(w.Donor);
                    c(w.Captured.Count == (fallbackInSource ? 0 : 1)
                        && w.Captured.All(r => r.PolicyVersion == 1 && r.Shares.All(s => s.Heir == local)),
                        "capture follows actual property recipients, not the separately selected Crown heir");
                    c(((List<Hero>)Call(Mod.GetType("BellumCivile.HereditaryRealmSuccession"), "GetLine", null,
                        w.Realm, w.Donor, null)).First() == advanced, "property projection leaves political Crown succession unchanged");
                });

            Case("sole advanced Crown heir and ordinary escheat", c =>
            {
                var w = _world; w.Rulers[w.Realm] = w.Source;
                var heir = w.Person("advanced_only_heir", w.Abroad, 35, w.Donor); w.Advance(heir);
                w.Land("royal"); w.Land("ordinary");
                var crown = w.Title("crown", FeudalTitleType.Kingdom, land: "royal"); w.Crowns[w.Realm] = crown;
                w.Title("royal_barony", FeudalTitleType.Barony, crown.TitleId, "royal");
                w.Title("ordinary_barony", FeudalTitleType.Barony, land: "ordinary");
                var estate = w.Project();
                c(estate.DomesticHeirs.Count == 0 && estate.Shares.All(s => s.IsEscheat && s.Heir != heir && !s.ReceivesGold),
                    "sole advanced political heir receives no new property or gold allocation");
                c(estate.Shares.SelectMany(s => s.Fiefs).Count() == 2 && estate.Shares.SelectMany(s => s.Fiefs).Distinct().Count() == 2
                    && !estate.Shares.SelectMany(s => s.Titles).Contains(crown.TitleId),
                    "entire personal estate reverts while the active political Crown stays separate");
                c(((List<Hero>)Call(Mod.GetType("BellumCivile.HereditaryRealmSuccession"), "GetLine", null,
                    w.Realm, w.Donor, null)).First() == heir, "advance does not remove sole heir from the political Crown line");
            });

            Case("domestic advances distinguish delivery from preparation", c =>
            {
                var w = _world;
                var advanced = w.Person("advanced_local", w.Domestic, 35, w.Donor);
                var younger = w.Person("younger_local", w.Source, 20, w.Donor);
                var receipt = w.Advance(advanced);
                w.Land("ordinary"); w.Title("ordinary_barony", FeudalTitleType.Barony, land: "ordinary");
                c(w.Project().Shares.Single().Heir == younger, "settled living advance excludes even a domestic eldest from ordinary inheritance");
                receipt.EndowmentSettled = false;
                c(w.Project().Shares.Single().Heir == advanced, "unsettled advance does not erase ordinary entitlement");
                receipt.EndowmentSettled = true; receipt.DeliveredGold = 0;
                c(w.Project().Shares.Single().Heir == advanced, "prepared household without delivered assets is not an inheritance advance");
            });

            Case("political footprint cannot export detached ordinary land", c =>
            {
                var w = _world; w.Rulers[w.Realm] = w.Source; w.PartitionEnabled = false;
                w.Person("royal_foreign", w.Abroad, 35, w.Donor); var domestic = w.Person("local", w.Domestic, 20, w.Donor);
                w.Land("royal"); w.Land("detached");
                var crown = w.Title("crown", FeudalTitleType.Kingdom, land: "royal"); w.Crowns[w.Realm] = crown;
                w.Title("royal_barony", FeudalTitleType.Barony, crown.TitleId, "royal");
                var detached = w.Title("detached_barony", FeudalTitleType.Barony, crown.TitleId, "detached");
                detached.SetDeFactoParentTitle(""); w.IndexTitles();
                var estate = w.Project();
                c(estate.Shares.Single(s => s.Fiefs.Contains("detached")).DomesticOnly
                    && estate.Shares.Single(s => s.Fiefs.Contains("detached")).Heir == domestic,
                    "historical legal ancestry alone does not grant foreign succession to detached property");
            });

            Case("unstarted emigrant reroutes and exhausted blood escheats", c =>
            {
                var w = _world;
                var emigrant = w.Person("emigrant", w.Abroad, 30, w.Donor);
                var local = w.Person("local", w.Domestic, 15, w.Donor);
                var estate = w.Pending(emigrant, local);
                var share = new CrossClanEstateShare { Heir = emigrant, Recipient = w.Abroad, DomesticOnly = true,
                    CadetPlan = new CrownAccessionRecord(), ReceivesGold = true };
                c(w.Prepare(estate, share) && share.Heir == local && share.Recipient == null && share.CadetPlan == null
                    && share.BeneficiaryReconciled && !share.IsEscheat, "uncommitted ordinary share follows an eligible domestic minor");
                w.Allegiance[w.Domestic] = w.Foreign;
                c(w.Prepare(estate, share) && share.IsEscheat && share.Recipient == w.Crown && !share.ReceivesGold,
                    "foreign-only remainder returns to original realm's Crown");
                c(w.Prepare(estate, share) && share.Recipient == w.Crown, "repeated Crown resolution is stable");
            });

            Case("dead beneficiary representation", c =>
            {
                var w = _world;
                var elder = w.Person("elder", w.Domestic, 35, w.Donor);
                var sibling = w.Person("sibling", w.Source, 25, w.Donor);
                var estate = w.Pending(elder, sibling);
                var child = w.Person("later_born_child", w.Domestic, 1, elder);
                Set(elder, "_heroState", Hero.CharacterStates.Dead);
                var share = new CrossClanEstateShare { Heir = elder, DomesticOnly = true };
                c(w.Prepare(estate, share) && share.Heir == child && !share.IsEscheat,
                    "dead beneficiary's domestic child represents the vested share before a sibling takes it");
            });

            Case("commitment receipts prevent rerouting", c =>
            {
                var w = _world; var foreign = w.Person("moved", w.Abroad, 30, w.Donor);
                var local = w.Person("local", w.Domestic, 20, w.Donor); var estate = w.Pending(foreign, local);
                var commitments = new Action<CrossClanEstateShare>[] {
                    s => s.DeliveredFiefs.Add("land"), s => s.DeliveredTitles.Add("title"),
                    s => s.DeliveredSovereignTransfers.Add("crown"), s => s.CadetPlan = new CrownAccessionRecord { Cadet = w.Abroad },
                    s => s.CadetPlan = new CrownAccessionRecord { CadetInitialized = true },
                    s => s.GoldPayment = new CrownAccessionRecord { GoldDebited = true, DeliveredGold = 70 } };
                foreach (var commit in commitments)
                {
                    var share = new CrossClanEstateShare { Heir = foreign, Recipient = w.Abroad, DomesticOnly = true };
                    commit(share);
                    c(w.Committed(estate, share) && !w.Prepare(estate, share) && share.Heir == foreign && share.Recipient == w.Abroad
                        && !share.IsEscheat, "started share is retained without replay, confiscation or replacement");
                }
                var fief = w.Land("callback_land"); Set(fief, "_ownerClan", w.Abroad);
                var interrupted = new CrossClanEstateShare { Heir = foreign, Recipient = w.Abroad, DomesticOnly = true,
                    Fiefs = new List<string> { "callback_land" } };
                c(w.Committed(estate, interrupted) && !w.Prepare(estate, interrupted), "possession changed before receipt is still committed");
            });

            foreach (string location in new[] { "receiving house", "other domestic house", "emigrated receiving house" })
                Case("committed dead beneficiary / " + location, c =>
                {
                    var w = _world;
                    var beneficiary = w.Person("dead_beneficiary", w.Domestic, 35, w.Donor);
                    var child = w.Person("successor_child", location == "other domestic house" ? w.Source : w.Domestic, 8, beneficiary);
                    var estate = w.Pending(beneficiary);
                    Set(beneficiary, "_heroState", Hero.CharacterStates.Dead);
                    if (location == "emigrated receiving house") w.Allegiance[w.Domestic] = w.Foreign;
                    var cadet = new CrownAccessionRecord { Cadet = w.Domestic, Heir = beneficiary, CadetInitialized = true };
                    var gold = new CrownAccessionRecord { GoldDebited = true, DeliveredGold = 70, GoldCredited = false };
                    var share = new CrossClanEstateShare { Heir = beneficiary, Recipient = w.Domestic, DomesticOnly = true,
                        CadetPlan = cadet, GoldPayment = gold, ReceivesGold = true,
                        DeliveredFiefs = new List<string> { "delivered_land" }, DeliveredTitles = new List<string> { "delivered_title" },
                        SupersededFiefs = new List<string> { "occupied_land" }, SupersededTitles = new List<string> { "third_party_title" } };
                    bool allowed = location == "receiving house";
                    c(w.Prepare(estate, share) == allowed && share.Heir == (allowed ? child : beneficiary)
                        && share.Recipient == w.Domestic && !share.IsEscheat,
                        "only an eligible successor in the already receiving domestic house may resume");
                    c(ReferenceEquals(share.CadetPlan, cadet) && ReferenceEquals(share.GoldPayment, gold)
                        && gold.GoldDebited && gold.DeliveredGold == 70 && !gold.GoldCredited && share.ReceivesGold
                        && share.DeliveredFiefs.SequenceEqual(new[] { "delivered_land" })
                        && share.DeliveredTitles.SequenceEqual(new[] { "delivered_title" })
                        && share.SupersededFiefs.SequenceEqual(new[] { "occupied_land" })
                        && share.SupersededTitles.SequenceEqual(new[] { "third_party_title" }),
                        "succession or deferral retains cadet identity and every delivered, superseded and gold receipt");
                    c(w.Prepare(estate, share) == allowed && share.Recipient == w.Domestic
                        && !share.Completed && !share.LandedSettled, "retry neither redirects the house nor invents completed delivery");
                });

            Case("legal title callback before receipt is a commitment", c =>
            {
                var w = _world; var foreign = w.Person("moved", w.Abroad, 30, w.Donor);
                var local = w.Person("local", w.Domestic, 20, w.Donor); var estate = w.Pending(foreign, local);
                var title = w.Title("delivered_legal_title", FeudalTitleType.Duchy); title.SetDeJureHolder(w.Abroad.StringId); w.IndexTitles();
                var share = new CrossClanEstateShare { Heir = foreign, Recipient = w.Abroad, DomesticOnly = true,
                    Titles = new List<string> { title.TitleId } };
                c(w.Committed(estate, share) && !w.Prepare(estate, share) && share.Heir == foreign,
                    "legal ownership changed before receipt must not permit reallocation");
            });

            Case("unresolved reference and pending Crown", c =>
            {
                var w = _world; var estate = w.Pending();
                var missing = new CrossClanEstateShare { DomesticOnly = true };
                c(!w.Prepare(estate, missing) && !missing.IsEscheat, "unresolved beneficiary does not prove extinction");
                var escheat = new CrossClanEstateShare { DomesticOnly = true, IsEscheat = true };
                var accessions = (List<CrownAccessionRecord>)AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").GetValue(w.Accessions);
                accessions.Add(new CrownAccessionRecord { Realm = w.Realm });
                c(!w.Prepare(estate, escheat) && escheat.Recipient == null, "unsettled Crown cannot receive premature escheat");
                accessions[0].Completed = true;
                c(w.Prepare(estate, escheat) && escheat.Recipient == w.Crown, "settled Crown releases reversion");
                escheat.DeliveredTitles.Add("delivered"); w.Rulers[w.Realm] = w.Domestic;
                w.Person("new_monarch", w.Domestic, 40);
                c(!w.Prepare(estate, escheat) && escheat.Recipient == w.Crown, "partial escheat is not silently redirected to another ruler");
            });

            Case("saved law survives a later law change", c =>
            {
                var w = _world; w.Law = new SuccessionLawSet(GenderSuccessionLaw.FemaleOnly, HouseSuccessionLaw.Primogeniture);
                var daughter = w.Person("daughter", w.Domestic, 12, w.Donor, true);
                var son = w.Person("son", w.Source, 30, w.Donor);
                var estate = w.Pending(daughter, son);
                var share = new CrossClanEstateShare { Heir = daughter, DomesticOnly = true };
                w.Law = new SuccessionLawSet(GenderSuccessionLaw.MaleOnly, HouseSuccessionLaw.Primogeniture);
                c(w.Prepare(estate, share) && share.Heir == daughter && !share.IsEscheat,
                    "pending daughter keeps the captured female-only law");
                Set(daughter, "_heroState", Hero.CharacterStates.Dead);
                c(w.Prepare(estate, share) && share.IsEscheat && share.Recipient == w.Crown,
                    "new male-only law cannot make the excluded son inherit this saved share");
            });

            Case("verified union successor only", c =>
            {
                var w = _world; var estate = w.Pending();
                var share = new CrossClanEstateShare { DomesticOnly = true, IsEscheat = true };
                w.Person("foreign_monarch", w.Abroad, 40); w.EliminatedRealms.Add(w.Realm);
                var accessions = (List<CrownAccessionRecord>)AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").GetValue(w.Accessions);
                var union = new RealmUnionRecord { Source = w.Realm, Destination = w.Foreign };
                accessions.Add(new CrownAccessionRecord { Realm = w.Realm, Completed = true, Union = union });
                c(!w.Prepare(estate, share) && share.Recipient == null, "unverified retirement cannot redirect a dissolved realm's estate");
                union.Completed = true;
                c(!w.Prepare(estate, share), "completed union without source retirement is insufficient");
                union.SourceRetired = true;
                c(w.Prepare(estate, share) && share.Recipient == w.Abroad && estate.Realm == w.Realm,
                    "verified union routes to surviving Crown without rewriting estate origin");
            });

            Case("legacy zero fields and versioned delivery gate", c =>
            {
                var w = _world;
                var legacy = Blank<CrossClanEstateRecord>(); var legacyShare = Blank<CrossClanEstateShare>();
                c(legacy.PolicyVersion == 0 && legacy.DomesticHeirs == null && !legacyShare.DomesticOnly
                    && !legacyShare.IsEscheat && !legacyShare.ReceivesGold, "absent save fields default to legacy policy without restrictions");
                var estate = w.Pending(); estate.PolicyVersion = 0;
                var share = new CrossClanEstateShare { DomesticOnly = true };
                Call(typeof(PartitionSuccessionBehavior), "SettleCrossClanShare", w.Partition, estate, share, w.Titles, null);
                c(_preparations == 0 && !share.IsEscheat, "legacy allocation bypasses new eligibility even if a restriction bit exists");
                estate.PolicyVersion = 1;
                Call(typeof(PartitionSuccessionBehavior), "SettleCrossClanShare", w.Partition, estate, share, w.Titles, null);
                c(_preparations == 1 && !share.Completed, "new ordinary allocation validates before delivery");
                share.DomesticOnly = false;
                Call(typeof(PartitionSuccessionBehavior), "SettleCrossClanShare", w.Partition, estate, share, w.Titles, null);
                c(_preparations == 1, "new Crown package bypasses ordinary restriction");
                foreach (var type in new[] { typeof(CrossClanEstateRecord), typeof(CrossClanEstateShare) })
                {
                    var ids = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .SelectMany(f => f.GetCustomAttributesData()).Where(a => a.AttributeType.Name == "SaveableFieldAttribute")
                        .Select(a => Convert.ToInt32(a.ConstructorArguments[0].Value)).ToList();
                    c(ids.Count == ids.Distinct().Count(), type.Name + " has no save-field ID collisions");
                }
            });

            Case("manifest conservation rejects partial or duplicate allocation", c =>
            {
                var w = _world; var heir = w.Person("local", w.Domestic, 20, w.Donor);
                w.Land("first"); w.Land("second");
                w.Title("first_title", FeudalTitleType.Barony, land: "first");
                w.Title("second_title", FeudalTitleType.Barony, land: "second");
                var estate = w.Project();
                bool Complete() => (bool)Call(Policy, "HasCompleteManifest", null, estate, w.Records, w.Towns.Keys);
                c(Complete(), "valid full projection passes conservation");
                var share = estate.Shares.Single();
                share.Fiefs.Add("first"); c(!Complete(), "duplicate settlement in one share is rejected"); share.Fiefs.RemoveAt(share.Fiefs.Count - 1);
                var duplicate = new CrossClanEstateShare { Heir = heir, Titles = new List<string> { "first_title" } };
                estate.Shares.Add(duplicate); c(!Complete(), "duplicate title across shares is rejected"); estate.Shares.Remove(duplicate);
                share.Titles.Remove("first_title"); c(!Complete(), "missing ordinary title is rejected"); share.Titles.Add("first_title");
                share.Fiefs.Remove("first"); c(!Complete(), "missing possession is rejected"); share.Fiefs.Add("first");
                share.Titles.Add("invented"); c(!Complete(), "out-of-estate title is rejected"); share.Titles.Remove("invented");
                share.Fiefs.Add("invented"); c(!Complete(), "out-of-estate settlement is rejected"); share.Fiefs.Remove("invented");
                var political = w.Title("political_county", FeudalTitleType.County); estate.RealmCrownTitleId = political.TitleId;
                c(Complete(), "political Crown may be reserved for the accession executor");
                share.Titles.Add(political.TitleId); c(Complete(), "single political Crown receipt also passes");
                share.Titles.Add(political.TitleId); c(!Complete(), "political Crown exemption does not allow duplicate allocation");
            });
            RunMixedCases(Case);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
            _world = null;
        }
        if (failures.Count > 0) throw new Exception("Domestic estate regressions (" + failures.Count + "): " + string.Join(" | ", failures));
    }
}

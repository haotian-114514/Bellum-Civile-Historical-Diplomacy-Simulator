using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static partial class DomesticEstateTests
{
    private sealed class EstateStore : IDataStore
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) { Values[key] = data; return true; }
            if (!Values.TryGetValue(key, out var value)) return false;
            data = (T)value; return true;
        }
    }

    private static readonly Type Mixed = Mod.GetType("BellumCivile.MixedEstateBatch", true);
    private static bool Settlements(Clan __instance, ref MBReadOnlyList<Settlement> __result)
    { __result = new MBReadOnlyList<Settlement>(_world.Towns.Values.Where(t => t.OwnerClan == __instance).Select(t => t.Settlement).ToList()); return false; }
    private static bool RealmClans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(_world.Houses.Where(c => c.Kingdom == __instance).ToList()); return false; }
    private static bool Lord(ref bool __result) { __result = true; return false; }
    private static bool NoMainHero(ref Hero __result) { __result = null; return false; }
    private static bool Child(Hero __instance, ref bool __result) { __result = __instance.Age < 18; return false; }
    private static bool FindHero(string __0, ref Hero __result)
    { __result = _world.Ages.Keys.FirstOrDefault(h => h.StringId == __0); return false; }
    private static bool Diplomacy(object[] __args, ref bool __result)
    { __args[__args.Length - 1] = null; __result = true; return false; }
    private static bool Residence(Kingdom __0, FeudalTitleRecord __1, ref Town __result)
    { _world.Towns.TryGetValue(__1.CapitalSettlementId ?? "", out __result); return false; }
    private static int _mixedTransfers, _mixedClaims;
    private static Dictionary<Hero, int> _mixedBalances;
    private static bool _fixtureCadetsEnabled;
    private static int _fixtureCadetsCreated;
    private static bool MixedCadetName(ref string __result)
    { if (!_fixtureCadetsEnabled) return true; __result = "Mixed cadet"; return false; }
    private static bool MixedCadet(CrownAccessionRecord __0, ref bool __result)
    {
        if (!_fixtureCadetsEnabled) return true;
        var w = _world; _fixtureCadetsCreated++;
        __0.Cadet = w.House(__0.CadetId, w.Realm); __0.CadetInitialized = true;
        Set(__0.Heir, "_clan", __0.Cadet); w.Heads[__0.Cadet] = __0.Heir;
        __result = true; return false;
    }
    private static bool MixedGold(Hero __instance, ref int __result)
    {
        if (_mixedBalances == null || !_mixedBalances.TryGetValue(__instance, out __result)) return true;
        return false;
    }
    private static bool MixedPossession(Clan __0, Clan __1, Settlement __2, ref bool __result)
    {
        if (!_world.PendingPartitions.Any(p => p.MixedEstate != null)) return true;
        _mixedTransfers++;
        Set(_world.Towns[__2.StringId], "_ownerClan", __1);
        __result = (bool)Call(typeof(FeudalTitleBehavior), "TryApplyMixedEstateOwnership", _world.Titles, __2, __1);
        return false;
    }
    private static bool MixedParent(FeudalTitleRecord __0, Kingdom __1, ref FeudalTitleRecord __result)
    {
        if (!_world.PendingPartitions.Any(p => p.MixedEstate != null)) return true;
        __result = _world.Crowns[__1];
        if (__result == __0) __result = null;
        return false;
    }
    private static bool MixedClaims()
    {
        if (!_world.PendingPartitions.Any(p => p.MixedEstate != null)) return true;
        _mixedClaims++; return false;
    }
    private static bool MixedQueue() => !_world.PendingPartitions.Any(p => p.MixedEstate != null);

    private static void ConfigureMixedFixture(Action<MethodBase, string> patch, Action<Type, string, string> getter)
    {
        getter(typeof(Clan), "Settlements", nameof(Settlements)); getter(typeof(Kingdom), "Clans", nameof(RealmClans));
        getter(typeof(Hero), "IsLord", nameof(Lord)); getter(typeof(Hero), "IsChild", nameof(Child));
        getter(typeof(Hero), "Gold", nameof(MixedGold));
        getter(typeof(Hero), "MainHero", nameof(NoMainHero));
        patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "ResolveHero"), nameof(FindHero));
        patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "CanPrepareCrownPartitionDiplomacy"), nameof(Diplomacy));
        patch(AccessTools.Method(typeof(FeudalTitleBehavior), "ResolveCrownPartitionResidence"), nameof(Residence));
        patch(AccessTools.Method(typeof(FeudalTitleBehavior), "TransferInheritedPossession"), nameof(MixedPossession));
        patch(AccessTools.Method(typeof(FeudalTitleBehavior), "ResolveBestDeFactoParent"), nameof(MixedParent));
        patch(AccessTools.Method(typeof(FeudalTitleBehavior), "QueueServiceReview"), nameof(MixedQueue));
        patch(AccessTools.Method(typeof(FeudalTitleBehavior), "RegisterPartitionHouseClaimsFromSnapshot"), nameof(MixedClaims));
        patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "PrepareAbdicationCadet"), nameof(MixedCadet));
        patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "PreviewAbdicationCadetName"), nameof(MixedCadetName));
    }

    private static CrossClanEstateRecord MixedFixture()
    {
        var w = _world; w.Rulers[w.Realm] = w.Source; _mixedTransfers = _mixedClaims = 0;
        _fixtureCadetsEnabled = false; _fixtureCadetsCreated = 0;
        var primary = w.Person("primary", w.Source, 40, w.Donor); w.Heads[w.Source] = primary;
        var secondary = w.Person("secondary", w.Source, 30, w.Donor);
        var ordinary = w.Person("ordinary", w.Domestic, 25, w.Donor);
        Set(w.Donor, "_heroState", Hero.CharacterStates.Dead);
        w.Land("main"); w.Land("second"); w.Land("ordinary");
        var crown = w.Title("primary_crown", FeudalTitleType.Kingdom, land: "main"); w.Crowns[w.Realm] = crown;
        w.Title("secondary_crown", FeudalTitleType.Kingdom, land: "second");
        w.Title("main_barony", FeudalTitleType.Barony, "primary_crown", "main");
        w.Title("second_barony", FeudalTitleType.Barony, "secondary_crown", "second");
        w.Title("ordinary_barony", FeudalTitleType.Barony, "primary_crown", "ordinary");
        var estate = w.Pending(primary, secondary, ordinary);
        estate.PreferredPrimaryTitleId = estate.RealmCrownTitleId = "primary_crown";
        estate.Shares.Add(new CrossClanEstateShare { Heir = primary, Primary = true, DomesticOnly = true, ReceivesGold = true,
            Titles = new List<string> { "primary_crown", "main_barony" }, Fiefs = new List<string> { "main" } });
        estate.Shares.Add(new CrossClanEstateShare { Heir = secondary, RootTitleId = "secondary_crown", ReceivesGold = true,
            PrimaryFiefId = "second", Titles = new List<string> { "secondary_crown", "second_barony" }, Fiefs = new List<string> { "second" } });
        estate.Shares.Add(new CrossClanEstateShare { Heir = ordinary, DomesticOnly = true, ReceivesGold = true,
            PrimaryFiefId = "ordinary", Titles = new List<string> { "ordinary_barony" }, Fiefs = new List<string> { "ordinary" } });
        w.Captured.Add(estate);
        _mixedBalances = w.Ages.Keys.ToDictionary(h => h, _ => 0);
        return estate;
    }

    private static PendingPartitionSuccessionRecord CaptureMixed(CrossClanEstateRecord estate)
    {
        var args = new object[] { estate, _world.Titles, null, null };
        if (!(bool)Call(typeof(PartitionSuccessionBehavior), "TryCaptureMixedEstate", _world.Partition, args))
            throw new InvalidOperationException("Mixed capture failed: " + args[3]);
        return (PendingPartitionSuccessionRecord)args[2];
    }

    private static bool VerifyMixed(PendingPartitionSuccessionRecord pending, out string reason)
    {
        var args = new object[] { pending, null };
        bool result = (bool)Call(typeof(PartitionSuccessionBehavior), "TryVerifyCrownBatchWorld", _world.Partition, args);
        reason = (string)args[1]; return result;
    }

    // Native realm creation/war declarations are covered by the Crown harness. Install
    // their verified result here to exercise the mixed executor and ownership callback.
    private static void InstallCrownResult(PendingPartitionSuccessionRecord pending)
    {
        var w = _world; var p = pending.CrownPromotions.Single();
        p.Founder = w.House(p.FounderId, w.Realm);
        Set(p.Heir, "_clan", p.Founder); w.Heads[p.Founder] = p.Heir;
        p.FounderCreationStarted = p.FounderInitializationCompleted = p.FounderVerified = true;
        p.Successor = w.RealmObject(p.SuccessorId); w.Rulers[p.Successor] = p.Founder;
        w.Crowns[p.Successor] = w.Records.Single(t => t.TitleId == p.CrownId);
        p.RealmCreationStarted = p.RealmInitializationCompleted = p.RealmVerified = true;
        p.EstateReceipts = p.EstateFiefIds.Select(id => new CrownEstateDeliveryRecord { AssetId = id })
            .Concat(p.EstateTitleIds.Select(id => new CrownEstateDeliveryRecord { AssetId = id, IsTitle = true })).ToList();
        foreach (var r in p.EstateReceipts)
        {
            r.Started = r.ActionReturned = r.Verified = true;
            if (!r.IsTitle) Set(w.Towns[r.AssetId], "_ownerClan", p.Founder);
            else
            {
                var title = w.Records.Single(t => t.TitleId == r.AssetId);
                title.SetDeJureHolder(p.FounderId); title.SetDeFactoHolder(p.FounderId);
            }
        }
        p.EstateDeliveryStarted = p.EstateVerified = true;
        p.GovernmentStarted = p.GovernmentReturned = p.GovernmentVerified = p.TransferDiplomacyPrepared = true;
        p.CrownRegistrationStarted = p.CrownRegistrationReturned = true;
        w.Crowns[p.Successor].SetAssociatedKingdom(p.SuccessorId);
        foreach (var house in p.Houses)
        {
            if (house.Transfer.Clan == null) house.Transfer.Clan = p.Founder;
            house.Transfer.ActionStarted = house.Transfer.ActionCompleted = true;
            house.Transfer.RestorationStarted = house.Transfer.RestorationCompleted = true;
            house.MovementReturned = house.RestorationReturned = house.PostMoveBalancesCaptured = true;
            w.Allegiance[house.Transfer.Clan] = p.Successor;
        }
        pending.CrownBatch.PoliticalParentTargets = w.Records.ToDictionary(t => t.TitleId, t => t.DeFactoParentTitleId);
        pending.CrownBatch.HierarchyStarted = pending.CrownBatch.HierarchyReturned = pending.CrownBatch.HierarchyVerified = true;
        w.IndexTitles();
    }

    private static bool DeliverMixed(PendingPartitionSuccessionRecord pending, out string reason)
    {
        var args = new object[] { pending, _world.Titles, null };
        bool result = (bool)Call(typeof(PartitionSuccessionBehavior), "TryDeliverMixedEstateLand", _world.Partition, args);
        reason = (string)args[2]; return result;
    }

    private static void RunMixedCases(Action<string, Action<Action<bool, string>>> test)
    {
        test("mixed retained accession precedes estate batch", c =>
        {
            var estate = MixedFixture(); var w = _world; var heir = estate.Shares[0].Heir;
            var accession = new CrownAccessionRecord { Realm = w.Realm, PreviousHouse = w.Source, Predecessor = w.Donor, Heir = heir };
            ((List<CrownAccessionRecord>)AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").GetValue(w.Accessions)).Add(accession);
            var args = new object[] { heir, accession };
            c((bool)Call(typeof(PartitionSuccessionBehavior), "SettleCrownDeathEstate", w.Partition, args),
                "retained lawful Crown head can finish political accession without circular estate wait");
            c(!estate.Completed && estate.Shares.All(s => !s.LandedSettled && s.GoldPayment == null)
                && w.PendingPartitions.Count == 0, "accession exception delivers no inheritance and marks no share complete");
            accession.Completed = true;
            var pending = CaptureMixed(estate);
            c(VerifyMixed(pending, out var reason), "full mixed estate starts after accession: " + reason);
            accession.Completed = false; accession.ElectiveElection = true;
            c(!(bool)Call(typeof(PartitionSuccessionBehavior), "CanFinishRetainedMixedAccession", w.Partition, estate, heir, accession),
                "elective accession does not receive hereditary exception");
            accession.ElectiveElection = false; accession.Realm = w.Foreign;
            c(!(bool)Call(typeof(PartitionSuccessionBehavior), "CanFinishRetainedMixedAccession", w.Partition, estate, heir, accession),
                "foreign accession still needs its estate delivered first");
        });

        test("mixed capture from live allocation policy", c =>
        {
            MixedFixture(); var w = _world;
            w.Records.Single(t => t.TitleId == "ordinary_barony").SetParentTitle("");
            for (int i = 0; i < 3; i++)
            {
                w.Land("loose" + i, 200 + i);
                w.Title("loose_barony" + i, FeudalTitleType.Barony, land: "loose" + i).SetDeFactoParentTitle("primary_crown");
            }
            w.IndexTitles(); w.Captured.Clear();
            var estate = w.Project();
            c(estate != null && estate.Shares.Any(s => s.Heir.Clan == w.Domestic), "real policy allocates ordinary land to a domestic external house");
            w.Captured.Add(estate);
            var pending = CaptureMixed(estate);
            c(VerifyMixed(pending, out var reason), "real Crown-first allocation is accepted intact: " + reason);
            InstallCrownResult(pending);
            c(DeliverMixed(pending, out reason), "real-policy mixed land delivery completes: " + reason);
        });

        test("mixed capture coordinates original manifest", c =>
        {
            var estate = MixedFixture(); var w = _world;
            var original = estate.Shares.ToList();
            var pending = CaptureMixed(estate);
            c(pending.MixedEstate == estate && w.PendingPartitions.Single() == pending && estate.Shares.SequenceEqual(original),
                "saved Crown adapter refers to the original allocation without replacing any shares");
            c(pending.CrownPromotions.Count == 1 && pending.CrownPromotions[0].Heir == estate.Shares[1].Heir,
                "additional Crown routed to assigned heir");
            c(estate.Shares[2].Recipient == w.Domestic && estate.GoldPrepared && estate.GoldDonor == w.Source.Leader,
                "ordinary domestic recipient and one treasury snapshot recorded");
            c(VerifyMixed(pending, out var reason), "untouched native world matches both manifests: " + reason);
            var previews = (IEnumerable<CrossClanEstateRecord>)Call(typeof(PartitionSuccessionBehavior), "GetPendingEstatePreviews", w.Partition);
            c(previews.Count() == 1 && previews.Single() == estate, "UI has one pending estate, not an adapter duplicate");
            var retry = new object[] { estate, w.Titles, null, null };
            c(!(bool)Call(typeof(PartitionSuccessionBehavior), "TryCaptureMixedEstate", w.Partition, retry)
                && w.PendingPartitions.Count == 1, "second capture cannot duplicate a batch");
            estate.Shares[2].Titles.Add("second_barony");
            c(!VerifyMixed(pending, out _), "overlapping ordinary and Crown award rejected");
            estate.Shares[2].Titles.Remove("second_barony");
            Set(estate.Shares[2].Heir, "_heroState", Hero.CharacterStates.Dead);
            c(!VerifyMixed(pending, out _), "ordinary heir death detected before any Crown mutation");
            Set(estate.Shares[2].Heir, "_heroState", Hero.CharacterStates.Active);
            w.Allegiance[w.Domestic] = w.Foreign;
            c(!VerifyMixed(pending, out _), "recipient's foreign defection detected before delivery");
            w.Allegiance[w.Domestic] = w.Realm;
            c(VerifyMixed(pending, out reason), "return to untouched original state is retryable: " + reason);
            c(!pending.CrownPromotions[0].FounderCreationStarted && estate.Shares.All(s => !s.LandedSettled && s.GoldPayment == null),
                "validation retries transfer no assets or money");
        });

        test("mixed minority postpones whole allocation", c =>
        {
            var estate = MixedFixture(); var w = _world;
            w.Ages[estate.Shares[1].Heir] = 12;
            var args = new object[] { estate, w.Titles, null, null };
            c(!(bool)Call(typeof(PartitionSuccessionBehavior), "TryCaptureMixedEstate", w.Partition, args), "minor Crown founder waits");
            c(w.PendingPartitions.Count == 0 && !estate.GoldPrepared && w.Towns.Values.All(t => t.OwnerClan == w.Source),
                "no land or money distributed while waiting");
            w.Ages[estate.Shares[1].Heir] = 18;
            var pending = CaptureMixed(estate);
            c(VerifyMixed(pending, out var reason), "adulthood permits the original allocation: " + reason);
        });

        test("mixed unstarted ordinary beneficiary follows domestic successor", c =>
        {
            var estate = MixedFixture(); var w = _world; var pending = CaptureMixed(estate);
            var original = estate.Shares[2]; var dead = original.Heir;
            var successor = w.Person("ordinary_successor", w.Domestic, 20, dead);
            w.Heads[w.Domestic] = successor; Set(dead, "_heroState", Hero.CharacterStates.Dead);
            Call(typeof(PartitionSuccessionBehavior), "TryHandleMixedEstate", w.Partition, estate, w.Titles);
            var refreshed = w.PendingPartitions.Single();
            c(refreshed != pending && refreshed.MixedEstate == estate && estate.Shares[2] == original,
                "only untouched adapter refreshed, original asset allocation kept");
            c(original.Heir == successor && original.BeneficiaryReconciled && original.Recipient == w.Domestic,
                "eligible domestic successor receives unstarted award");
            c(!pending.CrownPromotions[0].FounderCreationStarted && !refreshed.CrownPromotions[0].FounderCreationStarted
                && _mixedTransfers == 0, "unavailable diplomacy services leave refreshed batch unmutated");
        });

        test("mixed captive household waits before Crown delivery", c =>
        {
            var estate = MixedFixture(); var pending = CaptureMixed(estate);
            Set(estate.Shares[1].Heir, "_heroState", Hero.CharacterStates.Prisoner);
            var args = new object[] { pending, null };
            c(!(bool)Call(typeof(PartitionSuccessionBehavior), "MixedHouseholdsReady", null, args), "captured Crown heir holds back whole batch");
            Set(estate.Shares[1].Heir, "_heroState", Hero.CharacterStates.Active);
            c((bool)Call(typeof(PartitionSuccessionBehavior), "MixedHouseholdsReady", null, args), "release permits retry");
        });

        foreach (bool sameHeir in new[] { false, true })
        test("mixed delivery and retries / shared Crown heir " + sameHeir, c =>
        {
            var estate = MixedFixture(); var w = _world;
            if (sameHeir) { estate.Shares[2].Heir = estate.Shares[1].Heir; estate.Shares[2].ReceivesGold = false; }
            _mixedBalances[w.Source.Leader] = 900;
            var pending = CaptureMixed(estate);
            InstallCrownResult(pending);
            c(VerifyMixed(pending, out var reason), "verified Crown result resumes: " + reason);
            c(DeliverMixed(pending, out reason), "ordinary land delivered after Crown separation: " + reason);
            var recipient = sameHeir ? pending.CrownPromotions[0].Founder : w.Domestic;
            var ordinary = w.Records.Single(t => t.TitleId == "ordinary_barony");
            c(w.Towns["ordinary"].OwnerClan == recipient && ordinary.DeJureHolderClanId == recipient.StringId
                && ordinary.DeFactoHolderClanId == recipient.StringId, "legal and physical delivery agree");
            c(ordinary.ParentTitleId == "primary_crown" && ordinary.AssociatedKingdomId == w.Realm.StringId,
                "ordinary legal parent and origin preserved");
            c(ordinary.DeFactoParentTitleId == (sameHeir ? "secondary_crown" : "primary_crown"), "actual parent follows recipient realm");
            c(estate.Shares.All(s => s.LandedSettled) && pending.MixedHierarchyReturned, "whole landed estate verified before rewards");
            var store = new EstateStore(); w.Partition.SyncData(store); store.IsLoading = true;
            w.Partition.SyncData(store);
            c(w.PendingPartitions.Single() == pending && pending.MixedEstate == w.Captured.Single(),
                "SyncData/pruning retains one linked estate and its delivery stages");
            for (int retry = 0; retry < 2; retry++) c(DeliverMixed(pending, out reason), "delivery retry " + retry + ": " + reason);
            c(_mixedTransfers == 1, "one native ordinary transfer despite repeated delivery");
            pending.CrownBatch.CourtFinalizationStarted = pending.CrownBatch.CourtFinalizationReturned = true;
            var rewards = new object[] { pending, w.Titles, null };
            c((bool)Call(typeof(PartitionSuccessionBehavior), "TrySettleMixedEstateRewards", w.Partition, rewards), "rewards settle: " + rewards[2]);
            c((bool)Call(typeof(PartitionSuccessionBehavior), "TrySettleMixedEstateRewards", w.Partition, rewards) && _mixedClaims == 1,
                "claim application is not repeated");
            c(_mixedBalances.Values.Sum() == 900 && _mixedBalances[pending.CrownPromotions[0].Heir] == (sameHeir ? 450 : 300)
                && _mixedBalances[w.Source.Leader] == (sameHeir ? 450 : 300), "gold conserved and paid once per original beneficiary");
            pending.CrownBatch.OriginalAgreements = new Dictionary<string, string>(); pending.CrownBatch.ObligationsVerified = true;
            int announcements = 0;
            foreach (var key in Enumerable.Range(0, estate.Shares.Count).Select(i => "mixed-share:" + i).Concat(new[] { "treasury" }))
            for (int retry = 0; retry < 2; retry++)
            {
                var args = new object[] { key, (Action)(() => announcements++), null };
                c((bool)Call(typeof(CrownPartitionBatchRecord), "TryAnnounce", pending.CrownBatch, args), "announcement receipt: " + key);
            }
            c(announcements == estate.Shares.Count + 1, "announcements published once per original share");
            var completion = new object[] { pending, true, null };
            estate.Shares[2].PartitionReceipts[0].Verified = false;
            c(!(bool)Call(Mixed, "TryComplete", null, completion) && !estate.Completed, "settled flag cannot hide an unverified receipt");
            estate.Shares[2].PartitionReceipts[0].Verified = true;
            completion[1] = VerifyMixed(pending, out reason);
            c((bool)Call(Mixed, "TryComplete", null, completion), "complete original estate atomically: " + completion[2]);
            c(estate.Completed && pending.CrownBatch.Completed && estate.Shares.All(s => s.Completed), "both journals complete together");
        });

        test("mixed interrupted ownership callback is not replayed", c =>
        {
            var estate = MixedFixture(); var pending = CaptureMixed(estate); InstallCrownResult(pending);
            estate.Shares[2].PartitionReceipts = new List<CrownEstateDeliveryRecord> {
                new CrownEstateDeliveryRecord { AssetId = "ordinary", Started = true },
                new CrownEstateDeliveryRecord { AssetId = "ordinary_barony", IsTitle = true } };
            c(!DeliverMixed(pending, out _) && _mixedTransfers == 0, "ambiguous native interruption stays pending without replay");
            c(!estate.Completed && !pending.CrownBatch.Completed && estate.Shares[2].PartitionReceipts[0].Started,
                "original receipt and Crown progress retained for diagnosis");
        });

        test("mixed ordinary cadet joins retained realm", c =>
        {
            var estate = MixedFixture(); var w = _world;
            Set(estate.Shares[2].Heir, "_clan", w.Source);
            var pending = CaptureMixed(estate); InstallCrownResult(pending);
            _fixtureCadetsEnabled = true;
            try
            {
                c(DeliverMixed(pending, out var reason), "ordinary cadet delivery completes: " + reason);
                var share = estate.Shares[2];
                c(share.PartitionFounderStarted && share.PartitionFounderReturned && share.Recipient == share.CadetPlan.Cadet
                    && share.Recipient.Kingdom == w.Realm && share.Recipient.Leader == share.Heir,
                    "new ordinary house stays domestic with its recorded founder");
                c(DeliverMixed(pending, out reason) && _fixtureCadetsCreated == 1 && _mixedTransfers == 1,
                    "retry neither founds another house nor repeats land delivery: " + reason);
            }
            finally { _fixtureCadetsEnabled = false; }
        });

        test("mixed inheritance respects third-party occupation", c =>
        {
            var estate = MixedFixture(); var w = _world;
            estate.Shares[2].Fiefs.Clear();
            var title = w.Records.Single(t => t.TitleId == "ordinary_barony");
            title.SetDeFactoHolder(w.Crown.StringId); Set(w.Towns["ordinary"], "_ownerClan", w.Crown); w.IndexTitles();
            var pending = CaptureMixed(estate); InstallCrownResult(pending);
            c(DeliverMixed(pending, out var reason), "legal-only award delivered: " + reason);
            c(title.DeJureHolderClanId == w.Domestic.StringId && title.DeFactoHolderClanId == w.Crown.StringId
                && w.Towns["ordinary"].OwnerClan == w.Crown && _mixedTransfers == 0,
                "ordinary rights change without taking possession from the occupying house");
        });

        test("mixed founder uses laws captured at death", c =>
        {
            var estate = MixedFixture(); var w = _world;
            AccessTools.Property(typeof(Hero), "IsFemale").SetValue(estate.Shares[1].Heir, true);
            w.Law = new SuccessionLawSet(GenderSuccessionLaw.MaleOnly, HouseSuccessionLaw.Primogeniture);
            var pending = CaptureMixed(estate);
            c(pending.CrownPromotions[0].Heir == estate.Shares[1].Heir && VerifyMixed(pending, out _),
                "later gender-law reform does not revoke an already allocated Crown share");
        });

        test("mixed persisted fields are additive", c =>
        {
            var fields = typeof(PendingPartitionSuccessionRecord).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => new { Field = f, Attribute = f.GetCustomAttributesData().SingleOrDefault(a => a.AttributeType.Name == "SaveableFieldAttribute") })
                .Where(x => x.Attribute != null).ToList();
            var ids = fields.Select(x => Convert.ToInt32(x.Attribute.ConstructorArguments[0].Value)).ToList();
            c(ids.Count == ids.Distinct().Count(), "no duplicate field ids");
            c(fields.Single(x => x.Field.Name == "MixedEstate").Attribute.ConstructorArguments[0].Value.Equals((short)20)
                || Convert.ToInt32(fields.Single(x => x.Field.Name == "MixedEstate").Attribute.ConstructorArguments[0].Value) == 20,
                "owner link appended at field 20");
            c(!typeof(CrossClanEstateRecord).GetFields().Any(f => f.FieldType == typeof(PendingPartitionSuccessionRecord)),
                "saved association is one-way");
        });
    }
}

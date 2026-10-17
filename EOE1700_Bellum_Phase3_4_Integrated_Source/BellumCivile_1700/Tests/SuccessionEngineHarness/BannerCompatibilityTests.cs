using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class BannerCompatibilityTests
{
    private static readonly Assembly Bellum = typeof(FeudalTitleRecord).Assembly;
    private static Type Type(string name) => Bellum.GetType("BellumCivile." + name, true);
    private static object Call(string type, string method, params object[] args) => AccessTools.Method(Type(type), method).Invoke(null, args);
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name).GetValue(value);
    private const string Artwork = "11.1.2.1500.1500.764.764.1.0.0.505.2.1.300.400.700.600.1.1.45.506.1.2.200.250.800.900.0.0.90";
    private const string Simple = "11.1.1.1500.1500.764.764.0.0.0.505.2.2.300.400.764.764.0.0.0";
    private static bool NoPlayer(ref Clan __result) { __result = null; return false; }
    private static bool PreserveCustomArtwork(ref bool __result) { __result = true; return false; }
    private static bool NoVisuals() => false;
    private static Hero _playerHero;
    private static CharacterObject _playerCharacter;
    private static bool PlayerCharacter(ref CharacterObject __result) { __result = _playerCharacter; return false; }
    private static bool HumanPlayer(Hero __instance, ref bool __result) { __result = __instance == _playerHero; return false; }
    private static bool Background(ref int __result) { __result = 11; return false; }
    private static bool Icon(ref int __result) { __result = 505; return false; }
    private static bool SovereignName(ref TextObject __result) { __result = new TextObject("Kingdom of Vlandia"); return false; }
    private static bool ChangeRealm(Clan __instance, Kingdom value)
    {
        AccessTools.Field(typeof(Clan), "_kingdom").SetValue(__instance, value);
        // Simulate a destructive setter to test the independent creation snapshot as well as the global guard.
        if ((bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", __instance))
        {
            __instance.ClanOriginalBanner.ChangePrimaryColor(BannerManager.GetColor(3));
            __instance.ClanOriginalBanner.ChangeIconColors(BannerManager.GetColor(4));
        }
        else AccessTools.Method(typeof(Clan), "UpdateBannerColorsAccordingToKingdom").Invoke(__instance, null);
        return false;
    }
    private static Clan House(Banner banner, Kingdom realm = null)
    {
        var clan = Blank<Clan>();
        clan.Banner = banner;
        AccessTools.Field(typeof(Clan), "_kingdom").SetValue(clan, realm);
        AccessTools.Field(typeof(Clan), "_warPartyComponentsCache").SetValue(clan, new MBList<WarPartyComponent>());
        AccessTools.Property(typeof(Clan), "Name").SetValue(clan, new TextObject("Test House"));
        return clan;
    }

    internal static void Run(Action<bool, string> check, string game)
    {
        var harmony = new Harmony("bellum.test.banner_compatibility");
        BannerManager.Initialize();
        var colors = (Dictionary<int, BannerColor>)AccessTools.Field(typeof(BannerManager), "_colorPalette").GetValue(BannerManager.Instance);
        for (int i = 0; i <= 250; i++) colors[i] = new BannerColor(0xff000000u | (uint)(i * 65793), true, true);
        var realm = Blank<Kingdom>();
        AccessTools.Property(typeof(Kingdom), "Name").SetValue(realm, new TextObject("Vlandia"));
        AccessTools.Property(typeof(Kingdom), "PrimaryBannerColor").SetValue(realm, BannerManager.GetColor(1));
        AccessTools.Property(typeof(Kingdom), "SecondaryBannerColor").SetValue(realm, BannerManager.GetColor(2));
        AccessTools.Property(typeof(Kingdom), "Color").SetValue(realm, BannerManager.GetColor(1));
        AccessTools.Property(typeof(Kingdom), "Color2").SetValue(realm, BannerManager.GetColor(2));
        realm.Banner = new Banner(Simple);
        var parent = House(new Banner(Artwork), realm);
        var nativeSync = AccessTools.Method(typeof(Clan), "UpdateBannerColorsAccordingToKingdom");
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(NoPlayer)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(CharacterObject), "PlayerCharacter"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(PlayerCharacter)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "IsHumanPlayerCharacter"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(HumanPlayer)));
            harmony.Patch(AccessTools.Method(Type("KingdomVisualHelper"), "PickBackgroundMeshId"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(Background)));
            harmony.Patch(AccessTools.Method(Type("KingdomVisualHelper"), "PickIconMeshId"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(Icon)));
            harmony.CreateClassProcessor(Type("Patches.CustomClanBannerSyncPatch")).Patch();
            RunVanilla(check, realm, nativeSync, game);
            harmony.Patch(AccessTools.Method(Type("KingdomVisualHelper"), "MarkClanVisualsDirty"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(NoVisuals)));
            RunCompletedInheritance(check, realm, preserve: false);
            MethodInfo persistencePolicy = AccessTools.Method(Type("Patches.BannerPersistenceCompatibility"), "ShouldPreserve");
            harmony.Patch(persistencePolicy, prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(PreserveCustomArtwork)));
            string original = parent.ClanOriginalBanner.Serialize();
            object visuals = Call("KingdomVisualHelper", "ResolveCadetBranchVisuals", parent, "cadet_test");
            Banner inherited = Property<Banner>(visuals, "Banner");
            check(Property<bool>(visuals, "CopiedParentArtwork"), "Two-color layered banners are recognized even in kingdom colors");
            check(inherited.Serialize() == original, "Cadet clone preserves every layer, color, stroke and transform");
            check(!ReferenceEquals(inherited, parent.ClanOriginalBanner), "Cadet banner does not alias its parent's banner");
            check(Property<uint>(visuals, "PrimaryColor") == inherited.GetPrimaryColor()
                && Property<uint>(visuals, "SecondaryColor") == inherited.GetFirstIconColor(), "Copied banner supplies its own display colors");
            inherited.GetBannerDataAtIndex(1).ColorId = 4;
            check(parent.ClanOriginalBanner.Serialize() == original, "Editing a child's layer cannot change its parent");
            check((bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", House(new Banner(Artwork))), "Kingdomless layered artwork is recognized");
            check(!(bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", House(new Banner(Simple), realm)), "Uniform vanilla artwork keeps the generated path");
            var mixed = new Banner(Simple);
            mixed.GetBannerDataAtIndex(1).ColorId2 = 1;
            check((bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", House(mixed, realm)), "Single-icon custom artwork with mixed layer colors is recognized");

            harmony.Patch(AccessTools.PropertySetter(typeof(Clan), "Kingdom"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(ChangeRealm)));
            visuals = Call("KingdomVisualHelper", "ResolveCadetBranchVisuals", parent, "cadet_test");
            var cadet = House(Property<Banner>(visuals, "Banner"));
            Call("KingdomVisualHelper", "AssignCadetKingdom", cadet, realm, visuals);
            check(cadet.ClanOriginalBanner.Serialize() == original, "Creation survives in-place recoloring by the kingdom setter");
            check(cadet.Color == parent.ClanOriginalBanner.GetPrimaryColor(), "Creation retains banner-derived clan color metadata");

            nativeSync.Invoke(cadet, null);
            check(cadet.ClanOriginalBanner.Serialize() == original, "Native synchronization preserves layered cadet artwork");
            cadet.Banner = new Banner(cadet.ClanOriginalBanner.Serialize());
            nativeSync.Invoke(cadet, null);
            check(cadet.ClanOriginalBanner.Serialize() == original, "Serialized banner round trip followed by native load synchronization is lossless");
            object generated = Call("KingdomVisualHelper", "ResolveCadetBranchVisuals", House(new Banner(Simple), realm), "generated_test");
            check(!Property<bool>(generated, "CopiedParentArtwork") && Property<Banner>(generated, "Banner").BannerDataList.Count == 2,
                "Vanilla cadet generation still creates a fresh background and sigil");
            var generatedClan = House(Property<Banner>(generated, "Banner"));
            Call("KingdomVisualHelper", "AssignCadetKingdom", generatedClan, realm, generated);
            check(generatedClan.ClanOriginalBanner.GetPrimaryColor() == realm.PrimaryBannerColor
                && generatedClan.ClanOriginalBanner.GetFirstIconColor() == realm.SecondaryBannerColor,
                "Generated cadets retain native kingdom-color synchronization");
            realm.RulingClan = parent;
            string kingdomBanner = realm.Banner.Serialize();
            visuals = Call("KingdomVisualHelper", "ResolveCadetBranchVisuals", parent, "royal_cadet");
            check(Property<Banner>(visuals, "Banner").Serialize() == original, "Royal cadets inherit the house banner, not the kingdom banner proxy");
            nativeSync.Invoke(parent, null);
            check(parent.ClanOriginalBanner.Serialize() == original && realm.Banner.Serialize() == kingdomBanner,
                "Ruling house and kingdom artwork remain separate through native synchronization");
            object breakaway = Call("KingdomVisualHelper", "ResolveBreakawayKingdomVisuals", realm, parent, "custom_rebels");
            check(!Property<bool>(breakaway, "RecolorMemberClans") && Property<Banner>(breakaway, "Banner").Serialize() == original,
                "Custom breakaways retain the founding house's exact artwork");
            object successor = Call("KingdomVisualHelper", "ResolveIndependentSuccessorKingdomVisuals", realm, parent, "custom_successor");
            check(Property<Banner>(successor, "Banner").Serialize() == original, "Custom independent successors retain their exact artwork");
            RunCompletedInheritance(check, realm, preserve: true);
            harmony.Unpatch(persistencePolicy, AccessTools.Method(typeof(BannerCompatibilityTests), nameof(PreserveCustomArtwork)));
            realm.RulingClan = null;

            var map = new Hashtable { ["Vlandia"] = new object() };
            var display = new TextObject("Kingdom of Vlandia");
            var native = new TextObject("Vlandia");
            check(ReferenceEquals(Call("Patches.PocBannerCompatibility", "SelectName", map, display, native), native), "POC can fall back from a sovereign title to its configured native name");
            map[display.ToString()] = new object();
            check(ReferenceEquals(Call("Patches.PocBannerCompatibility", "SelectName", map, display, native), display), "Explicit displayed-name POC configuration takes precedence");
            check(ReferenceEquals(Call("Patches.PocBannerCompatibility", "SelectName", new Hashtable(), display, native), display), "Unknown realms retain POC's original fallback behavior");
            var translated = new TextObject("Royaume de Valandie");
            var translatedNative = new TextObject("Valandie");
            check(ReferenceEquals(Call("Patches.PocBannerCompatibility", "SelectName", new Hashtable { ["Valandie"] = new object() }, translated, translatedNative), translatedNative), "Lookup aliases work with localized names without English rank stripping");

            string bcpPath = Environment.GetEnvironmentVariable("BellumTestBannerPersistenceModule");
            if (!string.IsNullOrWhiteSpace(bcpPath)) RunPersistence(check, harmony, realm, bcpPath);
            else Console.WriteLine("SKIP: optional standalone Banner Color Persistence integration (set BellumTestBannerPersistenceModule).");
            string modulePath = Environment.GetEnvironmentVariable("BellumTestPocModule");
            if (!string.IsNullOrWhiteSpace(modulePath)) RunPoc(check, harmony, realm, modulePath);
            else Console.WriteLine("SKIP: optional installed POC integration (set BellumTestPocModule to its DLL).");
        }
        finally { _playerHero = null; _playerCharacter = null; harmony.UnpatchAll(harmony.Id); }
    }

    private static void RunVanilla(Action<bool, string> check, Kingdom realm, MethodInfo sync, string game)
    {
        var clans = XDocument.Load(Path.Combine(game, "Modules", "SandBox", "ModuleData", "spclans.xml"))
            .Descendants("Faction").Where(c => c.Attribute("banner_key") != null
                && ((string)c.Attribute("super_faction"))?.StartsWith("Kingdom.", StringComparison.Ordinal) == true).ToList();
        check(clans.Count > 0, "Regression fixtures read the installed game's noble clan banners");
        foreach (var entry in clans)
        {
            var house = House(new Banner((string)entry.Attribute("banner_key")), realm);
            string id = (string)entry.Attribute("id");
            check(!(bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", house),
                "Native raw banner is not mistaken for custom artwork: " + id);
            var expected = new Banner(house.ClanOriginalBanner);
            expected.ChangePrimaryColor(realm.PrimaryBannerColor);
            expected.ChangeIconColors(realm.SecondaryBannerColor);
            sync.Invoke(house, null);
            check(house.ClanOriginalBanner.Serialize() == expected.Serialize(),
                "Native kingdom colors apply without changing the sigil: " + id);
            house.Banner = new Banner(house.ClanOriginalBanner.Serialize());
            sync.Invoke(house, null);
            check(house.ClanOriginalBanner.Serialize() == expected.Serialize(),
                "Native banner remains correct after serialization and load-style synchronization: " + id);
        }

        var founder = House(new Banner(Artwork), realm);
        check(Call("KingdomVisualHelper", "CaptureClanBannerToPreserve", founder) == null,
            "Native transfer snapshots do not restore neutral household colors over the destination palette");
        uint primary = realm.PrimaryBannerColor, secondary = realm.SecondaryBannerColor;
        AccessTools.Property(typeof(Kingdom), "PrimaryBannerColor").SetValue(realm, BannerManager.GetColor(3));
        AccessTools.Property(typeof(Kingdom), "SecondaryBannerColor").SetValue(realm, BannerManager.GetColor(4));
        sync.Invoke(founder, null);
        check(founder.ClanOriginalBanner.GetPrimaryColor() == realm.PrimaryBannerColor
            && founder.ClanOriginalBanner.GetFirstIconColor() == realm.SecondaryBannerColor,
            "Native clans adopt a changed kingdom palette");
        AccessTools.Property(typeof(Kingdom), "PrimaryBannerColor").SetValue(realm, primary);
        AccessTools.Property(typeof(Kingdom), "SecondaryBannerColor").SetValue(realm, secondary);
        sync.Invoke(founder, null);
        check(founder.ClanOriginalBanner.GetPrimaryColor() == primary && founder.ClanOriginalBanner.GetFirstIconColor() == secondary,
            "Native clans can return to their original kingdom colors");
        founder.Banner = new Banner(Artwork);
        object breakaway = Call("KingdomVisualHelper", "ResolveBreakawayKingdomVisuals", realm, founder, "native_rebels");
        check(Property<bool>(breakaway, "RecolorMemberClans"), "Native breakaways use a uniform realm palette even with layered raw banners");
        object successor = Call("KingdomVisualHelper", "ResolveIndependentSuccessorKingdomVisuals", realm, founder, "native_successor");
        check(Property<Banner>(successor, "Banner").Serialize() != founder.ClanOriginalBanner.Serialize(),
            "Native independent successors receive their own realm palette");
        object cadet = Call("KingdomVisualHelper", "ResolveCadetBranchVisuals", founder, "native_cadet");
        check(!Property<bool>(cadet, "CopiedParentArtwork"), "Native layered parent banners still generate a new cadet sigil");
        check(founder.ClanOriginalBanner.Serialize() == new Banner(Artwork).Serialize(), "Founding visuals never mutate the parent's banner");
    }

    private static void RunPersistence(Action<bool, string> check, Harmony harmony, Kingdom realm, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Banner Color Persistence test module not found", path);
        Assembly bcp = Assembly.LoadFrom(path);
        PropertyInfo config = AccessTools.Property(bcp.GetType("BannerColorPersistence.PersistColorSubModule", true), "Config");
        object previous = config.GetValue(null);
        object options = Activator.CreateInstance(config.PropertyType);
        void Set(string name, bool value) => options.GetType().GetProperty(name).SetValue(options, value);
        MethodInfo sync = AccessTools.Method(typeof(Clan), "UpdateBannerColorsAccordingToKingdom");
        Type patch = bcp.GetType("BannerColorPersistence.PreventBannerColorUpdates", true);
        var house = House(new Banner(Artwork), realm);
        string artwork = house.ClanOriginalBanner.Serialize();
        try
        {
            config.SetValue(null, options);
            Set("BannerColorPersistenceEnabled", true);
            Set("ShouldPreventNPCBannerColorChanges", true);
            Call("Patches.BannerPersistenceCompatibility", "Initialize");
            harmony.CreateClassProcessor(patch).Patch();
            check((bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", house), "Standalone persistence enables NPC custom-banner preservation when configured");
            sync.Invoke(house, null);
            check(house.ClanOriginalBanner.Serialize() == artwork, "Standalone persistence and Bellum keep custom banners intact");
            object cadet = Call("KingdomVisualHelper", "ResolveCadetBranchVisuals", house, "bcp_cadet");
            check(Property<bool>(cadet, "CopiedParentArtwork") && Property<Banner>(cadet, "Banner").Serialize() == artwork,
                "Standalone persistence cadets copy the parent's exact artwork");
            Set("ShouldPreventNPCBannerColorChanges", false);
            check(!(bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", house), "Player-only persistence does not disable NPC kingdom coloring");
            sync.Invoke(house, null);
            check(house.ClanOriginalBanner.GetPrimaryColor() == realm.PrimaryBannerColor
                && house.ClanOriginalBanner.GetFirstIconColor() == realm.SecondaryBannerColor,
                "Player-only persistence lets native NPC synchronization apply the realm palette");
            _playerHero = Blank<Hero>();
            _playerCharacter = Blank<CharacterObject>();
            AccessTools.Field(typeof(Clan), "_leader").SetValue(house, _playerHero);
            house.Banner = new Banner(Artwork);
            check((bool)Call("Patches.BannerPersistenceCompatibility", "ShouldPreserve", house), "Player-only persistence still protects the actual player-led clan");
            sync.Invoke(house, null);
            check(house.ClanOriginalBanner.Serialize() == artwork, "Player artwork survives under the player-only persistence option");
            _playerHero = null;
            _playerCharacter = null;
            Set("ShouldPreventNPCBannerColorChanges", true);
            Set("BannerColorPersistenceEnabled", false);
            check(!(bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", house), "A loaded but disabled persistence option does not activate Bellum preservation");
            config.SetValue(null, null);
            check(!(bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", house), "An uninitialized persistence configuration does not disable native coloring");
        }
        finally
        {
            _playerHero = null;
            _playerCharacter = null;
            config.SetValue(null, previous);
            harmony.Unpatch(sync, AccessTools.Method(patch, "Prefix"));
        }
    }

    private static void RunCompletedInheritance(Action<bool, string> check, Kingdom realm, bool preserve)
    {
        var house = House(new Banner(Artwork), realm);
        var snapshot = new Banner(house.ClanOriginalBanner);
        string original = snapshot.Serialize();
        var expected = new Banner(snapshot);
        if (!preserve)
        {
            expected.ChangePrimaryColor(realm.PrimaryBannerColor);
            expected.ChangeIconColors(realm.SecondaryBannerColor);
        }
        var receipt = new RealmUnionClanRecord { Clan = house, OriginalBanner = snapshot };
        var accession = new CrownAccessionRecord { Union = new RealmUnionRecord {
            Destination = realm, RetirementStarted = true, RetirementReturned = true, SourceRetired = true,
            Announced = true, Completed = true, Clans = new List<RealmUnionClanRecord> { receipt } } };
        var unions = new CrownAccessionBehavior();
        ((List<CrownAccessionRecord>)AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").GetValue(unions)).Add(accession);
        var finishUnion = AccessTools.Method(typeof(CrownAccessionBehavior), "TryFinishRealmUnion");
        check((bool)finishUnion.Invoke(unions, new object[] { accession, null })
            && house.ClanOriginalBanner.Serialize() == expected.Serialize(),
            "Completed union applies the destination banner policy; custom=" + preserve);
        check(receipt.OriginalBanner.Serialize() == original, "Union refresh does not rewrite the saved source artwork");
        house.Banner = new Banner(snapshot);
        accession.Union.Completed = false;
        accession.Union.RetirementStarted = false;
        check(!(bool)finishUnion.Invoke(unions, new object[] { accession, null })
            && house.ClanOriginalBanner.Serialize() == snapshot.Serialize(),
            "Incomplete union cannot recolor banners before receipt verification");

        var pending = new PendingPartitionSuccessionRecord("", "", "", "", "", default) {
            CrownBatch = new CrownPartitionBatchRecord { Completed = true },
            CrownPromotions = new List<CrownPartitionPromotionRecord> { new CrownPartitionPromotionRecord {
                Successor = realm, Houses = new List<CrownPartitionHouseRecord> { new CrownPartitionHouseRecord { Transfer = receipt } } } } };
        var partitions = new PartitionSuccessionBehavior();
        ((List<PendingPartitionSuccessionRecord>)AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").GetValue(partitions)).Add(pending);
        var finishPartition = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryFinishCrownBatch");
        check((bool)finishPartition.Invoke(partitions, new object[] { pending, null })
            && house.ClanOriginalBanner.Serialize() == expected.Serialize(),
            "Completed partition applies the successor banner policy; custom=" + preserve);
        house.Banner = new Banner(snapshot);
        check(!(bool)finishPartition.Invoke(partitions, new object[] { pending, null })
            && house.ClanOriginalBanner.Serialize() == snapshot.Serialize(),
            "Unregistered partition cannot refresh banners");
    }

    private static void RunPoc(Action<bool, string> check, Harmony harmony, Kingdom realm, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("POC test module not found", path);
        Assembly poc = Assembly.LoadFrom(path);
        Type module = poc.GetType("PocColor.PocColorMod", true);
        PropertyInfo field = AccessTools.Property(module, "config");
        object old = field.GetValue(null);
        object config = Activator.CreateInstance(field.PropertyType);
        object New(string name) => Activator.CreateInstance(poc.GetType("PocColor.Config." + name, true));
        void Set(object obj, string name, object value) => obj.GetType().GetProperty(name).SetValue(obj, value);
        IDictionary Map(object obj, string name)
        {
            var p = obj.GetType().GetProperty(name);
            var m = (IDictionary)Activator.CreateInstance(p.PropertyType);
            p.SetValue(obj, m);
            return m;
        }
        try
        {
            field.SetValue(null, config);
            IDictionary kingdoms = Map(config, "kingdoms"), clans = Map(config, "clans");
            object rule = New("PocColorModConfigKingdom");
            Set(rule, "FollowKingdomColors", "false");
            Set(rule, "combatShields", new[] { Simple, Artwork });
            kingdoms["Vlandia"] = rule;
            Call("Patches.PocBannerCompatibility", "TryApply", harmony);
            check(AccessTools.Field(Type("Patches.PocBannerCompatibility"), "_readConfig").GetValue(null) != null, "Installed POC API and name-lookup transpilers are supported");
            foreach (string name in new[] { "PocColorModAgentEquipItemsFromSpawnEquipment", "PocColorModAgentEquipNewEntity" })
            {
                Type patch = poc.GetType("PocColor.PocColorModSetColors+" + name, true);
                MethodInfo method = AccessTools.Method(patch, name.EndsWith("NewEntity") ? "Postfix" : "Prefix");
                check(Harmony.GetPatchInfo(method)?.Transpilers.Any(p => p.PatchMethod.DeclaringType == Type("Patches.PocEquipmentOwnerCompatibility")) == true,
                    "Installed POC equipment lookup accepts the actual-owner adapter: " + name);
            }
            var owner = House(new Banner(Artwork), realm);
            AccessTools.Property(typeof(Clan), "Name").SetValue(owner, new TextObject("Cadet House"));
            check((string)Call("Patches.PocEquipmentOwnerCompatibility", "SelectClanName", "Parent House", owner, null) == "Cadet House",
                "An identical banner's cached parent cannot override its troop party's actual cadet owner");
            check((string)Call("Patches.PocEquipmentOwnerCompatibility", "SelectClanName", "Parent House", null, owner) == "Cadet House",
                "Equipment without a party can use a hero's real house");
            check((string)Call("Patches.PocEquipmentOwnerCompatibility", "SelectClanName", "Parent House", null, null) == "Parent House",
                "Equipment without campaign ownership retains POC's cache fallback");
            Call("DynamicKingdomTitleNameHelper", "RecordNativeInitialization", realm,
                new TextObject("Vlandia"), new TextObject("Vlandia"), new TextObject("Vlandia"));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(SovereignName)));
            var lookup = (TextObject)Call("Patches.PocBannerCompatibility", "ReadKingdomName", realm);
            check(lookup.ToString() == "Vlandia", "Installed POC adapter resolves a renamed realm to its native config key");
            string[] ShieldPool(string name)
            {
                var result = (ValueTuple<int, string[], string[], string[], string[]>)AccessTools.Method(config.GetType(), "GetBattleConfig")
                    .Invoke(config, new object[] { name, "", "Test troop", false, false, false, false, false, false, false, false, 1, "Vlandia" });
                return result.Item5;
            }
            check(ShieldPool("Kingdom of Vlandia") == null && ShieldPool(lookup.ToString()).Length == 2,
                "Actual POC battle resolver recovers the random-shield pool lost through realm renaming");
            foreach (string[] pool in new[] { new[] { Simple, Artwork }, new[] { Simple }, new[] { Artwork } })
            {
                Set(rule, "combatShields", pool);
                check(ShieldPool(lookup.ToString()).SequenceEqual(pool), "POC retains the configured random, clan or kingdom shield pool");
            }
            Set(rule, "combatShields", new[] { Simple, Artwork });
            object namedRule = New("PocColorModConfigKingdom");
            Set(namedRule, "combatShields", new[] { Artwork });
            kingdoms["Kingdom of Vlandia"] = namedRule;
            string explicitKey = ((TextObject)Call("Patches.PocBannerCompatibility", "ReadKingdomName", realm)).ToString();
            check(explicitKey == "Kingdom of Vlandia" && ShieldPool(explicitKey).SequenceEqual(new[] { Artwork }),
                "Actual POC battle resolver honors explicitly configured sovereign names ahead of native aliases");
            kingdoms.Remove("Kingdom of Vlandia");
            object houseRule = New("PocColorModConfigClan");
            Set(houseRule, "clanBanner", Simple);
            clans["Test House"] = houseRule;
            var house = House(new Banner(Simple), realm);
            check((bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", house), "POC's explicit simple banner is recognized in kingdom colors");
            var policyArgs = new object[] { house, false, false };
            Call("Patches.PocBannerCompatibility", "TryGetPolicy", policyArgs);
            check((bool)policyArgs[1] && (bool)policyArgs[2], "POC remains authoritative for explicitly configured artwork");
            clans.Clear();
            policyArgs = new object[] { house, false, false };
            Call("Patches.PocBannerCompatibility", "TryGetPolicy", policyArgs);
            check(!(bool)policyArgs[1] && !(bool)policyArgs[2], "Random combat shields do not count as a clan-banner recoloring instruction");
            Set(rule, "FollowKingdomColors", "true");
            Call("Patches.PocBannerCompatibility", "TryGetPolicy", policyArgs);
            check((bool)policyArgs[2], "Explicit kingdom-color following is honored");
            Set(rule, "FollowKingdomColors", "false");
            Set(rule, "FollowKingdomBackgroundOnly", "true");
            Call("Patches.PocBannerCompatibility", "TryGetPolicy", policyArgs);
            check((bool)policyArgs[2], "Explicit background-only following is honored");
            check(((string[])rule.GetType().GetProperty("combatShields").GetValue(rule)).Length == 2,
                "Compatibility leaves POC's combat shield pool unchanged");

            Set(rule, "FollowKingdomBackgroundOnly", "false");
            Type pocSync = poc.GetType("PocColor.PocColorModSetColors+PocColorModOverrideBanner", true);
            harmony.CreateClassProcessor(pocSync).Patch();
            var copy = House(new Banner(Artwork), realm);
            MethodInfo sync = AccessTools.Method(typeof(Clan), "UpdateBannerColorsAccordingToKingdom");
            string copyCode = copy.ClanOriginalBanner.Serialize();
            sync.Invoke(copy, null);
            check(copy.ClanOriginalBanner.Serialize() == copyCode, "POC and Bellum native-sync patches preserve copied cadet artwork together");

            string bcpPath = Environment.GetEnvironmentVariable("BellumTestBannerPersistenceModule");
            if (!string.IsNullOrWhiteSpace(bcpPath))
            {
                Assembly bcp = Assembly.LoadFrom(bcpPath);
                object options = Activator.CreateInstance(bcp.GetType("BannerColorPersistence.Config", true));
                PropertyInfo bcpConfig = AccessTools.Property(bcp.GetType("BannerColorPersistence.PersistColorSubModule", true), "Config");
                object previous = bcpConfig.GetValue(null);
                try
                {
                    Set(options, "BannerColorPersistenceEnabled", true);
                    Set(options, "ShouldPreventNPCBannerColorChanges", true);
                    bcpConfig.SetValue(null, options);
                    harmony.CreateClassProcessor(bcp.GetType("BannerColorPersistence.PreventBannerColorUpdates", true)).Patch();
                    sync.Invoke(copy, null);
                    check(copy.ClanOriginalBanner.Serialize() == copyCode, "POC, Banner Color Persistence and Bellum synchronize without losing artwork");
                    Set(rule, "FollowKingdomColors", "true");
                    sync.Invoke(copy, null);
                    check(copy.ClanOriginalBanner.Serialize() != copyCode, "Explicit POC recoloring remains authoritative with all three patches installed");
                    Set(rule, "FollowKingdomColors", "false");
                    harmony.Unpatch(sync, AccessTools.Method(pocSync, "Prefix"));
                    harmony.Unpatch(sync, AccessTools.Method(pocSync, "Postfix"));
                    harmony.CreateClassProcessor(pocSync).Patch();
                    copy.Banner = new Banner(copyCode);
                    sync.Invoke(copy, null);
                    check(copy.ClanOriginalBanner.Serialize() == copyCode, "Artwork also survives when persistence patches are installed before POC's sync patches");
                }
                finally { bcpConfig.SetValue(null, previous); }
            }
            else Console.WriteLine("SKIP: optional Banner Color Persistence integration (set BellumTestBannerPersistenceModule).");
        }
        finally { field.SetValue(null, old); }
    }
}

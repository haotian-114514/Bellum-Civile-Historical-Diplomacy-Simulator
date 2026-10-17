using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Definition;

internal static class CadetNamingTests
{
    private static readonly Type Builder = typeof(CadetNameRecord).Assembly.GetType("BellumCivile.CadetClanNameBuilder", true);
    private static readonly List<Clan> Houses = new List<Clan>();
    private static Clan ExtinctHouse;
    private static bool All(ref MBReadOnlyList<Clan> __result) { __result = new MBList<Clan>(Houses); return false; }
    private static bool Eliminated(Clan __instance, ref bool __result) { __result = __instance == ExtinctHouse; return false; }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(string method, params object[] args) => AccessTools.Method(Builder, method).Invoke(null, args);
    private static TextObject Text(string name) => name == null ? null : new TextObject("{=!}" + name);
    private static HashSet<string> Taken(params string[] names) => new HashSet<string>(names.Select(n => (string)Call("Normalize", n)), StringComparer.OrdinalIgnoreCase);
    private static CadetNameRecord Family(string name, CadetNameRecord known = null) => (CadetNameRecord)Call("Family", Text(name), known);
    private static CadetNameRecord Combine(CadetNameRecord a, CadetNameRecord b) => (CadetNameRecord)Call("Dynastic", a, b);
    private static CadetNameRecord Unique(CadetNameRecord name, params string[] taken) => (CadetNameRecord)Call("MakeUnique", name, Taken(taken));
    private static Clan House(string name, string culture = "vlandia")
    {
        var clan = Blank<Clan>(); clan.StringId = "house_" + Houses.Count;
        var c = Blank<CultureObject>(); c.StringId = culture; clan.Culture = c;
        AccessTools.Property(typeof(Clan), "Name").SetValue(clan, Text(name));
        Houses.Add(clan);
        return clan;
    }

    internal static void WithEmptyRegistry(Action action)
    {
        Houses.Clear();
        var h = new Harmony("bellum.test.cadet_names.registry");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), prefix: new HarmonyMethod(typeof(CadetNamingTests), nameof(All)));
            h.Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), prefix: new HarmonyMethod(typeof(CadetNamingTests), nameof(Eliminated)));
            action();
        }
        finally { h.UnpatchAll(h.Id); Houses.Clear(); ExtinctHouse = null; }
    }

    internal static void Run(Action<bool, string> check)
    {
        foreach (var test in new[] {
            new[] { "dey dey Meroc", "dey Meroc" }, new[] { "fen fen Gruffendoc", "fen Gruffendoc" },
            new[] { "Banu Banu Hulyan", "Banu Hulyan" }, new[] { "Gundaroving", "Gundaroving" },
            new[] { "Urkhunait", "Urkhunait" }, new[] { "Pethros", "Pethros" },
            new[] { "deyMeroc", "deyMeroc" }, new[] { "  dey\tdey   Meroc ", "dey Meroc" },
            new[] { "My-Handwritten-Surname", "My-Handwritten-Surname" } })
            check(Family(test[0]).Name.ToString() == test[1], "Existing surname is normalized without applying another cultural affix: " + test[0]);

        var meroc = Family("dey Meroc"); var pethros = Family("Pethros"); var cortain = Family("dey Cortain");
        var merocPethros = Combine(meroc, pethros);
        check(merocPethros.Name.ToString() == "dey Meroc-Pethros", "Dynasty stays first and keeps its leading style");
        check(Combine(pethros, meroc).Name.ToString() == "Pethros-Meroc", "Second-house prefix is removed independently of culture");
        check(Combine(meroc, Combine(pethros, meroc)).Name.ToString() == "dey Meroc-Pethros", "Reversed ancestral component is not duplicated");
        check(Combine(merocPethros, meroc).Name.ToString() == "dey Meroc-Pethros", "Existing distinct secondary survives a redundant new family");
        check(Combine(merocPethros, cortain).Name.ToString() == "dey Meroc-Pethros-Cortain", "A new distinct marital family is appended without discarding existing ancestry");
        var pethrosMestricaros = Combine(pethros, Family("Mestricaros"));
        var threeHouses = Combine(merocPethros, pethrosMestricaros);
        check(threeHouses.Name.ToString() == "dey Meroc-Pethros-Mestricaros",
            "Overlapping compound houses retain every distinct family exactly once");
        check(Combine(pethrosMestricaros, merocPethros).Name.ToString() == "Pethros-Mestricaros-Meroc",
            "Dynasty order takes precedence when the same houses combine in reverse");
        check(Combine(threeHouses, Combine(Family("  mEROC "), Family(" MESTRICAROS "))).Name.ToString() == threeHouses.Name.ToString(),
            "Duplicates anywhere in either compound are ignored regardless of case and surrounding whitespace");
        check(Combine(threeHouses, cortain).Name.ToString() == "dey Meroc-Pethros-Mestricaros-Cortain",
            "Further generations retain third and later family components");
        check(Combine(null, threeHouses).Name.ToString() == threeHouses.Name.ToString()
            && Combine(threeHouses, null).Name.ToString() == threeHouses.Name.ToString(),
            "A missing family does not truncate the surviving compound");
        var threeReadBack = Family(threeHouses.Name.ToString(), threeHouses);
        check(threeReadBack.AdditionalRoots.Single().ToString() == "Mestricaros"
            && !ReferenceEquals(threeReadBack.AdditionalRoots, threeHouses.AdditionalRoots)
            && !ReferenceEquals(threeReadBack.AdditionalRoots[0], threeHouses.AdditionalRoots[0]),
            "Reading provenance preserves all components without sharing mutable lists or text objects");
        var legacyTwo = new CadetNameRecord { Name = Text("dey Meroc-Pethros"), PrimaryName = Text("dey Meroc"),
            PrimaryRoot = Text("Meroc"), SecondaryRoot = Text("Pethros") };
        check(Combine(Family(legacyTwo.Name.ToString(), legacyTwo), pethrosMestricaros).Name.ToString() == threeHouses.Name.ToString(),
            "Older two-component save records join the extended naming path without migration");
        var longFamily = meroc;
        for (int i = 0; i < 12; i++) longFamily = Combine(longFamily, Family("House" + i));
        check(longFamily.AdditionalRoots.Count == 11 && Combine(longFamily, longFamily).Name.ToString() == longFamily.Name.ToString(),
            "Long family names have no component cap and repeated ancestry does not grow them");
        check(Combine(pethros, Family("pETHROS")).Name.ToString() == "Pethros", "Dynastic deduplication is case insensitive");
        check(Combine(pethros, Family("dey dey Meroc")).Name.ToString() == "Pethros-Meroc", "All recognized leading prefixes are removed from the second surname");
        check(Combine(pethros, Family("deyMeroc")).Name.ToString() == "Pethros-deyMeroc", "Prefix matching requires a word boundary");
        check(Combine(null, pethros).Name.ToString() == "Pethros" && Combine(null, null).Name.ToString() == "Cadet Branch", "Missing families have safe fallbacks");
        var renamed = Family("Player-Chosen-Name", merocPethros);
        check(renamed.PrimaryName.ToString() == "Player-Chosen-Name" && renamed.SecondaryRoot == null,
            "Player renames invalidate stored naming provenance without rewriting the chosen surname");
        check(Family("Player-Chosen-Name", threeHouses).AdditionalRoots == null,
            "Player renames also discard stale third and later components");
        check(Family(merocPethros.Name.ToString(), merocPethros).SecondaryRoot.ToString() == "Pethros",
            "Unchanged generated surnames retain structured family components");
        check(Family("dey Meroc-Pethros-Meroc").SecondaryRoot == null,
            "Unknown legacy compounds are not guessed apart by punctuation");

        CadetNameRecord Territory(string culture, params string[] roots) => (CadetNameRecord)Call("Territorial", culture, "house|holding",
            roots.Select(Text), meroc, Taken());
        foreach (var test in new[] { new[] { "vlandia", "dey Rovalt" }, new[] { "battania", "fen Rovalt" },
            new[] { "aserai", "Banu Rovalt" }, new[] { "sturgia", "Rovalting" }, new[] { "nord", "Rovalting" },
            new[] { "khuzait", "Rovaltit" }, new[] { "conversion", "Rovalt" } })
            check(Territory(test[0], "Rovalt Castle").Name.ToString() == test[1], "Territorial cultural format: " + test[0]);
        check(Territory("empire", "Rovalt").Name.ToString() == Territory("empire", "Rovalt").Name.ToString(), "Imperial territorial suffix remains deterministic");
        check(Territory("vlandia").Name.ToString() == "dey Meroc", "Unlanded branch uses the existing family without restyling it");
        check(Territory("vlandia", "Castleton").Name.ToString() == "dey Castleton", "Settlement cleanup does not remove a substring of a proper name");
        var villages = (CadetNameRecord)Call("Territorial", "vlandia", "seed", new[] { Text("Rovalt Castle"), Text("Mot") }, meroc, Taken("dey Rovalt"));
        check(villages.Name.ToString() == "dey Mot", "Occupied settlement tries the associated village before a numeral");
        var numbered = (CadetNameRecord)Call("Territorial", "vlandia", "seed", new[] { Text("Rovalt Castle"), Text("Mot") }, meroc, Taken("dey Rovalt", "dey Mot", "dey Rovalt II"));
        check(numbered.Name.ToString() == "dey Rovalt III", "Exhausted place alternatives number the principal settlement in Roman numerals");
        check(Unique(merocPethros, "dey Meroc-Pethros", "dey Meroc-Pethros II").Name.ToString() == "dey Meroc-Pethros III", "Dynastic names share Roman collision handling");
        var threeNumbered = Unique(threeHouses, threeHouses.Name.ToString());
        check(threeNumbered.Name.ToString() == "dey Meroc-Pethros-Mestricaros II"
            && threeNumbered.AdditionalRoots.Single().ToString() == "Mestricaros",
            "Roman collision suffixes preserve the full extended compound and its provenance");
        check(Combine(Family(threeNumbered.Name.ToString(), threeNumbered), cortain).Name.ToString() == "dey Meroc-Pethros-Mestricaros-Cortain",
            "The next generation inherits every family component but not its parent's collision suffix");
        var numberedFamily = Unique(meroc, "dey Meroc");
        check(Unique(Family(numberedFamily.Name.ToString(), numberedFamily), "dey Meroc", "dey Meroc II").Name.ToString() == "dey Meroc III",
            "A numbered parent's next branch does not accumulate II II");
        foreach (var pair in new[] { Tuple.Create(1, "I"), Tuple.Create(2, "II"), Tuple.Create(4, "IV"), Tuple.Create(9, "IX"),
            Tuple.Create(40, "XL"), Tuple.Create(49, "XLIX"), Tuple.Create(99, "XCIX"), Tuple.Create(3999, "MMMCMXCIX"), Tuple.Create(4000, "MMMM") })
            check((string)Call("ToRoman", pair.Item1) == pair.Item2, "Roman numeral conversion: " + pair.Item1);
        check(Unique(meroc, "  DEY\t MEROC ").Number == 2, "Collision checks normalize case and all whitespace");

        WithEmptyRegistry(() => RunPlans(check));
        RunLocalization(check);
        RunSaveDefinitions(check);
    }

    private static void RunPlans(Action<bool, string> check)
    {
        var partition = new PartitionSuccessionBehavior();
        var source = House("dey Meroc");
        ExtinctHouse = House("dey Meroc II");
        var plan = new CrownAccessionRecord { PreviousHouse = source, CadetId = "new_cadet" };
        var preview = AccessTools.Method(typeof(PartitionSuccessionBehavior), "PreviewAbdicationCadetName");
        var finalize = AccessTools.Method(typeof(PartitionSuccessionBehavior), "FinalizeCadetNameBeforeCreation");
        plan.CadetName = (string)preview.Invoke(partition, new object[] { plan });
        check(plan.CadetName == "dey Meroc III", "Historical clans participate in shared uniqueness checks");
        var pending = new PendingPartitionSuccessionRecord("", "", "", "", "", default) {
            EstateShares = new List<CrossClanEstateShare> { new CrossClanEstateShare { CadetPlan = plan } } };
        ((List<PendingPartitionSuccessionRecord>)AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").GetValue(partition)).Add(pending);
        var other = new CrownAccessionRecord { PreviousHouse = source, CadetId = "other_cadet" };
        other.CadetName = (string)preview.Invoke(partition, new object[] { other });
        check(other.CadetName == "dey Meroc IV", "An uncreated partition plan reserves its name");
        var alternative = new CrownAccessionRecord { PreviousHouse = source, CadetId = plan.CadetId };
        check((string)preview.Invoke(partition, new object[] { alternative }) == "dey Meroc III",
            "Alternative plans for the same future clan do not reserve names against one another");
        var cross = new CrossClanEstateRecord { Deceased = Blank<Hero>(), Shares = new List<CrossClanEstateShare> {
            new CrossClanEstateShare { CadetPlan = other } } };
        ((List<CrossClanEstateRecord>)AccessTools.Field(typeof(PartitionSuccessionBehavior), "_crossClanEstates").GetValue(partition)).Add(cross);
        check((string)preview.Invoke(partition, new object[] { new CrownAccessionRecord { PreviousHouse = source } }) == "dey Meroc V",
            "Cross-clan estates and ordinary partitions share naming reservations");
        cross.Completed = true;
        finalize.Invoke(partition, new object[] { plan });
        check(plan.CadetName == "dey Meroc III", "Final availability check excludes its own reservation");
        House("dey Meroc III");
        finalize.Invoke(partition, new object[] { plan });
        check(plan.CadetName == "dey Meroc IV", "External collision advances the numeral before first creation");
        var formed = House(plan.CadetName);
        plan.Cadet = formed;
        var recordedName = plan.CadetNaming;
        finalize.Invoke(partition, new object[] { plan });
        check(ReferenceEquals(recordedName, plan.CadetNaming), "Existing cadet creation never recalculates its saved name");
        var legacy = new CrownAccessionRecord { PreviousHouse = source, CadetName = "dey dey Meroc" };
        check((string)preview.Invoke(partition, new object[] { legacy }) == "dey dey Meroc" && legacy.CadetNaming == null,
            "Legacy literal plans are not reinterpreted by the new builder");
        finalize.Invoke(partition, new object[] { legacy });
        check(legacy.CadetName == "dey dey Meroc", "Legacy plans remain frozen at the creation boundary");
        var named = Family("Pethros");
        var remember = AccessTools.Method(typeof(PartitionSuccessionBehavior), "RememberCadetName");
        remember.Invoke(partition, new object[] { formed, named });
        check(ReferenceEquals(AccessTools.Method(typeof(PartitionSuccessionBehavior), "GetKnownCadetName").Invoke(partition, new object[] { formed }), named),
            "New clan provenance is recorded under its stable clan ID");

        var registry = new CrownAccessionBehavior();
        ((List<CrownAccessionRecord>)AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").GetValue(registry)).Add(plan);
        check(((IEnumerable<CrownAccessionRecord>)AccessTools.Method(typeof(CrownAccessionBehavior), "GetPendingCadetNamingPlans").Invoke(registry, null)).Contains(plan),
            "Crown accession exposes pending naming reservations");
        plan.CadetInitialized = true;
        check(!((IEnumerable<CrownAccessionRecord>)AccessTools.Method(typeof(CrownAccessionBehavior), "GetPendingCadetNamingPlans").Invoke(registry, null)).Contains(plan),
            "Initialized Crown households no longer reserve an extra pending name");
        var challenges = new SuccessionChallengeBehavior();
        var challenge = new SuccessionChallengeRecord { Estate = other, CrownEstate = legacy, WarEstate = plan };
        ((List<SuccessionChallengeRecord>)AccessTools.Field(typeof(SuccessionChallengeBehavior), "_records").GetValue(challenges)).Add(challenge);
        var plans = (IEnumerable<CrownAccessionRecord>)AccessTools.Method(typeof(SuccessionChallengeBehavior), "GetPendingCadetNamingPlans").Invoke(challenges, null);
        check(plans.Count() == 3, "Succession challenges expose concession, Crown and military naming plans");
    }

    private static void RunLocalization(Action<bool, string> check)
    {
        var language = AccessTools.Field(typeof(MBTextManager), "_activeTextLanguageId");
        var index = AccessTools.Field(typeof(MBTextManager), "_activeTextLanguageIndex");
        object previousLanguage = language.GetValue(null), previousIndex = index.GetValue(null);
        var texts = (IDictionary)AccessTools.Field(typeof(LocalizedTextManager), "_gameTextDictionary").GetValue(null);
        var translations = new Dictionary<string, string> {
            ["BC_CadetName_Vlandia"] = "de {ROOT_NAME}", ["BC_CadetName_Aserai"] = "Beni {ROOT_NAME}",
            ["BC_CadetName_Battania"] = "fen {ROOT_NAME}",
            ["BC_CadetName_CastlePattern"] = "Chateau de {ROOT_NAME}",
            ["BC_DynasticHeiress_CadetName"] = "{DYNASTY_NAME} / {BRANCH_NAME}",
            ["BC_Test_CadetPlace"] = "Rovalt traduit" };
        var previous = translations.Keys.ToDictionary(key => key, key => texts[key]);
        int revision = 2200;
        void Language(string value) { language.SetValue(null, value); index.SetValue(null, ++revision); }
        try
        {
            Language("English");
            var territory = (CadetNameRecord)Call("Territorial", "vlandia", "seed",
                new[] { new TextObject("{=BC_Test_CadetPlace}Rovalt") }, Family("dey Meroc"), Taken());
            var numbered = Unique(territory, "dey Rovalt");
            var compound = Combine(Combine(Family("dey Meroc"), Family("Pethros")), territory);
            check(numbered.Name.ToString() == "dey Rovalt II", "Generated names retain localizable roots before a language change");
            foreach (var pair in translations) texts[pair.Key] = pair.Value;
            Language("BellumCadetTestLanguage");
            check(numbered.Name.ToString() == "de Rovalt traduit II", "Saved naming components follow translated roots and prefixes");
            check(compound.Name.ToString() == "de Meroc / Pethros / Rovalt traduit"
                && Family(compound.Name.ToString(), compound).AdditionalRoots.Single().ToString() == "Rovalt traduit",
                "Third and later components retain localized roots, formatting and provenance across language changes");
            check(Family(numbered.Name.ToString(), numbered).PrimaryRoot.ToString() == "Rovalt traduit",
                "Language change preserves generated naming provenance");
            check(Family("de de Meroc").Name.ToString() == "de Meroc", "Translated duplicate prefixes are normalized");
            check(Combine(Family("de Meroc"), Family("Beni Hulyan")).Name.ToString() == "de Meroc / Hulyan",
                "Cross-cultural translated prefixes and compound format are respected");
            check(((TextObject)Call("CleanSettlementName", Text("Chateau de Rovalt"))).ToString() == "Rovalt",
                "Translated castle pattern can precede the root");
            check(((TextObject)Call("CleanSettlementName", Text("Rovalt Castle"))).ToString() == "Rovalt",
                "Native castle suffix remains a fallback for untranslated settlements");
            check(((TextObject)Call("CleanSettlementName", Text("Chateaubriand"))).ToString() == "Chateaubriand",
                "Translated cleanup does not remove unrelated name fragments");

            // Traditional Chinese prefixes use a middle dot without a following space (PR #2).
            texts["BC_CadetName_Vlandia"] = "\u6234\u00b7{ROOT_NAME}";
            texts["BC_CadetName_Battania"] = "\u82ac\u00b7{ROOT_NAME}";
            texts["BC_CadetName_Aserai"] = "\u5df4\u52aa\u00b7{ROOT_NAME}";
            texts["BC_DynasticHeiress_CadetName"] = "{DYNASTY_NAME}\u00b7{BRANCH_NAME}";
            Language("BellumCadetPunctuationTestLanguage");
            string merocRoot = "\u6885\u6d1b\u514b", pethrosRoot = "\u4f69\u7279\u7f85\u65af", cortainRoot = "\u79d1\u723e\u6cf0";
            string merocName = "\u6234\u00b7" + merocRoot, cortainName = "\u6234\u00b7" + cortainRoot;
            foreach (var pair in new[] {
                Tuple.Create("\u6234\u00b7", merocRoot),
                Tuple.Create("\u82ac\u00b7", "\u70cf\u842c"),
                Tuple.Create("\u5df4\u52aa\u00b7", "\u62c9\u7dad") })
            {
                check(Family(pair.Item1 + pair.Item2).PrimaryRoot.ToString() == pair.Item2,
                    "Localized punctuation prefix is stripped without whitespace: " + pair.Item1);
                check(Family(pair.Item1 + pair.Item1 + pair.Item2).Name.ToString() == pair.Item1 + pair.Item2,
                    "Repeated localized punctuation prefix collapses to one: " + pair.Item1);
                check(Family(pair.Item1 + " " + pair.Item2).PrimaryRoot.ToString() == pair.Item2,
                    "Localized punctuation prefix also accepts a following space: " + pair.Item1);
                check(Family(pair.Item1).Name.ToString() == pair.Item1,
                    "A prefix without a surname is left intact: " + pair.Item1);
            }
            var chineseMeroc = Family(merocName);
            check(Combine(Family(pethrosRoot), chineseMeroc).Name.ToString() == pethrosRoot + "\u00b7" + merocRoot,
                "Chinese dynastic names omit the second family's cultural prefix");
            check(Combine(Family(merocRoot), chineseMeroc).Name.ToString() == merocRoot,
                "Bare and prefixed Chinese forms of the same family are deduplicated");
            check(Combine(Combine(chineseMeroc, Family(pethrosRoot)), Family(cortainName)).Name.ToString()
                == merocName + "\u00b7" + pethrosRoot + "\u00b7" + cortainRoot,
                "Third Chinese family component is appended without another cultural prefix");
            check(Family(pethrosRoot + "\u00b7" + merocName).Name.ToString() == pethrosRoot + "\u00b7" + merocName,
                "Unknown surnames are not split at internal punctuation or prefix-like text");
            foreach (string name in new[] { "deyMeroc", "fenUvain", "BanuRawi", "\u6234" + merocRoot })
                check(Family(name).Name.ToString() == name,
                    "Unrecognized or unseparated word prefixes remain intact in Chinese: " + name);

            Language("English");
            check(numbered.Name.ToString() == "dey Rovalt II", "The same name follows a language switch back without renaming the clan");
            check(compound.Name.ToString() == "dey Meroc-Pethros-Rovalt", "Extended compounds also follow the switch back to English");
        }
        finally
        {
            foreach (var pair in previous)
                if (pair.Value == null) texts.Remove(pair.Key); else texts[pair.Key] = pair.Value;
            language.SetValue(null, previousLanguage); index.SetValue(null, previousIndex);
        }
    }

    private static bool OnlyRecord(Type type) => type == typeof(CadetNameRecord);
    private static bool OnlyNamingContainers(Type type) => type == typeof(Dictionary<string, CadetNameRecord>)
        || type == typeof(List<TextObject>);
    private static void RunSaveDefinitions(Action<bool, string> check)
    {
        var context = new DefinitionContext();
        var initialize = AccessTools.Method(typeof(SaveableTypeDefiner), "Initialize");
        var basic = new SaveableBasicTypeDefiner(); initialize.Invoke(basic, new object[] { context });
        AccessTools.Method(typeof(SaveableBasicTypeDefiner), "DefineBasicTypes").Invoke(basic, null);
        var localization = new SaveableLocalizationTypeDefiner(); initialize.Invoke(localization, new object[] { context });
        AccessTools.Method(typeof(SaveableLocalizationTypeDefiner), "DefineClassTypes").Invoke(localization, null);
        var definer = new BellumCivileSaveDefiner(); initialize.Invoke(definer, new object[] { context });
        var h = new Harmony("bellum.test.cadet_names.save_definitions");
        try
        {
            h.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "AddClassDefinition"),
                prefix: new HarmonyMethod(typeof(CadetNamingTests), nameof(OnlyRecord)));
            h.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "ConstructContainerDefinition"),
                prefix: new HarmonyMethod(typeof(CadetNamingTests), nameof(OnlyNamingContainers)));
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineClassTypes").Invoke(definer, null);
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineContainerDefinitions").Invoke(definer, null);
            check(AccessTools.Method(typeof(DefinitionContext), "GetClassDefinition", new[] { typeof(Type) })
                .Invoke(context, new object[] { typeof(CadetNameRecord) }) != null, "Native save registry resolves cadet naming records");
            check(AccessTools.Method(typeof(DefinitionContext), "GetContainerDefinition")
                .Invoke(context, new object[] { typeof(Dictionary<string, CadetNameRecord>) }) != null,
                "Native save registry resolves the clan naming dictionary");
            check(AccessTools.Method(typeof(DefinitionContext), "GetContainerDefinition")
                .Invoke(context, new object[] { typeof(List<TextObject>) }) != null,
                "Native save registry resolves the additional localized family-component list");
            check(!context.GotError, "New naming definitions introduce no registry errors");
            foreach (var type in new[] { typeof(CadetNameRecord), typeof(CrownAccessionRecord) })
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
                var ids = fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType == typeof(SaveableFieldAttribute)))
                    .Select(a => Convert.ToInt32(a.ConstructorArguments[0].Value)).ToArray();
                check(ids.Distinct().Count() == fields.Length, "Every saved field has a unique ID: " + type.Name);
            }
            check(new CrownAccessionRecord().CadetNaming == null, "Old accession records default to the literal naming path");
        }
        finally { h.UnpatchAll(h.Id); }
    }
}

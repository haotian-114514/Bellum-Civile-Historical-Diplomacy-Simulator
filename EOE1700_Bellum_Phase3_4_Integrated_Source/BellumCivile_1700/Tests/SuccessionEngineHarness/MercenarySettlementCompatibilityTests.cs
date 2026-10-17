using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Definition;

internal static class MercenarySettlementCompatibilityTests
{
    public static bool DeliberateExit = false;
    private static bool OnlyRecord(Type type) => type == typeof(CivilWarMercenarySettlementRecord);
    private static bool OnlyList(Type type) => type == typeof(List<CivilWarMercenarySettlementRecord>);

    internal static void Run(Action<bool, string> check, string modPath)
    {
        var integration = typeof(FactionObject).Assembly.GetType("BellumCivile.ModIntegrationHelper", true);
        var scan = AccessTools.Field(integration, "_mercenaryExitScanComplete");
        var field = AccessTools.Field(integration, "_deliberateMercenaryExitField");
        var execute = AccessTools.Method(integration, "ExecuteWithMercenarySettlementExit");
        object oldScan = scan.GetValue(null), oldField = field.GetValue(null);
        void Run(Action action) => execute.Invoke(null, new object[] { action });
        try
        {
            scan.SetValue(null, true); field.SetValue(null, null);
            int calls = 0;
            Run(() => calls++);
            check(calls == 1, "No optional mercenary mod: native departure runs exactly once");

            FieldInfo flag = typeof(MercenarySettlementCompatibilityTests).GetField(nameof(DeliberateExit));
            if (!string.IsNullOrWhiteSpace(modPath))
            {
                var mod = Assembly.Load(File.ReadAllBytes(modPath));
                flag = mod.GetType("LongerMercenaryContracts.SubModule", true).GetField("IsDeliberateExitInProgress");
                check(flag?.FieldType == typeof(bool) && !flag.IsInitOnly,
                    "Installed Happy's DLL exposes the expected deliberate-departure flag");
                scan.SetValue(null, false);
                Run(() => check((bool)flag.GetValue(null), "Runtime discovery enables the actual Happy's flag during departure"));
                check(Equals(field.GetValue(null), flag), "Compatibility discovers the loaded optional DLL without a compile-time dependency");
                var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
                var outer = AccessTools.Method(mod.GetType("LongerMercenaryContracts.SubModule+PreventLeaveAsMercenaryPatch", true), "Prefix");
                var inner = AccessTools.Method(mod.GetType("LongerMercenaryContracts.SubModule+PreventEarlyContractTerminationPatch", true), "Prefix");
                Run(() =>
                {
                    check((bool)outer.Invoke(null, new object[] { clan }), "Happy's actual departure prefix permits settlement scope");
                    check((bool)inner.Invoke(null, new object[] { clan, null, ChangeKingdomAction.ChangeKingdomActionDetail.LeaveAsMercenary }),
                        "Happy's actual internal-action prefix also permits settlement scope");
                });
            }
            field.SetValue(null, flag);
            bool originalFlag = (bool)flag.GetValue(null);
            try
            {
                flag.SetValue(null, false);
                Run(() =>
                {
                    check((bool)flag.GetValue(null), "Contract lock bypass is enabled only inside settlement departure");
                    Run(() => check((bool)flag.GetValue(null), "Nested settlement departure keeps the outer bypass active"));
                    check((bool)flag.GetValue(null), "Nested scope restores the outer value");
                });
                check(!(bool)flag.GetValue(null), "Settlement restores ordinary contract enforcement afterward");
                flag.SetValue(null, true);
                Run(() => { });
                check((bool)flag.GetValue(null), "An already active deliberate exit is preserved");
                flag.SetValue(null, false); calls = 0;
                bool threw = false;
                try { Run(() => { calls++; throw new InvalidOperationException("departure failed"); }); }
                catch (TargetInvocationException ex) { threw = ex.InnerException is InvalidOperationException; }
                check(threw && calls == 1 && !(bool)flag.GetValue(null),
                    "Departure exceptions restore compatibility state and never replay the action");
            }
            finally { flag.SetValue(null, originalFlag); }
        }
        finally { scan.SetValue(null, oldScan); field.SetValue(null, oldField); }

        var context = new DefinitionContext();
        var initialize = AccessTools.Method(typeof(SaveableTypeDefiner), "Initialize");
        var basic = new SaveableBasicTypeDefiner(); initialize.Invoke(basic, new object[] { context });
        AccessTools.Method(typeof(SaveableBasicTypeDefiner), "DefineBasicTypes").Invoke(basic, null);
        var definer = new BellumCivileSaveDefiner(); initialize.Invoke(definer, new object[] { context });
        var harmony = new Harmony("bellum.tests.mercenary_settlement.save");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "AddClassDefinition"),
                prefix: new HarmonyMethod(typeof(MercenarySettlementCompatibilityTests), nameof(OnlyRecord)));
            harmony.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "ConstructContainerDefinition"),
                prefix: new HarmonyMethod(typeof(MercenarySettlementCompatibilityTests), nameof(OnlyList)));
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineClassTypes").Invoke(definer, null);
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineContainerDefinitions").Invoke(definer, null);
            check(AccessTools.Method(typeof(DefinitionContext), "GetClassDefinition", new[] { typeof(Type) })
                .Invoke(context, new object[] { typeof(CivilWarMercenarySettlementRecord) }) != null
                && AccessTools.Method(typeof(DefinitionContext), "GetContainerDefinition")
                    .Invoke(context, new object[] { typeof(List<CivilWarMercenarySettlementRecord>) }) != null
                && !context.GotError, "Native registry accepts the pending settlement record and list");
            check(typeof(CivilWarMercenarySettlementRecord).GetFields().All(f =>
                f.GetCustomAttributesData().Count(a => a.AttributeType == typeof(SaveableFieldAttribute)) == 1),
                "All pending settlement fields have explicit save IDs");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}

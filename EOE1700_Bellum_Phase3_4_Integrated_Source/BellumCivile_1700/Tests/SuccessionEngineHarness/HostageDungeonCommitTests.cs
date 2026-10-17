using System;
using System.Runtime.Serialization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

internal static class HostageDungeonCommitTests
{
    private static int _commitCalls;
    private static bool _commitResult;

    // Exercise Bellum's actual prefix and field injection without running campaign transactions.
    private static bool FinishDone(ref bool __result)
    {
        _commitCalls++;
        __result = _commitResult;
        return false;
    }

    private static TroopRoster Roster(params CharacterObject[] characters)
    {
        var roster = TroopRoster.CreateDummyTroopRoster();
        foreach (var character in characters) roster.AddToCounts(character, 1);
        return roster;
    }

    private static PartyBase Owner(TroopRoster roster)
    {
        var owner = (PartyBase)FormatterServices.GetUninitializedObject(typeof(PartyBase));
        AccessTools.PropertySetter(typeof(PartyBase), nameof(PartyBase.PrisonRoster)).Invoke(owner, new object[] { roster });
        return owner;
    }

    private static PartyScreenLogic Screen(TroopRoster left, TroopRoster right)
    {
        var screen = new PartyScreenLogic();
        screen.MemberRosters[0] = Roster();
        screen.MemberRosters[1] = Roster();
        screen.PrisonerRosters[0] = left;
        screen.PrisonerRosters[1] = right;
        screen.CurrentData.BindRostersFrom(screen.MemberRosters[1], right, screen.MemberRosters[0], left, null, null);
        var initial = (PartyScreenData)AccessTools.Field(typeof(PartyScreenLogic), "_initialData").GetValue(screen);
        initial.InitializeCopyFrom(null, null);
        initial.CopyFromScreenData(screen.CurrentData);
        AccessTools.PropertySetter(typeof(PartyScreenLogic), nameof(PartyScreenLogic.RightOwnerParty))
            .Invoke(screen, new object[] { Owner(right) });
        return screen;
    }

    private static void Move(PartyScreenLogic screen, CharacterObject troop, int fromSide)
    {
        screen.PrisonerRosters[fromSide].AddToCounts(troop, -1);
        screen.PrisonerRosters[1 - fromSide].AddToCounts(troop, 1);
    }

    private static void CheckDone(Action<bool, string> check, PartyScreenLogic screen, bool allowed,
        string message, bool forced = false, bool nativeResult = true)
    {
        _commitCalls = 0;
        _commitResult = nativeResult;
        bool result = screen.DoneLogic(forced);
        check(result == (allowed && nativeResult) && _commitCalls == (allowed ? 1 : 0), message);
    }

    internal static void Run(Action<bool, string> check, Harmony harmony, CharacterObject hostage,
        CharacterObject otherHostage, CharacterObject soldier, CharacterObject noble)
    {
        harmony.Patch(AccessTools.Method(typeof(PartyScreenLogic), nameof(PartyScreenLogic.DoneLogic)),
            prefix: new HarmonyMethod(typeof(HostageDungeonCommitTests), nameof(FinishDone)) { priority = Priority.Last });

        var donation = Screen(Roster(hostage), Roster(soldier, noble));
        donation.IsTroopTransferableDelegate = Helpers.PartyScreenHelper.DonatePrisonerTransferableDelegate;
        check(donation.LeftOwnerParty == null, "Dungeon donation has no left owner party, as in vanilla");
        CheckDone(check, donation, true, "Untouched dungeon hostage does not block Done");
        Move(donation, soldier, 1);
        Move(donation, noble, 1);
        CheckDone(check, donation, true, "Dungeon accepts ordinary soldier and noble donations alongside a hostage");
        CheckDone(check, donation, true, "Hostage guard preserves native rejection of an otherwise safe donation", nativeResult: false);
        donation.Reset(true);
        check(donation.PrisonerRosters[0].GetTroopCount(hostage) == 1
            && donation.PrisonerRosters[0].GetTroopCount(soldier) == 0
            && donation.PrisonerRosters[0].GetTroopCount(noble) == 0
            && donation.PrisonerRosters[1].GetTroopCount(soldier) == 1
            && donation.PrisonerRosters[1].GetTroopCount(noble) == 1,
            "Cancel restores donated prisoners without moving the hostage");
        CheckDone(check, donation, true, "Restored dungeon roster still passes custody validation");

        var management = Screen(Roster(hostage, noble), Roster(soldier));
        management.IsTroopTransferableDelegate = Helpers.PartyScreenHelper.TroopTransferableDelegate;
        Move(management, noble, 0);
        Move(management, soldier, 1);
        CheckDone(check, management, true, "Manage Prisoners permits ordinary withdrawals and deposits beside a hostage");
        Move(management, hostage, 0);
        CheckDone(check, management, false, "Manage Prisoners blocks removal of the protected hostage");
        CheckDone(check, management, false, "Forced confirmation cannot bypass hostage custody", forced: true);
        Move(management, hostage, 1);
        CheckDone(check, management, true, "Returning the hostage to its original side permits confirmation");
        management.PrisonerRosters[0].AddToCounts(otherHostage, 1);
        CheckDone(check, management, false, "Dungeon rejects unauthorized addition of another protected hostage");

        var exchange = Screen(Roster(hostage), Roster(otherHostage));
        AccessTools.PropertySetter(typeof(PartyScreenLogic), nameof(PartyScreenLogic.LeftOwnerParty))
            .Invoke(exchange, new object[] { Owner(exchange.PrisonerRosters[0]) });
        CheckDone(check, exchange, true, "Unchanged hostages on both sides remain valid with live owner rosters");
        Move(exchange, hostage, 0);
        Move(exchange, otherHostage, 1);
        CheckDone(check, exchange, false, "Snapshot rejects swapped hostages even when owner rosters alias edited rosters");

        var rightOnly = Screen(Roster(), Roster(otherHostage));
        rightOnly.PrisonerRosters[1].AddToCounts(otherHostage, -1);
        CheckDone(check, rightOnly, false, "Snapshot separately detects removal of a right-side hostage");
        var ordinary = Screen(Roster(noble), Roster(soldier));
        Move(ordinary, noble, 0);
        Move(ordinary, soldier, 1);
        CheckDone(check, ordinary, true, "Dungeon without protected hostages retains ordinary prisoner management");
    }
}

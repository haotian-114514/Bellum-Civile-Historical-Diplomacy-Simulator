using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CivilWarMercenarySettlementRecord
    {
        [SaveableField(1)] public FactionObject Faction;
        [SaveableField(2)] public Kingdom Realm;
        [SaveableField(3)] public int Outcome;
        [SaveableField(4)] public bool AllowEliminated;
        [SaveableField(5)] public bool DemandSatisfied;
        [SaveableField(6)] public TextObject Cause;
    }
}

using System.Collections.Generic;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CadetNameRecord
    {
        [SaveableField(1)] public TextObject Name;
        [SaveableField(2)] public TextObject PrimaryName;
        [SaveableField(3)] public TextObject PrimaryRoot;
        [SaveableField(4)] public TextObject SecondaryRoot;
        [SaveableField(5)] public int Number = 1;
        // Keep the original fields readable; older records simply have no further roots.
        [SaveableField(6)] public List<TextObject> AdditionalRoots;
    }
}

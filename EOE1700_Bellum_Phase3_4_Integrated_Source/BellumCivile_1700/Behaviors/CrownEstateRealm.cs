using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal Kingdom ResolveEstateRealm(Kingdom original)
        {
            var visited = new HashSet<Kingdom>();
            while (original != null && visited.Add(original))
            {
                var union = _accessions.Select(a => a.Union).FirstOrDefault(u => u != null
                    && u.Source == original && u.Completed && u.SourceRetired);
                if (union == null) return original;
                original = union.Destination;
            }
            return null;
        }
    }
}

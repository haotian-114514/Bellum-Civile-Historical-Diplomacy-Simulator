using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        private Dictionary<string, CadetNameRecord> _cadetNames = new Dictionary<string, CadetNameRecord>();

        internal CadetNameRecord GetKnownCadetName(Clan clan) => clan?.StringId != null
            && _cadetNames.TryGetValue(clan.StringId, out var name) ? name : null;

        internal string PreviewAbdicationCadetName(CrownAccessionRecord record)
        {
            // A literal name without provenance belongs to an older saved plan.
            if (!string.IsNullOrWhiteSpace(record.CadetName)) return record.CadetName;
            var occupied = GetOccupiedCadetNames(record);
            Clan household = record.EndowmentHouse;
            if (record.Heir?.Spouse?.Clan == household && household != null
                && record.PreviousHouse != null && record.PreviousHouse != household)
                record.CadetNaming = CadetClanNameBuilder.MakeUnique(CadetClanNameBuilder.Dynastic(
                    ReadCadetFamily(record.PreviousHouse), ReadCadetFamily(household)), occupied);
            else
            {
                var ids = GetCadetNamingFiefIds(record).ToList();
                string preferred = record.CadetNamingSettlementId;
                var residence = ids.OrderBy(id => id == preferred ? 0 : 1).Select(Settlement.Find)
                    .Select(s => s?.Town).FirstOrDefault(t => t != null);
                record.CadetNaming = BuildTerritorialCadetName(household, residence, occupied);
            }
            return record.CadetNaming.Name.ToString();
        }

        private CadetNameRecord ReadCadetFamily(Clan clan) => string.IsNullOrWhiteSpace(clan?.Name?.ToString()) ? null
            : CadetClanNameBuilder.Family(clan.Name, GetKnownCadetName(clan));

        private CadetNameRecord BuildTerritorialCadetName(Clan parent, Town residence, HashSet<string> occupied)
        {
            IEnumerable<TextObject> roots = residence == null ? Enumerable.Empty<TextObject>()
                : new[] { residence.Settlement?.Name ?? residence.Name }
                    .Concat(residence.Villages.Select(v => v.Settlement?.Name));
            return CadetClanNameBuilder.Territorial(parent?.Culture?.StringId,
                $"{parent?.StringId ?? string.Empty}|{residence?.StringId ?? string.Empty}", roots, ReadCadetFamily(parent), occupied);
        }

        private IEnumerable<CrownAccessionRecord> GetPendingCadetNames()
        {
            return (Campaign.Current?.GetCampaignBehavior<CrownAccessionBehavior>()?.GetPendingCadetNamingPlans()
                    ?? Enumerable.Empty<CrownAccessionRecord>())
                .Concat(Campaign.Current?.GetCampaignBehavior<SuccessionChallengeBehavior>()?.GetPendingCadetNamingPlans()
                    ?? Enumerable.Empty<CrownAccessionRecord>())
                .Concat(_crossClanEstates.Where(e => e != null && !e.Completed)
                    .SelectMany(e => e.Shares ?? Enumerable.Empty<CrossClanEstateShare>())
                    .Where(s => s != null && !s.Completed).Select(s => s.CadetPlan))
                .Concat(_pendingPartitions.Where(p => p != null)
                    .SelectMany(p => p.EstateShares ?? Enumerable.Empty<CrossClanEstateShare>())
                    .Where(s => s != null && !s.Completed).Select(s => s.CadetPlan))
                .Where(p => p != null && !p.Completed && !p.CadetInitialized).Distinct();
        }

        private HashSet<string> GetOccupiedCadetNames(CrownAccessionRecord ownPlan = null)
        {
            var names = Clan.All.Where(c => c != null && c != ownPlan?.Cadet).Select(c => c.Name?.ToString())
                .Concat(GetPendingCadetNames().Where(p => p != ownPlan
                    && (string.IsNullOrEmpty(ownPlan?.CadetId) || p.CadetId != ownPlan.CadetId))
                    .Select(p => p.CadetNaming?.Name?.ToString() ?? p.CadetName));
            return new HashSet<string>(names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(CadetClanNameBuilder.Normalize),
                StringComparer.OrdinalIgnoreCase);
        }

        private void FinalizeCadetNameBeforeCreation(CrownAccessionRecord record)
        {
            if (record.Cadet != null || record.CadetNaming == null) return;
            // Reservations cover Bellum plans; a later external rename can still occupy the name.
            // Only new, not-yet-created plans can advance their numeral. Legacy plans are untouched.
            record.CadetNaming = CadetClanNameBuilder.MakeUnique(record.CadetNaming, GetOccupiedCadetNames(record));
            record.CadetName = record.CadetNaming.Name.ToString();
        }

        private void RememberCadetName(Clan clan, CadetNameRecord name)
        {
            if (clan != null && name != null) _cadetNames[clan.StringId] = name;
        }
    }
}

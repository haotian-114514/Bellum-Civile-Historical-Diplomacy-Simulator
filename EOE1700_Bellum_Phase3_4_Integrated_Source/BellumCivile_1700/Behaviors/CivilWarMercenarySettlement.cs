using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private const int MercenaryRebelVictory = 1, MercenaryLiegeVictory = 2,
            MercenaryWhitePeace = 3, MercenaryIndependence = 4, MercenaryParentCollapse = 5;
        private List<CivilWarMercenarySettlementRecord> _pendingMercenarySettlements = new List<CivilWarMercenarySettlementRecord>();

        private bool PrepareMercenarySettlement(FactionObject faction, Kingdom realm, int outcome,
            bool allowEliminated = false, bool demandSatisfied = false, TextObject cause = null,
            IEnumerable<Kingdom> additionalRealms = null, IEnumerable<Clan> additionalMercenaries = null)
        {
            if (realm == null) return false;
            bool ready = ReleaseMercenariesFromKingdom(realm);
            foreach (var other in (additionalRealms ?? Enumerable.Empty<Kingdom>()).Where(k => k != realm).Distinct())
                ready &= ReleaseMercenariesFromKingdom(other);
            foreach (var clan in (additionalMercenaries ?? Enumerable.Empty<Clan>()).ToList())
                ready &= ReleaseCivilWarMercenary(clan, clan?.Kingdom) != MercenaryReleaseResult.Blocked;

            var pending = _pendingMercenarySettlements.FirstOrDefault(r => r.Realm == realm && r.Faction == faction);
            if (ready)
            {
                if (pending != null) _pendingMercenarySettlements.Remove(pending);
                return true;
            }
            if (pending == null)
            {
                pending = new CivilWarMercenarySettlementRecord { Faction = faction, Realm = realm };
                _pendingMercenarySettlements.Add(pending);
                var remaining = new[] { realm }.Concat(additionalRealms ?? Enumerable.Empty<Kingdom>())
                    .Where(k => k != null).SelectMany(k => k.Clans)
                    .Concat(additionalMercenaries ?? Enumerable.Empty<Clan>())
                    .Where(c => c != null && c.IsUnderMercenaryService).Distinct();
                BellumCivileLogger.Log($"Civil-war settlement pending mercenary release; realm={realm.StringId}; outcome={outcome}; "
                    + $"mercenaries={string.Join(",", remaining.Select(c => c.StringId))}.");
            }
            pending.Outcome = outcome;
            pending.AllowEliminated = allowEliminated;
            pending.DemandSatisfied = demandSatisfied;
            pending.Cause = cause;
            return false;
        }

        private void ResumePendingMercenarySettlements()
        {
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return;
            foreach (var pending in _pendingMercenarySettlements.ToList())
            {
                var faction = pending.Faction;
                if (pending.Realm == null || (pending.Outcome != MercenaryParentCollapse
                    && (faction?.ParentKingdom == null
                        || !manager.GetFactionsInKingdom(faction.ParentKingdom).Contains(faction))))
                {
                    _pendingMercenarySettlements.Remove(pending);
                    continue;
                }
                switch (pending.Outcome)
                {
                    case MercenaryRebelVictory: ResolveRebelVictory(faction, pending.Realm); break;
                    case MercenaryLiegeVictory: ResolveLiegeVictoryInternal(faction, pending.Realm, pending.AllowEliminated); break;
                    case MercenaryWhitePeace: ResolveWhitePeace(faction, pending.Realm, pending.DemandSatisfied, pending.Cause); break;
                    case MercenaryIndependence: ResolveRecognizedIndependence(faction, pending.Realm, pending.AllowEliminated); break;
                    case MercenaryParentCollapse: HandleParentCollapse(pending.Realm, pending.Realm.IsEliminated, faction); break;
                    default: _pendingMercenarySettlements.Remove(pending); break;
                }
            }
        }
    }
}

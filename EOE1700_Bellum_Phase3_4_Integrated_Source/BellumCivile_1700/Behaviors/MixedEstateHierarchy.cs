using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class FeudalTitleBehavior
    {
        private bool TryApplyMixedEstateOwnership(Settlement settlement, Clan recipient)
        {
            var pending = Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()
                ?.GetMixedEstateDeliveryInProgress(settlement?.StringId, recipient);
            if (pending == null) return false;
            var before = pending.CrownBatch.Titles.Single(t => t.Rank == FeudalTitleType.Barony && t.CapitalId == settlement.StringId);
            var title = GetTitle(before.TitleId);
            if (settlement.OwnerClan != recipient || title?.IsActive != true
                || title.DeJureHolderClanId != before.LegalHolderId || title.ParentTitleId != before.LegalParentId
                || title.AssociatedKingdomId != before.OriginRealmId
                || title.DeFactoParentTitleId != pending.CrownBatch.PoliticalParentTargets[title.TitleId]
                || title.DeFactoHolderClanId != pending.ParentClanId)
                throw new InvalidOperationException("Mixed estate callback no longer matches captured rights.");
            title.SetDeFactoHolder(recipient.StringId);
            title.MarkSynced(CurrentDay);
            RebuildRuntimeIndexes();
            QueueServiceReview(title, "mixed estate possession");
            return true;
        }

        internal bool TryFinalizeMixedEstateHierarchy(PendingPartitionSuccessionRecord pending, out string reason)
        {
            var behavior = Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>();
            reason = "mixed estate land must be delivered before hierarchy settlement";
            if (pending.MixedEstate?.Shares.All(s => s.LandedSettled) != true || behavior == null) return false;
            if (pending.MixedHierarchyReturned) return behavior.TryVerifyCrownBatchWorld(pending, out reason);
            if (pending.MixedHierarchyStarted || pending.MixedParentTargets != null)
            { reason = "mixed hierarchy application was interrupted; native state requires reconciliation"; return false; }
            if (!behavior.TryVerifyCrownBatchWorld(pending, out reason)) return false;
            var realms = new HashSet<Kingdom>(pending.CrownPromotions.Select(p => p.Successor)) { pending.MixedEstate.Realm };
            var targets = GetAllTitles().Where(t => t.IsActive).ToDictionary(t => t.TitleId, t => t.DeFactoParentTitleId);
            foreach (var title in GetAllTitles().Where(t => t.IsActive))
            {
                var realm = ResolveClan(title.DeFactoHolderClanId)?.Kingdom;
                if (realm != null && realms.Contains(realm)) targets[title.TitleId] = ResolveBestDeFactoParent(title, realm)?.TitleId ?? string.Empty;
                if (title.TitleId == pending.PrimarySovereignTitleId || pending.CrownBatch.Recipients.ContainsKey(title.TitleId))
                    targets[title.TitleId] = string.Empty;
            }
            var projected = GetAllTitles().Where(t => t.IsActive).ToDictionary(t => t.TitleId, t => RealmUnionTitleRecord.Capture(t));
            var houseRealms = Clan.All.Where(c => c?.Kingdom != null && realms.Contains(c.Kingdom))
                .ToDictionary(c => c.StringId, c => c.Kingdom.StringId);
            if (!MixedEstateBatch.ValidateHierarchy(pending, projected, houseRealms, targets, out reason)) return false;
            pending.MixedParentTargets = targets;
            pending.MixedHierarchyStarted = true;
            foreach (var pair in targets)
            {
                var title = GetTitle(pair.Key);
                if (title.DeFactoParentTitleId == pair.Value) continue;
                title.SetDeFactoParentTitle(pair.Value);
                title.MarkSynced(CurrentDay);
                QueueServiceReview(title, "mixed estate hierarchy");
            }
            RebuildRuntimeIndexes();
            pending.MixedHierarchyReturned = true;
            return behavior.TryVerifyCrownBatchWorld(pending, out reason);
        }
    }
}

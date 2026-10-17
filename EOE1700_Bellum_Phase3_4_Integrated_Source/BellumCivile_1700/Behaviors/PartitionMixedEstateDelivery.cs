using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal PendingPartitionSuccessionRecord GetMixedEstateDeliveryInProgress(string settlementId, Clan recipient)
            => recipient == null || string.IsNullOrWhiteSpace(settlementId) ? null : _pendingPartitions
                .SingleOrDefault(p => p.MixedEstate != null && p.CrownBatch?.Completed == false
                    && MixedEstateBatch.OrdinaryShares(p).Any(s => s.Recipient == recipient
                        && s.PartitionReceipts?.Any(r => !r.IsTitle && r.AssetId == settlementId && r.Started && !r.ActionReturned) == true));

        internal bool TryDeliverMixedEstateLand(PendingPartitionSuccessionRecord pending, FeudalTitleBehavior titles, out string reason)
        {
            reason = "mixed estate requires verified Crown hierarchy";
            if (pending.CrownBatch?.HierarchyVerified != true || !TryVerifyCrownBatchWorld(pending, out reason)) return false;
            var estate = pending.MixedEstate;
            var snapshot = pending.CrownBatch.Titles.ToDictionary(t => t.TitleId, StringComparer.Ordinal);
            foreach (var share in estate.Shares)
            {
                var promotion = MixedEstateBatch.Promotion(pending, share);
                if (promotion == null) continue;
                if (!promotion.HasVerifiedEstateReceipts() || promotion.Founder?.Leader != share.Heir) return false;
                share.Recipient = promotion.Founder;
                share.DeliveredFiefs = promotion.EstateFiefIds.ToList();
                share.DeliveredTitles = promotion.EstateTitleIds.ToList();
                share.LandedSettled = true;
            }
            foreach (var share in MixedEstateBatch.OrdinaryShares(pending))
            {
                if (share.Heir?.IsAlive != true) { reason = "mixed estate beneficiary died after batch capture"; return false; }
                if (share.Recipient == null)
                {
                    var promoted = pending.CrownPromotions.SingleOrDefault(p => p.Heir == share.Heir);
                    if (promoted != null) share.Recipient = promoted.Founder;
                    else if (!TryPrepareMixedEstateCadet(pending, share, out reason)) return false;
                }
                var recipient = share.Recipient;
                if (recipient?.Leader?.IsAlive != true || recipient.IsEliminated || share.Heir.Clan != recipient)
                { reason = "mixed estate receiving house or beneficiary changed"; return false; }
                if (share.PartitionFounderReturned) titles.MoveCrownHeirClaims(share.Heir, estate.Source, recipient);
                if (share.PartitionReceipts == null)
                    share.PartitionReceipts = share.Fiefs.Select(id => new CrownEstateDeliveryRecord { AssetId = id })
                        .Concat(share.Titles.Where(id => id != estate.RealmCrownTitleId)
                            .OrderBy(id => snapshot[id].Rank).ThenBy(id => id, StringComparer.Ordinal)
                            .Select(id => new CrownEstateDeliveryRecord { AssetId = id, IsTitle = true })).ToList();
                foreach (var receipt in share.PartitionReceipts)
                {
                    bool ok;
                    if (receipt.IsTitle)
                    {
                        var title = titles.GetTitle(receipt.AssetId);
                        var before = snapshot[receipt.AssetId];
                        string actual = before.ActualHolderId == estate.Source.StringId ? recipient.StringId : before.ActualHolderId;
                        ok = receipt.TryDeliver(() => title?.DeJureHolderClanId == estate.Source.StringId,
                            () => titles.LegalizeTitleInheritance(recipient, title, "mixed estate inheritance",
                                preserveDeFacto: before.ActualHolderId != estate.Source.StringId),
                            () => title?.DeJureHolderClanId == recipient.StringId && title.DeFactoHolderClanId == actual, out reason);
                        if (ok && !share.DeliveredTitles.Contains(receipt.AssetId)) share.DeliveredTitles.Add(receipt.AssetId);
                    }
                    else
                    {
                        var settlement = Settlement.Find(receipt.AssetId);
                        ok = receipt.TryDeliver(() => settlement?.OwnerClan == estate.Source && settlement.SiegeEvent == null
                                && settlement.Party?.MapEvent == null && !FiefDeliberationBehavior.IsAwaitingAllocation(settlement),
                            () => { if (recipient != estate.Source && !titles.TransferInheritedPossession(estate.Source, recipient, settlement))
                                    throw new InvalidOperationException("mixed estate possession transfer failed"); },
                            () => settlement?.OwnerClan == recipient, out reason);
                        if (ok && !share.DeliveredFiefs.Contains(receipt.AssetId)) share.DeliveredFiefs.Add(receipt.AssetId);
                    }
                    if (!ok || !TryVerifyCrownBatchWorld(pending, out reason)) return false;
                }
                if (share.Titles.Contains(estate.RealmCrownTitleId)) SupersedeEstateTitle(share, estate.RealmCrownTitleId);
                share.LandedSettled = true;
            }
            return titles.TryFinalizeMixedEstateHierarchy(pending, out reason);
        }

        private bool TryPrepareMixedEstateCadet(PendingPartitionSuccessionRecord pending, CrossClanEstateShare share, out string reason)
        {
            reason = "mixed ordinary cadet is unavailable";
            var estate = pending.MixedEstate;
            if (share.PartitionFounderStarted) return share.PartitionFounderReturned && share.CadetPlan?.Cadet == share.Recipient;
            if (!IsEligibleMixedFounder(estate, share.Heir)) return false;
            if (share.CadetPlan == null)
            {
                var otherHeirs = new HashSet<Hero>(estate.Shares.Where(s => s.Heir != share.Heir).Select(s => s.Heir));
                var draft = new CrownAccessionRecord { Realm = estate.Realm, PreviousHouse = estate.Source,
                    Predecessor = estate.Deceased, Heir = share.Heir, CadetId = "bc_mixed_estate_" + Guid.NewGuid().ToString("N"),
                    EndowmentFiefs = share.Fiefs.ToList(), CadetNamingSettlementId = share.PrimaryFiefId,
                    Household = new[] { share.Heir, share.Heir.Spouse }.Concat(share.Heir.Children)
                        .Where(h => h != null && h.IsAlive && h.Clan == estate.Source && h != estate.Source.Leader
                            && h != Hero.MainHero && !otherHeirs.Contains(h)).Distinct().Select(h => h.StringId).ToList() };
                draft.CadetName = PreviewAbdicationCadetName(draft);
                share.CadetPlan = draft;
            }
            var home = Settlement.Find(share.PrimaryFiefId);
            if (home?.Town == null || home.OwnerClan != estate.Source || !share.Fiefs.Contains(home.StringId)
                || Clan.All.Any(c => c.StringId == share.CadetPlan.CadetId)
                || !TryValidateCadetHousehold(share.CadetPlan, out _)) return false;
            if (share.CadetPlan.Household.Select(ResolveHero).Any(h => h == null || !h.IsAlive || h.IsPrisoner || h.IsTraveling
                || h.PartyBelongedTo?.MapEvent != null || h.PartyBelongedTo?.SiegeEvent != null || h.PartyBelongedTo?.Army != null)) return false;
            share.PartitionFounderStarted = true;
            if (!PrepareAbdicationCadet(share.CadetPlan)) { reason = share.CadetPlan.AbdicationFailure; return false; }
            share.Recipient = share.CadetPlan.Cadet;
            share.PartitionFounderReturned = true;
            return TryVerifyCrownBatchWorld(pending, out reason);
        }
    }
}

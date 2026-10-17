using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class MixedEstateBatch
    {
        internal static CrownPartitionPromotionRecord Promotion(PendingPartitionSuccessionRecord pending, CrossClanEstateShare share)
            => pending.CrownPromotions?.SingleOrDefault(p => p.CrownId == share.RootTitleId && share.Titles.Contains(p.CrownId));

        internal static IEnumerable<CrossClanEstateShare> OrdinaryShares(PendingPartitionSuccessionRecord pending)
            => pending.MixedEstate.Shares.Where(s => Promotion(pending, s) == null);

        internal static bool TryComplete(PendingPartitionSuccessionRecord pending, bool worldVerified, out string reason)
        {
            if (!TryValidate(pending, out reason)) return false;
            var estate = pending.MixedEstate;
            var batch = pending.CrownBatch;
            reason = "mixed estate completion lacks verified stages, payments or announcements";
            var announcements = new HashSet<string>(Enumerable.Range(0, estate.Shares.Count).Select(i => "mixed-share:" + i)) { "treasury" };
            if (!worldVerified || batch.Completed || !batch.HierarchyVerified || !batch.CourtFinalizationReturned
                || !batch.ObligationsVerified || batch.OriginalAgreements == null || !pending.MixedHierarchyStarted
                || !pending.MixedHierarchyReturned || pending.MixedParentTargets == null
                || !pending.ClaimsStarted || !pending.ClaimsReturned || pending.OriginalClaims == null
                || pending.GoldPayments == null || pending.GoldPayments.Count != estate.Shares.Count
                || pending.CrownPromotions.Any(p => !p.HasVerifiedEstateReceipts() || !p.CrownRegistrationReturned
                    || !p.GovernmentVerified || !p.RealmVerified || !p.FounderVerified)
                || OrdinaryShares(pending).Any(s => s.PartitionReceipts == null
                    || s.PartitionReceipts.Any(r => r == null || !r.Started || !r.ActionReturned || !r.Verified))
                || estate.Shares.Any(s => !s.LandedSettled || s.Recipient == null || s.GoldPayment == null
                    || !s.GoldPayment.GoldDebited || !s.GoldPayment.GoldCredited)
                || !pending.GoldPayments.SequenceEqual(estate.Shares.Select(s => s.GoldPayment))
                || batch.AnnouncementsStarted == null || batch.AnnouncementsReturned == null
                || batch.AnnouncementsStarted.Count != announcements.Count || batch.AnnouncementsReturned.Count != announcements.Count
                || !announcements.SetEquals(batch.AnnouncementsStarted) || !announcements.SetEquals(batch.AnnouncementsReturned)) return false;
            foreach (var share in estate.Shares)
            {
                var payment = share.GoldPayment;
                int amount = !share.ReceivesGold || share.IsEscheat || share.Recipient.Leader == estate.GoldDonor ? 0 : estate.GoldPerShare;
                if (payment.EndowmentDonor != estate.GoldDonor || payment.GoldRecipient != share.Recipient.Leader
                    || payment.EndowmentGold != amount || payment.DeliveredGold < 0 || payment.DeliveredGold > amount) return false;
            }
            foreach (var promotion in pending.CrownPromotions)
            {
                promotion.ObligationsSettled = true;
                promotion.Announced = true;
                promotion.Completed = true;
            }
            foreach (var share in estate.Shares) { share.Completed = true; share.Status = "completed in mixed Crown batch"; }
            estate.Completed = true;
            estate.Failure = null;
            batch.Completed = true;
            reason = null;
            return true;
        }

        internal static bool TryValidate(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "mixed estate no longer matches its recorded Crown batch";
            var estate = pending?.MixedEstate;
            var batch = pending?.CrownBatch;
            if (estate?.PolicyVersion != 1 || batch == null || pending.CrownPromotions == null || pending.CrownPromotions.Count == 0
                || pending.CrownPromotions.Any(p => p == null || p.EstateTitleIds == null || p.EstateFiefIds == null)
                || pending.CrownPromotions.Select(p => p.CrownId).Distinct().Count() != pending.CrownPromotions.Count
                || estate.Source?.StringId != pending.ParentClanId || estate.Realm?.StringId != pending.KingdomId
                || estate.Deceased?.StringId != pending.DeadLeaderId || estate.Shares == null
                || estate.Shares.Any(s => s?.Heir == null || s.Titles == null || s.Fiefs == null)) return false;
            var titleIds = estate.Shares.SelectMany(s => s.Titles).ToList();
            var fiefIds = estate.Shares.SelectMany(s => s.Fiefs).ToList();
            string[] Split(string ids) => (ids ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (titleIds.Distinct().Count() != titleIds.Count || fiefIds.Distinct().Count() != fiefIds.Count
                || !new HashSet<string>(titleIds).SetEquals(Split(pending.TitleIds))
                || !new HashSet<string>(fiefIds).SetEquals(Split(pending.FiefIds))) return false;
            foreach (var promotion in pending.CrownPromotions)
            {
                var matches = estate.Shares.Where(s => s.RootTitleId == promotion.CrownId).ToList();
                if (matches.Count != 1 || matches[0].Heir != promotion.Heir
                    || !new HashSet<string>(matches[0].Titles).SetEquals(promotion.EstateTitleIds)
                    || !new HashSet<string>(matches[0].Fiefs).SetEquals(promotion.EstateFiefIds)) return false;
            }
            reason = null;
            return true;
        }

        internal static bool TryProject(PendingPartitionSuccessionRecord pending,
            Dictionary<string, RealmUnionTitleRecord> titles, Dictionary<string, string> realms,
            Dictionary<string, List<string>> holdings, out string reason)
        {
            reason = null;
            if (pending.MixedEstate == null) return true;
            if (!TryValidate(pending, out reason)) return false;
            var batch = pending.CrownBatch;
            reason = "mixed estate receipts or receiving household are inconsistent";
            foreach (var share in pending.MixedEstate.Shares)
            {
                var promotion = pending.CrownPromotions.SingleOrDefault(p => p.Heir == share.Heir);
                var expectedHouse = share.Recipient ?? (promotion?.FounderCreationStarted == true
                    ? promotion.Founder : pending.MixedEstate.Source);
                if (!share.Heir.IsAlive || share.Heir.IsDisabled || expectedHouse == null || expectedHouse.IsEliminated
                    || share.Heir.Clan != expectedHouse || expectedHouse.Leader?.IsAlive != true
                    || !TaleWorlds.CampaignSystem.Clan.All.Contains(expectedHouse)) return false;
            }
            foreach (var share in OrdinaryShares(pending))
            {
                if (share.Titles.Any(id => !titles.ContainsKey(id))) return false;
                if (share.PartitionFounderStarted != share.PartitionFounderReturned) return false;
                string recipient = share.Recipient?.StringId;
                if (share.PartitionFounderReturned)
                {
                    if (share.CadetPlan?.Cadet != share.Recipient || !share.CadetPlan.CadetInitialized
                        || share.CadetPlan.Heir != share.Heir || string.IsNullOrWhiteSpace(recipient)
                        || realms.ContainsKey(recipient)) return false;
                    realms.Add(recipient, batch.SourceRealmId);
                    holdings.Add(recipient, new List<string>());
                }
                if (share.PartitionReceipts == null)
                {
                    if (share.LandedSettled) return false;
                    continue;
                }
                if (!batch.HierarchyVerified || recipient == null || !holdings.ContainsKey(recipient)
                    || share.Recipient.IsEliminated || share.Heir.IsDead || share.Heir.Clan != share.Recipient
                    || share.PartitionFounderReturned && share.Recipient.Leader != share.Heir) return false;
                var ids = share.Fiefs.Select(id => Tuple.Create(false, id))
                    .Concat(share.Titles.Where(id => id != pending.MixedEstate.RealmCrownTitleId)
                        .OrderBy(id => titles[id].Rank).ThenBy(id => id, StringComparer.Ordinal)
                        .Select(id => Tuple.Create(true, id))).ToList();
                if (share.PartitionReceipts.Any(r => r == null)
                    || !ids.SequenceEqual(share.PartitionReceipts.Select(r => Tuple.Create(r.IsTitle, r.AssetId)))) return false;
                bool waiting = false;
                foreach (var receipt in share.PartitionReceipts)
                {
                    if (!receipt.Started)
                    {
                        if (receipt.ActionReturned || receipt.Verified) return false;
                        waiting = true;
                        continue;
                    }
                    if (waiting || !receipt.ActionReturned || !receipt.Verified) return false;
                    if (receipt.IsTitle)
                    {
                        var title = titles[receipt.AssetId];
                        if (title.LegalHolderId != batch.RetainedHouseId) return false;
                        title.LegalHolderId = recipient;
                        if (title.ActualHolderId == batch.RetainedHouseId) title.ActualHolderId = recipient;
                    }
                    else
                    {
                        var title = titles.Values.SingleOrDefault(t => t.Rank == FeudalTitleType.Barony && t.CapitalId == receipt.AssetId);
                        if (title?.ActualHolderId != batch.RetainedHouseId
                            || !holdings[batch.RetainedHouseId].Remove(receipt.AssetId)) return false;
                        holdings[recipient].Add(receipt.AssetId);
                        title.ActualHolderId = recipient;
                    }
                }
                if (share.LandedSettled && waiting) return false;
            }
            if (pending.MixedHierarchyStarted != pending.MixedHierarchyReturned) return false;
            if (pending.MixedHierarchyReturned)
            {
                if (pending.MixedParentTargets == null || !new HashSet<string>(titles.Keys).SetEquals(pending.MixedParentTargets.Keys)) return false;
                if (!ValidateHierarchy(pending, titles, realms, pending.MixedParentTargets, out reason)) return false;
                foreach (var entry in pending.MixedParentTargets) titles[entry.Key].ActualParentId = entry.Value;
            }
            else if (pending.MixedParentTargets != null) return false;
            reason = null;
            return true;
        }

        internal static bool ValidateHierarchy(PendingPartitionSuccessionRecord pending,
            IReadOnlyDictionary<string, RealmUnionTitleRecord> titles, IReadOnlyDictionary<string, string> realms,
            IReadOnlyDictionary<string, string> targets, out string reason)
        {
            reason = "mixed hierarchy contains a missing, lower-ranked or foreign parent";
            if (!new HashSet<string>(titles.Keys).SetEquals(targets.Keys)) return false;
            foreach (var entry in targets)
            {
                var child = titles[entry.Key];
                if (entry.Value == null) return false;
                if (!realms.TryGetValue(child.ActualHolderId ?? "", out var realm))
                {
                    if (entry.Value != child.ActualParentId) return false;
                    continue;
                }
                var crownId = realm == pending.KingdomId ? pending.PrimarySovereignTitleId
                    : pending.CrownPromotions.FirstOrDefault(p => p.SuccessorId == realm)?.CrownId;
                if (entry.Value.Length == 0)
                {
                    if (crownId != null && child.Rank < titles[crownId].Rank) return false;
                    continue;
                }
                if (entry.Key == pending.PrimarySovereignTitleId || pending.CrownBatch.Recipients.ContainsKey(entry.Key)
                    || !titles.TryGetValue(entry.Value, out var parent) || parent.Rank <= child.Rank) return false;
                if (!realms.TryGetValue(parent.ActualHolderId ?? "", out var parentRealm) || realm != parentRealm) return false;
            }
            reason = null;
            return true;
        }
    }
}

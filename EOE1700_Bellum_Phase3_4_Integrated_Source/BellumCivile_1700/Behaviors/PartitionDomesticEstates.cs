using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal IEnumerable<CrossClanEstateRecord> GetPendingEstatePreviews()
        {
            foreach (var record in _crossClanEstates.Where(r => r != null && !r.Completed)) yield return record;
            foreach (var pending in _pendingPartitions.Where(p => p != null && p.MixedEstate == null && p.CrownBatch?.Completed != true))
                yield return new CrossClanEstateRecord
                {
                    Source = ResolveClan(pending.ParentClanId), Deceased = ResolveHero(pending.DeadLeaderId),
                    Realm = ResolveKingdom(pending.KingdomId),
                    Shares = pending.EstateShares ?? new List<CrossClanEstateShare>()
                };
        }

        internal static bool HasCommittedEstateShare(CrossClanEstateRecord estate, CrossClanEstateShare share)
        {
            if (share.DeliveredFiefs.Count > 0 || share.DeliveredTitles.Count > 0
                || share.DeliveredSovereignTransfers?.Count > 0 || share.CadetPlan?.Cadet != null
                || share.CadetPlan?.CadetInitialized == true || share.GoldPayment?.GoldDebited == true) return true;
            // A native callback may have changed possession before the receipt was written.
            var recipient = share.Recipient ?? share.Heir?.Clan;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            return recipient != null && recipient != estate.Source
                && (share.Fiefs.Any(id => Settlement.Find(id)?.OwnerClan == recipient)
                    || share.Titles.Any(id => titles?.GetTitle(id)?.DeJureHolderClanId == recipient.StringId));
        }

        private static bool RetainsSovereignEstateInSource(CrossClanEstateRecord estate, CrossClanEstateShare share,
            FeudalTitleRecord title)
        {
            Clan source = estate.Source;
            Kingdom realm = EstateInheritancePolicy.ResolvePermanentRealm(source);
            return estate.PolicyVersion > 0 && source != null && realm?.RulingClan == source
                && CrownAccessionBehavior.Instance?.IsPending(realm) != true
                && share.Heir?.IsAlive == true && share.Heir.Clan == source
                && (RegencyBehavior.Instance?.GetLegalClanHead(source) ?? source.Leader) == share.Heir
                && share.CadetPlan == null && (share.Recipient == null || share.Recipient == source)
                && title.DeJureHolderClanId == source.StringId && title.DeFactoHolderClanId == source.StringId;
        }

        private bool CanStartDomesticEstateDelivery(CrossClanEstateRecord estate, FeudalTitleBehavior titles)
        {
            if (estate.PolicyVersion == 0) return true;
            if (estate.Shares.Any(s => HasCommittedEstateShare(estate, s))) return true;
            foreach (var share in estate.Shares.Where(s => !s.Completed && !s.IsEscheat))
            foreach (var title in share.Titles.Except(share.DeliveredTitles).Except(share.SupersededTitles)
                .Select(titles.GetTitle).Where(t => RequiresSovereignEstateExecutor(estate, titles, t)))
            {
                if (RetainsSovereignEstateInSource(estate, share, title)) continue;
                Clan house = share.Heir?.Clan;
                if (house != null && house != estate.Source && house.Kingdom != estate.Realm
                    && CrownAccessionBehavior.CanUseExistingCrownHouse(share.Heir, house.Leader,
                        RegencyBehavior.Instance?.GetLegalClanHead(house))
                    && titles.CanReceiveForeignSovereignInheritance(house, title)) continue;
                // Local Crown batches own a complete estate. Do not partially execute a
                // mixed journal that still requires a separate domestic Crown promotion.
                ReportEstateShare(estate, share, "mixed estate awaiting sovereign executor before delivery: " + title.TitleId);
                return false;
            }
            return true;
        }

        private bool PrepareDomesticEstateShare(CrossClanEstateRecord estate, CrossClanEstateShare share)
        {
            Kingdom realm = CrownAccessionBehavior.Instance?.ResolveEstateRealm(estate.Realm) ?? estate.Realm;
            if (realm == null || realm.IsEliminated)
            { ReportEstateShare(estate, share, "awaiting the original estate realm or its verified union successor"); return false; }
            bool Valid(Hero heir) => heir != null && heir.Clan != null && !heir.IsWanderer && !heir.IsNotable
                && SuccessionLawHelper.IsBasicSuccessionCandidate(heir, estate.Deceased, true)
                && EstateInheritancePolicy.IsDomesticHouse(heir.Clan, realm)
                && SuccessionLawHelper.IsEligibleUnderSuccessionLaws(heir, estate.Deceased,
                    new SuccessionLawSet(estate.GenderLaw, estate.SuccessionLaw));
            if (!share.IsEscheat && !Valid(share.Heir))
            {
                if (share.Heir == null)
                { ReportEstateShare(estate, share, "awaiting unresolved domestic beneficiary reference"); return false; }
                if (HasCommittedEstateShare(estate, share))
                {
                    Clan receivingHouse = share.Recipient ?? share.CadetPlan?.Cadet ?? share.Heir.Clan;
                    Hero successor = share.Heir.IsDead && (share.CadetPlan == null || share.CadetPlan.CadetInitialized)
                        ? OrderEstateHeirs(receivingHouse?.Heroes ?? Enumerable.Empty<Hero>(), share.Heir,
                            new SuccessionLawSet(estate.GenderLaw, estate.SuccessionLaw))
                            .FirstOrDefault(h => h.Clan == receivingHouse && Valid(h)) : null;
                    if (successor == null)
                    { ReportEstateShare(estate, share, "domestic beneficiary unavailable after estate delivery began; receipts retained"); return false; }
                    // Finish into the already receiving house; never move a partially delivered
                    // estate to another house merely because its beneficiary died.
                    share.Heir = successor;
                    share.Recipient = receivingHouse;
                    share.BeneficiaryReconciled = true;
                    ReportEstateShare(estate, share, "partial domestic estate followed successor within its receiving house " + successor.StringId);
                    return true;
                }
                Hero replacement = null;
                if (share.Heir.IsDead)
                    replacement = OrderEstateHeirs(share.Heir.Clan?.Heroes ?? Enumerable.Empty<Hero>(), share.Heir,
                        new SuccessionLawSet(estate.GenderLaw, estate.SuccessionLaw)).FirstOrDefault(Valid);
                replacement = replacement ?? (estate.DomesticHeirs ?? new List<Hero>()).FirstOrDefault(Valid);
                share.Heir = replacement;
                share.Recipient = null;
                share.CadetPlan = null;
                share.BeneficiaryReconciled = true;
                share.IsEscheat = replacement == null;
                ReportEstateShare(estate, share, replacement == null ? "no eligible domestic beneficiary; estate reverting to Crown"
                    : "unstarted domestic share followed eligible beneficiary " + replacement.StringId);
            }
            if (share.IsEscheat)
            {
                Clan crown = realm.RulingClan;
                if (CrownAccessionBehavior.Instance?.IsPending(realm) == true || crown?.Leader?.IsAlive != true)
                { ReportEstateShare(estate, share, "awaiting the lawful Crown before estate reversion"); return false; }
                if (share.Recipient != null && share.Recipient != crown && HasCommittedEstateShare(estate, share))
                { ReportEstateShare(estate, share, "Crown changed during partial reversion; receipts retained"); return false; }
                share.Heir = crown.Leader;
                share.Recipient = crown;
                share.ReceivesGold = false;
                return true;
            }
            if (share.Recipient != null && !EstateInheritancePolicy.IsDomesticHouse(share.Recipient, realm))
            {
                if (HasCommittedEstateShare(estate, share))
                { ReportEstateShare(estate, share, "receiving house left the realm during partial delivery; receipts retained"); return false; }
                share.Recipient = null;
            }
            return true;
        }
    }
}

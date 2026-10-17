using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal bool TrySettleMixedEstateRewards(PendingPartitionSuccessionRecord pending, FeudalTitleBehavior titles, out string reason)
        {
            reason = "mixed rewards require completed land, hierarchy and court stages";
            var estate = pending.MixedEstate;
            if (estate == null || !pending.MixedHierarchyReturned || !pending.CrownBatch.CourtFinalizationReturned
                || !estate.GoldPrepared || estate.Shares.Any(s => !s.LandedSettled)
                || !TryVerifyCrownBatchWorld(pending, out reason)) return false;
            foreach (var share in estate.Shares)
            {
                var recipient = share.Recipient;
                if (recipient?.Leader?.IsAlive != true || share.Heir?.IsAlive != true || share.Heir.Clan != recipient)
                { reason = "mixed beneficiary changed before reward settlement"; return false; }
                if (share.GoldPayment == null)
                    share.GoldPayment = new CrownAccessionRecord { IncomingSourceHead = estate.GoldDonor,
                        GoldRecipient = recipient.Leader, EndowmentGold = !share.ReceivesGold || share.IsEscheat
                            || recipient.Leader == estate.GoldDonor ? 0 : estate.GoldPerShare };
                if (!share.GoldPayment.GoldDebited && share.GoldPayment.EndowmentDonor?.IsAlive != true)
                { reason = "mixed estate treasury donor died before debit"; return false; }
                if (!share.GoldPayment.GoldCredited && share.GoldPayment.GoldRecipient != recipient.Leader)
                { reason = "mixed estate treasury recipient changed before credit"; return false; }
                if (!CrownAccessionBehavior.DeliverAbdicationGold(share.GoldPayment))
                { reason = "mixed estate treasury delivery remains pending"; return false; }
            }
            pending.GoldPayments = estate.Shares.Select(s => s.GoldPayment).ToList();
            if (!pending.TryApplyClaimSettlement(() =>
            {
                titles.RegisterPartitionHouseClaimsFromSnapshot(estate.Source, estate.Deceased,
                    estate.Shares.Where(s => !s.IsEscheat && !s.BeneficiaryReconciled)
                        .Select(s => Tuple.Create(s.Recipient, s.Heir)).Distinct().ToList(),
                    estate.Shares.SelectMany(s => s.DeliveredFiefs).Distinct().Select(Settlement.Find)
                        .Where(s => s?.Town != null).Select(s => s.Town).ToList(),
                    estate.Shares.SelectMany(s => s.DeliveredTitles).Distinct().Select(titles.GetTitle).Where(t => t != null).ToList(),
                    pending.OriginalClaims);
                foreach (var share in estate.Shares) RestoreEstateReversionClaims(estate, share, titles);
            }, out reason)) return false;
            return TryVerifyCrownBatchWorld(pending, out reason);
        }

        private bool TryFinishMixedEstateBatch(PendingPartitionSuccessionRecord pending, out string reason)
        {
            var batch = pending.CrownBatch;
            var estate = pending.MixedEstate;
            if (!batch.Completed)
            {
                if (!TryPrepareCrownBatch(pending, out reason) || !TryVerifyCrownBatchObligations(pending, out reason)) return false;
                var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
                var summary = Campaign.Current.GetCampaignBehavior<SuccessionYearlySummaryBehavior>();
                for (int i = 0; i < estate.Shares.Count; i++)
                {
                    var share = estate.Shares[i];
                    var promotion = MixedEstateBatch.Promotion(pending, share);
                    if (!batch.TryAnnounce("mixed-share:" + i, () =>
                    {
                        if (promotion != null)
                        {
                            var package = new FeudalInheritancePackage { RootTitle = titles.GetTitle(promotion.CrownId),
                                PrimaryFief = ResolveTown(share.PrimaryFiefId) };
                            summary?.RecordPartitionCadetBranch(share.Recipient, estate.Source, estate.Realm, package.PrimaryFief);
                            summary?.RecordSovereignPartition(promotion.Successor, package.RootTitle);
                            ShowSovereignPartitionMessage(estate.Source, share.Recipient, share.Heir, package,
                                estate.Realm, promotion.Successor, titles);
                        }
                        else
                        {
                            if (share.CadetPlan?.Cadet != null)
                                summary?.RecordPartitionCadetBranch(share.Recipient, estate.Source, estate.Realm, ResolveTown(share.PrimaryFiefId));
                            ShowEstateInheritanceMessage(estate, share, titles);
                        }
                    }, out reason)) return false;
                }
                if (!batch.TryAnnounce("treasury", () => summary?.RecordPartitionGoldTransfer(
                    pending.GoldPayments.Sum(p => p.DeliveredGold)), out reason)
                    || !TryVerifyCrownBatchWorld(pending, out reason) || !TryVerifyCrownBatchObligations(pending, out reason)
                    || !MixedEstateBatch.TryComplete(pending, true, out reason)) return false;
            }
            PublishCrossClanPartition(estate);
            _pendingPartitions.Remove(pending);
            reason = null;
            return true;
        }
    }
}

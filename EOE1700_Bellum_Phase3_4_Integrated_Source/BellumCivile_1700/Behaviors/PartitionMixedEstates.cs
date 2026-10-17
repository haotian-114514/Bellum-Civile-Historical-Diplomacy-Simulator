using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        private bool CanFinishRetainedMixedAccession(CrossClanEstateRecord estate, Hero heir, CrownAccessionRecord accession)
        {
            if (estate.PolicyVersion != 1 || accession == null || accession.Completed || accession.IsAbdication
                || accession.ElectiveElection || accession.Realm != estate.Realm || accession.PreviousHouse != estate.Source
                || accession.Predecessor != estate.Deceased
                || accession.Heir != heir || heir?.Clan != estate.Source || heir.IsDead
                || estate.Realm.RulingClan != estate.Source
                || (RegencyBehavior.Instance?.GetLegalClanHead(estate.Source) ?? estate.Source.Leader) != heir
                || estate.Shares.Any(s => HasCommittedEstateShare(estate, s))) return false;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var primary = titles?.GetTitle(estate.PreferredPrimaryTitleId);
            if (primary == null || primary.DeJureHolderClanId != estate.Source.StringId
                || primary.DeFactoHolderClanId != estate.Source.StringId) return false;
            // The retained house already owns its Crown and land. Finish its political
            // accession before a separate Crown batch starts; do not deliver any share here.
            return estate.Shares.Any(s => s.Heir != heir && !s.IsEscheat
                && EstateInheritancePolicy.IsDomesticHouse(s.Heir?.Clan, estate.Realm)
                && s.Titles.Select(titles.GetTitle).Any(t => RequiresSovereignEstateExecutor(estate, titles, t)));
        }

        private bool TryHandleMixedEstate(CrossClanEstateRecord estate, FeudalTitleBehavior titles)
        {
            var pending = _pendingPartitions.FirstOrDefault(p => p?.MixedEstate == estate);
            if (pending != null && CanRefreshUnstartedMixedEstate(pending)
                && estate.Shares.Any(s => s.DomesticOnly && (s.Heir?.IsAlive != true
                    || !EstateInheritancePolicy.IsDomesticHouse(s.Heir.Clan, estate.Realm)
                    || s.Recipient != null && s.Heir.Clan != s.Recipient)))
            {
                // No native action has run. Reconcile beneficiaries on the original
                // allocation, then capture a fresh adapter; never redistribute assets.
                _pendingPartitions.Remove(pending);
                foreach (var share in estate.Shares) share.Recipient = null;
                pending = null;
            }
            // Older partial deliveries retain their original executor and receipts.
            if (pending == null && estate.Shares.Any(s => s.Completed || HasCommittedEstateShare(estate, s))) return false;
            if (pending == null && (estate.PolicyVersion == 0 || !estate.Shares.Any(s => !s.Completed && !s.IsEscheat
                && s.Titles.Select(titles.GetTitle).Any(t => RequiresSovereignEstateExecutor(estate, titles, t)
                    && !RetainsSovereignEstateInSource(estate, s, t)
                    && EstateInheritancePolicy.IsDomesticHouse(s.Heir?.Clan, estate.Realm))))) return false;
            string reason = null;
            try
            {
                if (pending == null && !TryCaptureMixedEstate(estate, titles, out pending, out reason))
                    return DeferMixedEstate(estate, reason);
                if (pending.CrownPromotions == null)
                {
                    var heirs = pending.EstateShares.Where(s => !s.Primary).ToDictionary(s => s.RootTitleId, s => s.Heir);
                    if (!TryCaptureCrownBatchPromotions(pending, heirs, out reason)) return DeferMixedEstate(estate, reason);
                }
                if (!TryFinishCrownBatch(pending, out reason)) return DeferMixedEstate(estate, reason);
            }
            catch (Exception ex) { return DeferMixedEstate(estate, ex.ToString()); }
            return true;
        }

        private static bool CanRefreshUnstartedMixedEstate(PendingPartitionSuccessionRecord pending) =>
            pending.CrownBatch?.HierarchyStarted != true && !pending.ClaimsStarted && !pending.MixedHierarchyStarted
            && pending.CrownPromotions?.All(p => !p.FounderCreationStarted && !p.RealmCreationStarted && !p.EstateDeliveryStarted) == true
            && pending.MixedEstate.Shares.All(s => !s.Completed && !s.PartitionFounderStarted
                && s.PartitionReceipts?.Any(r => r.Started) != true && !HasCommittedEstateShare(pending.MixedEstate, s));

        private static bool IsEligibleMixedFounder(CrossClanEstateRecord estate, Hero heir) =>
            IsValidNaturalHeirAtDeath(heir, estate.Source, estate.Deceased)
            && heir != estate.Source.Leader
            && CrownAccessionBehavior.Instance?.HasInheritanceAdvance(estate.Deceased, heir) != true
            && SuccessionLawHelper.IsEligibleUnderSuccessionLaws(heir, estate.Deceased,
                new SuccessionLawSet(estate.GenderLaw, estate.SuccessionLaw));

        private static bool IsEligibleBatchFounder(PendingPartitionSuccessionRecord pending, Hero heir, Clan source, Hero deceased) =>
            pending.MixedEstate == null ? IsStillEligibleSecondaryHeir(heir, source, deceased)
                : IsEligibleMixedFounder(pending.MixedEstate, heir);

        private static bool MixedHouseholdsReady(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = null;
            if (pending.MixedEstate == null || !CanRefreshUnstartedMixedEstate(pending)) return true;
            var estate = pending.MixedEstate;
            var heirs = new HashSet<Hero>(estate.Shares.Select(s => s.Heir));
            foreach (var heir in heirs.Where(h => h.Clan == estate.Source && h != estate.Source.Leader))
            foreach (var member in new[] { heir, heir.Spouse }.Concat(heir.Children)
                .Where(h => h != null && h.IsAlive && h.Clan == estate.Source && h != estate.Source.Leader
                    && (h == heir || !heirs.Contains(h))))
                if (member.IsPrisoner || member.IsTraveling || member.PartyBelongedTo?.Army != null
                    || member.PartyBelongedTo?.MapEvent != null || member.PartyBelongedTo?.SiegeEvent != null)
                { reason = "mixed estate household awaits freedom and completion of military duties"; return false; }
            return true;
        }

        private bool DeferMixedEstate(CrossClanEstateRecord estate, string reason)
        {
            reason = reason ?? "mixed estate participants are not ready";
            if (estate.Failure != reason)
                BellumCivileLogger.Log($"Mixed death estate deferred; deceased={estate.Deceased.StringId}; source={estate.Source.StringId}; reason={reason}.");
            estate.Failure = reason;
            estate.ReadyDate = CampaignTime.Now + CampaignTime.Days(1f);
            return true;
        }

        internal bool TryCaptureMixedEstate(CrossClanEstateRecord estate, FeudalTitleBehavior titles,
            out PendingPartitionSuccessionRecord pending, out string reason)
        {
            pending = null;
            reason = "mixed capture requires an untouched domestic death estate and a settled hereditary Crown";
            var source = estate?.Source;
            var realm = estate?.Realm;
            if (estate?.PolicyVersion != 1 || estate.Completed || source == null || titles == null
                || realm?.RulingClan != source || source.Kingdom != realm || source.Leader?.IsAlive != true
                || source.Leader == estate.Deceased || CrownAccessionBehavior.Instance?.IsPending(realm) == true
                || RegencyBehavior.Instance?.TryGetRegency(source, out _) == true
                || !CrownAccessionBehavior.IsHereditaryRealm(realm) || !BellumCivileOptions.EnablePartitionSuccession
                || estate.Shares.Any(s => s.Completed || HasCommittedEstateShare(estate, s))
                || _pendingPartitions.Any(p => p != null && (p.MixedEstate == estate || p.ParentClanId == source.StringId))) return false;
            if (!CanPrepareCrownPartitionDiplomacy(realm, null, out reason)) return false;
            var assignments = new Dictionary<string, CrossClanEstateShare>(StringComparer.Ordinal);
            foreach (var share in estate.Shares)
            {
                if (share.DomesticOnly && !PrepareDomesticEstateShare(estate, share)) return false;
                var crowns = share.Titles.Select(titles.GetTitle)
                    .Where(t => !share.IsEscheat && RequiresSovereignEstateExecutor(estate, titles, t)
                        && !RetainsSovereignEstateInSource(estate, share, t)).ToList();
                if (crowns.Count == 0) continue;
                if (crowns.Count != 1 || crowns[0].TitleId != share.RootTitleId
                    || !IsEligibleMixedFounder(estate, share.Heir))
                { reason = "a separating Crown awaits an eligible adult founder in the estate house"; return false; }
                assignments.Add(crowns[0].TitleId, share);
            }
            if (assignments.Count == 0) { reason = "estate no longer requires mixed Crown promotion"; return false; }
            if (estate.Shares.SelectMany(s => s.Titles).Any(id => titles.GetTitle(id)?.DeJureHolderClanId != source.StringId)
                || estate.Shares.SelectMany(s => s.Fiefs).Any(id => TaleWorlds.CampaignSystem.Settlements.Settlement.Find(id)?.OwnerClan != source))
            { reason = "mixed estate ownership changed before coordinated delivery"; return false; }
            var allTitles = titles.GetAllTitles().ToList();
            var recipients = assignments.ToDictionary(p => p.Key, p => BuildUniqueClanIdFromSeed(source, p.Key, p.Value.Heir));
            var holdings = realm.Clans.Where(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService
                && !c.IsBanditFaction && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(c))
                .ToDictionary(c => c.StringId, c => c.Settlements.Select(s => s.StringId).ToArray());
            if (!CrownPartitionBatchPlan.TryCreate(source.StringId, estate.PreferredPrimaryTitleId,
                recipients, holdings, allTitles, out var plan, out reason)) return false;
            // Ordinary recipients must remain in the retained realm. Their old holdings and
            // the new award must not introduce a second allegiance into a Crown package.
            foreach (var share in estate.Shares.Except(assignments.Values))
            {
                Clan house = share.IsEscheat ? source : share.Heir?.Clan;
                if (house == null || !EstateInheritancePolicy.IsDomesticHouse(house, realm)
                    || !plan.DestinationCrownByHouse.TryGetValue(house.StringId, out var destination)
                    || destination != estate.PreferredPrimaryTitleId)
                { reason = "ordinary beneficiary would leave the retained realm with a separating Crown"; return false; }
                if (house == source && share.Heir != source.Leader && !share.IsEscheat
                    && !assignments.Values.Any(s => s.Heir == share.Heir)
                    && (!IsEligibleMixedFounder(estate, share.Heir) || share.Fiefs.Count == 0))
                { reason = "ordinary cadet beneficiary awaits adulthood and an inherited residence"; return false; }
            }
            var candidate = new PendingPartitionSuccessionRecord(estate.Deceased.StringId, source.StringId, realm.StringId,
                JoinIds(estate.Shares.SelectMany(s => s.Fiefs)), JoinIds(assignments.Values.Select(s => s.Heir.StringId)),
                CampaignTime.Now, true, JoinIds(estate.Shares.SelectMany(s => s.Titles)), estate.PreferredPrimaryTitleId)
            {
                MixedEstate = estate, CrownRoutingRequested = true,
                OriginalClaims = titles.GetActiveClaimsByClan(source).OrderBy(c => c.ClaimId, StringComparer.Ordinal)
                    .Select(c => c.CopyForEstate()).ToList(),
                EstateShares = new List<CrossClanEstateShare> { new CrossClanEstateShare {
                    Heir = source.Leader, Recipient = source, Primary = true, Titles = new List<string> { estate.PreferredPrimaryTitleId } } }
            };
            candidate.EstateShares.AddRange(assignments.Select(p => new CrossClanEstateShare {
                Heir = p.Value.Heir, RootTitleId = p.Key, PrimaryFiefId = p.Value.PrimaryFiefId,
                Titles = p.Value.Titles.ToList(), Fiefs = p.Value.Fiefs.ToList() }));
            _pendingPartitions.Add(candidate);
            bool captured = false;
            try
            {
                captured = TryRecordCrownPartitionBatch(candidate, plan, out reason)
                    && TryCaptureCrownBatchPromotions(candidate, assignments.ToDictionary(p => p.Key, p => p.Value.Heir), out reason)
                    && MixedEstateBatch.TryValidate(candidate, out reason);
            }
            finally { if (!captured) _pendingPartitions.Remove(candidate); }
            if (!captured) return false;
            foreach (var share in MixedEstateBatch.OrdinaryShares(candidate))
                if (share.IsEscheat || share.Heir.Clan != source || share.Heir == source.Leader)
                    share.Recipient = share.IsEscheat ? source : share.Heir.Clan;
            if (!estate.GoldPrepared)
            {
                estate.GoldDonor = source.Leader;
                estate.GoldPerShare = Math.Max(0, source.Leader.Gold) / Math.Max(1, estate.Shares.Count(s => s.ReceivesGold));
                estate.GoldPrepared = true;
            }
            pending = candidate;
            BellumCivileLogger.Log($"Mixed death estate coordinated; deceased={estate.Deceased.StringId}; source={source.StringId}; crowns={assignments.Count}; shares={estate.Shares.Count}.");
            reason = null;
            return true;
        }
    }
}

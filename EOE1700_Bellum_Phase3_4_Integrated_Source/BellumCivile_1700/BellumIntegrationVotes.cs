using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public static partial class BellumIntegration
    {
        public static IReadOnlyList<PendingVoteSnapshot> GetPendingVotes(Kingdom realm)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            return BellumIntegrationBehavior.Current.GetPendingVotes(realm);
        }

        public static event Action<CouncilAppointmentResultSnapshot> CouncilAppointmentDecided
        {
            add { BellumIntegrationBehavior.RequireCampaignThread(); BellumIntegrationBehavior.Current.CouncilAppointmentDecided += value; }
            remove { BellumIntegrationBehavior.RequireCampaignThread(); BellumIntegrationBehavior.Current.CouncilAppointmentDecided -= value; }
        }

        public static IReadOnlyList<CouncilAppointmentResultSnapshot> GetRecentCouncilAppointments()
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            return BellumIntegrationBehavior.Current.GetRecentCouncilAppointments();
        }
    }
}

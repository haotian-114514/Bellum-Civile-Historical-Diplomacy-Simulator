using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.EOE1700
{
    /// <summary>
    /// Read-only, save-persisted political state observer for the 1700 scenario.
    /// It never invokes game actions or writes AI Influence-owned files.
    /// Snapshot changes are evidence of outcomes, not their causes.
    /// </summary>
    public sealed class EoeHistoricalWorldObserverBehavior : CampaignBehaviorBase
    {
        private static readonly HashSet<string> HistoricalIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "aserai", "austria", "britain", "cossack", "crimea", "denmark", "dutch",
            "genoa", "germany", "ottoman", "persia", "poland", "pope", "portugal",
            "prussia", "russia", "spain", "sweden", "swiss", "tuscany", "venice", "vlandia"
        };

        // Both dictionaries contain only primitive strings, using Bellum's already-established
        // Dictionary<string,string> save format. Never store runtime game objects in them.
        private Dictionary<string, string> _lastKingdomStates = new Dictionary<string, string>();
        private Dictionary<string, string> _lastSettlementOwners = new Dictionary<string, string>();
        private Dictionary<string, string> _lastWarStates = new Dictionary<string, string>();
        private Dictionary<string, string> _latestChanges = new Dictionary<string, string>();
        private int _lastSurveyDay = -1;
        // Append-only bounded chronicle; primitive save types avoid custom save schema changes.
        // Each entry stores: sequence|day|event-kind|subject|old-value|new-value.
        private Dictionary<string, string> _chronicleEntries = new Dictionary<string, string>();
        private int _chronicleSequence;
        private const int MaxChronicleEntries = 1000;
        // Schema v2 adds unambiguous typed evidence alongside the preserved v1 entries.
        // Do not silently reinterpret survey dates as the exact event time.
        private Dictionary<string, string> _chronicleContextV2 = new Dictionary<string, string>();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnOwnerChanged);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnOwnerChanged);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("EOE1700_Observer_KingdomStates_v1", ref _lastKingdomStates);
            dataStore.SyncData("EOE1700_Observer_SettlementOwners_v1", ref _lastSettlementOwners);
            dataStore.SyncData("EOE1700_Observer_WarStates_v1", ref _lastWarStates);
            dataStore.SyncData("EOE1700_Observer_LatestChanges_v1", ref _latestChanges);
            dataStore.SyncData("EOE1700_Observer_LastSurveyDay_v1", ref _lastSurveyDay);
            dataStore.SyncData("EOE1700_Chronicle_Entries_v1", ref _chronicleEntries);
            dataStore.SyncData("EOE1700_Chronicle_NextSequence_v1", ref _chronicleSequence);
            dataStore.SyncData("EOE1700_Chronicle_Context_v2", ref _chronicleContextV2);
            Initialize();
        }

        private void Initialize()
        {
            if (_lastKingdomStates == null) _lastKingdomStates = new Dictionary<string, string>();
            if (_lastSettlementOwners == null) _lastSettlementOwners = new Dictionary<string, string>();
            if (_lastWarStates == null) _lastWarStates = new Dictionary<string, string>();
            if (_latestChanges == null) _latestChanges = new Dictionary<string, string>();
            if (_chronicleEntries == null) _chronicleEntries = new Dictionary<string, string>();
            if (_chronicleContextV2 == null) _chronicleContextV2 = new Dictionary<string, string>();
            // Recover the monotonically increasing sequence if a partial/older save lacks it.
            if (_chronicleEntries.Count > 0 && _chronicleSequence <= 0)
            {
                int max = 0;
                foreach (string k in _chronicleEntries.Keys)
                    if (int.TryParse(k, out int parsed) && parsed > max) max = parsed;
                _chronicleSequence = max;
            }
        }


        // Direct notifications improve timeliness. The daily survey remains the authoritative
        // reconciliation fallback when another mod bypasses the normal campaign events.
        private void OnWarDeclared(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            RecordDirectWar(first as Kingdom, second as Kingdom, "war", "declare_war_detail=" + detail);
        }

        private void OnPeaceMade(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            RecordDirectWar(first as Kingdom, second as Kingdom, "peace", "make_peace_detail=" + detail);
        }

        private void RecordDirectWar(Kingdom first, Kingdom second, string state, string detail)
        {
            if (first == null || second == null || first == second ||
                !HistoricalIds.Contains(first.StringId) || !HistoricalIds.Contains(second.StringId)) return;
            Initialize();
            string[] ids = { first.StringId, second.StringId };
            Array.Sort(ids, StringComparer.Ordinal);
            Record(_lastWarStates, "war:" + ids[0] + ":" + ids[1], state,
                (int)CampaignTime.Now.ToDays, "direct_campaign_event", detail);
        }

        private void OnOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner,
            Hero oldOwner, Hero capturer,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;
            Initialize();
            // The event carries the new owner explicitly; do not assume a cause from a changed owner.
            string owner = newOwner?.Clan?.Kingdom?.StringId ?? settlement.OwnerClan?.Kingdom?.StringId ?? "";
            Record(_lastSettlementOwners, "settlement:" + settlement.StringId, owner,
                (int)CampaignTime.Now.ToDays, "direct_campaign_event", "transfer_detail=" + detail);
        }

        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null || victim.Clan?.Kingdom == null ||
                !HistoricalIds.Contains(victim.Clan.Kingdom.StringId)) return;
            Initialize();
            // This is a confirmed death, not a confirmed assassination or deposition.
            AppendChronicle((int)CampaignTime.Now.ToDays, "hero:" + victim.StringId,
                "alive", "dead", "direct_campaign_event", "death_detail=" + detail,
                "HERO_DEATH_CONFIRMED");
            // Ruler replacement will be captured in the next snapshot, even if accession
            // takes place after this event in another campaign behavior.
        }


        // Direct notifications improve timeliness. The daily survey remains the authoritative
        // reconciliation fallback when another mod bypasses the normal campaign events.
        private void OnWarDeclared(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            RecordDirectWar(first as Kingdom, second as Kingdom, "war", "declare_war_detail=" + detail);
        }

        private void OnPeaceMade(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            RecordDirectWar(first as Kingdom, second as Kingdom, "peace", "make_peace_detail=" + detail);
        }

        private void RecordDirectWar(Kingdom first, Kingdom second, string state, string detail)
        {
            if (first == null || second == null || first == second ||
                !HistoricalIds.Contains(first.StringId) || !HistoricalIds.Contains(second.StringId)) return;
            Initialize();
            string[] ids = { first.StringId, second.StringId };
            Array.Sort(ids, StringComparer.Ordinal);
            Record(_lastWarStates, "war:" + ids[0] + ":" + ids[1], state,
                (int)CampaignTime.Now.ToDays, "direct_campaign_event", detail);
        }

        private void OnOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner,
            Hero oldOwner, Hero capturer,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;
            Initialize();
            // The event carries the new owner explicitly; do not assume a cause from a changed owner.
            string owner = newOwner?.Clan?.Kingdom?.StringId ?? settlement.OwnerClan?.Kingdom?.StringId ?? "";
            Record(_lastSettlementOwners, "settlement:" + settlement.StringId, owner,
                (int)CampaignTime.Now.ToDays, "direct_campaign_event", "transfer_detail=" + detail);
        }

        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null || victim.Clan?.Kingdom == null ||
                !HistoricalIds.Contains(victim.Clan.Kingdom.StringId)) return;
            Initialize();
            // This is a confirmed death, not a confirmed assassination or deposition.
            AppendChronicle((int)CampaignTime.Now.ToDays, "hero:" + victim.StringId,
                "alive", "dead", "direct_campaign_event", "death_detail=" + detail,
                "HERO_DEATH_CONFIRMED");
            // Ruler replacement will be captured in the next snapshot, even if accession
            // takes place after this event in another campaign behavior.
        }

        private void OnDailyTick()
        {
            if (Campaign.Current == null) return;
            Initialize();
            int day = (int)CampaignTime.Now.ToDays;
            if (day == _lastSurveyDay) return;
            try
            {
                Survey(day);
                _lastSurveyDay = day;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("[EOE1700 Observer] survey error: " + ex);
            }
        }

        private void Survey(int day)
        {
            List<Kingdom> kingdoms = Kingdom.All.Where(k => k != null && HistoricalIds.Contains(k.StringId))
                .OrderBy(k => k.StringId).ToList();
            foreach (Kingdom k in kingdoms)
            {
                string ruler = k.Leader == null ? "" : k.Leader.StringId;
                string clan = k.RulingClan == null ? "" : k.RulingClan.StringId;
                // Do not use names as identity: translated/naming-patch labels can change.
                string state = ruler + "|" + clan;
                Record(_lastKingdomStates, "kingdom:" + k.StringId, state, day);
            }

            foreach (Settlement s in Settlement.All.Where(s => s != null && !string.IsNullOrEmpty(s.StringId)))
            {
                if (!s.IsTown && !s.IsCastle) continue;
                string owner = s.OwnerClan?.Kingdom?.StringId ?? "";
                Record(_lastSettlementOwners, "settlement:" + s.StringId, owner, day);
            }

            for (int i = 0; i < kingdoms.Count; i++)
                for (int j = i + 1; j < kingdoms.Count; j++)
                {
                    Kingdom a = kingdoms[i];
                    Kingdom b = kingdoms[j];
                    string key = "war:" + a.StringId + ":" + b.StringId;
                    string atWar = FactionManager.IsAtWarAgainstFaction(a, b) ? "war" : "peace";
                    Record(_lastWarStates, key, atWar, day);
                }
        }

        private void Record(Dictionary<string, string> states, string key, string newValue, int day, string evidence = "observation", string actionDetail = "")
        {
            if (states.TryGetValue(key, out string oldValue) && oldValue != newValue)
            {
                string message = "day=" + day + " | " + key + " | before=" + oldValue + " | after=" + newValue;
                _latestChanges[key] = message;
                AppendChronicle(day, key, oldValue, newValue, evidence, actionDetail);
                BellumCivileLogger.Log("[EOE1700 Observer] " + message);
            }
            // First sighting establishes a baseline; it is not a historical event.
            states[key] = newValue;
        }

        private void AppendChronicle(int day, string subject, string before, string after, string evidence = "observation", string actionDetail = "", string forcedKind = null)
        {
            string kind = forcedKind ?? (subject.StartsWith("war:", StringComparison.Ordinal) ?
                (after == "war" ? "WAR_STARTED_OBSERVED" : "WAR_ENDED_OBSERVED") :
                subject.StartsWith("settlement:", StringComparison.Ordinal) ? "SETTLEMENT_OWNER_CHANGED_OBSERVED" :
                "KINGDOM_LEADERSHIP_CHANGED_OBSERVED");
            _chronicleSequence++;
            string sequence = _chronicleSequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
            // Percent-escape potentially uncontrolled ids so records remain unambiguous.
            string entry = sequence + "|" + day + "|" + kind + "|" +
                Uri.EscapeDataString(subject) + "|" + Uri.EscapeDataString(before ?? "") +
                "|" + Uri.EscapeDataString(after ?? "");
            _chronicleEntries[sequence] = entry;
            // All original observations remain valid with no invented political cause.
            // v2 context is a pipe-delimited, escaped, self-describing record keyed by v1 sequence.
            _chronicleContextV2[sequence] = BuildContextV2(sequence, day, kind, subject, before, after, evidence, actionDetail);
            if (_chronicleEntries.Count > MaxChronicleEntries)
            {
                // Bounded long-running campaign memory; prune oldest recorded entry.
                string oldest = _chronicleEntries.Keys.OrderBy(x => x, StringComparer.Ordinal).First();
                _chronicleEntries.Remove(oldest);
                _chronicleContextV2.Remove(oldest);
            }
            BellumCivileLogger.Log("[EOE1700 Chronicle] " + entry);
        }

        private static string BuildContextV2(int sequence, int day, string kind, string subject, string before, string after, string evidence, string actionDetail)
        {
            // Event identity is the canonical v1 sequence; context can be regenerated without
            // confusing an observed transition with an action or its motivating cause.
            string eventDomain = subject.StartsWith("war:", StringComparison.Ordinal) ? "war" :
                subject.StartsWith("settlement:", StringComparison.Ordinal) ? "settlement" :
                subject.StartsWith("hero:", StringComparison.Ordinal) ? "hero" : "leadership";
            string detail = eventDomain == "hero" ? "death_confirmed" : "state_changed";
            if (eventDomain == "leadership")
            {
                string[] b = (before ?? "").Split('|');
                string[] a = (after ?? "").Split('|');
                if (b.Length == 2 && a.Length == 2)
                {
                    bool rulerChanged = b[0] != a[0];
                    bool dynastyChanged = b[1] != a[1];
                    detail = rulerChanged && dynastyChanged ? "ruler_and_ruling_clan_changed" :
                        rulerChanged ? "ruler_changed" : dynastyChanged ? "ruling_clan_changed" : "state_changed";
                }
            }
            return "v2|" + sequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) +
                "|" + day + "|" + Escape(eventDomain) + "|" + Escape(kind) + "|" +
                Escape(subject) + "|" + Escape(detail) + "|" + Escape(before) +
                "|" + Escape(after) + "|" + Escape(evidence) + "|cause_unknown|" +
                Escape(evidence == "direct_campaign_event" ? "event_callback_day" : "survey_day_not_exact_time") +
                "|" + Escape(actionDetail);
        }

        private static string Escape(string value) => Uri.EscapeDataString(value ?? "");

        /// <summary>
        /// Returns supplementary evidence for v1 chronicle entries. The keys are the exact
        /// v1 sequence, so older saves without context remain readable and are not rewritten.
        /// </summary>
        public IEnumerable<string> GetChronicleContextV2() =>
            (_chronicleContextV2 ?? new Dictionary<string, string>())
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Value).ToArray();

        /// <summary>
        /// Ordered, verified OBSERVATIONS. They assert changed world state,
        /// never an unobserved political motive or inferred treaty clause.
        /// </summary>
        public IEnumerable<string> GetChronicleEntries() =>
            (_chronicleEntries ?? new Dictionary<string, string>())
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Value).ToArray();

        /// <summary>Diagnostic data for future Chronicle integration; no external writes.</summary>
        public IEnumerable<string> GetLastObservedChanges() =>
            (_latestChanges ?? new Dictionary<string, string>()).Values.OrderBy(x => x).ToArray();
    }
}

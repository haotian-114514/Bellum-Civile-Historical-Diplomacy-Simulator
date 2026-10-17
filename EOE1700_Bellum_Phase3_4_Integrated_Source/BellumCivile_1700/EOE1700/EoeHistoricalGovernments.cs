// Copyright 2026, EOE1700 compatibility layer contributors.
// A policy model for integration into the user's properly licensed Bellum Civile fork.
// No Harmony patches or campaign behaviors are registered by this file.
using System;
using System.Collections.Generic;

namespace BellumCivile.EOE1700
{
    internal enum EoeSuccessionKind { Hereditary, Dynastic, Elective, Republican }
    internal enum EoeInternalConflictKind { RoutineCourtDispute, ClaimantCivilWar, ForeignSupportedRevolt, SystemicCrisis }

    internal sealed class EoeGovernmentPolicy
    {
        public readonly string KingdomId;
        public readonly string StartingName;
        public readonly string GovernmentType;
        public readonly string Institution;
        public readonly EoeSuccessionKind Succession;
        public readonly bool AllowSovereignPartition;
        public readonly bool AllowRoutineCourtRebellion;

        internal EoeGovernmentPolicy(string id, string name, string government, string institution, EoeSuccessionKind succession, bool sovereignPartition, bool routineRebellion)
        {
            KingdomId = id;
            StartingName = name;
            GovernmentType = government;
            Institution = institution;
            Succession = succession;
            AllowSovereignPartition = sovereignPartition;
            AllowRoutineCourtRebellion = routineRebellion;
        }

        // This is a policy gate, not the final civil-war decision. The Bellum executor
        // must still verify claimant legitimacy, strength, treaties and all game invariants.
        internal bool AllowsInternalConflict(EoeInternalConflictKind kind, bool verifiedSevereCrisis)
        {
            if (kind == EoeInternalConflictKind.RoutineCourtDispute)
                return AllowRoutineCourtRebellion;
            return verifiedSevereCrisis;
        }
    }

    internal static class EoeHistoricalGovernments
    {
        // The Bannerlord IDs remain unchanged. These are initial EoE historical profiles,
        // not executable succession laws or live political offices.
        private static readonly Dictionary<string, EoeGovernmentPolicy> Profiles =
            new Dictionary<string, EoeGovernmentPolicy>(StringComparer.OrdinalIgnoreCase)
            {
                { "aserai", new EoeGovernmentPolicy("aserai", "The Alawi Dynasty", "dynastic_monarchy", "royal_court", EoeSuccessionKind.Dynastic, false, false) },
                { "austria", new EoeGovernmentPolicy("austria", "The Archduchy of Austria", "composite_habsburg_monarchy", "estates_and_court", EoeSuccessionKind.Hereditary, false, false) },
                { "britain", new EoeGovernmentPolicy("britain", "Kingdom of Great Britain", "parliamentary_monarchy", "parliament_and_court", EoeSuccessionKind.Hereditary, false, false) },
                { "cossack", new EoeGovernmentPolicy("cossack", "Viysko Zaporozke", "cossack_hetmanate", "officer_council", EoeSuccessionKind.Elective, false, false) },
                { "crimea", new EoeGovernmentPolicy("crimea", "The Crimean Khanate", "dynastic_khanate", "noble_clans", EoeSuccessionKind.Dynastic, false, false) },
                { "denmark", new EoeGovernmentPolicy("denmark", "Kingdom of Denmark-Norway", "centralized_monarchy", "royal_council", EoeSuccessionKind.Hereditary, false, false) },
                { "dutch", new EoeGovernmentPolicy("dutch", "The Dutch Republic", "federal_republic", "provincial_states", EoeSuccessionKind.Republican, false, false) },
                { "genoa", new EoeGovernmentPolicy("genoa", "The Republic of Genoa", "oligarchic_republic", "patrician_council", EoeSuccessionKind.Republican, false, false) },
                { "germany", new EoeGovernmentPolicy("germany", "Holy Roman Empire", "elective_empire", "imperial_estates", EoeSuccessionKind.Elective, false, false) },
                { "ottoman", new EoeGovernmentPolicy("ottoman", "The Ottoman Empire", "dynastic_empire", "imperial_divan", EoeSuccessionKind.Dynastic, false, false) },
                { "persia", new EoeGovernmentPolicy("persia", "The Safavid Empire", "dynastic_empire", "royal_court", EoeSuccessionKind.Dynastic, false, false) },
                { "poland", new EoeGovernmentPolicy("poland", "Kingdom of Poland and the Grand Duchy of Lithuania", "elective_commonwealth", "sejm_and_magnates", EoeSuccessionKind.Elective, false, false) },
                { "pope", new EoeGovernmentPolicy("pope", "The Papal States", "ecclesiastical_elective", "college_of_cardinals", EoeSuccessionKind.Elective, false, false) },
                { "portugal", new EoeGovernmentPolicy("portugal", "Kingdom of Portugal", "monarchy", "royal_council", EoeSuccessionKind.Hereditary, false, false) },
                { "prussia", new EoeGovernmentPolicy("prussia", "The Kingdom of Prussia", "electoral_monarchy", "royal_court_and_estates", EoeSuccessionKind.Dynastic, false, false) },
                { "russia", new EoeGovernmentPolicy("russia", "Tsardom of Muscovy", "tsardom", "boyar_council", EoeSuccessionKind.Dynastic, false, false) },
                { "spain", new EoeGovernmentPolicy("spain", "Kingdom of Spain", "composite_monarchy", "royal_council_and_estates", EoeSuccessionKind.Dynastic, false, false) },
                { "sweden", new EoeGovernmentPolicy("sweden", "Kingdom of Sweden", "monarchy", "royal_council_and_estates", EoeSuccessionKind.Hereditary, false, false) },
                { "swiss", new EoeGovernmentPolicy("swiss", "The Swiss Confederation", "confederation", "cantonal_councils", EoeSuccessionKind.Republican, false, false) },
                { "tuscany", new EoeGovernmentPolicy("tuscany", "Grand Duchy of Tuscany", "grand_duchy", "ducal_court", EoeSuccessionKind.Hereditary, false, false) },
                { "venice", new EoeGovernmentPolicy("venice", "Republic of Venice", "oligarchic_republic", "patrician_council", EoeSuccessionKind.Republican, false, false) },
                { "vlandia", new EoeGovernmentPolicy("vlandia", "Kingdom of France", "centralized_monarchy", "royal_council", EoeSuccessionKind.Hereditary, false, false) },
            };

        internal static bool TryGet(string bannerlordKingdomId, out EoeGovernmentPolicy profile)
        {
            return Profiles.TryGetValue(bannerlordKingdomId ?? string.Empty, out profile);
        }

        internal static bool BlocksRoutineCourtRebellion(string bannerlordKingdomId)
        {
            EoeGovernmentPolicy p;
            return TryGet(bannerlordKingdomId, out p) && !p.AllowRoutineCourtRebellion;
        }

        // Conservative fallback: do not silently impose a new constitution on unknown mods.
        internal static bool CanOverrideSovereignPartition(string bannerlordKingdomId)
        {
            EoeGovernmentPolicy p;
            return TryGet(bannerlordKingdomId, out p) && !p.AllowSovereignPartition;
        }
    }
}

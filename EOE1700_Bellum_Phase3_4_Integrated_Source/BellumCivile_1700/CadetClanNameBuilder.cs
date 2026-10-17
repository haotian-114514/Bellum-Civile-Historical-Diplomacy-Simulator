using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.Localization;

namespace BellumCivile
{
    // Naming inputs retain their source: territorial roots get cultural styling;
    // existing surnames do not. Only recorded components are treated as compounds.
    internal static class CadetClanNameBuilder
    {
        private static readonly string[] PrefixFormats = {
            "{=BC_CadetName_Vlandia}dey {ROOT_NAME}",
            "{=BC_CadetName_Battania}fen {ROOT_NAME}",
            "{=BC_CadetName_Aserai}Banu {ROOT_NAME}" };
        private static readonly string[] NativePrefixes = { "dey", "fen", "Banu" };
        private const string Marker = "BCNAMEROOTMARKER";

        internal static CadetNameRecord Territorial(string culture, string seed, IEnumerable<TextObject> roots,
            CadetNameRecord family, HashSet<string> occupied)
        {
            CadetNameRecord first = null;
            foreach (var name in roots ?? Enumerable.Empty<TextObject>())
            {
                var root = CleanSettlementName(name);
                if (string.IsNullOrWhiteSpace(root?.ToString())) continue;
                TextObject styled = Style(culture, seed, root);
                // A suffix is part of the surname, while a leading dey/fen/Banu is omitted as a second family.
                var candidate = Create(styled, IsPrefixCulture(culture) ? root : styled, null);
                if (first == null) first = candidate;
                if (!occupied.Contains(Normalize(candidate.Name.ToString()))) return candidate;
            }
            return MakeUnique(first ?? family ?? Family(null, null), occupied);
        }

        internal static CadetNameRecord Family(TextObject currentName, CadetNameRecord known)
        {
            if (known?.PrimaryName != null && known.PrimaryRoot != null && known.Name != null
                && SameName(known.Name.ToString(), currentName?.ToString()))
                return Create(known.PrimaryName, known.PrimaryRoot, OtherRoots(known));

            string name = Clean(currentName?.ToString());
            if (name.Length == 0)
            {
                var generic = new TextObject("{=BC_CadetName_Generic}Cadet Branch");
                return Create(generic, generic, null);
            }
            var prefixes = GetPrefixes();
            int style = FindPrefix(name, prefixes, out _);
            if (style >= 0)
            {
                string root = StripPrefixes(name, prefixes);
                var rootText = Literal(root);
                return Create(Format(PrefixFormats[style], "ROOT_NAME", rootText), rootText, null);
            }
            TextObject intact = name == currentName.ToString() ? currentName : Literal(name);
            return Create(intact, intact, null);
        }

        internal static CadetNameRecord Dynastic(CadetNameRecord dynasty, CadetNameRecord marital)
        {
            if (dynasty == null) return marital == null ? Family(null, null) : Create(marital.PrimaryName, marital.PrimaryRoot, OtherRoots(marital));
            var incoming = marital == null ? Enumerable.Empty<TextObject>()
                : new[] { marital.PrimaryRoot }.Concat(OtherRoots(marital));
            return Create(dynasty.PrimaryName, dynasty.PrimaryRoot, OtherRoots(dynasty).Concat(incoming));
        }

        internal static CadetNameRecord MakeUnique(CadetNameRecord basis, HashSet<string> occupied)
        {
            var result = Create(basis.PrimaryName, basis.PrimaryRoot, OtherRoots(basis));
            TextObject baseName = result.Name;
            int number = Math.Max(1, basis.Number);
            do
            {
                result.Number = number;
                result.Name = number == 1 ? baseName : Format("{=BC_CadetName_Numbered}{BASE_NAME} {NUMBER}",
                    "BASE_NAME", baseName, "NUMBER", Literal(ToRoman(number)));
                number++;
            } while (occupied.Contains(Normalize(result.Name.ToString())));
            return result;
        }

        private static IEnumerable<TextObject> OtherRoots(CadetNameRecord name) => new[] { name.SecondaryRoot }
            .Concat(name.AdditionalRoots ?? Enumerable.Empty<TextObject>());

        private static CadetNameRecord Create(TextObject primary, TextObject root, IEnumerable<TextObject> otherRoots)
        {
            var result = new CadetNameRecord { PrimaryName = primary.CopyTextObject(), PrimaryRoot = root.CopyTextObject() };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Normalize(root.ToString()) };
            result.Name = result.PrimaryName.CopyTextObject();
            foreach (var candidate in otherRoots ?? Enumerable.Empty<TextObject>())
            {
                string key = Normalize(candidate?.ToString());
                if (key.Length == 0 || !seen.Add(key)) continue;
                TextObject component = candidate.CopyTextObject();
                if (result.SecondaryRoot == null) result.SecondaryRoot = component;
                else
                {
                    if (result.AdditionalRoots == null) result.AdditionalRoots = new List<TextObject>();
                    result.AdditionalRoots.Add(component);
                }
                var combined = new TextObject("{=BC_DynasticHeiress_CadetName}{DYNASTY_NAME}-{BRANCH_NAME}");
                combined.SetTextVariable("DYNASTY_NAME", result.Name);
                combined.SetTextVariable("BRANCH_NAME", component);
                result.Name = combined;
            }
            return result;
        }

        private static TextObject Style(string culture, string seed, TextObject root)
        {
            switch (culture?.ToLowerInvariant())
            {
                case "vlandia": return Format(PrefixFormats[0], "ROOT_NAME", root);
                case "battania": return Format(PrefixFormats[1], "ROOT_NAME", root);
                case "aserai": return Format(PrefixFormats[2], "ROOT_NAME", root);
                case "sturgia": return Format("{=BC_CadetName_Sturgia}{ROOT_NAME}ing", "ROOT_NAME", root);
                case "nord": return Format("{=BC_CadetName_Nord}{ROOT_NAME}ing", "ROOT_NAME", root);
                case "khuzait": return Format("{=BC_CadetName_Khuzait}{ROOT_NAME}it", "ROOT_NAME", root);
                case "empire":
                    uint hash = 17;
                    unchecked { foreach (char ch in seed ?? string.Empty) hash = hash * 31 + ch; }
                    // Retain the old deterministic suffix selection without Math.Abs(int.MinValue).
                    long signed = (int)hash;
                    switch (Math.Abs(signed) % 3)
                    {
                        case 0: return Format("{=BC_CadetName_EmpireEs}{ROOT_NAME}es", "ROOT_NAME", root);
                        case 1: return Format("{=BC_CadetName_EmpireOs}{ROOT_NAME}os", "ROOT_NAME", root);
                        default: return Format("{=BC_CadetName_EmpireIs}{ROOT_NAME}is", "ROOT_NAME", root);
                    }
                default: return Format("{=BC_CadetName_Default}{ROOT_NAME}", "ROOT_NAME", root);
            }
        }

        private static bool IsPrefixCulture(string culture) => string.Equals(culture, "vlandia", StringComparison.OrdinalIgnoreCase)
            || string.Equals(culture, "battania", StringComparison.OrdinalIgnoreCase)
            || string.Equals(culture, "aserai", StringComparison.OrdinalIgnoreCase);

        internal static TextObject CleanSettlementName(TextObject name)
        {
            string value = Clean(name?.ToString());
            if (value.Length == 0) return null;
            string localized = Format("{=BC_CadetName_CastlePattern}{ROOT_NAME} Castle", "ROOT_NAME", Literal(Marker)).ToString();
            foreach (string pattern in new[] { localized, Marker + " Castle" }.Distinct())
            {
                int index = pattern.IndexOf(Marker, StringComparison.Ordinal);
                if (index < 0 || pattern.IndexOf(Marker, index + Marker.Length, StringComparison.Ordinal) >= 0) continue;
                string before = pattern.Substring(0, index), after = pattern.Substring(index + Marker.Length);
                if (before.Length + after.Length == 0 || value.Length <= before.Length + after.Length) continue;
                if (value.StartsWith(before, StringComparison.OrdinalIgnoreCase) && value.EndsWith(after, StringComparison.OrdinalIgnoreCase))
                    return Literal(value.Substring(before.Length, value.Length - before.Length - after.Length).Trim());
            }
            return value == name.ToString() ? name.CopyTextObject() : Literal(value);
        }

        private static List<Tuple<string, int>> GetPrefixes()
        {
            var result = new List<Tuple<string, int>>();
            for (int i = 0; i < PrefixFormats.Length; i++)
            {
                result.Add(Tuple.Create(NativePrefixes[i], i));
                string rendered = Format(PrefixFormats[i], "ROOT_NAME", Literal(Marker)).ToString();
                if (rendered.EndsWith(Marker, StringComparison.Ordinal))
                {
                    string prefix = rendered.Substring(0, rendered.Length - Marker.Length).Trim();
                    if (prefix.Length > 0) result.Add(Tuple.Create(prefix, i));
                }
            }
            return result.OrderByDescending(p => p.Item1.Length).ToList();
        }

        private static int FindPrefix(string name, List<Tuple<string, int>> prefixes, out string prefix)
        {
            foreach (var entry in prefixes)
                // A localized prefix that already ends in punctuation (such as a middle dot) needs no space after it.
                if (name.Length > entry.Item1.Length && name.StartsWith(entry.Item1, StringComparison.OrdinalIgnoreCase)
                    && (char.IsWhiteSpace(name[entry.Item1.Length]) || !char.IsLetterOrDigit(entry.Item1[entry.Item1.Length - 1])))
                { prefix = entry.Item1; return entry.Item2; }
            prefix = null;
            return -1;
        }

        private static string StripPrefixes(string name, List<Tuple<string, int>> prefixes)
        {
            while (FindPrefix(name, prefixes, out string prefix) >= 0)
                name = name.Substring(prefix.Length).Trim();
            return name;
        }

        internal static string Normalize(string name) => Clean(name).Normalize(NormalizationForm.FormC);
        private static bool SameName(string first, string second) => string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);
        private static string Clean(string name) => string.IsNullOrWhiteSpace(name) ? string.Empty
            : string.Join(" ", name.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        private static TextObject Literal(string value) => new TextObject("{=!}" + value);
        private static TextObject Format(string format, string key, TextObject value, string otherKey = null, TextObject other = null)
        {
            var text = new TextObject(format);
            text.SetTextVariable(key, value.CopyTextObject());
            if (otherKey != null) text.SetTextVariable(otherKey, other.CopyTextObject());
            return text;
        }

        internal static string ToRoman(int number)
        {
            if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var result = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
                while (number >= values[i]) { result.Append(symbols[i]); number -= values[i]; }
            return result.ToString();
        }
    }
}

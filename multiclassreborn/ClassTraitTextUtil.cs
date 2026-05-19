using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

#nullable disable

namespace multiclassreborn
{
    // Shared text helpers for class, trait, and VTML stat display.
    internal static class ClassTraitTextUtil
    {
        private static readonly TextDrawUtil TextDraw = new TextDrawUtil();

        // Returns a localized class name, falling back to a readable code.
        internal static string GetClassName(string classCode)
        {
            string name = Lang.GetIfExists("characterclass-" + classCode);
            return string.IsNullOrWhiteSpace(name) ? CapitalizeCode(classCode) : name;
        }

        // Formats a trait name with the same polarity colors the game uses.
        internal static string BuildTraitNameText(Trait trait)
        {
            return $"<font color=\"{GetTraitColor(trait)}\"><strong>{GetTraitName(trait.Code)}</strong></font>";
        }

        // Returns a localized trait name from either vanilla trait key format.
        internal static string GetTraitName(string traitCode)
        {
            string name = Lang.GetIfExists("traitname-" + traitCode);
            if (!string.IsNullOrWhiteSpace(name)) return name;

            name = StripVtml(Lang.Get("trait-" + traitCode)).TrimStart('\u2022').Trim();
            if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("trait-", StringComparison.OrdinalIgnoreCase)) return name;

            return CapitalizeCode(traitCode);
        }

        // Adds one indented bullet that can wrap inside narrow panes.
        internal static void AppendWrappedBullet(StringBuilder text, string content, CairoFont font, double maxWidth)
        {
            AppendWrappedLine(text, "    \u2022 ", "      ", content, font, maxWidth);
        }

        // Keep VTML tags out of the width math, otherwise rich trait text wraps early.
        internal static void AppendWrappedLine(StringBuilder text, string prefix, string continuationPrefix, string content, CairoFont font, double maxWidth)
        {
            string cleanContent = StripVtml(content);
            if (string.IsNullOrWhiteSpace(cleanContent)) return;

            double wrapTolerance = Math.Min(96, maxWidth * 0.22);
            double firstWidth = Math.Max(1, maxWidth - MeasureTextWidth(prefix, font) + wrapTolerance);
            double nextWidth = Math.Max(1, maxWidth - MeasureTextWidth(continuationPrefix, font) + wrapTolerance);
            List<string> lines = WrapVtmlText(content, font, firstWidth, nextWidth);

            for (int i = 0; i < lines.Count; i++)
            {
                text.AppendLine((i == 0 ? prefix : continuationPrefix) + lines[i]);
            }
        }

        // Estimates richtext height from line count and current font metrics.
        internal static double MeasureExplicitTextHeight(string text, CairoFont font, double minimumHeight)
        {
            string trimmed = (text ?? "").TrimEnd('\r', '\n');
            int lines = trimmed.Length == 0 ? 1 : trimmed.Split('\n').Length;

            return Math.Max(minimumHeight, lines * TextDraw.GetLineHeight(font));
        }

        // Removes VTML tags before plain-text checks and measurements.
        internal static string StripVtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            return Regex.Replace(text, "<.*?>", "").Trim();
        }

        // Filters missing localization keys and empty VTML.
        internal static bool HasVisibleLocalizedText(string text, string localizationKey)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Equals(localizationKey, StringComparison.OrdinalIgnoreCase)) return false;

            return !string.IsNullOrWhiteSpace(StripVtml(text));
        }

        // Mirrors the server-side duplicate-stat rules so previews match applied stats.
        internal static HashSet<string> BuildAppliedExtraStatKeys(MulticlassRebornModSystem classSystem, IEnumerable<string> extraClassCodes, bool onlyBestPositive, bool onlyWorstNegative)
        {
            List<TraitStatLine> candidates = GatherExtraStatLines(classSystem, extraClassCodes);
            HashSet<string> appliedKeys = new HashSet<string>();

            foreach (var group in candidates.GroupBy(candidate => new { candidate.StatCode, candidate.TraitType }))
            {
                if (ShouldKeepOnlyStrongest(group.Key.TraitType, onlyBestPositive, onlyWorstNegative))
                {
                    appliedKeys.Add(group.OrderByDescending(candidate => Math.Abs(candidate.RawValue)).First().Key);
                    continue;
                }

                foreach (TraitStatLine candidate in group)
                {
                    appliedKeys.Add(candidate.Key);
                }
            }

            return appliedKeys;
        }

        // Checks whether duplicate-stat filtering left this stat visible.
        internal static bool IsAppliedExtraStat(HashSet<string> appliedStatKeys, string traitCode, string statCode)
        {
            return appliedStatKeys == null || appliedStatKeys.Contains(BuildStatKey(traitCode, statCode));
        }

        // Builds the stat line shown in the dialog and Traits tab.
        internal static string BuildStatText(KeyValuePair<string, double> stat, float scale, bool showScaledValue, bool isApplied = true)
        {
            string baseText = Lang.Get($"charattribute-{stat.Key}-{stat.Value}");
            if (!showScaledValue) return baseText;
            if (!isApplied) return $"{baseText} (0%)";

            string scaledText = BuildScaledStatText(baseText, scale);
            if (scaledText == baseText) return baseText;

            string scaledValue = BuildCompactScaledValue(scaledText);
            return $"{baseText} ({scaledValue})";
        }

        // Collects unique trait stats from the selected extra classes.
        private static List<TraitStatLine> GatherExtraStatLines(MulticlassRebornModSystem classSystem, IEnumerable<string> extraClassCodes)
        {
            List<TraitStatLine> candidates = new List<TraitStatLine>();
            HashSet<string> traitCodes = new HashSet<string>();

            foreach (string classCode in extraClassCodes ?? Enumerable.Empty<string>())
            {
                if (!classSystem.Ledger.ClassByCode.TryGetValue(classCode, out CharacterClass classDef)) continue;
                if (classDef.Traits == null) continue;

                foreach (string traitCode in classDef.Traits)
                {
                    if (!traitCodes.Add(traitCode)) continue;
                    if (!classSystem.Ledger.TraitByCode.TryGetValue(traitCode, out Trait trait)) continue;
                    if (trait.Attributes == null) continue;

                    foreach (KeyValuePair<string, double> stat in trait.Attributes)
                    {
                        candidates.Add(new TraitStatLine(traitCode, stat.Key, stat.Value, trait.Type));
                    }
                }
            }

            return candidates;
        }

        // Applies the matching duplicate rule for the trait polarity.
        private static bool ShouldKeepOnlyStrongest(EnumTraitType traitType, bool onlyBestPositive, bool onlyWorstNegative)
        {
            if (traitType == EnumTraitType.Positive) return onlyBestPositive;
            if (traitType == EnumTraitType.Negative) return onlyWorstNegative;

            return false;
        }

        // Uses a compact key that cannot collide with normal trait or stat codes.
        private static string BuildStatKey(string traitCode, string statCode)
        {
            return traitCode + "\n" + statCode;
        }

        // Vintage Story stat lines are localized phrases, so scale the displayed number
        // instead of rebuilding the sentence from raw stat codes.
        private static string BuildScaledStatText(string baseText, float scale)
        {
            Match match = Regex.Match(baseText, @"([+-]?\d+(?:\.\d+)?)(%)?");
            if (!match.Success) return baseText;

            double value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            string scaledNumber = FormatScaledNumber(value * scale);
            string replacement = scaledNumber + match.Groups[2].Value;

            return baseText.Substring(0, match.Index) + replacement + baseText.Substring(match.Index + match.Length);
        }

        // Keeps positive values signed so bonuses stay obvious in compact text.
        private static string FormatScaledNumber(double value)
        {
            string sign = value > 0 ? "+" : "";
            return sign + value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // Pulls out the numeric part for the parenthesized scaled value.
        private static string BuildCompactScaledValue(string scaledText)
        {
            Match numericValue = Regex.Match(scaledText, @"[+-]?\d+(?:\.\d+)?%?");
            if (numericValue.Success) return numericValue.Value;

            return scaledText;
        }

        // Uses Cairo font metrics instead of guessing character widths.
        private static double MeasureTextWidth(string text, CairoFont font)
        {
            return font.GetTextExtents(text).Width;
        }

        // Wraps VTML text while keeping tags attached to their visible words.
        private static List<string> WrapVtmlText(string text, CairoFont font, double firstWidth, double nextWidth)
        {
            List<string> lines = new List<string>();
            StringBuilder line = new StringBuilder();
            List<VtmlTag> activeTags = new List<VtmlTag>();
            string visibleLine = "";
            double maxWidth = firstWidth;

            foreach (Match match in Regex.Matches(text ?? "", "<[^>]+>|[^<]+"))
            {
                string part = match.Value;

                if (IsVtmlTag(part))
                {
                    if (IsLineBreakTag(part))
                    {
                        FinishWrappedLine(lines, line, activeTags);
                        visibleLine = "";
                        maxWidth = nextWidth;
                        continue;
                    }

                    line.Append(part);
                    TrackActiveTag(activeTags, part);
                    continue;
                }

                foreach (Match wordMatch in Regex.Matches(part, @"\S+"))
                {
                    string word = wordMatch.Value;
                    bool punctuationOnly = IsTrailingPunctuation(word);
                    string separator = string.IsNullOrEmpty(visibleLine) || punctuationOnly ? "" : " ";
                    string candidate = visibleLine + separator + word;

                    if (!string.IsNullOrEmpty(visibleLine) && MeasureTextWidth(candidate, font) > maxWidth)
                    {
                        FinishWrappedLine(lines, line, activeTags);
                        visibleLine = "";
                        maxWidth = nextWidth;
                        separator = "";
                    }

                    if (!string.IsNullOrEmpty(separator))
                    {
                        line.Append(separator);
                        visibleLine += separator;
                    }

                    line.Append(word);
                    visibleLine += word;
                }
            }

            if (!string.IsNullOrWhiteSpace(StripVtml(line.ToString())))
            {
                FinishWrappedLine(lines, line, activeTags);
            }

            return lines.Count == 0 ? new List<string> { StripVtml(text) } : lines;
        }

        // Close and reopen active VTML tags so a wrapped link or font tag stays valid.
        private static void FinishWrappedLine(List<string> lines, StringBuilder line, List<VtmlTag> activeTags)
        {
            if (line.Length == 0) return;

            for (int i = activeTags.Count - 1; i >= 0; i--)
            {
                line.Append("</").Append(activeTags[i].Name).Append(">");
            }

            lines.Add(line.ToString());
            line.Clear();

            foreach (VtmlTag tag in activeTags)
            {
                line.Append(tag.OpeningTag);
            }
        }

        // Updates the active tag stack as raw VTML tokens are processed.
        private static void TrackActiveTag(List<VtmlTag> activeTags, string tag)
        {
            string name = GetTagName(tag);
            if (string.IsNullOrWhiteSpace(name)) return;
            if (tag.StartsWith("</", StringComparison.Ordinal))
            {
                for (int i = activeTags.Count - 1; i >= 0; i--)
                {
                    if (!activeTags[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

                    activeTags.RemoveAt(i);
                    break;
                }

                return;
            }

            if (tag.EndsWith("/>", StringComparison.Ordinal)) return;
            activeTags.Add(new VtmlTag(name, tag));
        }

        // Extracts a VTML tag name from an opening or closing token.
        private static string GetTagName(string tag)
        {
            Match match = Regex.Match(tag, @"^</?\s*([a-zA-Z0-9]+)");
            return match.Success ? match.Groups[1].Value : "";
        }

        // Detects tokenized VTML tags.
        private static bool IsVtmlTag(string part)
        {
            return part.StartsWith("<", StringComparison.Ordinal) && part.EndsWith(">", StringComparison.Ordinal);
        }

        // Detects VTML line breaks in both short and explicit forms.
        private static bool IsLineBreakTag(string tag)
        {
            return Regex.IsMatch(tag, @"^<\s*br\s*/?\s*>$", RegexOptions.IgnoreCase);
        }

        // Keeps punctuation attached to the previous visible word.
        private static bool IsTrailingPunctuation(string text)
        {
            return Regex.IsMatch(text, @"^[,.;:!?]+$");
        }

        // Maps trait polarity to vanilla-style display colors.
        private static string GetTraitColor(Trait trait)
        {
            if (trait.Type == EnumTraitType.Negative) return "#ff8484";
            if (trait.Type == EnumTraitType.Positive) return "#84ff84";

            return "#ffffff";
        }

        // Turns missing localization codes into readable fallback labels.
        private static string CapitalizeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";

            string[] parts = code.Replace('-', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }

            return string.Join(" ", parts);
        }

        // Tracks tags that need reopening when a VTML line wraps.
        private sealed class VtmlTag
        {
            internal readonly string Name;
            internal readonly string OpeningTag;

            // Stores the tag name and original opening text for later reopening.
            internal VtmlTag(string name, string openingTag)
            {
                Name = name;
                OpeningTag = openingTag;
            }
        }

        // Minimal stat candidate used by the dialog and traits tab previews.
        private sealed class TraitStatLine
        {
            internal readonly string Key;
            internal readonly string StatCode;
            internal readonly double RawValue;
            internal readonly EnumTraitType TraitType;

            // Captures the values needed for duplicate-stat filtering.
            internal TraitStatLine(string traitCode, string statCode, double rawValue, EnumTraitType traitType)
            {
                Key = BuildStatKey(traitCode, statCode);
                StatCode = statCode;
                RawValue = rawValue;
                TraitType = traitType;
            }
        }
    }
}

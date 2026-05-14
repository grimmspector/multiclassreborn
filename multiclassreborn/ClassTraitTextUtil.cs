using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

#nullable disable

namespace multiclassreborn
{
    /// <summary>
    /// Shared text helpers for compact class trait panes.
    /// </summary>
    internal static class ClassTraitTextUtil
    {
        private static readonly TextDrawUtil TextDraw = new TextDrawUtil();

        /// <summary>
        /// Gets a localized class name with a readable code fallback.
        /// </summary>
        internal static string GetClassName(string classCode)
        {
            string name = Lang.GetIfExists("characterclass-" + classCode);
            return string.IsNullOrWhiteSpace(name) ? CapitalizeCode(classCode) : name;
        }

        /// <summary>
        /// Formats a trait name using the vanilla trait polarity colors.
        /// </summary>
        internal static string BuildTraitNameText(Trait trait)
        {
            return $"<font color=\"{GetTraitColor(trait)}\"><strong>{GetTraitName(trait.Code)}</strong></font>";
        }

        /// <summary>
        /// Gets a localized trait name with a readable code fallback.
        /// </summary>
        internal static string GetTraitName(string traitCode)
        {
            string name = Lang.GetIfExists("traitname-" + traitCode);
            if (!string.IsNullOrWhiteSpace(name)) return name;

            name = StripVtml(Lang.Get("trait-" + traitCode)).TrimStart('•').Trim();
            if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("trait-", StringComparison.OrdinalIgnoreCase)) return name;

            return CapitalizeCode(traitCode);
        }

        /// <summary>
        /// Adds a wrapped bullet line using measured font width.
        /// </summary>
        internal static void AppendWrappedBullet(StringBuilder text, string content, CairoFont font, double maxWidth)
        {
            AppendWrappedLine(text, "    • ", "      ", StripVtml(content), font, maxWidth);
        }

        /// <summary>
        /// Adds a wrapped line using measured font width.
        /// </summary>
        internal static void AppendWrappedLine(StringBuilder text, string prefix, string continuationPrefix, string content, CairoFont font, double maxWidth)
        {
            string cleanContent = StripVtml(content);
            if (string.IsNullOrWhiteSpace(cleanContent))
            {
                text.AppendLine(prefix.TrimEnd());
                return;
            }

            double firstWidth = Math.Max(1, maxWidth - MeasureTextWidth(prefix, font));
            double nextWidth = Math.Max(1, maxWidth - MeasureTextWidth(continuationPrefix, font));
            List<string> lines = WrapPlainText(cleanContent, font, firstWidth, nextWidth);

            for (int i = 0; i < lines.Count; i++)
            {
                text.AppendLine((i == 0 ? prefix : continuationPrefix) + lines[i]);
            }
        }

        /// <summary>
        /// Measures content height from explicit lines and actual font metrics.
        /// </summary>
        internal static double MeasureExplicitTextHeight(string text, CairoFont font, double minimumHeight)
        {
            string trimmed = (text ?? "").TrimEnd('\r', '\n');
            int lines = trimmed.Length == 0 ? 1 : trimmed.Split('\n').Length;

            return Math.Max(minimumHeight, lines * TextDraw.GetLineHeight(font));
        }

        /// <summary>
        /// Removes VTML tags for safe wrapping in tight panes.
        /// </summary>
        internal static string StripVtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            return Regex.Replace(text, "<.*?>", "").Trim();
        }

        /// <summary>
        /// Measures rendered text width with the active GUI font.
        /// </summary>
        private static double MeasureTextWidth(string text, CairoFont font)
        {
            return font.GetTextExtents(text).Width;
        }

        /// <summary>
        /// Splits plain text into measured lines without breaking words.
        /// </summary>
        private static List<string> WrapPlainText(string text, CairoFont font, double firstWidth, double nextWidth)
        {
            List<string> lines = new List<string>();
            string line = "";
            double maxWidth = firstWidth;

            foreach (string word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
                if (!string.IsNullOrEmpty(line) && MeasureTextWidth(candidate, font) > maxWidth)
                {
                    lines.Add(line);
                    line = word;
                    maxWidth = nextWidth;
                    continue;
                }

                line = candidate;
            }

            if (!string.IsNullOrEmpty(line)) lines.Add(line);
            return lines.Count == 0 ? new List<string> { text } : lines;
        }

        /// <summary>
        /// Returns the vanilla color used for trait polarity.
        /// </summary>
        private static string GetTraitColor(Trait trait)
        {
            if (trait.Type == EnumTraitType.Negative) return "#ff8484";
            if (trait.Type == EnumTraitType.Positive) return "#84ff84";

            return "#ffffff";
        }

        /// <summary>
        /// Provides a readable fallback for missing localization entries.
        /// </summary>
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
    }
}

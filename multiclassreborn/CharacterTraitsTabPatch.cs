using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using multiclassreborn.systems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

#nullable disable

namespace multiclassreborn
{
    /// <summary>
    /// Replaces the vanilla Traits tab with wrapped, scrollable class text.
    /// </summary>
    [HarmonyPatch]
    internal static class CharacterTraitsTabPatch
    {
        private const double TraitTabVisibleHeight = 320;
        private const double TraitTabContentWidth = 440;
        private const double ScrollbarWidth = 14;
        private const double ScrollbarPadding = 4;

        private static GuiComposer traitsComposer;

        /// <summary>
        /// Replaces the fixed vanilla trait text with clipped scrollable text.
        /// </summary>
        [HarmonyPatch("Vintagestory.GameContent.CharacterSystem", "composeTraitsTab")]
        [HarmonyPrefix]
        private static bool ComposeScrollableTraitsTab(GuiComposer compo)
        {
            CairoFont font = CreateTraitsFont();
            double textWidth = TraitTabContentWidth - ScrollbarWidth - ScrollbarPadding;
            string traitText = BuildTraitTabText(font, textWidth);
            double contentHeight = ClassTraitTextUtil.MeasureExplicitTextHeight(traitText, font, TraitTabVisibleHeight);
            traitsComposer = compo;

            ElementBounds clipBounds = ElementBounds.Fixed(0, 25, TraitTabContentWidth, TraitTabVisibleHeight);
            ElementBounds textBounds = ElementBounds.Fixed(0, 0, textWidth, contentHeight);
            ElementBounds scrollbarBounds = ElementBounds.Fixed(EnumDialogArea.RightFixed, 0, 25, ScrollbarWidth, TraitTabVisibleHeight);

            compo.AddInset(clipBounds, 2);
            compo.BeginClip(clipBounds);
            compo.AddRichtext(traitText, font, textBounds, "multiclassTraitsText");
            compo.EndClip();

            if (contentHeight > TraitTabVisibleHeight)
            {
                compo.AddVerticalScrollbar(OnTraitsScroll, scrollbarBounds, "multiclassTraitsScrollbar");
                compo.OnComposed += () => compo.GetScrollbar("multiclassTraitsScrollbar")?.SetHeights((float)TraitTabVisibleHeight, (float)contentHeight);
            }

            return false;
        }

        /// <summary>
        /// Scrolls the traits text inside the clipped tab area.
        /// </summary>
        private static void OnTraitsScroll(float dy)
        {
            ElementBounds bounds = traitsComposer?.GetRichtext("multiclassTraitsText")?.Bounds;
            if (bounds == null) return;

            bounds.fixedY = -dy;
            bounds.CalcWorldBounds();
        }

        /// <summary>
        /// Builds the shared traits tab font.
        /// </summary>
        private static CairoFont CreateTraitsFont()
        {
            return CairoFont.WhiteDetailText().WithLineHeightMultiplier(1.15);
        }

        /// <summary>
        /// Builds the complete Traits tab text.
        /// </summary>
        private static string BuildTraitTabText(CairoFont font, double maxWidth)
        {
            MulticlassRebornModSystem classSystem = MulticlassRebornModSystem.ClientInstance;
            EntityPlayer entity = classSystem?.ClientApi?.World?.Player?.Entity;
            if (entity == null) return "";

            RebornPlayerClassState state = new RebornPlayerClassState(entity);
            string mainClassCode = entity.WatchedAttributes.GetString("characterClass", "none");

            StringBuilder text = new StringBuilder();
            if (classSystem.Ledger.ClassByCode.TryGetValue(mainClassCode, out CharacterClass mainClass))
            {
                text.AppendLine("<strong>Base Class:</strong>");
                text.AppendLine($"<strong>{ClassTraitTextUtil.GetClassName(mainClass.Code)}</strong>");
                AppendTraitDetails(text, mainClass, classSystem, font, maxWidth, false);
                text.AppendLine();
            }

            List<string> extraClasses = state.ExtraClasses
                .Where(classCode => classSystem.Ledger.ClassByCode.ContainsKey(classCode))
                .Distinct()
                .ToList();

            if (extraClasses.Count == 0) return text.ToString();

            text.AppendLine("<strong>Extra classes:</strong>");
            foreach (string classCode in extraClasses)
            {
                if (!classSystem.Ledger.ClassByCode.TryGetValue(classCode, out CharacterClass classDef)) continue;

                text.AppendLine($"<strong>{ClassTraitTextUtil.GetClassName(classDef.Code)}</strong>");
                AppendTraitDetails(text, classDef, classSystem, font, maxWidth, true);
            }

            return text.ToString();
        }

        /// <summary>
        /// Writes trait names and wrapped bullet details for one class.
        /// </summary>
        private static void AppendTraitDetails(StringBuilder text, CharacterClass classDef, MulticlassRebornModSystem classSystem, CairoFont font, double maxWidth, bool showScaledValues)
        {
            if (classDef.Traits == null || classDef.Traits.Length == 0)
            {
                text.AppendLine("  <i>This class has no listed traits.</i>");
                return;
            }

            foreach (string traitCode in classDef.Traits)
            {
                if (!classSystem.Ledger.TraitByCode.TryGetValue(traitCode, out Trait trait)) continue;

                text.AppendLine($"  {ClassTraitTextUtil.BuildTraitNameText(trait)}");

                if (trait.Attributes != null)
                {
                    foreach (KeyValuePair<string, double> stat in trait.Attributes)
                    {
                        ClassTraitTextUtil.AppendWrappedBullet(text, BuildStatText(stat, classSystem, showScaledValues), font, maxWidth);
                    }
                }

                string description = Lang.GetIfExists("traitdesc-" + traitCode);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    ClassTraitTextUtil.AppendWrappedBullet(text, description, font, maxWidth);
                }
            }
        }

        /// <summary>
        /// Formats base stat text and optional extra-class scaled value.
        /// </summary>
        private static string BuildStatText(KeyValuePair<string, double> stat, MulticlassRebornModSystem classSystem, bool showScaledValue)
        {
            string baseText = Lang.Get($"charattribute-{stat.Key}-{stat.Value}");
            if (!showScaledValue) return baseText;

            string scaledText = BuildScaledStatText(baseText, classSystem.Config?.ExtraClassScale ?? 0.8f);
            if (scaledText == baseText) return baseText;

            string scaledValue = BuildCompactScaledValue(baseText, scaledText);
            return $"{baseText} ({scaledValue})";
        }

        /// <summary>
        /// Scales the first numeric value inside a translated stat phrase.
        /// </summary>
        private static string BuildScaledStatText(string baseText, float scale)
        {
            Match match = Regex.Match(baseText, @"([+-]?\d+(?:\.\d+)?)(%)?");
            if (!match.Success) return baseText;

            double value = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            string scaledNumber = FormatScaledNumber(value * scale);
            string replacement = scaledNumber + match.Groups[2].Value;

            return baseText.Substring(0, match.Index) + replacement + baseText.Substring(match.Index + match.Length);
        }

        /// <summary>
        /// Keeps scaled display numbers compact and signed when useful.
        /// </summary>
        private static string FormatScaledNumber(double value)
        {
            string sign = value > 0 ? "+" : "";
            return sign + value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Removes repeated stat labels from the scaled value when possible.
        /// </summary>
        private static string BuildCompactScaledValue(string baseText, string scaledText)
        {
            Match numericValue = Regex.Match(scaledText, @"[+-]?\d+(?:\.\d+)?%?");
            if (numericValue.Success) return numericValue.Value;

            return scaledText;
        }
    }
}

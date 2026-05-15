using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private static CairoFont traitsFont;
        private static double traitsTextWidth;
        private static string lastTraitText;

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
            double contentHeight = ClassTraitTextUtil.MeasureExplicitTextHeight(traitText, font, 1);
            traitsComposer = compo;
            traitsFont = font;
            traitsTextWidth = textWidth;
            lastTraitText = traitText;

            ElementBounds clipBounds = ElementBounds.Fixed(0, 25, TraitTabContentWidth, TraitTabVisibleHeight);
            ElementBounds textBounds = ElementBounds.Fixed(0, 0, textWidth, contentHeight);
            ElementBounds scrollbarBounds = ElementBounds.Fixed(EnumDialogArea.RightFixed, 0, 25, ScrollbarWidth, TraitTabVisibleHeight);

            compo.AddInset(clipBounds, 2);
            compo.BeginClip(clipBounds);
            compo.AddRichtext(traitText, font, textBounds, "multiclassTraitsText");
            compo.EndClip();

            compo.AddVerticalScrollbar(OnTraitsScroll, scrollbarBounds, "multiclassTraitsScrollbar");
            compo.OnComposed += () => compo.GetScrollbar("multiclassTraitsScrollbar")?.SetHeights((float)TraitTabVisibleHeight, (float)contentHeight);

            return false;
        }

        /// <summary>
        /// Rebuilds the Traits tab text after class changes while the screen is open.
        /// </summary>
        internal static void RefreshOpenTraitsTab()
        {
            if (traitsComposer == null || traitsFont == null) return;

            string traitText = BuildTraitTabText(traitsFont, traitsTextWidth);
            if (traitText == lastTraitText) return;

            double contentHeight = ClassTraitTextUtil.MeasureExplicitTextHeight(traitText, traitsFont, 1);
            GuiElementRichtext richtext = traitsComposer.GetRichtext("multiclassTraitsText");
            if (richtext == null) return;

            richtext.Bounds.fixedHeight = contentHeight;
            richtext.Bounds.CalcWorldBounds();
            richtext.SetNewText(traitText, traitsFont);
            richtext.RecomposeText();
            traitsComposer.GetScrollbar("multiclassTraitsScrollbar")?.SetHeights((float)TraitTabVisibleHeight, (float)contentHeight);
            lastTraitText = traitText;
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
                text.AppendLine(Lang.Get("multiclassreborn:traits-tab-base-class"));
                text.AppendLine($"<strong>{ClassTraitTextUtil.GetClassName(mainClass.Code)}</strong>");
                AppendTraitDetails(text, mainClass, classSystem, state, font, maxWidth, false, null);
                text.AppendLine();
            }

            List<string> extraClasses = state.ExtraClasses
                .Where(classCode => classSystem.Ledger.ClassByCode.ContainsKey(classCode))
                .Distinct()
                .ToList();

            if (extraClasses.Count == 0) return text.ToString();

            HashSet<string> appliedStatKeys = ClassTraitTextUtil.BuildAppliedExtraStatKeys(
                classSystem,
                extraClasses,
                state.OnlyApplyBestPositiveTraitBonus,
                state.OnlyApplyWorstNegativeTraitPenalty);
            text.AppendLine(Lang.Get("multiclassreborn:traits-tab-extra-classes"));
            foreach (string classCode in extraClasses)
            {
                if (!classSystem.Ledger.ClassByCode.TryGetValue(classCode, out CharacterClass classDef)) continue;

                text.AppendLine($"<strong>{ClassTraitTextUtil.GetClassName(classDef.Code)}</strong>");
                AppendTraitDetails(text, classDef, classSystem, state, font, maxWidth, true, appliedStatKeys);
            }

            return text.ToString();
        }

        /// <summary>
        /// Writes trait names and wrapped bullet details for one class.
        /// </summary>
        private static void AppendTraitDetails(StringBuilder text, CharacterClass classDef, MulticlassRebornModSystem classSystem, RebornPlayerClassState state, CairoFont font, double maxWidth, bool showScaledValues, HashSet<string> appliedStatKeys)
        {
            if (classDef.Traits == null || classDef.Traits.Length == 0)
            {
                text.AppendLine(Lang.Get("multiclassreborn:traits-tab-no-listed-traits"));
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
                        bool isApplied = ClassTraitTextUtil.IsAppliedExtraStat(appliedStatKeys, traitCode, stat.Key);
                        ClassTraitTextUtil.AppendWrappedBullet(text, ClassTraitTextUtil.BuildStatText(stat, state.ExtraClassScale, showScaledValues, isApplied), font, maxWidth);
                    }
                }

                string descriptionKey = "traitdesc-" + traitCode;
                string description = Lang.GetIfExists(descriptionKey);
                if (ClassTraitTextUtil.HasVisibleLocalizedText(description, descriptionKey))
                {
                    ClassTraitTextUtil.AppendWrappedBullet(text, description, font, maxWidth);
                }
            }
        }
    }
}

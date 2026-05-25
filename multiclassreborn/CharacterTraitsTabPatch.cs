using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using multiclassreborn.systems;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

#nullable disable

namespace multiclassreborn
{
    // Replaces the vanilla Traits tab with a scrollable class summary.
    [HarmonyPatch]
    internal static class CharacterTraitsTabPatch
    {
        private const double TraitTabVisibleHeight = 320;
        private const double TraitTabContentWidth = 440;
        private const double ScrollbarWidth = 14;
        private const double ScrollbarPadding = 4;
        private static readonly string[] ModelTreeKeys = { "customModel", "playerModel", "playermodel", "playerModelLib", "skinConfig" };
        private static readonly string[] ModelCodeKeys = { "currentModel", "modelCode", "ModelCode", "CurrentModelCode", "playermodel", "playerModel", "customPlayerModel" };

        private static GuiComposer traitsComposer;
        private static CairoFont traitsFont;
        private static double traitsTextWidth;
        private static string lastTraitText;
        private static List<ModelTraitRecord> cachedModelTraits;

        // Vanilla trait text clips long multiclass descriptions; this keeps the same tab
        // but gives the text a measured scroll area.
        [HarmonyPatch("Vintagestory.GameContent.CharacterSystem", "composeTraitsTab")]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(new[] { "playermodellib", "PlayerModelLib", "com.maltiez.playermodellib" })]
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

        // Refreshes the already-open character window after a class command changes state.
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

        // Moves the richtext inside the clipped traits area.
        private static void OnTraitsScroll(float dy)
        {
            ElementBounds bounds = traitsComposer?.GetRichtext("multiclassTraitsText")?.Bounds;
            if (bounds == null) return;

            bounds.fixedY = -dy;
            bounds.CalcWorldBounds();
        }

        // Uses the vanilla detail font with a little extra line spacing.
        private static CairoFont CreateTraitsFont()
        {
            return CairoFont.WhiteDetailText().WithLineHeightMultiplier(1.15);
        }

        // Builds the base and extra class sections shown in the patched Traits tab.
        private static string BuildTraitTabText(CairoFont font, double maxWidth)
        {
            MulticlassRebornModSystem classSystem = MulticlassRebornModSystem.ClientInstance;
            EntityPlayer entity = classSystem?.ClientApi?.World?.Player?.Entity;
            if (entity == null) return "";

            RebornPlayerClassState state = new RebornPlayerClassState(entity);
            string mainClassCode = entity.WatchedAttributes.GetString("characterClass", "none");

            StringBuilder text = new StringBuilder();
            HashSet<string> classTraitCodes = GatherClassTraitCodes(classSystem, mainClassCode, state.ExtraClasses);
            List<string> additionalTraits = GatherAdditionalTraitCodes(entity, classSystem, classTraitCodes);
            ModelTraitRecord modelTraits = additionalTraits.Count > 0
                ? FindModelTraits(entity, classSystem, additionalTraits)
                : null;
            List<string> modelTraitCodes = modelTraits == null
                ? new List<string>()
                : modelTraits.TraitCodes
                    .Where(additionalTraits.Contains)
                    .ToList();

            if (modelTraitCodes.Count > 0)
            {
                text.AppendLine(Lang.Get("multiclassreborn:traits-tab-model-traits"));
                text.AppendLine($"<strong>{GetModelName(modelTraits.ModelCode)}</strong>");
                AppendLooseTraitDetails(text, modelTraitCodes, classSystem, font, maxWidth);
                text.AppendLine();
            }

            text.AppendLine(Lang.Get("multiclassreborn:traits-tab-base-class"));
            if (classSystem.Ledger.ClassByCode.TryGetValue(mainClassCode, out CharacterClass mainClass))
            {
                text.AppendLine($"<strong>{ClassTraitTextUtil.GetClassName(mainClass.Code)}</strong>");
                AppendTraitDetails(text, mainClass, classSystem, state, font, maxWidth, false, null);
            }
            else
            {
                text.AppendLine($"<strong>{ClassTraitTextUtil.GetClassName(mainClassCode)}</strong>");
                text.AppendLine(Lang.Get("multiclassreborn:traits-tab-no-listed-traits"));
            }

            text.AppendLine();

            List<string> looseTraits = additionalTraits
                .Where(traitCode => modelTraits == null || !modelTraits.TraitCodes.Contains(traitCode))
                .ToList();
            if (looseTraits.Count > 0)
            {
                text.AppendLine(Lang.Get("multiclassreborn:traits-tab-additional-traits"));
                AppendLooseTraitDetails(text, looseTraits, classSystem, font, maxWidth);
                text.AppendLine();
            }

            List<string> extraClasses = state.ExtraClasses
                .Distinct()
                .ToList();

            if (extraClasses.Count == 0)
            {
                return text.ToString();
            }

            HashSet<string> appliedStatKeys = ClassTraitTextUtil.BuildAppliedExtraStatKeys(
                classSystem,
                extraClasses,
                state.OnlyApplyBestPositiveTraitBonus,
                state.OnlyApplyWorstNegativeTraitPenalty);
            text.AppendLine(Lang.Get("multiclassreborn:traits-tab-extra-classes"));
            foreach (string classCode in extraClasses)
            {
                if (!classSystem.Ledger.ClassByCode.TryGetValue(classCode, out CharacterClass classDef))
                {
                    text.AppendLine($"<strong>{ClassTraitTextUtil.GetClassName(classCode)}</strong>");
                    text.AppendLine(Lang.Get("multiclassreborn:traits-tab-no-listed-traits"));
                    continue;
                }

                text.AppendLine($"<strong>{ClassTraitTextUtil.GetClassName(classDef.Code)}</strong>");
                AppendTraitDetails(text, classDef, classSystem, state, font, maxWidth, true, appliedStatKeys);
            }

            return text.ToString();
        }

        // Collects traits already shown through base and extra class sections.
        private static HashSet<string> GatherClassTraitCodes(MulticlassRebornModSystem classSystem, string mainClassCode, IEnumerable<string> extraClasses)
        {
            HashSet<string> traitCodes = new HashSet<string>();
            AddClassTraitCodes(traitCodes, classSystem, mainClassCode);

            foreach (string classCode in extraClasses ?? Enumerable.Empty<string>())
            {
                AddClassTraitCodes(traitCodes, classSystem, classCode);
            }

            return traitCodes;
        }

        // Adds one class' trait codes to the provided set.
        private static void AddClassTraitCodes(HashSet<string> traitCodes, MulticlassRebornModSystem classSystem, string classCode)
        {
            if (!classSystem.Ledger.ClassByCode.TryGetValue(classCode, out CharacterClass classDef)) return;
            if (classDef.Traits == null) return;

            foreach (string traitCode in classDef.Traits)
            {
                traitCodes.Add(traitCode);
            }
        }

        // Reads shared vanilla extra traits without duplicating class-owned traits.
        private static List<string> GatherAdditionalTraitCodes(EntityPlayer entity, MulticlassRebornModSystem classSystem, HashSet<string> classTraitCodes)
        {
            string[] extraTraits = entity.WatchedAttributes.GetStringArray("extraTraits", null);
            if (extraTraits == null || extraTraits.Length == 0) return new List<string>();

            return extraTraits
                .Where(traitCode => !string.IsNullOrWhiteSpace(traitCode))
                .Where(traitCode => !classTraitCodes.Contains(traitCode))
                .Where(traitCode => classSystem.Ledger.TraitByCode.ContainsKey(traitCode))
                .Distinct()
                .ToList();
        }

        // Finds the PlayerModelLib model whose configured traits are currently active.
        private static ModelTraitRecord FindModelTraits(EntityPlayer entity, MulticlassRebornModSystem classSystem, List<string> additionalTraits)
        {
            List<ModelTraitRecord> modelTraits = GetModelTraitRecords(classSystem);
            if (modelTraits.Count == 0) return null;

            string modelCode = GetWatchedModelCode(entity);
            ModelTraitRecord currentModel = modelTraits.FirstOrDefault(record => record.MatchesCode(modelCode));
            if (currentModel != null && currentModel.TraitCodes.Any(additionalTraits.Contains)) return currentModel;

            return modelTraits
                .Where(record => record.TraitCodes.Count > 0)
                .Where(record => record.TraitCodes.All(additionalTraits.Contains))
                .OrderByDescending(record => record.TraitCodes.Count)
                .FirstOrDefault();
        }

        // Reads model trait metadata from PlayerModelLib-style custom model assets.
        private static List<ModelTraitRecord> GetModelTraitRecords(MulticlassRebornModSystem classSystem)
        {
            if (cachedModelTraits != null) return cachedModelTraits;

            cachedModelTraits = new List<ModelTraitRecord>();
            foreach (IAsset asset in classSystem.ClientApi.Assets.GetMany("config/customplayermodels", null, true))
            {
                JObject root = asset.ToObject<JObject>();
                foreach (JProperty modelProperty in root.Properties())
                {
                    List<string> traitCodes = modelProperty.Value["ExtraTraits"]?.Values<string>()
                        .Where(traitCode => classSystem.Ledger.TraitByCode.ContainsKey(traitCode))
                        .Distinct()
                        .ToList() ?? new List<string>();

                    cachedModelTraits.Add(new ModelTraitRecord(modelProperty.Name, traitCodes));
                }
            }

            return cachedModelTraits;
        }

        // Looks for common PlayerModelLib watched-attribute locations without taking a hard dependency.
        private static string GetWatchedModelCode(EntityPlayer entity)
        {
            foreach (string key in ModelCodeKeys)
            {
                string modelCode = entity.WatchedAttributes.GetString(key, null);
                if (!string.IsNullOrWhiteSpace(modelCode)) return modelCode;
            }

            foreach (string treeKey in ModelTreeKeys)
            {
                ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(treeKey);
                if (tree == null) continue;

                foreach (string key in ModelCodeKeys)
                {
                    string modelCode = tree.GetString(key, null);
                    if (!string.IsNullOrWhiteSpace(modelCode)) return modelCode;
                }
            }

            return "";
        }

        // Returns a localized model name using PlayerModelLib and vanilla key formats.
        private static string GetModelName(string modelCode)
        {
            foreach (string key in new[] { "playermodel-" + modelCode, "game:playermodel-" + modelCode })
            {
                string name = Lang.GetIfExists(key);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }

            return CapitalizeCode(modelCode);
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

        // Adds loose model, race, or mod-provided traits that are not part of a class.
        private static void AppendLooseTraitDetails(StringBuilder text, IEnumerable<string> traitCodes, MulticlassRebornModSystem classSystem, CairoFont font, double maxWidth)
        {
            foreach (string traitCode in traitCodes)
            {
                if (!classSystem.Ledger.TraitByCode.TryGetValue(traitCode, out Trait trait)) continue;

                AppendTraitDetails(text, trait, font, maxWidth, false, null);
            }
        }

        // Adds trait names, stat lines, and descriptions for one class.
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

                AppendTraitDetails(text, trait, font, maxWidth, showScaledValues, appliedStatKeys, state.ExtraClassScale);
            }
        }

        // Adds one trait's name, stat lines, and description.
        private static void AppendTraitDetails(StringBuilder text, Trait trait, CairoFont font, double maxWidth, bool showScaledValues, HashSet<string> appliedStatKeys, float scale = 1)
        {
            text.AppendLine($"  {ClassTraitTextUtil.BuildTraitNameText(trait)}");

            if (trait.Attributes != null)
            {
                foreach (KeyValuePair<string, double> stat in trait.Attributes)
                {
                    bool isApplied = ClassTraitTextUtil.IsAppliedExtraStat(appliedStatKeys, trait.Code, stat.Key);
                    ClassTraitTextUtil.AppendWrappedBullet(text, ClassTraitTextUtil.BuildStatText(stat, scale, showScaledValues, isApplied), font, maxWidth);
                }
            }

            string descriptionKey = "traitdesc-" + trait.Code;
            string description = Lang.GetIfExists(descriptionKey);
            if (ClassTraitTextUtil.HasVisibleLocalizedText(description, descriptionKey))
            {
                ClassTraitTextUtil.AppendWrappedBullet(text, description, font, maxWidth);
            }
        }

        // Holds model trait codes discovered from PlayerModelLib-compatible assets.
        private sealed class ModelTraitRecord
        {
            internal readonly string ModelCode;
            internal readonly List<string> TraitCodes;

            public ModelTraitRecord(string modelCode, List<string> traitCodes)
            {
                ModelCode = modelCode;
                TraitCodes = traitCodes;
            }

            internal bool MatchesCode(string modelCode)
            {
                return !string.IsNullOrWhiteSpace(modelCode) && ModelCode.Equals(modelCode, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

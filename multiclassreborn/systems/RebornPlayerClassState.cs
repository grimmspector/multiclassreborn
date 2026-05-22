using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

#nullable disable

namespace multiclassreborn.systems
{
    // Wrapper around watched attributes for persistent per-player class state.
    internal class RebornPlayerClassState
    {
        private const string StateTreeCode = "multiclassreborn";
        private const string LegacyStateTreeCode = "multiclass";
        private const string ExtraClassesKey = "extraClasses";
        private const string AvailableSlotsKey = "availableSlots";
        private const string UsedSlotsKey = "usedSlots";
        private const string RemovalCreditsKey = "removalSlots";
        private const string RequireGlyphsKey = "requireTokens";
        private const string RetrainFreeKey = "retrainFree";
        private const string AllowBaseForgetKey = "allowBaseForget";
        private const string AllowCommonerBaseChoiceKey = "allowCommonerBaseChoice";
        private const string ExtraClassScaleKey = "extraClassScale";
        private const string OnlyBestPositiveKey = "onlyBestPositive";
        private const string OnlyWorstNegativeKey = "onlyWorstNegative";
        private const string SlotsInitializedKey = "slotsInitialized";
        private const string ConfigReviewedKey = "configReviewed";
        private const string ReviewedRequireGlyphsKey = "reviewedRequireTokens";
        private const string ReviewedMaxExtraClassesKey = "reviewedMaxExtraClasses";

        private readonly EntityPlayer entity;

        // Wraps one player's watched attribute tree and runs legacy migration.
        public RebornPlayerClassState(EntityPlayer entity)
        {
            this.entity = entity;
            MigrateLegacyState();
        }

        public List<string> ExtraClasses
        {
            get
            {
                IAttribute attribute = Tree[ExtraClassesKey];
                StringArrayAttribute strings = attribute as StringArrayAttribute;

                return strings?.value?.ToList() ?? new List<string>();
            }
            set
            {
                Tree[ExtraClassesKey] = new StringArrayAttribute(value.ToArray());
                MarkTreePath(ExtraClassesKey);
            }
        }

        public int AvailableSlots
        {
            get => Tree.GetInt(AvailableSlotsKey, 0);
            set
            {
                Tree.SetInt(AvailableSlotsKey, value);
                MarkTreePath(AvailableSlotsKey);
            }
        }

        public int UsedSlots
        {
            get => Tree.GetInt(UsedSlotsKey, 0);
            set
            {
                Tree.SetInt(UsedSlotsKey, value);
                MarkTreePath(UsedSlotsKey);
            }
        }

        public int RemovalCredits
        {
            get => Tree.GetInt(RemovalCreditsKey, 0);
            set
            {
                Tree.SetInt(RemovalCreditsKey, value);
                MarkTreePath(RemovalCreditsKey);
            }
        }

        public bool RequiresGlyphs
        {
            get => Tree.GetBool(RequireGlyphsKey, false);
            set
            {
                Tree.SetBool(RequireGlyphsKey, value);
                MarkTreePath(RequireGlyphsKey);
            }
        }

        public bool RetrainFree
        {
            get => Tree.GetBool(RetrainFreeKey, false);
            set
            {
                Tree.SetBool(RetrainFreeKey, value);
                MarkTreePath(RetrainFreeKey);
            }
        }

        public bool AllowsBaseClassForgetting
        {
            get => Tree.GetBool(AllowBaseForgetKey, false);
            set
            {
                Tree.SetBool(AllowBaseForgetKey, value);
                MarkTreePath(AllowBaseForgetKey);
            }
        }

        public bool AllowsCommonerBaseClassChoice
        {
            get => Tree.GetBool(AllowCommonerBaseChoiceKey, false);
            set
            {
                Tree.SetBool(AllowCommonerBaseChoiceKey, value);
                MarkTreePath(AllowCommonerBaseChoiceKey);
            }
        }

        public float ExtraClassScale
        {
            get => Tree.GetFloat(ExtraClassScaleKey, 0.8f);
            set
            {
                Tree.SetFloat(ExtraClassScaleKey, value);
                MarkTreePath(ExtraClassScaleKey);
            }
        }

        public bool OnlyApplyBestPositiveTraitBonus
        {
            get => Tree.GetBool(OnlyBestPositiveKey, false);
            set
            {
                Tree.SetBool(OnlyBestPositiveKey, value);
                MarkTreePath(OnlyBestPositiveKey);
            }
        }

        public bool OnlyApplyWorstNegativeTraitPenalty
        {
            get => Tree.GetBool(OnlyWorstNegativeKey, false);
            set
            {
                Tree.SetBool(OnlyWorstNegativeKey, value);
                MarkTreePath(OnlyWorstNegativeKey);
            }
        }

        public bool SlotsInitialized
        {
            get => Tree.GetBool(SlotsInitializedKey, false);
            set
            {
                Tree.SetBool(SlotsInitializedKey, value);
                MarkTreePath(SlotsInitializedKey);
            }
        }

        public bool ConfigReviewed
        {
            get => Tree.GetBool(ConfigReviewedKey, false);
            set
            {
                Tree.SetBool(ConfigReviewedKey, value);
                MarkTreePath(ConfigReviewedKey);
            }
        }

        public bool ReviewedRequireGlyphs
        {
            get => Tree.GetBool(ReviewedRequireGlyphsKey, false);
            set
            {
                Tree.SetBool(ReviewedRequireGlyphsKey, value);
                MarkTreePath(ReviewedRequireGlyphsKey);
            }
        }

        public int ReviewedMaxExtraClasses
        {
            get => Tree.GetInt(ReviewedMaxExtraClassesKey, 0);
            set
            {
                Tree.SetInt(ReviewedMaxExtraClassesKey, value);
                MarkTreePath(ReviewedMaxExtraClassesKey);
            }
        }

        private ITreeAttribute Tree => entity.WatchedAttributes.GetOrAddTreeAttribute(StateTreeCode);

        // Keep the old "multiclass" tree intact; copy only missing values into Reborn.
        private void MigrateLegacyState()
        {
            ITreeAttribute legacyTree = entity.WatchedAttributes.GetTreeAttribute(LegacyStateTreeCode);
            if (legacyTree == null) return;

            CopyLegacyStringArray(legacyTree, ExtraClassesKey);
            CopyLegacyInt(legacyTree, AvailableSlotsKey);
            CopyLegacyInt(legacyTree, UsedSlotsKey);
            CopyLegacyInt(legacyTree, RemovalCreditsKey);
            CopyLegacyBool(legacyTree, RequireGlyphsKey);
            CopyLegacyBool(legacyTree, RetrainFreeKey);
            CopyLegacyBool(legacyTree, AllowBaseForgetKey);
            CopyLegacyBool(legacyTree, AllowCommonerBaseChoiceKey);
            CopyLegacyFloat(legacyTree, ExtraClassScaleKey);
            CopyLegacyBool(legacyTree, OnlyBestPositiveKey);
            CopyLegacyBool(legacyTree, OnlyWorstNegativeKey);
            CopyLegacyBool(legacyTree, SlotsInitializedKey);
            CopyLegacyBool(legacyTree, ConfigReviewedKey);
            CopyLegacyBool(legacyTree, ReviewedRequireGlyphsKey);
            CopyLegacyInt(legacyTree, ReviewedMaxExtraClassesKey);
        }

        // Copies a legacy string array only when Reborn has no value yet.
        private void CopyLegacyStringArray(ITreeAttribute legacyTree, string key)
        {
            if (Tree[key] != null || legacyTree[key] == null) return;

            StringArrayAttribute strings = legacyTree[key] as StringArrayAttribute;
            if (strings?.value == null) return;

            Tree[key] = new StringArrayAttribute(strings.value.ToArray());
            MarkTreePath(key);
        }

        // Copies a legacy integer only when Reborn has no value yet.
        private void CopyLegacyInt(ITreeAttribute legacyTree, string key)
        {
            if (Tree[key] != null || legacyTree[key] == null) return;

            Tree.SetInt(key, legacyTree.GetInt(key, 0));
            MarkTreePath(key);
        }

        // Copies a legacy boolean only when Reborn has no value yet.
        private void CopyLegacyBool(ITreeAttribute legacyTree, string key)
        {
            if (Tree[key] != null || legacyTree[key] == null) return;

            Tree.SetBool(key, legacyTree.GetBool(key, false));
            MarkTreePath(key);
        }

        // Copies a legacy float only when Reborn has no value yet.
        private void CopyLegacyFloat(ITreeAttribute legacyTree, string key)
        {
            if (Tree[key] != null || legacyTree[key] == null) return;

            Tree.SetFloat(key, legacyTree.GetFloat(key, 0f));
            MarkTreePath(key);
        }

        // Mark the nested path; marking only the root can leave the client dialog stale.
        private void MarkTreePath(string key)
        {
            entity.WatchedAttributes.MarkPathDirty($"{StateTreeCode}/{key}");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

#nullable disable

namespace multiclassreborn.systems
{
    internal class RebornPlayerClassState
    {
        private const string StateTreeCode = "multiclass";
        private const string ExtraClassesKey = "extraClasses";
        private const string AvailableSlotsKey = "availableSlots";
        private const string UsedSlotsKey = "usedSlots";
        private const string RemovalCreditsKey = "removalSlots";
        private const string RequireGlyphsKey = "requireTokens";
        private const string AllowBaseForgetKey = "allowBaseForget";
        private const string AllowCommonerBaseChoiceKey = "allowCommonerBaseChoice";
        private const string SlotsInitializedKey = "slotsInitialized";

        private readonly EntityPlayer entity;

        public RebornPlayerClassState(EntityPlayer entity)
        {
            this.entity = entity;
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

        public bool SlotsInitialized
        {
            get => Tree.GetBool(SlotsInitializedKey, false);
            set
            {
                Tree.SetBool(SlotsInitializedKey, value);
                MarkTreePath(SlotsInitializedKey);
            }
        }

        private ITreeAttribute Tree => entity.WatchedAttributes.GetOrAddTreeAttribute(StateTreeCode);

        /// <summary>
        /// Marks one nested multiclass state path dirty so the client receives
        /// fresh slot and class data without waiting for a full entity refresh.
        /// </summary>
        private void MarkTreePath(string key)
        {
            entity.WatchedAttributes.MarkPathDirty($"{StateTreeCode}/{key}");
        }
    }
}

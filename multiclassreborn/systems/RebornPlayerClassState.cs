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
        private const string RequireRunesKey = "requireTokens";
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

        public bool RequiresRunes
        {
            get => Tree.GetBool(RequireRunesKey, false);
            set
            {
                Tree.SetBool(RequireRunesKey, value);
                MarkTreePath(RequireRunesKey);
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

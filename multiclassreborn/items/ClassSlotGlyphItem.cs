using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

#nullable disable

namespace multiclassreborn.items
{
    // Right-click glyphstone for generic slots and class-bound class picks.
    internal class ClassSlotGlyphItem : Item
    {
        private const string AttributeTreeKey = "multiclassreborn";
        private const string TargetClassKey = "targetClass";
        private const string TargetClassesKey = "targetClasses";
        private const string LocksTargetClassKey = "locksTargetClass";

        // Bound glyphstones are confirmed later through the picker; generic ones still resolve here.
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);

            if (!firstEvent) return;
            if (byEntity?.Controls?.Sneak == true) return;

            handHandling = EnumHandHandling.PreventDefault;

            MulticlassRebornModSystem classSystem = api.ModLoader.GetModSystem<MulticlassRebornModSystem>();
            List<string> targetClasses = GetTargetClasses(slot?.Itemstack);

            if (byEntity?.World?.Side == EnumAppSide.Client)
            {
                if (targetClasses.Count == 0)
                {
                    classSystem.OpenClassDialogForLearning();
                    return;
                }

                classSystem.OpenClassDialogForBoundGlyphstone(targetClasses);
                return;
            }

            if (byEntity?.World?.Side != EnumAppSide.Server) return;
            if (targetClasses.Count > 0) return;
            if (byEntity is not EntityPlayer entityPlayer) return;

            IServerPlayer player = byEntity.World.PlayerByUid(entityPlayer.PlayerUID) as IServerPlayer;
            if (player == null) return;

            if (!classSystem.TryGrantClassSlot(player)) return;

            // Consume only after the class system accepts the slot grant.
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        // Shows the right-click help text in the item interaction overlay.
        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            string actionLangCode = GetTargetClasses(inSlot?.Itemstack).Count == 0
                ? "multiclassreborn:heldhelp-addslot"
                : "multiclassreborn:heldhelp-boundglyphactivate";

            return new WorldInteraction[]
            {
                new WorldInteraction()
                {
                    ActionLangCode = actionLangCode,
                    MouseButton = EnumMouseButton.Right
                }
            };
        }

        // Preserves the older single-target call sites while the JSON supports several.
        internal static string GetTargetClass(ItemStack stack)
        {
            return GetTargetClasses(stack).FirstOrDefault();
        }

        // Reads the bound target list from item JSON and keeps duplicate entries harmless.
        internal static List<string> GetTargetClasses(ItemStack stack)
        {
            JToken attributes = stack?.Collectible?.Attributes?.Token?.SelectToken(AttributeTreeKey);
            List<string> targetClasses = new List<string>();

            AddTargetClass(targetClasses, attributes?.SelectToken(TargetClassKey)?.Value<string>());

            if (attributes?.SelectToken(TargetClassesKey) is JArray targetArray)
            {
                foreach (JToken token in targetArray)
                {
                    AddTargetClass(targetClasses, token?.Value<string>());
                }
            }

            return targetClasses.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Lets content items reserve their target classes from generic learning.
        internal static bool LocksTargetClass(ItemStack stack)
        {
            JToken attributes = stack?.Collectible?.Attributes?.Token?.SelectToken(AttributeTreeKey);
            return attributes?.SelectToken(LocksTargetClassKey)?.Value<bool>() == true;
        }

        // Ignores blank JSON entries and normalizes the rest for ledger lookups.
        private static void AddTargetClass(List<string> targetClasses, string targetClass)
        {
            if (string.IsNullOrWhiteSpace(targetClass)) return;

            targetClasses.Add(targetClass.Trim().ToLowerInvariant());
        }
    }
}

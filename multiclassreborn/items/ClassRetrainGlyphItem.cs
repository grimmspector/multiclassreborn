using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

#nullable disable

namespace multiclassreborn.items
{
    // Right-click glyphstone that opens the retraining flow on the client.
    internal class ClassRetrainGlyphItem : Item
    {
        // Actual consumption happens later through the confirmed server command.
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);

            if (!firstEvent) return;
            if (byEntity?.Controls?.Sneak == true) return;

            handHandling = EnumHandHandling.PreventDefault;

            if (byEntity?.World?.Side != EnumAppSide.Client) return;

            MulticlassRebornModSystem classSystem = api.ModLoader.GetModSystem<MulticlassRebornModSystem>();
            classSystem?.OpenClassDialogForRetraining();
        }

        // Shows the right-click help text in the item interaction overlay.
        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction()
                {
                    ActionLangCode = "multiclassreborn:heldhelp-retrainglyphactivate",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }
    }
}

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

#nullable disable

namespace multiclassreborn.items
{
    internal class ClassRetrainGlyphItem : Item
    {
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

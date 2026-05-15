using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

#nullable disable

namespace multiclassreborn.items
{
    internal class ClassSlotGlyphItem : Item
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);

            if (!firstEvent) return;
            if (byEntity?.Controls?.Sneak == true) return;

            handHandling = EnumHandHandling.PreventDefault;

            MulticlassRebornModSystem classSystem = api.ModLoader.GetModSystem<MulticlassRebornModSystem>();

            if (byEntity?.World?.Side == EnumAppSide.Client)
            {
                classSystem.OpenClassDialogForLearning();
                return;
            }

            if (byEntity?.World?.Side != EnumAppSide.Server) return;
            if (byEntity is not EntityPlayer entityPlayer) return;

            IServerPlayer player = byEntity.World.PlayerByUid(entityPlayer.PlayerUID) as IServerPlayer;
            if (player == null) return;

            if (!classSystem.TryGrantClassSlot(player)) return;

            // Consume only after the class system accepts the slot grant.
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction()
                {
                    ActionLangCode = "multiclassreborn:heldhelp-addslot",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }
    }
}

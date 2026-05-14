using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

#nullable disable

namespace multiclassreborn.items
{
    internal class ClassSlotRuneItem : Item
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);

            if (!firstEvent) return;

            handHandling = EnumHandHandling.PreventDefault;

            if (byEntity?.World?.Side != EnumAppSide.Server) return;
            if (byEntity is not EntityPlayer entityPlayer) return;

            IServerPlayer player = byEntity.World.PlayerByUid(entityPlayer.PlayerUID) as IServerPlayer;
            if (player == null) return;

            MulticlassRebornModSystem classSystem = api.ModLoader.GetModSystem<MulticlassRebornModSystem>();
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
                    ActionLangCode = "multiclass:heldhelp-addslot",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }
    }
}

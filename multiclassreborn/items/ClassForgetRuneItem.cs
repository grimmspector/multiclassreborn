using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

#nullable disable

namespace multiclassreborn.items
{
    internal class ClassForgetRuneItem : Item
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
            if (!classSystem.TryGrantForgetCredit(player)) return;

            // Consume only after the forget credit is written to the player.
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction()
                {
                    ActionLangCode = "multiclass:heldhelp-addunlearnslot",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }
    }
}

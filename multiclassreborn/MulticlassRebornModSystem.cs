using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using multiclassreborn.items;
using multiclassreborn.systems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

#nullable disable

namespace multiclassreborn
{
    public class MulticlassRebornModSystem : ModSystem
    {
        private const string ExtraTraitsAttribute = "extraTraits";
        private const string AptitudeGlyphItemCode = "multiclassreborn:aptitude-glyphstone";
        private const string RetrainGlyphItemCode = "multiclassreborn:retraining-glyphstone";
        private const string HandbookPageCode = "gamemechanicinfo-multiclassreborn";
        private const string CommonerClassCode = "commoner";

        private ICoreAPI api;
        private ICoreServerAPI sapi;
        private ICoreClientAPI capi;
        private ClassPickerDialog classDialog;
        private Harmony clientHarmony;
        private ModSystemSurvivalHandbook survivalHandbook;

        internal RebornClassConfig Config { get; private set; }
        internal ICoreClientAPI ClientApi => capi;
        internal static MulticlassRebornModSystem ClientInstance { get; private set; }
        internal ClassLedger Ledger { get; } = new ClassLedger();

        public override double ExecuteOrder()
        {
            return 2;
        }

        // Called on server and client.
        // Registers item class aliases used by the JSON assets.
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            this.api = api;

            api.RegisterItemClass("AptitudeGlyphstone", typeof(ClassSlotGlyphItem));
            api.RegisterItemClass("RetrainingGlyphstone", typeof(ClassRetrainGlyphItem));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            Config = RebornClassConfig.Load(api);
            Ledger.Reload(api);

            // Sync after character setup so first-join class selection cannot overwrite it.
            sapi.Event.PlayerNowPlaying += PreparePlayerState;
            RegisterClassCommands();
            RegisterGlyphstoneRecipes();

            sapi.Logger.Notification("[Multiclass Reborn] Loaded {0} class definitions. Stats={1}, Recipes={2}, GlyphstoneRecipes={3}, Scale={4:P0}, MaxSlots={5}, RequireGlyphs={6}, StartingAptitudeTokens={7}, BestPositiveOnly={8}, WorstNegativeOnly={9}",
                Ledger.EnabledClasses.Count,
                Config.AllowStatBonuses,
                Config.AllowRecipeTraits,
                Config.EnableGlyphstoneRecipes,
                Config.ExtraClassScale,
                Config.MaxExtraClasses,
                Config.RequireGlyphs,
                Config.StartingAptitudeTokens,
                Config.OnlyApplyBestPositiveTraitBonus,
                Config.OnlyApplyWorstNegativeTraitPenalty);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            ClientInstance = this;
            Ledger.Reload(api);
            Config = new RebornClassConfig();

            clientHarmony = new Harmony("multiclassreborn.client");
            clientHarmony.PatchAll();

            survivalHandbook = capi.ModLoader.GetModSystem<ModSystemSurvivalHandbook>();
            if (survivalHandbook != null)
            {
                survivalHandbook.OnInitCustomPages += MoveGuideAfterVanillaGuides;
            }

            classDialog = new ClassPickerDialog(capi, this);
            capi.Input.RegisterHotKey("multiclassgui", Lang.Get("multiclassreborn:hotkey-open-class-selection"), GlKeys.K, HotkeyType.GUIOrOtherControls, false, true, false);
            capi.Input.SetHotKeyHandler("multiclassgui", _ =>
            {
                classDialog.Toggle();
                return true;
            });
        }

        /// <summary>
        /// Removes client patches when the mod system shuts down.
        /// </summary>
        public override void Dispose()
        {
            if (survivalHandbook != null)
            {
                survivalHandbook.OnInitCustomPages -= MoveGuideAfterVanillaGuides;
            }

            clientHarmony?.UnpatchAll("multiclassreborn.client");
            if (ClientInstance == this) ClientInstance = null;

            base.Dispose();
        }

        /// <summary>
        /// Places our guide after vanilla game mechanic pages and before tutorials.
        /// </summary>
        private static void MoveGuideAfterVanillaGuides(List<GuiHandbookPage> pages)
        {
            int guideIndex = pages.FindIndex(page => page.PageCode == HandbookPageCode);
            if (guideIndex < 0) return;

            GuiHandbookPage guidePage = pages[guideIndex];
            pages.RemoveAt(guideIndex);

            int insertIndex = pages.FindLastIndex(IsVanillaGameMechanicPage) + 1;
            pages.Insert(insertIndex, guidePage);
        }

        /// <summary>
        /// Detects vanilla game mechanic pages by their handbook page code.
        /// </summary>
        private static bool IsVanillaGameMechanicPage(GuiHandbookPage page)
        {
            return page.PageCode.StartsWith("gamemechanicinfo-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds the /multiclass command surface used by both chat and the GUI.
        /// </summary>
        private void RegisterClassCommands()
        {
            CommandArgumentParsers parsers = sapi.ChatCommands.Parsers;

            sapi.ChatCommands.Create("multiclass")
                .WithDescription(Lang.Get("multiclassreborn:command-multiclass-description"))
                .RequiresPlayer()
                .RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("add")
                    .WithArgs(parsers.Word("classcode"))
                    .WithDescription(Lang.Get("multiclassreborn:command-add-description"))
                    .HandleWith(args => RunPlayerCommand(args, player => LearnExtraClass(player, (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("remove")
                    .WithArgs(parsers.Word("classcode"))
                    .WithDescription(Lang.Get("multiclassreborn:command-remove-description"))
                    .HandleWith(args => RunPlayerCommand(args, RejectUnconfirmedForget))
                .EndSubCommand()
                .BeginSubCommand("confirmforget")
                    .WithArgs(parsers.Word("classcode"))
                    .WithDescription(Lang.Get("multiclassreborn:command-confirmforget-description"))
                    .HandleWith(args => RunPlayerCommand(args, player => ForgetClassAfterConfirmation(player, (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("setbase")
                    .WithArgs(parsers.Word("classcode"))
                    .WithDescription(Lang.Get("multiclassreborn:command-setbase-description"))
                    .HandleWith(args => RunPlayerCommand(args, player => ChooseBaseClass(player, (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("list")
                    .WithDescription(Lang.Get("multiclassreborn:command-list-description"))
                    .HandleWith(args => RunPlayerCommand(args, ShowExtraClassList))
                .EndSubCommand()
                .BeginSubCommand("available")
                    .WithDescription(Lang.Get("multiclassreborn:command-available-description"))
                    .HandleWith(args => RunPlayerCommand(args, ShowAvailableClasses))
                .EndSubCommand()
                .BeginSubCommand("info")
                    .WithArgs(parsers.Word("classcode"))
                    .WithDescription(Lang.Get("multiclassreborn:command-info-description"))
                    .HandleWith(args => RunPlayerCommand(args, player => ShowClassDetails(player, (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("summary")
                    .WithDescription(Lang.Get("multiclassreborn:command-summary-description"))
                    .HandleWith(args => RunPlayerCommand(args, ShowClassSummary))
                .EndSubCommand()
                .BeginSubCommand("clear")
                    .WithDescription(Lang.Get("multiclassreborn:command-clear-description"))
                    .HandleWith(args => RunPlayerCommand(args, ClearExtraClasses))
                .EndSubCommand()
                .BeginSubCommand("giveglyph")
                    .WithArgs(parsers.Word("playername"))
                    .WithDescription(Lang.Get("multiclassreborn:command-giveaptitudeglyph-description"))
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => RunPlayerCommand(args, player => GiveAptitudeGlyphItem(player, (string)args[0], AptitudeGlyphItemCode, "item-aptitude-glyphstone")))
                .EndSubCommand()
                .BeginSubCommand("giveretrainglyph")
                    .WithArgs(parsers.Word("playername"))
                    .WithDescription(Lang.Get("multiclassreborn:command-giveretrainglyph-description"))
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => RunPlayerCommand(args, player => GiveAptitudeGlyphItem(player, (string)args[0], RetrainGlyphItemCode, "item-retraining-glyphstone")))
                .EndSubCommand();
        }

        /// <summary>
        /// Registers optional glyphstone crafting recipes when enabled.
        /// </summary>
        private void RegisterGlyphstoneRecipes()
        {
            if (!Config.EnableGlyphstoneRecipes) return;

            RegisterGlyphstoneRecipe("craft-aptitude-glyphstone", AptitudeGlyphItemCode, "clearquartz");
            RegisterGlyphstoneRecipe("craft-retraining-glyphstone", RetrainGlyphItemCode, "gear-rusty");
        }

        /// <summary>
        /// Registers one shaped glyphstone recipe with a role item.
        /// </summary>
        private void RegisterGlyphstoneRecipe(string recipeName, string outputCode, string roleItemCode)
        {
            GridRecipe recipe = new GridRecipe()
            {
                Name = new AssetLocation("multiclassreborn", recipeName),
                Enabled = true,
                Shapeless = false,
                IngredientPattern = "ATB,CDE,FRH",
                Width = 3,
                Height = 3,
                Ingredients = new Dictionary<string, CraftingRecipeIngredient>()
                {
                    { "A", StoneIngredient() },
                    { "B", StoneIngredient() },
                    { "C", MetalBitIngredient() },
                    { "D", ItemIngredient("gem-diamond-rough") },
                    { "E", MetalBitIngredient() },
                    { "F", StoneIngredient() },
                    { "H", StoneIngredient() },
                    { "R", ItemIngredient(roleItemCode) },
                    { "T", ItemIngredient("gear-temporal") }
                },
                Output = ItemIngredient(outputCode)
            };

            if (!recipe.Resolve(sapi.World, recipe.Name.ToString()))
            {
                sapi.Logger.Warning("[Multiclass Reborn] Could not resolve glyphstone recipe {0}.", recipe.Name);
                return;
            }

            sapi.RegisterCraftingRecipe(recipe);
        }

        /// <summary>
        /// Builds a wildcard ingredient for any vanilla-category stone piece.
        /// </summary>
        private CraftingRecipeIngredient StoneIngredient()
        {
            return ItemIngredient("stone-*");
        }

        /// <summary>
        /// Builds a wildcard ingredient for gold or silver metal bits.
        /// </summary>
        private CraftingRecipeIngredient MetalBitIngredient()
        {
            CraftingRecipeIngredient ingredient = ItemIngredient("metalbit-*");
            ingredient.AllowedVariants = new[] { "gold", "silver" };

            return ingredient;
        }

        /// <summary>
        /// Builds one item ingredient for programmatic grid recipes.
        /// </summary>
        private CraftingRecipeIngredient ItemIngredient(string code)
        {
            return new CraftingRecipeIngredient()
            {
                Type = EnumItemClass.Item,
                Code = new AssetLocation(code),
                Quantity = 1
            };
        }

        private TextCommandResult RunPlayerCommand(TextCommandCallingArgs args, Action<IServerPlayer> action)
        {
            if (args.Caller.Player is not IServerPlayer player)
            {
                return TextCommandResult.Error(Lang.Get("multiclassreborn:message-must-be-player"), "");
            }

            action(player);
            return TextCommandResult.Success("", null);
        }

        internal void OpenClassDialogForRetraining()
        {
            classDialog?.OpenForRetraining();
        }

        internal void OpenClassDialogForLearning()
        {
            classDialog?.OpenForLearning();
        }

        private void PreparePlayerState(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);

            // Always sync the current token rule. The old implementation only
            // wrote true, which could leave clients stuck after config changes.
            state.RequiresGlyphs = Config.RequireGlyphs;
            state.AllowsBaseClassForgetting = Config.AllowForgettingBaseClass;
            state.AllowsCommonerBaseClassChoice = Config.AllowCommonersChooseBaseClass;
            state.ExtraClassScale = Config.ExtraClassScale;
            state.OnlyApplyBestPositiveTraitBonus = Config.OnlyApplyBestPositiveTraitBonus;
            state.OnlyApplyWorstNegativeTraitPenalty = Config.OnlyApplyWorstNegativeTraitPenalty;

            if (!Config.RequireGlyphs)
            {
                // Free-slot servers should give every player the configured
                // class capacity, including players migrated from rune servers.
                state.AvailableSlots = Config.MaxExtraClasses;
                state.SlotsInitialized = true;
            }
            else if (!state.SlotsInitialized)
            {
                GrantStartingAptitudeTokens(player);
                state.SlotsInitialized = true;
            }

            RecountUsedSlots(state);
            MigrateLegacyGlyphItems(player);
            ReapplyClassEffects(player);
        }

        /// <summary>
        /// Grants first-join Aptitude Glyphstones only on token-gated servers.
        /// </summary>
        private void GrantStartingAptitudeTokens(IServerPlayer player)
        {
            if (!Config.RequireGlyphs || Config.StartingAptitudeTokens <= 0) return;

            Item item = sapi.World.GetItem(new AssetLocation(AptitudeGlyphItemCode));
            if (item == null)
            {
                sapi.Logger.Warning("[Multiclass Reborn] Could not resolve starting token item {0}.", AptitudeGlyphItemCode);
                return;
            }

            ItemStack stack = new ItemStack(item, Config.StartingAptitudeTokens);
            if (player.InventoryManager.TryGiveItemstack(stack)) return;
            if (stack.StackSize <= 0) return;

            Vec3d dropPos = player.Entity.Pos.XYZ.Add(0, 0.25, 0);
            sapi.World.SpawnItemEntity(stack, dropPos);
        }

        internal bool TryGrantClassSlot(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);

            if (state.AvailableSlots >= Config.MaxExtraClasses)
            {
                Tell(player, Lang.Get("multiclassreborn:message-max-class-slots", Config.MaxExtraClasses), EnumChatType.Notification);
                return false;
            }

            state.AvailableSlots++;
            Tell(player, Lang.Get("multiclassreborn:message-aptitude-consumed", state.AvailableSlots, state.UsedSlots), EnumChatType.Notification);

            return true;
        }

        internal void LearnExtraClass(IServerPlayer player, string classCode)
        {
            string normalizedCode = NormalizeClassCode(classCode);
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);

            if (!Ledger.ClassByCode.ContainsKey(normalizedCode))
            {
                Tell(player, Lang.Get("multiclassreborn:message-class-does-not-exist", normalizedCode), EnumChatType.Notification);
                return;
            }

            if (GetMainClassCode(player.Entity).Equals(normalizedCode, StringComparison.OrdinalIgnoreCase))
            {
                Tell(player, Lang.Get("multiclassreborn:message-already-main-class", normalizedCode), EnumChatType.Notification);
                return;
            }

            List<string> extraClasses = state.ExtraClasses;
            if (extraClasses.Contains(normalizedCode))
            {
                Tell(player, Lang.Get("multiclassreborn:message-already-have-class", normalizedCode), EnumChatType.Notification);
                return;
            }

            if (state.UsedSlots >= state.AvailableSlots)
            {
                Tell(player, Lang.Get("multiclassreborn:message-no-class-slots", state.UsedSlots, state.AvailableSlots), EnumChatType.Notification);
                return;
            }

            if (extraClasses.Count >= Config.MaxExtraClasses)
            {
                Tell(player, Lang.Get("multiclassreborn:message-cannot-add-more-classes", Config.MaxExtraClasses), EnumChatType.Notification);
                return;
            }

            extraClasses.Add(normalizedCode);
            state.ExtraClasses = extraClasses;
            state.UsedSlots = extraClasses.Count;

            ReapplyClassEffects(player);
            Tell(player, Lang.Get("multiclassreborn:message-added-extra-class", normalizedCode, state.UsedSlots, state.AvailableSlots), EnumChatType.Notification);
        }

        internal void ForgetExtraClass(IServerPlayer player, string classCode)
        {
            ForgetExtraClass(player, classCode, false);
        }

        private void RejectUnconfirmedForget(IServerPlayer player)
        {
            Tell(player, Lang.Get("multiclassreborn:message-open-retraining-confirm"), EnumChatType.Notification);
        }

        internal void ForgetClassAfterConfirmation(IServerPlayer player, string classCode)
        {
            string normalizedCode = NormalizeClassCode(classCode);
            string mainClassCode = GetMainClassCode(player.Entity);

            if (mainClassCode.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase))
            {
                ForgetBaseClassAfterConfirmation(player, normalizedCode);
                return;
            }

            ForgetExtraClass(player, normalizedCode, Config.RequireGlyphs);
        }

        private void ForgetExtraClass(IServerPlayer player, string classCode, bool consumeGlyphstone)
        {
            string normalizedCode = NormalizeClassCode(classCode);
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);

            if (consumeGlyphstone && !TryConsumeRetrainingGlyphstone(player))
            {
                Tell(player, Lang.Get("multiclassreborn:message-need-retraining-extra"), EnumChatType.Notification);
                return;
            }

            List<string> extraClasses = state.ExtraClasses;
            if (!extraClasses.Remove(normalizedCode))
            {
                Tell(player, Lang.Get("multiclassreborn:message-extra-class-not-found", normalizedCode), EnumChatType.Notification);
                return;
            }

            state.ExtraClasses = extraClasses;
            state.UsedSlots = extraClasses.Count;

            ReapplyClassEffects(player);
            Tell(player, Lang.Get("multiclassreborn:message-removed-extra-class", normalizedCode, state.UsedSlots, state.AvailableSlots), EnumChatType.Notification);
        }

        private void ForgetBaseClassAfterConfirmation(IServerPlayer player, string classCode)
        {
            if (!Config.AllowForgettingBaseClass)
            {
                Tell(player, Lang.Get("multiclassreborn:message-base-forgetting-disabled"), EnumChatType.Notification);
                return;
            }

            if (NormalizeClassCode(classCode).Equals(CommonerClassCode, StringComparison.OrdinalIgnoreCase))
            {
                Tell(player, Lang.Get("multiclassreborn:message-already-commoner"), EnumChatType.Notification);
                return;
            }

            if (Config.RequireGlyphs && !TryConsumeRetrainingGlyphstone(player))
            {
                Tell(player, Lang.Get("multiclassreborn:message-need-retraining-base"), EnumChatType.Notification);
                return;
            }

            player.Entity.WatchedAttributes.SetString("characterClass", CommonerClassCode);
            player.Entity.WatchedAttributes.MarkPathDirty("characterClass");
            ReapplyClassEffects(player);
            Tell(player, Lang.Get("multiclassreborn:message-forgot-base-class"), EnumChatType.Notification);
        }

        private void ChooseBaseClass(IServerPlayer player, string classCode)
        {
            string normalizedCode = NormalizeClassCode(classCode);

            if (!Config.AllowCommonersChooseBaseClass)
            {
                Tell(player, Lang.Get("multiclassreborn:message-commoner-base-choice-disabled"), EnumChatType.Notification);
                return;
            }

            if (!GetMainClassCode(player.Entity).Equals(CommonerClassCode, StringComparison.OrdinalIgnoreCase))
            {
                Tell(player, Lang.Get("multiclassreborn:message-only-commoners-choose-base"), EnumChatType.Notification);
                return;
            }

            if (!Ledger.ClassByCode.ContainsKey(normalizedCode) || normalizedCode.Equals(CommonerClassCode, StringComparison.OrdinalIgnoreCase))
            {
                Tell(player, Lang.Get("multiclassreborn:message-class-cannot-be-base", normalizedCode), EnumChatType.Notification);
                return;
            }

            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            List<string> extraClasses = state.ExtraClasses;
            if (extraClasses.Remove(normalizedCode))
            {
                state.ExtraClasses = extraClasses;
                state.UsedSlots = extraClasses.Count;
            }

            player.Entity.WatchedAttributes.SetString("characterClass", normalizedCode);
            player.Entity.WatchedAttributes.MarkPathDirty("characterClass");
            ReapplyClassEffects(player);
            Tell(player, Lang.Get("multiclassreborn:message-set-base-class", normalizedCode), EnumChatType.Notification);
        }

        private bool TryConsumeRetrainingGlyphstone(IServerPlayer player)
        {
            ItemSlot activeSlot = player.InventoryManager.ActiveHotbarSlot;
            if (TryConsumeFromSlot(activeSlot, RetrainGlyphItemCode)) return true;

            foreach (IInventory inventory in player.InventoryManager.InventoriesOrdered)
            {
                if (!IsHotbarInventory(inventory)) continue;
                if (!TryGetInventoryCount(inventory, out int slotCount)) continue;

                for (int slotId = 0; slotId < slotCount; slotId++)
                {
                    if (TryConsumeFromSlot(inventory[slotId], RetrainGlyphItemCode)) return true;
                }
            }

            return false;
        }

        private bool TryConsumeFromSlot(ItemSlot slot, string itemCode)
        {
            if (slot == null || slot.Empty) return false;
            if (!slot.Itemstack.Collectible.Code.Equals(new AssetLocation(itemCode))) return false;

            slot.TakeOut(1);
            slot.MarkDirty();
            return true;
        }

        private bool IsHotbarInventory(IInventory inventory)
        {
            string inventoryId = inventory?.InventoryID ?? "";
            string className = inventory?.ClassName ?? "";

            return inventoryId.IndexOf("hotbar", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("hotbar", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void MigrateLegacyGlyphItems(IServerPlayer player)
        {
            foreach (IInventory inventory in player.InventoryManager.InventoriesOrdered)
            {
                if (!ShouldMigrateInventory(inventory)) continue;
                if (!TryGetInventoryCount(inventory, out int slotCount)) continue;

                for (int slotId = 0; slotId < slotCount; slotId++)
                {
                    MigrateLegacyGlyphSlot(inventory[slotId]);
                }
            }
        }

        private bool ShouldMigrateInventory(IInventory inventory)
        {
            if (inventory == null) return false;

            string inventoryId = inventory.InventoryID ?? "";
            string className = inventory.ClassName ?? "";

            if (className.IndexOf("creative", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (inventoryId.IndexOf("creative", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            return inventoryId.IndexOf("hotbar", StringComparison.OrdinalIgnoreCase) >= 0
                || inventoryId.IndexOf("backpack", StringComparison.OrdinalIgnoreCase) >= 0
                || inventoryId.IndexOf("character", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryGetInventoryCount(IInventory inventory, out int slotCount)
        {
            slotCount = 0;

            try
            {
                slotCount = inventory?.Count ?? 0;
                return slotCount > 0;
            }
            catch (Exception exception)
            {
                sapi.Logger.VerboseDebug("[Multiclass Reborn] Skipping inventory migration for {0}: {1}",
                    inventory?.InventoryID ?? "(unknown)",
                    exception.Message);
                return false;
            }
        }

        private void MigrateLegacyGlyphSlot(ItemSlot slot)
        {
            if (slot == null || slot.Empty) return;

            string oldCode = slot.Itemstack.Collectible.Code.ToString();
            string newCode = oldCode switch
            {
                "multiclass:classrune" => AptitudeGlyphItemCode,
                "multiclass:classglyphstone" => AptitudeGlyphItemCode,
                "multiclassreborn:classrune" => AptitudeGlyphItemCode,
                "multiclassreborn:classglyphstone" => AptitudeGlyphItemCode,
                "multiclass:unlearnrune" => RetrainGlyphItemCode,
                "multiclass:forgetglyphstone" => RetrainGlyphItemCode,
                "multiclassreborn:unlearnrune" => RetrainGlyphItemCode,
                "multiclassreborn:forgetglyphstone" => RetrainGlyphItemCode,
                _ => null
            };

            if (newCode == null) return;

            Item replacementItem = sapi.World.GetItem(new AssetLocation(newCode));
            if (replacementItem == null) return;

            ItemStack replacementStack = new ItemStack(replacementItem, slot.Itemstack.StackSize);
            replacementStack.Attributes = slot.Itemstack.Attributes?.Clone();
            slot.Itemstack = replacementStack;
            slot.MarkDirty();
        }

        private void ClearExtraClasses(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            state.ExtraClasses = new List<string>();
            state.UsedSlots = 0;

            ClearRebornStats(player);
            Tell(player, Lang.Get("multiclassreborn:message-extra-classes-cleared"), EnumChatType.Notification);
        }

        private void ShowExtraClassList(IServerPlayer player)
        {
            List<string> extraClasses = new RebornPlayerClassState(player.Entity).ExtraClasses;
            Tell(player, extraClasses.Count == 0
                ? Lang.Get("multiclassreborn:message-no-extra-classes")
                : Lang.Get("multiclassreborn:message-extra-classes", string.Join(", ", extraClasses)), EnumChatType.Notification);
        }

        private void ShowAvailableClasses(IServerPlayer player)
        {
            List<string> classCodes = Ledger.EnabledClasses.Select(classDef => classDef.Code).OrderBy(code => code).ToList();
            Tell(player, classCodes.Count == 0
                ? Lang.Get("multiclassreborn:message-no-class-definitions")
                : Lang.Get("multiclassreborn:message-available-classes", classCodes.Count, string.Join(", ", classCodes)), EnumChatType.Notification);
        }

        private void ShowClassSummary(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            List<string> extraClasses = state.ExtraClasses;
            string message = Lang.Get("multiclassreborn:message-class-summary", state.UsedSlots, state.AvailableSlots, GetMainClassCode(player.Entity));

            if (extraClasses.Count > 0)
            {
                message += Lang.Get("multiclassreborn:message-class-summary-extra", string.Join(", ", extraClasses));
            }

            Tell(player, message, EnumChatType.Notification);
        }

        private void ShowClassDetails(IServerPlayer player, string classCode)
        {
            string normalizedCode = NormalizeClassCode(classCode);

            if (!Ledger.ClassByCode.TryGetValue(normalizedCode, out CharacterClass classDef))
            {
                Tell(player, Lang.Get("multiclassreborn:message-class-not-found", normalizedCode), EnumChatType.Notification);
                return;
            }

            StringBuilder text = new StringBuilder();
            text.AppendLine(Lang.Get("multiclassreborn:message-class-details-header", normalizedCode));

            if (classDef.Traits == null || classDef.Traits.Length == 0)
            {
                text.AppendLine(Lang.Get("multiclassreborn:message-traits-none"));
            }
            else
            {
                text.AppendLine(Lang.Get("multiclassreborn:message-traits-count", classDef.Traits.Length));

                foreach (string traitCode in classDef.Traits)
                {
                    text.AppendLine($"  - {traitCode}");

                    if (!Ledger.TraitByCode.TryGetValue(traitCode, out Trait trait) || trait.Attributes == null) continue;

                    foreach (KeyValuePair<string, double> stat in trait.Attributes)
                    {
                        text.AppendLine($"    {stat.Key}: {stat.Value:0.###}x");
                    }
                }
            }

            Tell(player, text.ToString(), EnumChatType.Notification);
        }

        private void GiveAptitudeGlyphItem(IServerPlayer admin, string playerName, string itemCode, string displayNameKey)
        {
            IServerPlayer target = sapi.World.AllOnlinePlayers
                .OfType<IServerPlayer>()
                .FirstOrDefault(player => player.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                Tell(admin, Lang.Get("multiclassreborn:message-player-not-found", playerName), EnumChatType.Notification);
                return;
            }

            Item item = sapi.World.GetItem(new AssetLocation(itemCode));
            if (item == null)
            {
                Tell(admin, Lang.Get("multiclassreborn:message-could-not-resolve-item", itemCode), EnumChatType.Notification);
                return;
            }

            ItemStack stack = new ItemStack(item, 1);
            if (!target.InventoryManager.TryGiveItemstack(stack))
            {
                Tell(admin, Lang.Get("multiclassreborn:message-could-not-give-item", Lang.Get(displayNameKey), target.PlayerName), EnumChatType.Notification);
                return;
            }

            Tell(admin, Lang.Get("multiclassreborn:message-gave-item", Lang.Get(displayNameKey), target.PlayerName), EnumChatType.Notification);
            Tell(target, Lang.Get("multiclassreborn:message-received-item", Lang.Get(displayNameKey)), EnumChatType.Notification);
        }

        internal void ReapplyClassEffects(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            HashSet<string> traitCodes = GatherExtraTraitCodes(state.ExtraClasses);

            ClearRebornStats(player);
            WriteRecipeTraits(player.Entity, traitCodes);

            if (Config.AllowStatBonuses)
            {
                ApplyScaledStats(player, traitCodes);
            }

            // Stat changes propagate through the entity stat collection. Health
            // recalculation is intentionally left to the game behavior layer.
        }

        private HashSet<string> GatherExtraTraitCodes(List<string> extraClasses)
        {
            HashSet<string> traitCodes = new HashSet<string>();

            foreach (string classCode in extraClasses)
            {
                if (!Ledger.ClassByCode.TryGetValue(classCode, out CharacterClass classDef)) continue;
                if (classDef.Traits == null) continue;

                foreach (string traitCode in classDef.Traits)
                {
                    traitCodes.Add(traitCode);
                }
            }

            return traitCodes;
        }

        private void ApplyScaledStats(IServerPlayer player, HashSet<string> traitCodes)
        {
            List<TraitStatCandidate> candidates = GatherTraitStatCandidates(traitCodes);
            IEnumerable<TraitStatCandidate> appliedCandidates = ChooseConfiguredTraitStats(candidates);

            foreach (TraitStatCandidate candidate in appliedCandidates)
            {
                float scaledValue = (float)candidate.RawValue * Config.ExtraClassScale;
                player.Entity.Stats.Set(candidate.StatCode, BuildCanonicalStatSourceCode(candidate.TraitCode), scaledValue, false);
            }
        }

        /// <summary>
        /// Flattens extra-class traits into individual stat candidates.
        /// </summary>
        private List<TraitStatCandidate> GatherTraitStatCandidates(HashSet<string> traitCodes)
        {
            List<TraitStatCandidate> candidates = new List<TraitStatCandidate>();

            foreach (string traitCode in traitCodes)
            {
                if (!Ledger.TraitByCode.TryGetValue(traitCode, out Trait trait)) continue;
                if (trait.Attributes == null) continue;

                foreach (KeyValuePair<string, double> stat in trait.Attributes)
                {
                    candidates.Add(new TraitStatCandidate(traitCode, stat.Key, stat.Value, trait.Type));
                }
            }

            return candidates;
        }

        /// <summary>
        /// Applies configured duplicate handling separately by trait polarity.
        /// </summary>
        private IEnumerable<TraitStatCandidate> ChooseConfiguredTraitStats(List<TraitStatCandidate> candidates)
        {
            foreach (var group in candidates.GroupBy(candidate => new { candidate.StatCode, candidate.TraitType }))
            {
                if (ShouldKeepOnlyStrongest(group.Key.TraitType))
                {
                    yield return ChooseStrongestTraitStat(group);
                    continue;
                }

                foreach (TraitStatCandidate candidate in group)
                {
                    yield return candidate;
                }
            }
        }

        /// <summary>
        /// Tests whether this trait polarity should suppress weaker duplicates.
        /// </summary>
        private bool ShouldKeepOnlyStrongest(EnumTraitType traitType)
        {
            if (traitType == EnumTraitType.Positive) return Config.OnlyApplyBestPositiveTraitBonus;
            if (traitType == EnumTraitType.Negative) return Config.OnlyApplyWorstNegativeTraitPenalty;

            return false;
        }

        /// <summary>
        /// Chooses the largest absolute stat change from one duplicate group.
        /// </summary>
        private TraitStatCandidate ChooseStrongestTraitStat(IEnumerable<TraitStatCandidate> candidates)
        {
            return candidates
                .OrderByDescending(candidate => Math.Abs(candidate.RawValue))
                .First();
        }

        private void ClearRebornStats(IServerPlayer player)
        {
            foreach (Trait trait in Ledger.TraitByCode.Values)
            {
                if (trait.Attributes == null) continue;

                foreach (string statCode in trait.Attributes.Keys)
                {
                    foreach (string sourceCode in BuildStatSourceCodes(trait.Code))
                    {
                        player.Entity.Stats.Remove(statCode, sourceCode);
                    }
                }
            }

            RemoveRecipeTraits(player.Entity);
        }

        private void WriteRecipeTraits(EntityPlayer entity, HashSet<string> traitCodes)
        {
            if (!Config.AllowRecipeTraits || traitCodes.Count == 0)
            {
                RemoveRecipeTraits(entity);
                return;
            }

            entity.WatchedAttributes.SetStringArray(ExtraTraitsAttribute, traitCodes.ToArray());
            entity.WatchedAttributes.MarkPathDirty(ExtraTraitsAttribute);
        }

        private void RemoveRecipeTraits(EntityPlayer entity)
        {
            entity.WatchedAttributes.RemoveAttribute(ExtraTraitsAttribute);
            entity.WatchedAttributes.MarkPathDirty(ExtraTraitsAttribute);
        }

        private void RecountUsedSlots(RebornPlayerClassState state)
        {
            List<string> cleanClasses = state.ExtraClasses
                .Where(classCode => Ledger.ClassByCode.ContainsKey(classCode))
                .Distinct()
                .Take(Config.MaxExtraClasses)
                .ToList();

            state.ExtraClasses = cleanClasses;
            state.UsedSlots = cleanClasses.Count;
        }

        private string NormalizeClassCode(string classCode)
        {
            return (classCode ?? "").Trim().ToLowerInvariant();
        }

        private string GetMainClassCode(EntityPlayer entity)
        {
            return entity.WatchedAttributes.GetString("characterClass", "none");
        }

        /// <summary>
        /// Uses the original mod stat source so migrated worlds overwrite old values.
        /// </summary>
        private string BuildCanonicalStatSourceCode(string traitCode)
        {
            return $"multiclass_{traitCode}";
        }

        /// <summary>
        /// Includes the early Reborn source so 0.1.0 test worlds are cleaned up.
        /// </summary>
        private IEnumerable<string> BuildStatSourceCodes(string traitCode)
        {
            yield return BuildCanonicalStatSourceCode(traitCode);
            yield return $"multiclassreborn_{traitCode}";
        }

        /// <summary>
        /// Carries one trait stat value before duplicate handling is applied.
        /// </summary>
        private sealed class TraitStatCandidate
        {
            public readonly string TraitCode;
            public readonly string StatCode;
            public readonly double RawValue;
            public readonly EnumTraitType TraitType;

            public TraitStatCandidate(string traitCode, string statCode, double rawValue, EnumTraitType traitType)
            {
                TraitCode = traitCode;
                StatCode = statCode;
                RawValue = rawValue;
                TraitType = traitType;
            }
        }

        private void Tell(IServerPlayer player, string message, EnumChatType chatType)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup, message, chatType, null);
        }
    }
}

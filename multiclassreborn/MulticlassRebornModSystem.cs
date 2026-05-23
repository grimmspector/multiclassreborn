using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using multiclassreborn.items;
using multiclassreborn.systems;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    // Main entry point for class slots, class commands, glyphstones, and stat effects.
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

        // Runs after core survival systems have registered class data.
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

        // Loads server config, player state hooks, commands, and recipes.
        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            Config ??= RebornClassConfig.Load(api);
            Ledger.Reload(api);

            // Sync after character setup so first-join class selection cannot overwrite it.
            sapi.Event.PlayerNowPlaying += PreparePlayerState;
            RegisterClassCommands();
            RegisterGlyphstoneRecipes();

            sapi.Logger.Notification("[Multiclass Reborn] Loaded {0} class definitions. Stats={1}, Recipes={2}, GlyphstoneRecipes={3}, ClassBoundGlyphstones={4}, DisableGenericGlyphstones={5}, Scale={6:P0}, MaxSlots={7}, DropOverMax={8}, RequireGlyphs={9}, RetrainFree={10}, StartingAptitudeTokens={11}, BestPositiveOnly={12}, WorstNegativeOnly={13}",
                Ledger.EnabledClasses.Count,
                Config.AllowStatBonuses,
                Config.AllowRecipeTraits,
                Config.EnableGlyphstoneRecipes,
                Config.EnableClassBoundGlyphstones,
                Config.DisableGenericGlyphstones,
                Config.ExtraClassScale,
                Config.MaxExtraClasses,
                Config.DropExtraClassesOverMax,
                Config.RequireGlyphs,
                Config.RetrainFreeApplies,
                Config.StartingAptitudeTokens,
                Config.OnlyApplyBestPositiveTraitBonus,
                Config.OnlyApplyWorstNegativeTraitPenalty);
        }

        // Prunes JSON trader offers for glyphstones disabled by the current ruleset.
        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            if (api.Side != EnumAppSide.Server) return;
            if (api is ICoreServerAPI serverApi) Config ??= RebornClassConfig.Load(serverApi);
            if (Config == null) return;

            if (Config.DisableGenericGlyphstones)
            {
                RemoveGlyphstoneTraderOffers(api, AptitudeGlyphItemCode);
            }

            if (Config.RetrainFreeApplies)
            {
                RemoveGlyphstoneTraderOffers(api, RetrainGlyphItemCode);
            }
        }

        // Loads client lookup data, GUI patches, handbook ordering, and hotkeys.
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

        // Removes client hooks when the mod system shuts down.
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

        // Keep the guide near the vanilla mechanics pages instead of burying it with tutorials.
        private static void MoveGuideAfterVanillaGuides(List<GuiHandbookPage> pages)
        {
            int guideIndex = pages.FindIndex(page => page.PageCode == HandbookPageCode);
            if (guideIndex < 0) return;

            GuiHandbookPage guidePage = pages[guideIndex];
            pages.RemoveAt(guideIndex);

            int insertIndex = pages.FindLastIndex(IsVanillaGameMechanicPage) + 1;
            pages.Insert(insertIndex, guidePage);
        }

        // Identifies the vanilla guide group by page code.
        private static bool IsVanillaGameMechanicPage(GuiHandbookPage page)
        {
            return page.PageCode.StartsWith("gamemechanicinfo-", StringComparison.OrdinalIgnoreCase);
        }

        // Registers the /multiclass command surface used by chat and the GUI.
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
                .BeginSubCommand("confirmglyph")
                    .WithArgs(parsers.Word("classcode"))
                    .WithDescription(Lang.Get("multiclassreborn:command-confirmglyph-description"))
                    .HandleWith(args => RunPlayerCommand(args, player => ApplyClassBoundGlyphstone(player, (string)args[0])))
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
                    .RequiresPrivilege(Privilege.controlserver)
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
                    .HandleWith(args => RunPlayerCommand(args, player => GiveRetrainGlyphItem(player, (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("giveboundglyph")
                    .WithArgs(parsers.Word("playername"), parsers.Word("itemcode"))
                    .WithDescription(Lang.Get("multiclassreborn:command-giveboundglyph-description"))
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => RunPlayerCommand(args, player => GiveBoundGlyphItem(player, (string)args[0], (string)args[1])))
                .EndSubCommand();
        }

        // Adds optional craftable glyphstone recipes when the config allows it.
        private void RegisterGlyphstoneRecipes()
        {
            if (!Config.EnableGlyphstoneRecipes) return;

            if (!Config.DisableGenericGlyphstones)
            {
                RegisterGlyphstoneRecipe("craft-aptitude-glyphstone", AptitudeGlyphItemCode, "clearquartz");
            }

            RegisterClassBoundGlyphstoneRecipes();
            if (Config.RetrainFreeApplies) return;

            RegisterGlyphstoneRecipe("craft-retraining-glyphstone", RetrainGlyphItemCode, "gear-rusty");
        }

        // Registers the built-in bound glyphstone recipes for enabled vanilla classes.
        private void RegisterClassBoundGlyphstoneRecipes()
        {
            if (!Config.EnableClassBoundGlyphstones) return;

            RegisterClassBoundGlyphstoneRecipe("craft-blackguard-glyphstone", "multiclassreborn:blackguard-glyphstone", "blackguard", ItemIngredient("metalbit-blackbronze"));
            RegisterClassBoundGlyphstoneRecipe("craft-clockmaker-glyphstone", "multiclassreborn:clockmaker-glyphstone", "clockmaker", ItemIngredient("metalbit-brass"));
            RegisterClassBoundGlyphstoneRecipe("craft-commoner-glyphstone", "multiclassreborn:commoner-glyphstone", "commoner", BlockIngredient("packeddirt"));
            RegisterClassBoundGlyphstoneRecipe("craft-hunter-glyphstone", "multiclassreborn:hunter-glyphstone", "hunter", ItemIngredient("arrowhead-flint"));
            RegisterClassBoundGlyphstoneRecipe("craft-malefactor-glyphstone", "multiclassreborn:malefactor-glyphstone", "malefactor", BlockIngredient("metal-parts"));
            RegisterClassBoundGlyphstoneRecipe("craft-tailor-glyphstone", "multiclassreborn:tailor-glyphstone", "tailor", BlockIngredient("linen-normal-down"));
        }

        // Avoids recipes for classes that another content pack removed or replaced.
        private void RegisterClassBoundGlyphstoneRecipe(string recipeName, string outputCode, string targetClassCode, CraftingRecipeIngredient roleIngredient)
        {
            if (!Ledger.ClassByCode.ContainsKey(targetClassCode)) return;

            RegisterGlyphstoneRecipe(recipeName, outputCode, roleIngredient);
        }

        // Removes trader offers for one glyphstone item code from the loaded trade list.
        private void RemoveGlyphstoneTraderOffers(ICoreAPI api, string glyphstoneItemCode)
        {
            IAsset tradeListAsset = api.Assets.TryGet(new AssetLocation("game:config/tradelists/trader-luxuries.json"), true);
            if (tradeListAsset == null) return;

            JObject tradeList = JObject.Parse(tradeListAsset.ToText());
            if (tradeList.SelectToken("selling.list") is not JArray sellingList) return;

            List<JToken> matchingOffers = sellingList
                .Where(token => token?["code"]?.ToString().Equals(glyphstoneItemCode, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (matchingOffers.Count == 0) return;

            foreach (JToken matchingOffer in matchingOffers)
            {
                matchingOffer.Remove();
            }

            tradeListAsset.Data = Encoding.UTF8.GetBytes(tradeList.ToString(Formatting.Indented));
        }

        // Uses an item code for the recipe's class-flavored ingredient.
        private void RegisterGlyphstoneRecipe(string recipeName, string outputCode, string roleItemCode)
        {
            RegisterGlyphstoneRecipe(recipeName, outputCode, ItemIngredient(roleItemCode));
        }

        // Keeps all glyphstone recipes on the same frame while swapping the role ingredient.
        private void RegisterGlyphstoneRecipe(string recipeName, string outputCode, CraftingRecipeIngredient roleIngredient)
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
                    { "R", roleIngredient },
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

        // Allows any vanilla stone variant in the recipe corners.
        private CraftingRecipeIngredient StoneIngredient()
        {
            return ItemIngredient("stone-*");
        }

        // Limits the wildcard metal bit ingredient to gold and silver.
        private CraftingRecipeIngredient MetalBitIngredient()
        {
            CraftingRecipeIngredient ingredient = ItemIngredient("metalbit-*");
            ingredient.AllowedVariants = new[] { "gold", "silver" };

            return ingredient;
        }

        // Uses a block ingredient when a vanilla class flavor is block-based.
        private CraftingRecipeIngredient BlockIngredient(string code)
        {
            return new CraftingRecipeIngredient()
            {
                Type = EnumItemClass.Block,
                Code = new AssetLocation(code),
                Quantity = 1
            };
        }

        // Creates a one-item crafting ingredient for programmatic recipes.
        private CraftingRecipeIngredient ItemIngredient(string code)
        {
            return new CraftingRecipeIngredient()
            {
                Type = EnumItemClass.Item,
                Code = new AssetLocation(code),
                Quantity = 1
            };
        }

        // Normalizes command handlers that require an actual server player.
        private TextCommandResult RunPlayerCommand(TextCommandCallingArgs args, Action<IServerPlayer> action)
        {
            if (args.Caller.Player is not IServerPlayer player)
            {
                return TextCommandResult.Error(Lang.Get("multiclassreborn:message-must-be-player"), "");
            }

            action(player);
            return TextCommandResult.Success("", null);
        }

        // Opens the client dialog in retraining mode.
        internal void OpenClassDialogForRetraining()
        {
            classDialog?.OpenForRetraining();
        }

        // Opens the client dialog in learning mode.
        internal void OpenClassDialogForLearning()
        {
            classDialog?.OpenForLearning();
        }

        // Opens the client dialog around the targets supplied by the held glyphstone.
        internal void OpenClassDialogForBoundGlyphstone(IEnumerable<string> targetClasses)
        {
            classDialog?.OpenForBoundGlyphstone(targetClasses?.Select(NormalizeClassCode));
        }

        // Syncs config-backed player state after character creation finishes.
        private void PreparePlayerState(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);

            RecountUsedSlots(state);
            PruneExtraClassesOverChangedMax(state);
            ReviewPlayerConfig(player, state);
            MigrateLegacyGlyphItems(player);
            ReapplyClassEffects(player);
        }

        // Reviews config-sensitive player state every join without removing classes.
        private void ReviewPlayerConfig(IServerPlayer player, RebornPlayerClassState state)
        {
            bool previousRequireGlyphs = state.ConfigReviewed
                ? state.ReviewedRequireGlyphs
                : state.SlotsInitialized && state.RequiresGlyphs;

            state.RequiresGlyphs = Config.RequireGlyphs;
            state.RetrainFree = Config.RetrainFreeApplies;
            state.AllowsBaseClassForgetting = Config.AllowForgettingBaseClass;
            state.AllowsCommonerBaseClassChoice = Config.AllowCommonersChooseBaseClass;
            state.ExtraClassScale = Config.ExtraClassScale;
            state.OnlyApplyBestPositiveTraitBonus = Config.OnlyApplyBestPositiveTraitBonus;
            state.OnlyApplyWorstNegativeTraitPenalty = Config.OnlyApplyWorstNegativeTraitPenalty;

            if (!Config.RequireGlyphs)
            {
                // Free-slot worlds grant the current configured capacity.
                state.AvailableSlots = Math.Max(state.UsedSlots, Config.MaxExtraClasses);
                state.SlotsInitialized = true;
            }
            else if (state.SlotsInitialized && !previousRequireGlyphs)
            {
                // Switching to glyphstones removes only unused free capacity.
                state.AvailableSlots = state.UsedSlots;
            }
            else if (!state.SlotsInitialized)
            {
                GrantStartingAptitudeTokens(player);
                state.AvailableSlots = Math.Max(state.AvailableSlots, state.UsedSlots);
                state.SlotsInitialized = true;
            }
            else
            {
                // Glyphstone worlds keep earned or occupied slots, but trim unused over-cap space.
                state.AvailableSlots = Math.Max(state.UsedSlots, Math.Min(state.AvailableSlots, Config.MaxExtraClasses));
            }

            state.ReviewedRequireGlyphs = Config.RequireGlyphs;
            state.ReviewedMaxExtraClasses = Config.MaxExtraClasses;
            state.ConfigReviewed = true;
        }

        // Optionally drops learned classes only when the configured max changes.
        private void PruneExtraClassesOverChangedMax(RebornPlayerClassState state)
        {
            if (!Config.DropExtraClassesOverMax) return;
            if (!state.ConfigReviewed) return;
            if (state.ReviewedMaxExtraClasses == Config.MaxExtraClasses) return;
            if (state.ExtraClasses.Count <= Config.MaxExtraClasses) return;

            state.ExtraClasses = state.ExtraClasses.Take(Config.MaxExtraClasses).ToList();
            state.UsedSlots = state.ExtraClasses.Count;
        }

        // First-join tokens only matter on glyph-gated servers.
        private void GrantStartingAptitudeTokens(IServerPlayer player)
        {
            if (Config.DisableGenericGlyphstones) return;
            if (!Config.RequireGlyphs || Config.StartingAptitudeTokens <= 0) return;

            Item item = sapi.World.GetItem(new AssetLocation(AptitudeGlyphItemCode));
            if (item == null)
            {
                sapi.Logger.Warning("[Multiclass Reborn] Could not resolve starting token item {0}.", AptitudeGlyphItemCode);
                return;
            }

            int remainingTokens = Config.StartingAptitudeTokens;
            int stackLimit = Math.Max(1, item.MaxStackSize);

            while (remainingTokens > 0)
            {
                ItemStack stack = new ItemStack(item, Math.Min(remainingTokens, stackLimit));
                remainingTokens -= stack.StackSize;

                if (player.InventoryManager.TryGiveItemstack(stack)) continue;
                if (stack.StackSize <= 0) continue;

                Vec3d dropPos = player.Entity.Pos.XYZ.Add(0, 0.25, 0);
                sapi.World.SpawnItemEntity(stack, dropPos);
            }
        }

        // Adds one usable extra-class slot if the player is below the configured cap.
        internal bool TryGrantClassSlot(IServerPlayer player)
        {
            if (Config.DisableGenericGlyphstones)
            {
                Tell(player, Lang.Get("multiclassreborn:message-generic-aptitude-glyphstones-disabled"), EnumChatType.Notification);
                return false;
            }

            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            TrimUnusedSlotsOverConfiguredMax(state);

            if (state.UsedSlots >= Config.MaxExtraClasses || state.AvailableSlots >= Config.MaxExtraClasses)
            {
                Tell(player, Lang.Get("multiclassreborn:message-max-class-slots", Config.MaxExtraClasses), EnumChatType.Notification);
                return false;
            }

            state.AvailableSlots++;
            Tell(player, Lang.Get("multiclassreborn:message-aptitude-consumed", state.AvailableSlots, state.UsedSlots), EnumChatType.Notification);

            return true;
        }

        // Preserves occupied classes while removing unused capacity above the current config.
        private void TrimUnusedSlotsOverConfiguredMax(RebornPlayerClassState state)
        {
            int availableSlots = Math.Max(state.UsedSlots, Math.Min(state.AvailableSlots, Config.MaxExtraClasses));
            if (state.AvailableSlots == availableSlots) return;

            state.AvailableSlots = availableSlots;
        }

        // Learns one extra class after validating slots, duplicates, and class existence.
        internal void LearnExtraClass(IServerPlayer player, string classCode)
        {
            string normalizedCode = NormalizeClassCode(classCode);
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            TrimUnusedSlotsOverConfiguredMax(state);

            if (!Ledger.ClassByCode.ContainsKey(normalizedCode))
            {
                Tell(player, Lang.Get("multiclassreborn:message-class-does-not-exist", normalizedCode), EnumChatType.Notification);
                return;
            }

            if (Ledger.ClassBoundOnlyCodes.Contains(normalizedCode))
            {
                Tell(player, Lang.Get("multiclassreborn:message-class-requires-bound-glyphstone", normalizedCode), EnumChatType.Notification);
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

        // Applies a bound glyphstone only after re-checking the player's hotbar.
        internal void ApplyClassBoundGlyphstone(IServerPlayer player, string requestedClassCode)
        {
            string normalizedCode = NormalizeClassCode(requestedClassCode);

            if (!Config.EnableClassBoundGlyphstones)
            {
                Tell(player, Lang.Get("multiclassreborn:message-bound-glyphstones-disabled"), EnumChatType.Notification);
                return;
            }

            if (!TryFindClassBoundGlyphstone(player, normalizedCode, out ItemSlot glyphSlot))
            {
                Tell(player, Lang.Get("multiclassreborn:message-bound-glyphstone-missing", normalizedCode), EnumChatType.Notification);
                return;
            }

            if (!Ledger.ClassByCode.ContainsKey(normalizedCode))
            {
                Tell(player, Lang.Get("multiclassreborn:message-class-does-not-exist", normalizedCode), EnumChatType.Notification);
                return;
            }

            bool applied = Config.MaxExtraClasses == 0
                ? TryReplaceBaseClassWithGlyphstone(player, normalizedCode)
                : TryLearnBoundExtraClass(player, normalizedCode);

            if (!applied) return;

            glyphSlot.TakeOut(1);
            glyphSlot.MarkDirty();
        }

        // Learns the bound class as an extra class without relying on a generic slot token.
        private bool TryLearnBoundExtraClass(IServerPlayer player, string classCode)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            TrimUnusedSlotsOverConfiguredMax(state);

            if (GetMainClassCode(player.Entity).Equals(classCode, StringComparison.OrdinalIgnoreCase))
            {
                Tell(player, Lang.Get("multiclassreborn:message-already-main-class", classCode), EnumChatType.Notification);
                return false;
            }

            List<string> extraClasses = state.ExtraClasses;
            if (extraClasses.Contains(classCode))
            {
                Tell(player, Lang.Get("multiclassreborn:message-already-have-class", classCode), EnumChatType.Notification);
                return false;
            }

            if (extraClasses.Count >= Config.MaxExtraClasses)
            {
                Tell(player, Lang.Get("multiclassreborn:message-cannot-add-more-classes", Config.MaxExtraClasses), EnumChatType.Notification);
                return false;
            }

            state.AvailableSlots = Math.Min(Config.MaxExtraClasses, Math.Max(state.AvailableSlots, state.UsedSlots + 1));
            extraClasses.Add(classCode);
            state.ExtraClasses = extraClasses;
            state.UsedSlots = extraClasses.Count;

            ReapplyClassEffects(player);
            Tell(player, Lang.Get("multiclassreborn:message-bound-glyphstone-learned", classCode, state.UsedSlots, state.AvailableSlots), EnumChatType.Notification);
            return true;
        }

        // Reuses the existing base-class rules for zero-extra-class worlds.
        private bool TryReplaceBaseClassWithGlyphstone(IServerPlayer player, string classCode)
        {
            string mainClassCode = GetMainClassCode(player.Entity);
            bool isCommoner = mainClassCode.Equals(CommonerClassCode, StringComparison.OrdinalIgnoreCase);
            bool canReplaceBase = Config.AllowForgettingBaseClass || (isCommoner && Config.AllowCommonersChooseBaseClass);

            if (!canReplaceBase)
            {
                Tell(player, Lang.Get("multiclassreborn:message-bound-base-replacement-disabled"), EnumChatType.Notification);
                return false;
            }

            if (mainClassCode.Equals(classCode, StringComparison.OrdinalIgnoreCase))
            {
                Tell(player, Lang.Get("multiclassreborn:message-already-main-class", classCode), EnumChatType.Notification);
                return false;
            }

            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            if (state.ExtraClasses.Contains(classCode))
            {
                Tell(player, Lang.Get("multiclassreborn:message-already-have-class", classCode), EnumChatType.Notification);
                return false;
            }

            player.Entity.WatchedAttributes.SetString("characterClass", classCode);
            player.Entity.WatchedAttributes.MarkPathDirty("characterClass");
            ReapplyClassEffects(player);
            Tell(player, Lang.Get("multiclassreborn:message-bound-glyphstone-replaced-base", classCode), EnumChatType.Notification);
            return true;
        }

        // Searches the hotbar so the GUI can confirm from any visible glyphstone stack.
        private bool TryFindClassBoundGlyphstone(IServerPlayer player, string classCode, out ItemSlot glyphSlot)
        {
            glyphSlot = null;

            if (TryMatchClassBoundGlyphstone(player.InventoryManager.ActiveHotbarSlot, classCode))
            {
                glyphSlot = player.InventoryManager.ActiveHotbarSlot;
                return true;
            }

            foreach (IInventory inventory in player.InventoryManager.InventoriesOrdered)
            {
                if (!IsHotbarInventory(inventory)) continue;
                if (!TryGetInventoryCount(inventory, out int slotCount)) continue;

                for (int slotId = 0; slotId < slotCount; slotId++)
                {
                    ItemSlot slot = inventory[slotId];
                    if (!TryMatchClassBoundGlyphstone(slot, classCode)) continue;

                    glyphSlot = slot;
                    return true;
                }
            }

            return false;
        }

        // Checks one slot for a bound glyphstone that can pay for this class.
        private bool TryMatchClassBoundGlyphstone(ItemSlot slot, string classCode)
        {
            if (slot == null || slot.Empty) return false;
            if (slot.Itemstack.Collectible is not ClassSlotGlyphItem) return false;

            List<string> glyphClassCodes = ClassSlotGlyphItem.GetTargetClasses(slot.Itemstack);
            if (!glyphClassCodes.Contains(classCode, StringComparer.OrdinalIgnoreCase)) return false;
            if (!Ledger.RequiredGlyphstoneByClassCode.TryGetValue(classCode, out string requiredGlyphstoneCode)) return true;

            return RequiredGlyphstoneMatches(slot.Itemstack.Collectible.Code, requiredGlyphstoneCode);
        }

        // Treats unqualified requiredGlyphstone values as this mod's item domain.
        private bool RequiredGlyphstoneMatches(AssetLocation actualCode, string requiredGlyphstoneCode)
        {
            if (actualCode == null || string.IsNullOrWhiteSpace(requiredGlyphstoneCode)) return false;

            string normalizedCode = requiredGlyphstoneCode.Trim().ToLowerInvariant();
            if (actualCode.Equals(new AssetLocation(normalizedCode))) return true;
            if (normalizedCode.Contains(':')) return false;

            return actualCode.Equals(new AssetLocation("multiclassreborn", normalizedCode));
        }

        // Forgets an extra class through the non-glyph command path.
        internal void ForgetExtraClass(IServerPlayer player, string classCode)
        {
            ForgetExtraClass(player, classCode, false);
        }

        // Directs players to the safer confirmation flow.
        private void RejectUnconfirmedForget(IServerPlayer player)
        {
            Tell(player, Lang.Get("multiclassreborn:message-open-retraining-confirm"), EnumChatType.Notification);
        }

        // Routes confirmed forgets to either base-class or extra-class removal.
        internal void ForgetClassAfterConfirmation(IServerPlayer player, string classCode)
        {
            string normalizedCode = NormalizeClassCode(classCode);
            string mainClassCode = GetMainClassCode(player.Entity);

            if (mainClassCode.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase))
            {
                ForgetBaseClassAfterConfirmation(player, normalizedCode);
                return;
            }

            ForgetExtraClass(player, normalizedCode, ShouldConsumeRetrainingGlyphstone());
        }

        // Removes one extra class and optionally consumes a retraining glyphstone first.
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

        // Resets the main class to Commoner when the server allows base-class forgetting.
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

            if (ShouldConsumeRetrainingGlyphstone() && !TryConsumeRetrainingGlyphstone(player))
            {
                Tell(player, Lang.Get("multiclassreborn:message-need-retraining-base"), EnumChatType.Notification);
                return;
            }

            player.Entity.WatchedAttributes.SetString("characterClass", CommonerClassCode);
            player.Entity.WatchedAttributes.MarkPathDirty("characterClass");
            ReapplyClassEffects(player);
            Tell(player, Lang.Get("multiclassreborn:message-forgot-base-class"), EnumChatType.Notification);
        }

        // RetrainFree only waives the removal cost on token-gated worlds.
        private bool ShouldConsumeRetrainingGlyphstone()
        {
            return Config.RequireGlyphs && !Config.RetrainFreeApplies;
        }

        // Lets Commoners promote a chosen class to their main class when configured.
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

            if (Ledger.ClassBoundOnlyCodes.Contains(normalizedCode))
            {
                Tell(player, Lang.Get("multiclassreborn:message-class-requires-bound-glyphstone", normalizedCode), EnumChatType.Notification);
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

        // Retraining glyphstones are intentionally hotbar-only so use is a deliberate action.
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

        // Consumes one matching item from a slot.
        private bool TryConsumeFromSlot(ItemSlot slot, string itemCode)
        {
            if (slot == null || slot.Empty) return false;
            if (!slot.Itemstack.Collectible.Code.Equals(new AssetLocation(itemCode))) return false;

            slot.TakeOut(1);
            slot.MarkDirty();
            return true;
        }

        // Handles hotbar detection across inventory ids and class names.
        private bool IsHotbarInventory(IInventory inventory)
        {
            string inventoryId = inventory?.InventoryID ?? "";
            string className = inventory?.ClassName ?? "";

            return inventoryId.IndexOf("hotbar", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("hotbar", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Converts old rune/glyph item stacks in player inventories.
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

        // Limits item migration to player-owned inventories and skips creative storage.
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

        // Some inventory implementations can throw while loading; skip them instead of
        // blocking player login over a best-effort item migration.
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

        // Replaces one legacy glyph item stack while preserving count and attributes.
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

        // Clears all extra classes and their applied effects.
        private void ClearExtraClasses(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            state.ExtraClasses = new List<string>();
            state.UsedSlots = 0;

            ClearRebornStats(player);
            Tell(player, Lang.Get("multiclassreborn:message-extra-classes-cleared"), EnumChatType.Notification);
        }

        // Prints the player's learned extra classes.
        private void ShowExtraClassList(IServerPlayer player)
        {
            List<string> extraClasses = new RebornPlayerClassState(player.Entity).ExtraClasses;
            Tell(player, extraClasses.Count == 0
                ? Lang.Get("multiclassreborn:message-no-extra-classes")
                : Lang.Get("multiclassreborn:message-extra-classes", string.Join(", ", extraClasses)), EnumChatType.Notification);
        }

        // Prints enabled class codes for command users.
        private void ShowAvailableClasses(IServerPlayer player)
        {
            List<string> classCodes = Ledger.EnabledClasses.Select(classDef => classDef.Code).OrderBy(code => code).ToList();
            Tell(player, classCodes.Count == 0
                ? Lang.Get("multiclassreborn:message-no-class-definitions")
                : Lang.Get("multiclassreborn:message-available-classes", classCodes.Count, string.Join(", ", classCodes)), EnumChatType.Notification);
        }

        // Prints current main class, slot usage, and extra class list.
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

        // Prints trait and stat details for one class code.
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

        // Gives a configured glyphstone item to an online player.
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

        // Keeps retraining glyphstones unavailable when forgetting has no token cost.
        private void GiveRetrainGlyphItem(IServerPlayer admin, string playerName)
        {
            if (Config.RetrainFreeApplies)
            {
                Tell(admin, Lang.Get("multiclassreborn:message-retraining-glyphstones-disabled"), EnumChatType.Notification);
                return;
            }

            GiveAptitudeGlyphItem(admin, playerName, RetrainGlyphItemCode, "item-retraining-glyphstone");
        }

        // Gives a bound glyphstone only after confirming the item really has targets.
        private void GiveBoundGlyphItem(IServerPlayer admin, string playerName, string itemCode)
        {
            Item item = ResolveBoundGlyphItem(itemCode);
            if (item == null)
            {
                Tell(admin, Lang.Get("multiclassreborn:message-could-not-resolve-item", itemCode), EnumChatType.Notification);
                return;
            }

            ItemStack stack = new ItemStack(item, 1);
            if (ClassSlotGlyphItem.GetTargetClasses(stack).Count == 0)
            {
                Tell(admin, Lang.Get("multiclassreborn:message-not-bound-glyphstone", item.Code.ToString()), EnumChatType.Notification);
                return;
            }

            GiveAptitudeGlyphItem(admin, playerName, item.Code.ToString(), "item-" + item.Code.Path);
        }

        // Resolves admin shorthand such as hunter to a loaded bound glyphstone item.
        private Item ResolveBoundGlyphItem(string itemCode)
        {
            string normalizedInput = NormalizeClassCode(itemCode);
            foreach (AssetLocation candidateCode in BuildBoundGlyphItemCandidates(normalizedInput))
            {
                Item candidateItem = sapi.World.GetItem(candidateCode);
                if (IsClassBoundGlyphItem(candidateItem)) return candidateItem;
            }

            return sapi.World.Items
                .Where(IsClassBoundGlyphItem)
                .FirstOrDefault(item => MatchesBoundGlyphLookup(item, normalizedInput));
        }

        // Tries common item-code forms before falling back to the full item scan.
        private IEnumerable<AssetLocation> BuildBoundGlyphItemCandidates(string itemCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode)) yield break;

            string exactCode = itemCode.Contains(':') ? itemCode : "multiclassreborn:" + itemCode;
            yield return new AssetLocation(exactCode);

            if (exactCode.EndsWith("-glyphstone", StringComparison.OrdinalIgnoreCase)) yield break;

            yield return new AssetLocation(exactCode + "-glyphstone");
        }

        // Filters the item scan to glyphstones with bound targets.
        private bool IsClassBoundGlyphItem(Item item)
        {
            if (item is not ClassSlotGlyphItem) return false;

            return ClassSlotGlyphItem.GetTargetClasses(new ItemStack(item)).Count > 0;
        }

        // Lets admins use either the item path or the target class code.
        private bool MatchesBoundGlyphLookup(Item item, string itemCode)
        {
            if (item.Code.Path.Equals(itemCode, StringComparison.OrdinalIgnoreCase)) return true;
            if (item.Code.Path.Equals(itemCode + "-glyphstone", StringComparison.OrdinalIgnoreCase)) return true;

            return ClassSlotGlyphItem.GetTargetClasses(new ItemStack(item)).Contains(itemCode, StringComparer.OrdinalIgnoreCase);
        }

        // Rebuilds all extra-class stat and recipe effects from current state.
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

        // Collects distinct trait codes from learned extra classes.
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

        // Applies scaled stat modifiers after config duplicate handling.
        private void ApplyScaledStats(IServerPlayer player, HashSet<string> traitCodes)
        {
            List<TraitStatCandidate> candidates = GatherTraitStatCandidates(traitCodes);
            IEnumerable<TraitStatCandidate> appliedCandidates = ChooseConfiguredTraitStats(candidates);

            foreach (TraitStatCandidate candidate in appliedCandidates)
            {
                float scaledValue = ShouldScaleStat(candidate.StatCode)
                    ? (float)candidate.RawValue * Config.ExtraClassScale
                    : (float)candidate.RawValue;

                player.Entity.Stats.Set(candidate.StatCode, BuildCanonicalStatSourceCode(candidate.TraitCode), scaledValue, false);
            }
        }

        // Some trait stats are discrete unlocks, thresholds, or tier values.
        private static bool ShouldScaleStat(string statCode)
        {
            if (string.IsNullOrWhiteSpace(statCode)) return true;
            if (statCode.Equals("temporalGearTLRepairCost", StringComparison.OrdinalIgnoreCase)) return false;
            if (statCode.Equals("dodgeGuaranteedCooldown", StringComparison.OrdinalIgnoreCase)) return false;
            if (statCode.Equals("fallDamageThreshold", StringComparison.OrdinalIgnoreCase)) return false;
            if (statCode.StartsWith("can", StringComparison.OrdinalIgnoreCase)) return false;
            if (statCode.IndexOf("DamageTierBonus", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            return true;
        }

        // Flattens selected traits into stat candidates for filtering.
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

        // Positive and negative traits can use different duplicate rules.
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

        // Checks whether this trait type should suppress weaker duplicate stats.
        private bool ShouldKeepOnlyStrongest(EnumTraitType traitType)
        {
            if (traitType == EnumTraitType.Positive) return Config.OnlyApplyBestPositiveTraitBonus;
            if (traitType == EnumTraitType.Negative) return Config.OnlyApplyWorstNegativeTraitPenalty;

            return false;
        }

        // Chooses the candidate with the largest absolute stat change.
        private TraitStatCandidate ChooseStrongestTraitStat(IEnumerable<TraitStatCandidate> candidates)
        {
            return candidates
                .OrderByDescending(candidate => Math.Abs(candidate.RawValue))
                .First();
        }

        // Removes all stat and recipe effects owned by this mod.
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

        // Writes extra trait codes into watched attributes for recipe checks.
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

        // Removes extra recipe traits from watched attributes.
        private void RemoveRecipeTraits(EntityPlayer entity)
        {
            entity.WatchedAttributes.RemoveAttribute(ExtraTraitsAttribute);
            entity.WatchedAttributes.MarkPathDirty(ExtraTraitsAttribute);
        }

        // Cleans invalid or duplicate extra classes before recounting used slots.
        private void RecountUsedSlots(RebornPlayerClassState state)
        {
            List<string> cleanClasses = state.ExtraClasses
                .Where(classCode => Ledger.ClassByCode.ContainsKey(classCode))
                .Distinct()
                .ToList();

            state.ExtraClasses = cleanClasses;
            state.UsedSlots = cleanClasses.Count;
        }

        // Normalizes command input to class ledger keys.
        private string NormalizeClassCode(string classCode)
        {
            return (classCode ?? "").Trim().ToLowerInvariant();
        }

        // Reads the vanilla character class attribute.
        private string GetMainClassCode(EntityPlayer entity)
        {
            return entity.WatchedAttributes.GetString("characterClass", "none");
        }

        // Match the original mod's stat source so migrated worlds overwrite old values.
        private string BuildCanonicalStatSourceCode(string traitCode)
        {
            return $"multiclass_{traitCode}";
        }

        // Include the early Reborn source so 0.1.0 test worlds are cleaned up.
        private IEnumerable<string> BuildStatSourceCodes(string traitCode)
        {
            yield return BuildCanonicalStatSourceCode(traitCode);
            yield return $"multiclassreborn_{traitCode}";
        }

        // Carries one raw trait stat before config duplicate handling is applied.
        private sealed class TraitStatCandidate
        {
            public readonly string TraitCode;
            public readonly string StatCode;
            public readonly double RawValue;
            public readonly EnumTraitType TraitType;

            // Captures the values needed for server-side duplicate-stat filtering.
            public TraitStatCandidate(string traitCode, string statCode, double rawValue, EnumTraitType traitType)
            {
                TraitCode = traitCode;
                StatCode = statCode;
                RawValue = rawValue;
                TraitType = traitType;
            }
        }

        // Sends a chat message to the player's general chat group.
        private void Tell(IServerPlayer player, string message, EnumChatType chatType)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup, message, chatType, null);
        }
    }
}

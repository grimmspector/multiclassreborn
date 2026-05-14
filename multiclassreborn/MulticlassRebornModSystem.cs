using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using multiclassreborn.items;
using multiclassreborn.systems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

#nullable disable

namespace multiclassreborn
{
    public class MulticlassRebornModSystem : ModSystem
    {
        private const string ExtraTraitsAttribute = "extraTraits";
        private const string ClassRuneItemCode = "multiclass:classrune";
        private const string ForgetRuneItemCode = "multiclass:unlearnrune";

        private ICoreAPI api;
        private ICoreServerAPI sapi;
        private ICoreClientAPI capi;
        private ClassPickerDialog classDialog;

        internal RebornClassConfig Config { get; private set; }
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

            api.RegisterItemClass("ClassRune", typeof(ClassSlotRuneItem));
            api.RegisterItemClass("UnlearnRune", typeof(ClassForgetRuneItem));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            Config = RebornClassConfig.Load(api);
            Ledger.Reload(api);

            sapi.Event.PlayerJoin += PreparePlayerState;
            RegisterClassCommands();

            sapi.Logger.Notification("[Multiclass Reborn] Loaded {0} class definitions. Stats={1}, Recipes={2}, Scale={3:P0}, MaxSlots={4}, RequireRunes={5}",
                Ledger.EnabledClasses.Count,
                Config.AllowStatBonuses,
                Config.AllowRecipeTraits,
                Config.ExtraClassScale,
                Config.MaxExtraClasses,
                Config.RequireRunes);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            Ledger.Reload(api);

            classDialog = new ClassPickerDialog(capi, this);
            capi.Input.RegisterHotKey("multiclassgui", "Open Class Selection", GlKeys.K, HotkeyType.GUIOrOtherControls, false, true, false);
            capi.Input.SetHotKeyHandler("multiclassgui", _ =>
            {
                classDialog.Toggle();
                return true;
            });
        }

        /// <summary>
        /// Builds the /multiclass command surface used by both chat and the GUI.
        /// </summary>
        private void RegisterClassCommands()
        {
            CommandArgumentParsers parsers = sapi.ChatCommands.Parsers;

            sapi.ChatCommands.Create("multiclass")
                .WithDescription("Manage extra character classes")
                .RequiresPlayer()
                .RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("add")
                    .WithArgs(parsers.Word("classcode"))
                    .WithDescription("Add an extra class")
                    .HandleWith(args => RunPlayerCommand(args, player => LearnExtraClass(player, (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("remove")
                    .WithArgs(parsers.Word("classcode"))
                    .WithDescription("Remove an extra class")
                    .HandleWith(args => RunPlayerCommand(args, player => ForgetExtraClass(player, (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("list")
                    .WithDescription("List your extra classes")
                    .HandleWith(args => RunPlayerCommand(args, ShowExtraClassList))
                .EndSubCommand()
                .BeginSubCommand("available")
                    .WithDescription("List available class codes")
                    .HandleWith(args => RunPlayerCommand(args, ShowAvailableClasses))
                .EndSubCommand()
                .BeginSubCommand("info")
                    .WithArgs(parsers.Word("classcode"))
                    .WithDescription("Show class details")
                    .HandleWith(args => RunPlayerCommand(args, player => ShowClassDetails(player, (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("summary")
                    .WithDescription("Show your class slot summary")
                    .HandleWith(args => RunPlayerCommand(args, ShowClassSummary))
                .EndSubCommand()
                .BeginSubCommand("clear")
                    .WithDescription("Clear all extra classes")
                    .HandleWith(args => RunPlayerCommand(args, ClearExtraClasses))
                .EndSubCommand()
                .BeginSubCommand("giverune")
                    .WithArgs(parsers.Word("playername"))
                    .WithDescription("Give a class slot rune to an online player")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => RunPlayerCommand(args, player => GiveRuneItem(player, (string)args[0], ClassRuneItemCode, "Class Rune")))
                .EndSubCommand()
                .BeginSubCommand("giveremovalrune")
                    .WithArgs(parsers.Word("playername"))
                    .WithDescription("Give a class forget rune to an online player")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => RunPlayerCommand(args, player => GiveRuneItem(player, (string)args[0], ForgetRuneItemCode, "Class Forget Rune")))
                .EndSubCommand();
        }

        private TextCommandResult RunPlayerCommand(TextCommandCallingArgs args, Action<IServerPlayer> action)
        {
            if (args.Caller.Player is not IServerPlayer player)
            {
                return TextCommandResult.Error("Must be a player.", "");
            }

            action(player);
            return TextCommandResult.Success("", null);
        }

        private void PreparePlayerState(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);

            // Always sync the current token rule. The old implementation only
            // wrote true, which could leave clients stuck after config changes.
            state.RequiresRunes = Config.RequireRunes;

            if (!Config.RequireRunes)
            {
                // Free-slot servers should give every player the configured
                // class capacity, including players migrated from rune servers.
                state.AvailableSlots = Config.MaxExtraClasses;
                state.SlotsInitialized = true;
            }
            else if (!state.SlotsInitialized)
            {
                state.SlotsInitialized = true;
            }

            RecountUsedSlots(state);
            ReapplyClassEffects(player);
        }

        internal bool TryGrantClassSlot(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);

            if (state.AvailableSlots >= Config.MaxExtraClasses)
            {
                Tell(player, $"You already have the maximum class slots ({Config.MaxExtraClasses}).", EnumChatType.Notification);
                return false;
            }

            state.AvailableSlots++;
            Tell(player, $"Class Rune consumed! You now have {state.AvailableSlots} class slots ({state.UsedSlots} used).", EnumChatType.Notification);

            return true;
        }

        internal bool TryGrantForgetCredit(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            state.RemovalCredits++;

            Tell(player, $"Class Forget Rune consumed! You can now forget {state.RemovalCredits} extra classes.", EnumChatType.Notification);
            return true;
        }

        internal void LearnExtraClass(IServerPlayer player, string classCode)
        {
            string normalizedCode = NormalizeClassCode(classCode);
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);

            if (!Ledger.ClassByCode.ContainsKey(normalizedCode))
            {
                Tell(player, $"Class '{normalizedCode}' does not exist. Use /multiclass available to see valid classes.", EnumChatType.Notification);
                return;
            }

            if (GetMainClassCode(player.Entity).Equals(normalizedCode, StringComparison.OrdinalIgnoreCase))
            {
                Tell(player, $"'{normalizedCode}' is already your main class.", EnumChatType.Notification);
                return;
            }

            List<string> extraClasses = state.ExtraClasses;
            if (extraClasses.Contains(normalizedCode))
            {
                Tell(player, $"You already have {normalizedCode}.", EnumChatType.Notification);
                return;
            }

            if (state.UsedSlots >= state.AvailableSlots)
            {
                Tell(player, $"No available class slots ({state.UsedSlots}/{state.AvailableSlots}). Use a Class Rune or forget another class.", EnumChatType.Notification);
                return;
            }

            if (extraClasses.Count >= Config.MaxExtraClasses)
            {
                Tell(player, $"Cannot add more classes. Maximum is {Config.MaxExtraClasses}.", EnumChatType.Notification);
                return;
            }

            extraClasses.Add(normalizedCode);
            state.ExtraClasses = extraClasses;
            state.UsedSlots = extraClasses.Count;

            ReapplyClassEffects(player);
            Tell(player, $"Added extra class '{normalizedCode}' (slots: {state.UsedSlots}/{state.AvailableSlots}).", EnumChatType.Notification);
        }

        internal void ForgetExtraClass(IServerPlayer player, string classCode)
        {
            string normalizedCode = NormalizeClassCode(classCode);
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);

            if (Config.RequireRunes && state.RemovalCredits <= 0)
            {
                Tell(player, "You need a Class Forget Rune before removing an extra class.", EnumChatType.Notification);
                return;
            }

            List<string> extraClasses = state.ExtraClasses;
            if (!extraClasses.Remove(normalizedCode))
            {
                Tell(player, $"Extra class '{normalizedCode}' not found.", EnumChatType.Notification);
                return;
            }

            state.ExtraClasses = extraClasses;
            state.UsedSlots = extraClasses.Count;

            if (Config.RequireRunes)
            {
                state.RemovalCredits = Math.Max(0, state.RemovalCredits - 1);
            }

            ReapplyClassEffects(player);
            Tell(player, $"Removed extra class '{normalizedCode}' (slots: {state.UsedSlots}/{state.AvailableSlots}).", EnumChatType.Notification);
        }

        private void ClearExtraClasses(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            state.ExtraClasses = new List<string>();
            state.UsedSlots = 0;

            ClearRebornStats(player);
            Tell(player, "All extra classes cleared and stats reset.", EnumChatType.Notification);
        }

        private void ShowExtraClassList(IServerPlayer player)
        {
            List<string> extraClasses = new RebornPlayerClassState(player.Entity).ExtraClasses;
            Tell(player, extraClasses.Count == 0 ? "No extra classes assigned." : $"Extra classes: {string.Join(", ", extraClasses)}", EnumChatType.Notification);
        }

        private void ShowAvailableClasses(IServerPlayer player)
        {
            List<string> classCodes = Ledger.EnabledClasses.Select(classDef => classDef.Code).OrderBy(code => code).ToList();
            Tell(player, classCodes.Count == 0 ? "No class definitions found." : $"Available classes ({classCodes.Count}): {string.Join(", ", classCodes)}", EnumChatType.Notification);
        }

        private void ShowClassSummary(IServerPlayer player)
        {
            RebornPlayerClassState state = new RebornPlayerClassState(player.Entity);
            List<string> extraClasses = state.ExtraClasses;
            string message = $"Slots: {state.UsedSlots}/{state.AvailableSlots} | Main: {GetMainClassCode(player.Entity)}";

            if (extraClasses.Count > 0)
            {
                message += $" | Extra: {string.Join(", ", extraClasses)}";
            }

            Tell(player, message, EnumChatType.Notification);
        }

        private void ShowClassDetails(IServerPlayer player, string classCode)
        {
            string normalizedCode = NormalizeClassCode(classCode);

            if (!Ledger.ClassByCode.TryGetValue(normalizedCode, out CharacterClass classDef))
            {
                Tell(player, $"Class '{normalizedCode}' not found.", EnumChatType.Notification);
                return;
            }

            StringBuilder text = new StringBuilder();
            text.AppendLine($"=== Class '{normalizedCode}' ===");

            if (classDef.Traits == null || classDef.Traits.Length == 0)
            {
                text.AppendLine("Traits: none");
            }
            else
            {
                text.AppendLine($"Traits ({classDef.Traits.Length}):");

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

        private void GiveRuneItem(IServerPlayer admin, string playerName, string itemCode, string displayName)
        {
            IServerPlayer target = sapi.World.AllOnlinePlayers
                .OfType<IServerPlayer>()
                .FirstOrDefault(player => player.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                Tell(admin, $"Player '{playerName}' not found or not online.", EnumChatType.Notification);
                return;
            }

            Item item = sapi.World.GetItem(new AssetLocation(itemCode));
            if (item == null)
            {
                Tell(admin, $"Could not resolve item '{itemCode}'.", EnumChatType.Notification);
                return;
            }

            ItemStack stack = new ItemStack(item, 1);
            if (!target.InventoryManager.TryGiveItemstack(stack))
            {
                Tell(admin, $"Could not give {displayName} to {target.PlayerName} (inventory full?).", EnumChatType.Notification);
                return;
            }

            Tell(admin, $"Gave {displayName} to {target.PlayerName}.", EnumChatType.Notification);
            Tell(target, $"You received a {displayName}.", EnumChatType.Notification);
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
            foreach (string traitCode in traitCodes)
            {
                if (!Ledger.TraitByCode.TryGetValue(traitCode, out Trait trait)) continue;
                if (trait.Attributes == null) continue;

                foreach (KeyValuePair<string, double> stat in trait.Attributes)
                {
                    float scaledValue = (float)stat.Value * Config.ExtraClassScale;
                    player.Entity.Stats.Set(stat.Key, BuildStatSourceCode(traitCode), scaledValue, false);
                }
            }
        }

        private void ClearRebornStats(IServerPlayer player)
        {
            foreach (Trait trait in Ledger.TraitByCode.Values)
            {
                if (trait.Attributes == null) continue;

                foreach (string statCode in trait.Attributes.Keys)
                {
                    player.Entity.Stats.Remove(statCode, BuildStatSourceCode(trait.Code));
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

        private string BuildStatSourceCode(string traitCode)
        {
            return $"multiclassreborn_{traitCode}";
        }

        private void Tell(IServerPlayer player, string message, EnumChatType chatType)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup, message, chatType, null);
        }
    }
}

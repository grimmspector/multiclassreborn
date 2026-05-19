using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using multiclassreborn.systems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#nullable disable

namespace multiclassreborn
{
    // Client-side dialog for learning, previewing, and forgetting extra classes.
    internal class ClassPickerDialog : GuiDialog
    {
        private const double DetailScrollbarWidth = 14;
        private const double DetailScrollbarPadding = 4;
        private const string RetrainingGlyphItemCode = "multiclassreborn:retraining-glyphstone";

        private readonly ICoreClientAPI clientApi;
        private readonly MulticlassRebornModSystem classSystem;

        private string selectedClassCode;
        private string pendingForgetClassCode;
        private bool openedForRetraining;
        private int pageIndex;

        public override string ToggleKeyCombinationCode => "multiclassgui";

        // Keeps the client API and mod system close for dialog rebuilds.
        public ClassPickerDialog(ICoreClientAPI capi, MulticlassRebornModSystem classSystem) : base(capi)
        {
            clientApi = capi;
            this.classSystem = classSystem;
        }

        // Opens the normal learning view from the hotkey.
        public override bool TryOpen()
        {
            openedForRetraining = false;
            pendingForgetClassCode = null;
            ComposeDialog();
            return base.TryOpen();
        }

        // Opens the same dialog in forget-class mode.
        public bool OpenForRetraining()
        {
            openedForRetraining = true;
            pendingForgetClassCode = null;
            ComposeDialog();
            return IsOpened() || base.TryOpen();
        }

        // Opens the learning view after an Aptitude Glyphstone grants a slot.
        public bool OpenForLearning()
        {
            openedForRetraining = false;
            pendingForgetClassCode = null;
            ComposeDialog();

            // Slot grants happen server-side, so reopen from glyph use after the state sync.
            clientApi.Event.RegisterCallback(_ => ComposeDialog(), 500);
            return IsOpened() || base.TryOpen();
        }

        // Pull from watched attributes every time so the client reflects server-side
        // slot changes after commands, glyphs, and migration.
        private void ComposeDialog()
        {
            SingleComposer?.Dispose();

            EntityPlayer entity = clientApi.World.Player.Entity;
            RebornPlayerClassState state = new RebornPlayerClassState(entity);
            List<string> extraClasses = state.ExtraClasses;
            string mainClass = entity.WatchedAttributes.GetString("characterClass", "none");

            List<CharacterClass> classList = classSystem.Ledger.EnabledClasses
                .OrderByDescending(classDef => classDef.Code.Equals(mainClass, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(classDef => extraClasses.Contains(classDef.Code))
                .ThenBy(classDef => classDef.Code)
                .ToList();

            if (selectedClassCode == null && classList.Count > 0)
            {
                selectedClassCode = classList[0].Code;
            }

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds backgroundBounds = ElementBounds.Fixed(0, GuiStyle.TitleBarHeight, 760, 560);
            ElementBounds listBounds = ElementBounds.Fixed(0, GuiStyle.TitleBarHeight, 230, 560);
            ElementBounds detailPanelBounds = ElementBounds.Fixed(230, GuiStyle.TitleBarHeight, 530, 560);
            ElementBounds detailBounds = ElementBounds.Fixed(250, GuiStyle.TitleBarHeight + 5, 490, 505);
            ElementBounds actionBounds = ElementBounds.Fixed(250, GuiStyle.TitleBarHeight + 520, 490, 30);

            SingleComposer = clientApi.Gui
                .CreateCompo("multiclass-reborn-picker", dialogBounds)
                .AddShadedDialogBG(backgroundBounds, false)
                .AddDialogTitleBar(BuildTitle(state), () => TryClose())
                .BeginChildElements();

            AddClassButtons(classList, listBounds, mainClass, extraClasses);
            SingleComposer.AddInset(detailPanelBounds, 2);
            AddClassDetails(detailBounds, actionBounds, mainClass, extraClasses, state);

            SingleComposer.EndChildElements();
            SingleComposer.Compose();
        }

        // Shows slot usage in the title for both dialog modes.
        private string BuildTitle(RebornPlayerClassState state)
        {
            return openedForRetraining
                ? Lang.Get("multiclassreborn:dialog-title-retrain", state.UsedSlots, state.AvailableSlots)
                : Lang.Get("multiclassreborn:dialog-title-choose", state.UsedSlots, state.AvailableSlots);
        }

        // Adds the paged class list on the left side of the dialog.
        private void AddClassButtons(List<CharacterClass> classList, ElementBounds listBounds, string mainClass, List<string> extraClasses)
        {
            int rowsPerPage = 16;
            int pageCount = Math.Max(1, (int)Math.Ceiling(classList.Count / (double)rowsPerPage));
            pageIndex = Math.Min(pageIndex, pageCount - 1);

            SingleComposer.AddInset(listBounds, 2);
            SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-previous"), OnPreviousPage, ElementBounds.Fixed(0, GuiStyle.TitleBarHeight, 230, 30), EnumButtonStyle.Small, "previousPage");
            SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-next"), OnNextPage, ElementBounds.Fixed(0, GuiStyle.TitleBarHeight + 530, 230, 30), EnumButtonStyle.Small, "nextPage");

            double y = GuiStyle.TitleBarHeight + 35;
            foreach (CharacterClass classDef in classList.Skip(pageIndex * rowsPerPage).Take(rowsPerPage))
            {
                string label = BuildClassButtonLabel(classDef.Code, mainClass, extraClasses);
                CairoFont buttonFont = BuildClassButtonFont(classDef.Code, mainClass, extraClasses);
                string buttonKey = "class-" + classDef.Code;
                SingleComposer.AddButton(label, () => SelectClass(classDef.Code), ElementBounds.Fixed(0, y, 230, 30), buttonFont, EnumButtonStyle.Small, buttonKey);
                y += 30;
            }
        }

        // Prefixes the selected class without changing the localized class name.
        private string BuildClassButtonLabel(string classCode, string mainClass, List<string> extraClasses)
        {
            string className = ClassTraitTextUtil.GetClassName(classCode);
            if (classCode == selectedClassCode) return "> " + className;
            return "  " + className;
        }

        // Colors base and learned classes so the list can be scanned quickly.
        private CairoFont BuildClassButtonFont(string classCode, string mainClass, List<string> extraClasses)
        {
            CairoFont font = CairoFont.WhiteSmallText();
            if (classCode.Equals(mainClass, StringComparison.OrdinalIgnoreCase)) return font.WithColor(ColorUtil.Hex2Doubles("#79a9ff"));
            if (extraClasses.Contains(classCode)) return font.WithColor(ColorUtil.Hex2Doubles("#ffd36a"));

            return font;
        }

        // Chooses the correct action button for the selected class and dialog mode.
        private void AddClassDetails(ElementBounds detailBounds, ElementBounds actionBounds, string mainClass, List<string> extraClasses, RebornPlayerClassState state)
        {
            if (selectedClassCode == null || !classSystem.Ledger.ClassByCode.TryGetValue(selectedClassCode, out CharacterClass classDef))
            {
                return;
            }

            bool isMainClass = classDef.Code.Equals(mainClass, StringComparison.OrdinalIgnoreCase);
            bool isLearned = extraClasses.Contains(classDef.Code);
            bool isCommoner = mainClass.Equals("commoner", StringComparison.OrdinalIgnoreCase);
            bool canLearn = !isMainClass && !isLearned && state.UsedSlots < state.AvailableSlots;
            bool canForgetExtra = !isMainClass && isLearned;
            bool canForgetMain = isMainClass && state.AllowsBaseClassForgetting && !isCommoner;
            bool canChooseBase = isCommoner && state.AllowsCommonerBaseClassChoice && !isMainClass && !classDef.Code.Equals("commoner", StringComparison.OrdinalIgnoreCase);

            AddScrollableClassDetails(BuildClassDetailText(classDef, isMainClass, isLearned, extraClasses, state, detailBounds), detailBounds);

            if (pendingForgetClassCode == classDef.Code)
            {
                AddForgetConfirmation(actionBounds, classDef, isMainClass, state);
                return;
            }

            if (openedForRetraining)
            {
                if (canForgetExtra || canForgetMain)
                {
                    SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-forget"), () => ConfirmForget(classDef.Code), actionBounds, EnumButtonStyle.Normal, "forgetClass");
                    return;
                }

                SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-cannot-forget"), () => false, actionBounds, EnumButtonStyle.Small, "blockedForgetClass");
                return;
            }

            if (canChooseBase)
            {
                SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-set-base-class"), () => SendClassCommand("setbase", classDef.Code), actionBounds, EnumButtonStyle.Normal, "setBaseClass");
                return;
            }

            if (canLearn)
            {
                SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-learn"), () => SendClassCommand("add", classDef.Code), actionBounds, EnumButtonStyle.Normal, "learnClass");
                return;
            }

            if (isLearned)
            {
                SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-forget"), () => ConfirmForget(classDef.Code), actionBounds, EnumButtonStyle.Normal, "forgetClass");
                return;
            }

            if (!isMainClass)
            {
                SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-need-class-slot"), () => false, actionBounds, EnumButtonStyle.Small, "blockedClass");
            }
        }

        // Requires a second click before sending the destructive forget command.
        private void AddForgetConfirmation(ElementBounds actionBounds, CharacterClass classDef, bool isMainClass, RebornPlayerClassState state)
        {
            ElementBounds cancelBounds = ElementBounds.Fixed(actionBounds.fixedX, actionBounds.fixedY, 235, 30);
            ElementBounds forgetBounds = ElementBounds.Fixed(actionBounds.fixedX + 255, actionBounds.fixedY, 235, 30);

            SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-cancel"), CancelForget, cancelBounds, EnumButtonStyle.Small, "cancelForget");
            SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-forget"), () => SendClassCommand("confirmforget", classDef.Code), forgetBounds, EnumButtonStyle.Normal, "confirmForget");
        }

        // Richtext does not report a useful height until we measure explicit lines.
        private void AddScrollableClassDetails(string detailText, ElementBounds detailBounds)
        {
            CairoFont font = CairoFont.WhiteSmallText();
            double textWidth = detailBounds.fixedWidth - DetailScrollbarWidth - DetailScrollbarPadding;
            double contentHeight = ClassTraitTextUtil.MeasureExplicitTextHeight(detailText, font, 1);
            ElementBounds clipBounds = ElementBounds.Fixed(detailBounds.fixedX, detailBounds.fixedY, textWidth, detailBounds.fixedHeight);
            ElementBounds textBounds = ElementBounds.Fixed(0, 0, textWidth, contentHeight);

            SingleComposer.BeginClip(clipBounds);
            SingleComposer.AddRichtext(detailText, font, textBounds, "classDetails");
            SingleComposer.EndClip();

            if (contentHeight <= detailBounds.fixedHeight) return;

            ElementBounds scrollbarBounds = ElementBounds.Fixed(detailBounds.fixedX + textWidth + DetailScrollbarPadding, detailBounds.fixedY, DetailScrollbarWidth, detailBounds.fixedHeight);
            SingleComposer.AddVerticalScrollbar(OnClassDetailsScroll, scrollbarBounds, "classDetailsScrollbar");
            SingleComposer.OnComposed += () => SingleComposer.GetScrollbar("classDetailsScrollbar")?.SetHeights((float)detailBounds.fixedHeight, (float)contentHeight);
        }

        // Moves the detail richtext inside the clipped panel.
        private void OnClassDetailsScroll(float dy)
        {
            ElementBounds bounds = SingleComposer?.GetRichtext("classDetails")?.Bounds;
            if (bounds == null) return;

            bounds.fixedY = -dy;
            bounds.CalcWorldBounds();
        }

        // Builds the selected class preview with scaled extra-class stat values.
        private string BuildClassDetailText(CharacterClass classDef, bool isMainClass, bool isLearned, List<string> extraClasses, RebornPlayerClassState state, ElementBounds detailBounds)
        {
            CairoFont font = CairoFont.WhiteSmallText();
            double maxWidth = detailBounds.fixedWidth - DetailScrollbarWidth - DetailScrollbarPadding;
            bool showScaledValues = !isMainClass;
            HashSet<string> appliedStatKeys = BuildPreviewAppliedStatKeys(classDef, isMainClass, isLearned, extraClasses, state);
            StringBuilder text = new StringBuilder();
            text.AppendLine($"<strong><font size=\"18\">{ClassTraitTextUtil.GetClassName(classDef.Code)}</font></strong>");
            text.AppendLine();

            bool hasStatusLine = false;
            if (isMainClass)
            {
                text.AppendLine(Lang.Get("multiclassreborn:dialog-status-main-class"));
                hasStatusLine = true;
            }

            if (isLearned)
            {
                text.AppendLine(Lang.Get("multiclassreborn:dialog-status-extra-learned"));
                hasStatusLine = true;
            }

            if (!isMainClass && !isLearned && state.UsedSlots >= state.AvailableSlots)
            {
                text.AppendLine(Lang.Get("multiclassreborn:dialog-status-need-slot"));
                hasStatusLine = true;
            }

            if (hasStatusLine) text.AppendLine();

            if (classDef.Traits == null || classDef.Traits.Length == 0)
            {
                text.AppendLine(Lang.Get("multiclassreborn:dialog-no-listed-traits"));
                return text.ToString();
            }

            text.AppendLine(Lang.Get("multiclassreborn:dialog-traits-heading"));

            foreach (string traitCode in classDef.Traits)
            {
                if (!classSystem.Ledger.TraitByCode.TryGetValue(traitCode, out Trait trait)) continue;

                text.AppendLine($"  {ClassTraitTextUtil.BuildTraitNameText(trait)}");

                if (trait.Attributes != null)
                {
                    foreach (KeyValuePair<string, double> stat in trait.Attributes)
                    {
                        bool isApplied = ClassTraitTextUtil.IsAppliedExtraStat(appliedStatKeys, traitCode, stat.Key);
                        ClassTraitTextUtil.AppendWrappedBullet(text, ClassTraitTextUtil.BuildStatText(stat, state.ExtraClassScale, showScaledValues, isApplied), font, maxWidth);
                    }
                }

                string descriptionKey = "traitdesc-" + traitCode;
                string description = Lang.GetIfExists(descriptionKey);
                if (ClassTraitTextUtil.HasVisibleLocalizedText(description, descriptionKey))
                {
                    ClassTraitTextUtil.AppendWrappedBullet(text, description, font, maxWidth);
                }
            }

            return text.ToString();
        }

        // Preview the selected class as if it were learned, but only for extra classes.
        private HashSet<string> BuildPreviewAppliedStatKeys(CharacterClass classDef, bool isMainClass, bool isLearned, List<string> extraClasses, RebornPlayerClassState state)
        {
            if (isMainClass) return null;

            List<string> previewClasses = new List<string>(extraClasses);
            if (!isLearned && !previewClasses.Contains(classDef.Code))
            {
                previewClasses.Add(classDef.Code);
            }

            return ClassTraitTextUtil.BuildAppliedExtraStatKeys(
                classSystem,
                previewClasses,
                state.OnlyApplyBestPositiveTraitBonus,
                state.OnlyApplyWorstNegativeTraitPenalty);
        }

        // Selects a class and clears any pending forget confirmation.
        private bool SelectClass(string classCode)
        {
            selectedClassCode = classCode;
            pendingForgetClassCode = null;
            ComposeDialog();
            return true;
        }

        // Arms the selected class for the second forget click.
        private bool ConfirmForget(string classCode)
        {
            pendingForgetClassCode = classCode;
            ComposeDialog();
            return true;
        }

        // Leaves retraining mode without sending a command.
        private bool CancelForget()
        {
            pendingForgetClassCode = null;
            ComposeDialog();
            return true;
        }

        // Pages the class list backward.
        private bool OnPreviousPage()
        {
            pageIndex = Math.Max(0, pageIndex - 1);
            ComposeDialog();
            return true;
        }

        // Pages the class list forward.
        private bool OnNextPage()
        {
            pageIndex++;
            ComposeDialog();
            return true;
        }

        // Sends the selected class action through the existing chat command path.
        private bool SendClassCommand(string action, string classCode)
        {
            clientApi.SendChatMessage($"/multiclass {action} {classCode}");
            pendingForgetClassCode = null;

            // Chat commands complete after the packet round-trip; refresh once state catches up.
            clientApi.Event.RegisterCallback(_ => RefreshAfterClassCommand(), 500);
            return true;
        }

        // Rebuilds the dialog and Traits tab after server state syncs back.
        private void RefreshAfterClassCommand()
        {
            RebornPlayerClassState state = new RebornPlayerClassState(clientApi.World.Player.Entity);
            if (ShouldReturnToLearning(state)) openedForRetraining = false;

            ComposeDialog();
            CharacterTraitsTabPatch.RefreshOpenTraitsTab();
        }

        // Switches back to learning when retraining no longer has anything useful to do.
        private bool ShouldReturnToLearning(RebornPlayerClassState state)
        {
            if (!openedForRetraining) return false;
            if (state.UsedSlots >= state.AvailableSlots) return false;
            if (state.ExtraClasses.Count == 0) return true;

            return state.RequiresGlyphs && !ClientHasRetrainingGlyphstone();
        }

        // Checks the client hotbar mirror before keeping the retraining view open.
        private bool ClientHasRetrainingGlyphstone()
        {
            foreach (IInventory inventory in clientApi.World.Player.InventoryManager.InventoriesOrdered)
            {
                string inventoryId = inventory?.InventoryID ?? "";
                string className = inventory?.ClassName ?? "";
                if (inventoryId.IndexOf("hotbar", StringComparison.OrdinalIgnoreCase) < 0
                    && className.IndexOf("hotbar", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                for (int slotId = 0; slotId < inventory.Count; slotId++)
                {
                    ItemSlot slot = inventory[slotId];
                    if (slot == null || slot.Empty) continue;
                    if (slot.Itemstack.Collectible.Code.Equals(new AssetLocation(RetrainingGlyphItemCode))) return true;
                }
            }

            return false;
        }
    }
}

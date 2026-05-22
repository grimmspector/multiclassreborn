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
        private const double MinimumDialogWidth = 760;
        private const double AbsoluteMinimumDialogWidth = 620;
        private const double MinimumDialogContentHeight = 560;
        private const double AbsoluteMinimumDialogContentHeight = 460;
        private const double MaximumDialogWidth = 1220;
        private const double MaximumDialogContentHeight = 700;
        private const double DialogWidthScreenFraction = 0.52;
        private const double DialogHeightScreenFraction = 0.68;
        private const double MaximumDialogPixelWidthFraction = 0.68;
        private const double MaximumDialogPixelHeightFraction = 0.82;
        private const double PreferredLeftPixelFraction = 0.33;
        private const double RightHudPixelReserveFraction = 0.18;
        private const double MinimumRightHudPixelReserve = 280;
        private const double DialogScreenEdgePadding = 18;
        private const double ClassListWidth = 205;
        private const double DetailMargin = 14;
        private const double ActionHeight = 30;
        private const double ActionTopPadding = 15;
        private const double ListButtonHeight = 30;
        private const double ClassRowHeight = 30;
        private const double WideDetailWrapThreshold = 650;
        private const double WideDetailWrapMultiplier = 1.25;
        private const double DetailScrollbarWidth = 14;
        private const double DetailScrollbarPadding = 4;
        private const string RetrainingGlyphItemCode = "multiclassreborn:retraining-glyphstone";

        private readonly ICoreClientAPI clientApi;
        private readonly MulticlassRebornModSystem classSystem;

        private string selectedClassCode;
        private string pendingForgetClassCode;
        private bool openedForRetraining;
        private int pageIndex;
        private int lastFrameWidth;
        private int lastFrameHeight;

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

        // Opens the learning view from the hotkey or Aptitude Glyphstone.
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

            double contentWidth = CalculateDialogWidth();
            double contentHeight = CalculateDialogContentHeight();
            double detailPanelWidth = contentWidth - ClassListWidth;
            double detailX = ClassListWidth + DetailMargin;
            double detailWidth = detailPanelWidth - (DetailMargin * 2);
            double detailHeight = contentHeight - ActionHeight - ActionTopPadding - 10;

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle)
                .WithFixedAlignmentOffset(CalculateDialogXOffset(contentWidth), 0);
            ElementBounds backgroundBounds = ElementBounds.Fixed(0, GuiStyle.TitleBarHeight, contentWidth, contentHeight);
            ElementBounds listBounds = ElementBounds.Fixed(0, GuiStyle.TitleBarHeight, ClassListWidth, contentHeight);
            ElementBounds detailPanelBounds = ElementBounds.Fixed(ClassListWidth, GuiStyle.TitleBarHeight, detailPanelWidth, contentHeight);
            ElementBounds detailBounds = ElementBounds.Fixed(detailX, GuiStyle.TitleBarHeight + 5, detailWidth, detailHeight);
            ElementBounds actionBounds = ElementBounds.Fixed(detailX, GuiStyle.TitleBarHeight + detailHeight + ActionTopPadding, detailWidth, ActionHeight);

            SingleComposer = clientApi.Gui
                .CreateCompo("multiclass-reborn-picker", dialogBounds)
                .AddShadedDialogBG(backgroundBounds, false)
                .AddDialogTitleBar(BuildTitle(state), () => TryClose())
                .BeginChildElements();

            AddClassButtons(classList, listBounds, contentHeight, mainClass, extraClasses);
            SingleComposer.AddInset(detailPanelBounds, 2);
            AddClassDetails(detailBounds, actionBounds, mainClass, extraClasses, state);

            SingleComposer.EndChildElements();
            SingleComposer.Compose();
            lastFrameWidth = clientApi.Render.FrameWidth;
            lastFrameHeight = clientApi.Render.FrameHeight;
        }

        // Rebuilds while open so a window resize cannot strand controls off-screen.
        public override void OnRenderGUI(float deltaTime)
        {
            if (lastFrameWidth != clientApi.Render.FrameWidth || lastFrameHeight != clientApi.Render.FrameHeight)
            {
                ComposeDialog();
            }

            base.OnRenderGUI(deltaTime);
        }

        // Expands the picker on wide displays while preserving the proven compact layout.
        private double CalculateDialogWidth()
        {
            double guiScale = GuiElement.scaled(1);
            double guiWidth = clientApi.Render.FrameWidth / guiScale;
            double screenSafeMaximum = Math.Max(1, (clientApi.Render.FrameWidth - (DialogScreenEdgePadding * 2)) / guiScale);
            double pixelLimitedMaximum = clientApi.Render.FrameWidth * MaximumDialogPixelWidthFraction / guiScale;
            double preferredLeft = clientApi.Render.FrameWidth * PreferredLeftPixelFraction;
            double rightReserve = Math.Max(MinimumRightHudPixelReserve, clientApi.Render.FrameWidth * RightHudPixelReserveFraction);
            double preferredRightSafeMaximum = (clientApi.Render.FrameWidth - preferredLeft - rightReserve) / guiScale;
            double maximumWidth = Math.Min(Math.Min(MaximumDialogWidth, pixelLimitedMaximum), screenSafeMaximum);
            if (preferredRightSafeMaximum >= MinimumDialogWidth)
            {
                maximumWidth = Math.Min(maximumWidth, preferredRightSafeMaximum);
            }

            double minimumWidth = Math.Min(MinimumDialogWidth, maximumWidth);

            return GameMath.Clamp(guiWidth * DialogWidthScreenFraction, minimumWidth, maximumWidth);
        }

        // Uses more vertical room when available without overflowing smaller screens.
        private double CalculateDialogContentHeight()
        {
            double guiScale = GuiElement.scaled(1);
            double guiHeight = clientApi.Render.FrameHeight / guiScale;
            double pixelLimitedMaximum = clientApi.Render.FrameHeight * MaximumDialogPixelHeightFraction / guiScale;
            double maximumHeight = Math.Min(MaximumDialogContentHeight, pixelLimitedMaximum);
            double minimumHeight = Math.Min(MinimumDialogContentHeight, Math.Max(AbsoluteMinimumDialogContentHeight, maximumHeight));

            return GameMath.Clamp(guiHeight * DialogHeightScreenFraction, minimumHeight, maximumHeight);
        }

        // Nudges right when possible to reduce character-window overlap without hitting the HUD.
        private double CalculateDialogXOffset(double contentWidth)
        {
            double guiScale = GuiElement.scaled(1);
            double dialogPixelWidth = contentWidth * guiScale;
            double centeredLeft = (clientApi.Render.FrameWidth - dialogPixelWidth) / 2;
            double preferredLeft = clientApi.Render.FrameWidth * PreferredLeftPixelFraction;
            double rightReserve = Math.Max(MinimumRightHudPixelReserve, clientApi.Render.FrameWidth * RightHudPixelReserveFraction);
            double furthestSafeLeft = clientApi.Render.FrameWidth - dialogPixelWidth - rightReserve;
            double preferredTargetLeft = Math.Max(centeredLeft, Math.Min(preferredLeft, furthestSafeLeft));
            double maximumLeft = Math.Max(DialogScreenEdgePadding, clientApi.Render.FrameWidth - dialogPixelWidth - DialogScreenEdgePadding);
            double targetLeft = GameMath.Clamp(preferredTargetLeft, DialogScreenEdgePadding, maximumLeft);

            return (targetLeft - centeredLeft) / guiScale;
        }

        // Shows slot usage in the title for both dialog modes.
        private string BuildTitle(RebornPlayerClassState state)
        {
            return openedForRetraining
                ? Lang.Get("multiclassreborn:dialog-title-retrain", state.UsedSlots, state.AvailableSlots)
                : Lang.Get("multiclassreborn:dialog-title-choose", state.UsedSlots, state.AvailableSlots);
        }

        // Adds the paged class list on the left side of the dialog.
        private void AddClassButtons(List<CharacterClass> classList, ElementBounds listBounds, double contentHeight, string mainClass, List<string> extraClasses)
        {
            int rowsPerPage = Math.Max(1, (int)Math.Floor((contentHeight - 70) / ClassRowHeight));
            int pageCount = Math.Max(1, (int)Math.Ceiling(classList.Count / (double)rowsPerPage));
            pageIndex = Math.Min(pageIndex, pageCount - 1);

            SingleComposer.AddInset(listBounds, 2);
            SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-previous"), OnPreviousPage, ElementBounds.Fixed(0, GuiStyle.TitleBarHeight, ClassListWidth, ListButtonHeight), EnumButtonStyle.Small, "previousPage");
            SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-next"), OnNextPage, ElementBounds.Fixed(0, GuiStyle.TitleBarHeight + contentHeight - ListButtonHeight, ClassListWidth, ListButtonHeight), EnumButtonStyle.Small, "nextPage");

            double y = GuiStyle.TitleBarHeight + 35;
            foreach (CharacterClass classDef in classList.Skip(pageIndex * rowsPerPage).Take(rowsPerPage))
            {
                string label = BuildClassButtonLabel(classDef.Code, mainClass, extraClasses);
                CairoFont buttonFont = BuildClassButtonFont(classDef.Code, mainClass, extraClasses);
                string buttonKey = "class-" + classDef.Code;
                SingleComposer.AddButton(label, () => SelectClass(classDef.Code), ElementBounds.Fixed(0, y, ClassListWidth, ClassRowHeight), buttonFont, EnumButtonStyle.Small, buttonKey);
                y += ClassRowHeight;
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

            AddScrollableClassDetails(BuildClassDetailText(classDef, isMainClass, isLearned, canChooseBase, extraClasses, state, detailBounds), detailBounds);

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
            double buttonGap = 20;
            double buttonWidth = Math.Max(120, (actionBounds.fixedWidth - buttonGap) / 2);
            ElementBounds cancelBounds = ElementBounds.Fixed(actionBounds.fixedX, actionBounds.fixedY, buttonWidth, 30);
            ElementBounds forgetBounds = ElementBounds.Fixed(actionBounds.fixedX + buttonWidth + buttonGap, actionBounds.fixedY, buttonWidth, 30);

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

        // Builds the selected class preview with scaled values only for extra classes.
        private string BuildClassDetailText(CharacterClass classDef, bool isMainClass, bool isLearned, bool canChooseBase, List<string> extraClasses, RebornPlayerClassState state, ElementBounds detailBounds)
        {
            CairoFont font = CairoFont.WhiteSmallText();
            double maxWidth = detailBounds.fixedWidth - DetailScrollbarWidth - DetailScrollbarPadding;
            double wrapWidthMultiplier = CalculateDetailWrapWidthMultiplier(maxWidth);
            bool showScaledValues = !isMainClass && !canChooseBase;
            HashSet<string> appliedStatKeys = BuildPreviewAppliedStatKeys(classDef, isMainClass, isLearned, canChooseBase, extraClasses, state);
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
                        ClassTraitTextUtil.AppendWrappedBullet(text, ClassTraitTextUtil.BuildStatText(stat, state.ExtraClassScale, showScaledValues, isApplied), font, maxWidth, wrapWidthMultiplier);
                    }
                }

                string descriptionKey = "traitdesc-" + traitCode;
                string description = Lang.GetIfExists(descriptionKey);
                if (ClassTraitTextUtil.HasVisibleLocalizedText(description, descriptionKey))
                {
                    ClassTraitTextUtil.AppendWrappedBullet(text, description, font, maxWidth, wrapWidthMultiplier);
                }
            }

            return text.ToString();
        }

        // Large picker panes have extra visual room that Cairo measures conservatively.
        private static double CalculateDetailWrapWidthMultiplier(double maxWidth)
        {
            if (maxWidth <= WideDetailWrapThreshold) return 1;

            double wideRoom = Math.Min(1, (maxWidth - WideDetailWrapThreshold) / WideDetailWrapThreshold);
            return 1 + ((WideDetailWrapMultiplier - 1) * wideRoom);
        }

        // Preview duplicate filtering only when the selected class would be learned.
        private HashSet<string> BuildPreviewAppliedStatKeys(CharacterClass classDef, bool isMainClass, bool isLearned, bool canChooseBase, List<string> extraClasses, RebornPlayerClassState state)
        {
            if (isMainClass) return null;
            if (canChooseBase) return null;

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

            return state.RequiresGlyphs && !state.RetrainFree && !ClientHasRetrainingGlyphstone();
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

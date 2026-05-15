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
using Vintagestory.GameContent;

#nullable disable

namespace multiclassreborn
{
    internal class ClassPickerDialog : GuiDialog
    {
        private const double DetailScrollbarWidth = 14;
        private const double DetailScrollbarPadding = 4;

        private readonly ICoreClientAPI clientApi;
        private readonly MulticlassRebornModSystem classSystem;

        private string selectedClassCode;
        private string pendingForgetClassCode;
        private bool openedForRetraining;
        private int pageIndex;

        public override string ToggleKeyCombinationCode => "multiclassgui";

        public ClassPickerDialog(ICoreClientAPI capi, MulticlassRebornModSystem classSystem) : base(capi)
        {
            clientApi = capi;
            this.classSystem = classSystem;
        }

        public override bool TryOpen()
        {
            openedForRetraining = false;
            pendingForgetClassCode = null;
            ComposeDialog();
            return base.TryOpen();
        }

        public bool OpenForRetraining()
        {
            openedForRetraining = true;
            pendingForgetClassCode = null;
            ComposeDialog();
            return IsOpened() || base.TryOpen();
        }

        /// <summary>
        /// Rebuilds the dialog from watched player state each time it opens.
        /// </summary>
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
            ElementBounds detailBounds = ElementBounds.Fixed(250, GuiStyle.TitleBarHeight + 35, 490, 475);
            ElementBounds actionBounds = ElementBounds.Fixed(250, GuiStyle.TitleBarHeight + 520, 490, 30);

            SingleComposer = clientApi.Gui
                .CreateCompo("multiclass-reborn-picker", dialogBounds)
                .AddShadedDialogBG(backgroundBounds)
                .AddDialogTitleBar(BuildTitle(state), () => TryClose())
                .BeginChildElements();

            AddClassButtons(classList, listBounds);
            AddClassDetails(detailBounds, actionBounds, mainClass, extraClasses, state);

            SingleComposer.EndChildElements();
            SingleComposer.Compose();
        }

        private string BuildTitle(RebornPlayerClassState state)
        {
            return openedForRetraining
                ? Lang.Get("multiclassreborn:dialog-title-retrain", state.UsedSlots, state.AvailableSlots)
                : Lang.Get("multiclassreborn:dialog-title-choose", state.UsedSlots, state.AvailableSlots);
        }

        private void AddClassButtons(List<CharacterClass> classList, ElementBounds listBounds)
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
                string label = BuildClassButtonLabel(classDef.Code);
                string buttonKey = "class-" + classDef.Code;
                SingleComposer.AddSmallButton(label, () => SelectClass(classDef.Code), ElementBounds.Fixed(0, y, 230, 30), EnumButtonStyle.Small, buttonKey);
                y += 30;
            }
        }

        private string BuildClassButtonLabel(string classCode)
        {
            string className = ClassTraitTextUtil.GetClassName(classCode);
            if (classCode == selectedClassCode) return "> " + className;
            return "  " + className;
        }

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

            AddScrollableClassDetails(BuildClassDetailText(classDef, isMainClass, isLearned, state, detailBounds), detailBounds);

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

        private void AddForgetConfirmation(ElementBounds actionBounds, CharacterClass classDef, bool isMainClass, RebornPlayerClassState state)
        {
            ElementBounds textBounds = ElementBounds.Fixed(actionBounds.fixedX, actionBounds.fixedY - 56, actionBounds.fixedWidth, 50);
            ElementBounds cancelBounds = ElementBounds.Fixed(actionBounds.fixedX, actionBounds.fixedY, 235, 30);
            ElementBounds forgetBounds = ElementBounds.Fixed(actionBounds.fixedX + 255, actionBounds.fixedY, 235, 30);

            SingleComposer.AddRichtext(BuildForgetConfirmationText(classDef, isMainClass, state), CairoFont.WhiteSmallText(), textBounds, "forgetConfirmationText");
            SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-cancel"), CancelForget, cancelBounds, EnumButtonStyle.Small, "cancelForget");
            SingleComposer.AddSmallButton(Lang.Get("multiclassreborn:button-forget"), () => SendClassCommand("confirmforget", classDef.Code), forgetBounds, EnumButtonStyle.Normal, "confirmForget");
        }

        private string BuildForgetConfirmationText(CharacterClass classDef, bool isMainClass, RebornPlayerClassState state)
        {
            string className = ClassTraitTextUtil.GetClassName(classDef.Code);

            if (isMainClass)
            {
                string costText = state.RequiresGlyphs ? Lang.Get("multiclassreborn:dialog-forget-cost-retraining") + " " : "";
                string recoveryText = state.AllowsCommonerBaseClassChoice
                    ? Lang.Get("multiclassreborn:dialog-forget-base-recovery-choice")
                    : Lang.Get("multiclassreborn:dialog-forget-base-recovery-earn");
                return Lang.Get("multiclassreborn:dialog-confirm-forget-base", className, costText, recoveryText);
            }

            return state.RequiresGlyphs
                ? Lang.Get("multiclassreborn:dialog-confirm-forget-extra-cost", className)
                : Lang.Get("multiclassreborn:dialog-confirm-forget-extra", className);
        }

        /// <summary>
        /// Adds clipped, measured class details with a scrollbar when needed.
        /// </summary>
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

        /// <summary>
        /// Scrolls the class detail pane.
        /// </summary>
        private void OnClassDetailsScroll(float dy)
        {
            ElementBounds bounds = SingleComposer?.GetRichtext("classDetails")?.Bounds;
            if (bounds == null) return;

            bounds.fixedY = -dy;
            bounds.CalcWorldBounds();
        }

        private string BuildClassDetailText(CharacterClass classDef, bool isMainClass, bool isLearned, RebornPlayerClassState state, ElementBounds detailBounds)
        {
            CairoFont font = CairoFont.WhiteSmallText();
            double maxWidth = detailBounds.fixedWidth - DetailScrollbarWidth - DetailScrollbarPadding;
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
                        ClassTraitTextUtil.AppendWrappedBullet(text, Lang.Get($"charattribute-{stat.Key}-{stat.Value}"), font, maxWidth);
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

        private bool SelectClass(string classCode)
        {
            selectedClassCode = classCode;
            pendingForgetClassCode = null;
            ComposeDialog();
            return true;
        }

        private bool ConfirmForget(string classCode)
        {
            pendingForgetClassCode = classCode;
            ComposeDialog();
            return true;
        }

        private bool CancelForget()
        {
            pendingForgetClassCode = null;
            ComposeDialog();
            return true;
        }

        private bool OnPreviousPage()
        {
            pageIndex = Math.Max(0, pageIndex - 1);
            ComposeDialog();
            return true;
        }

        private bool OnNextPage()
        {
            pageIndex++;
            ComposeDialog();
            return true;
        }

        private bool SendClassCommand(string action, string classCode)
        {
            clientApi.SendChatMessage($"/multiclass {action} {classCode}");
            pendingForgetClassCode = null;
            clientApi.Event.RegisterCallback(_ => ComposeDialog(), 500);
            return true;
        }
    }
}

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
        private readonly ICoreClientAPI clientApi;
        private readonly MulticlassRebornModSystem classSystem;

        private string selectedClassCode;
        private int pageIndex;

        public override string ToggleKeyCombinationCode => "multiclassgui";

        public ClassPickerDialog(ICoreClientAPI capi, MulticlassRebornModSystem classSystem) : base(capi)
        {
            clientApi = capi;
            this.classSystem = classSystem;
        }

        public override bool TryOpen()
        {
            ComposeDialog();
            return base.TryOpen();
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
            ElementBounds detailBounds = ElementBounds.Fixed(250, GuiStyle.TitleBarHeight + 10, 490, 500);
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
            string title = $"Choose Extra Classes - Slots: {state.UsedSlots}/{state.AvailableSlots}";

            if (state.RequiresRunes)
            {
                title += $" | Forget Credits: {state.RemovalCredits}";
            }

            return title;
        }

        private void AddClassButtons(List<CharacterClass> classList, ElementBounds listBounds)
        {
            int rowsPerPage = 16;
            int pageCount = Math.Max(1, (int)Math.Ceiling(classList.Count / (double)rowsPerPage));
            pageIndex = Math.Min(pageIndex, pageCount - 1);

            SingleComposer.AddInset(listBounds, 2);
            SingleComposer.AddSmallButton("Previous", OnPreviousPage, ElementBounds.Fixed(0, GuiStyle.TitleBarHeight, 230, 30), EnumButtonStyle.Small, "previousPage");
            SingleComposer.AddSmallButton("Next", OnNextPage, ElementBounds.Fixed(0, GuiStyle.TitleBarHeight + 530, 230, 30), EnumButtonStyle.Small, "nextPage");

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
            if (classCode == selectedClassCode) return "> " + classCode;
            return "  " + classCode;
        }

        private void AddClassDetails(ElementBounds detailBounds, ElementBounds actionBounds, string mainClass, List<string> extraClasses, RebornPlayerClassState state)
        {
            if (selectedClassCode == null || !classSystem.Ledger.ClassByCode.TryGetValue(selectedClassCode, out CharacterClass classDef))
            {
                return;
            }

            bool isMainClass = classDef.Code.Equals(mainClass, StringComparison.OrdinalIgnoreCase);
            bool isLearned = extraClasses.Contains(classDef.Code);
            bool canLearn = !isMainClass && !isLearned && state.UsedSlots < state.AvailableSlots;
            bool canForget = !isMainClass && isLearned && (!state.RequiresRunes || state.RemovalCredits > 0);

            SingleComposer.AddRichtext(BuildClassDetailText(classDef, isMainClass, isLearned, state), CairoFont.WhiteSmallText(), detailBounds, "classDetails");

            if (canLearn)
            {
                SingleComposer.AddSmallButton("Learn", () => SendClassCommand("add", classDef.Code), actionBounds, EnumButtonStyle.Normal, "learnClass");
                return;
            }

            if (isLearned)
            {
                // Free-forget servers must still show the active Forget button.
                // Only rune-required servers need a positive forget credit.
                string label = canForget ? "Forget" : "Need Forget Rune";
                ActionConsumable action = canForget ? () => SendClassCommand("remove", classDef.Code) : () => false;
                SingleComposer.AddSmallButton(label, action, actionBounds, canForget ? EnumButtonStyle.Normal : EnumButtonStyle.Small, "forgetClass");
                return;
            }

            if (!isMainClass)
            {
                SingleComposer.AddSmallButton("Need Class Slot", () => false, actionBounds, EnumButtonStyle.Small, "blockedClass");
            }
        }

        private string BuildClassDetailText(CharacterClass classDef, bool isMainClass, bool isLearned, RebornPlayerClassState state)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine($"<strong><font size=\"18\">{classDef.Code}</font></strong>");
            text.AppendLine();

            if (isMainClass) text.AppendLine("<i>This is your main class.</i>");
            if (isLearned) text.AppendLine("<i>This extra class is learned.</i>");
            if (!isMainClass && !isLearned && state.UsedSlots >= state.AvailableSlots) text.AppendLine("<i>You need an open class slot.</i>");
            text.AppendLine();

            if (classDef.Traits == null || classDef.Traits.Length == 0)
            {
                text.AppendLine("<i>This class has no listed traits.</i>");
                return text.ToString();
            }

            text.AppendLine("<strong>Traits:</strong>");

            foreach (string traitCode in classDef.Traits)
            {
                text.AppendLine($"  <strong>{traitCode}</strong>");

                if (!classSystem.Ledger.TraitByCode.TryGetValue(traitCode, out Trait trait) || trait.Attributes == null) continue;

                foreach (KeyValuePair<string, double> stat in trait.Attributes)
                {
                    text.AppendLine($"    - {Lang.Get($"charattribute-{stat.Key}-{stat.Value}")}");
                }
            }

            return text.ToString();
        }

        private bool SelectClass(string classCode)
        {
            selectedClassCode = classCode;
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
            clientApi.Event.RegisterCallback(_ => ComposeDialog(), 500);
            return true;
        }
    }
}

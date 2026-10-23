using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private TrainCurrencyMessageMode _trainCurrencyMode;
    private string _trainCurrencyCopied = string.Empty;
    private string _trainCurrencyPreview = string.Empty;

    private void DrawTrainCurrency()
    {
        var summary = TrainCurrency.Calculate(_detector.Marks.Values);
        var overflows = TrainCurrency.FindOverflows(summary, _trainCurrencyWallet.Balances);
        ImGui.TextUnformatted($"{summary.MarkCount} living A ranks");
        if (summary.MarkCount == 0)
            ImGui.TextWrapped("Add living A ranks to the train to generate a currency message.");
        else ImGui.TextWrapped(string.Join(" · ", summary.Expansions));

        if (overflows.Count > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, HuntTheme.Danger);
            ImGui.TextWrapped("Over cap: " + string.Join(", ", overflows.Select(x => x.Name)) + ". Spend before the remaining kills.");
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(TrainCurrencyWarningText(overflows));
        }
        else if (summary.MarkCount > 0 && _trainCurrencyWallet.Balances is null)
            ImGui.TextColored(HuntTheme.Muted, "Waiting for your currency balances.");

        if (ImGui.BeginTable("Train currency totals", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Currency", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("Gain", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Start ≤", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableHeadersRow();
            foreach (var amount in summary.Amounts)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(amount.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(TrainCurrency.Number(amount.Gained));
                ImGui.TableNextColumn();
                if (amount.NeedsSpendingDuringTrain) ImGui.TextColored(HuntTheme.Warning, "Spend");
                else ImGui.TextUnformatted(TrainCurrency.Number(amount.MaximumStartingBalance));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(amount.NeedsSpendingDuringTrain
                    ? $"Rewards exceed the {TrainCurrency.Number(amount.HoldingCap)} holding cap. Spend during the train even if starting at zero."
                    : $"Maximum starting balance before the {TrainCurrency.Number(amount.HoldingCap)} holding cap.");
            }
            ImGui.EndTable();
        }
        ImGui.TextWrapped("Full-credit estimates for eligible jobs; current tomes require level 100. Start ≤ leaves room below holding caps. The red warning uses your balance and remaining weekly allowance.");
        ImGui.Separator();

        DrawTrainCurrencyMode("Starting balance", TrainCurrencyMessageMode.StartingBalance);
        DrawTrainCurrencyMode("Amount gained", TrainCurrencyMessageMode.AmountGained);
        if (ImGui.CollapsingHeader("Message template"))
        {
            ImGui.TextWrapped("$c = currencies, $n = living A ranks. Your template saves automatically and controls the copied wording.");
            ImGui.SetNextItemWidth(-1);
            var template = _config.TrainCurrencyTemplate;
            if (ImGui.InputTextWithHint("##Currency template", TrainCurrency.DefaultTemplate(_trainCurrencyMode),
                ref template, 2000))
            {
                _config.TrainCurrencyTemplate = template;
                _config.Save();
                _trainCurrencyCopied = string.Empty;
            }
            if (HuntUi.Button("currency-reset", "Reset template", quiet: true))
            {
                _config.TrainCurrencyTemplate = string.Empty;
                _config.Save();
                _trainCurrencyCopied = string.Empty;
            }
        }
        var message = TrainCurrency.CreateMessage(summary, _trainCurrencyMode, _config.TrainCurrencyTemplate);
        if (_trainCurrencyPreview != message.Plain)
        {
            _trainCurrencyPreview = message.Plain;
            _trainCurrencyCopied = string.Empty;
        }
        ImGui.TextWrapped(message.Plain);
        if (!message.HasCurrencyPlaceholder)
            ImGui.TextColored(HuntTheme.Warning, "Include $c in the template to copy totals.");
        else if (summary.MarkCount > 0 && !message.CanCopyShout)
            ImGui.TextWrapped($"Shout uses {Encoding.UTF8.GetByteCount(message.Shout)}/{TrainCurrency.ChatByteLimit} bytes. Shorten the template to copy it for chat.");

        DrawTrainCurrencyCopy("Copy text", message.Plain, message.CanCopyPlain);
        TrainControlSameLine(HuntUi.ButtonWidth("Party", FontAwesomeIcon.Copy));
        DrawTrainCurrencyCopy("Party", message.Party, message.CanCopyParty);
        TrainControlSameLine(HuntUi.ButtonWidth("Shout", FontAwesomeIcon.Copy));
        DrawTrainCurrencyCopy("Shout", message.Shout, message.CanCopyShout);
        if (_trainCurrencyCopied.Length > 0) ImGui.TextColored(HuntTheme.Muted, _trainCurrencyCopied);
    }

    private static string TrainCurrencyWarningText(IReadOnlyList<TrainCurrencyOverflow> overflows) =>
        "Remaining marks would exceed holding caps at full credit:\n" + string.Join("\n", overflows.Select(x =>
            $"{x.Name}: {TrainCurrency.Number(x.Held)} + {TrainCurrency.Number(x.Gained)} / {TrainCurrency.Number(x.HoldingCap)} ({TrainCurrency.Number(x.Lost)} over)"))
        + "\nAssumes eligible jobs; weekly allowance is included.";

    private void DrawTrainCurrencyMode(string label, TrainCurrencyMessageMode mode)
    {
        if (!ImGui.RadioButton(label, _trainCurrencyMode == mode)) return;
        _trainCurrencyMode = mode;
        _trainCurrencyCopied = string.Empty;
    }

    private void DrawTrainCurrencyCopy(string label, string text, bool enabled)
    {
        ImGui.BeginDisabled(!enabled);
        if (HuntUi.Button("currency-copy-" + label, label, FontAwesomeIcon.Copy, quiet: true,
            tooltip: label == "Copy text" ? "Copy the message to clipboard"
                : $"Copy the message with /{(label == "Party" ? "p" : "sh")} to clipboard"))
        {
            ImGui.SetClipboardText(text);
            _trainCurrencyCopied = $"{label} copied to clipboard.";
        }
        ImGui.EndDisabled();
    }
}

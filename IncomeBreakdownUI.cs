#nullable disable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Api.Components;
using BTD_Mod_Helper.Api.Enums;
using Il2Cpp; // NK_TextMeshProUGUI and other game types with no original namespace live here
using Il2CppAssets.Scripts.Unity.UI_New;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;
using Il2CppAssets.Scripts.Unity.UI_New.Pause;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IncomeBreakdown;

/// <summary>
/// Builds the custom pause-menu button and the income breakdown overlay. The overlay is a
/// self-contained ModHelper UI (not a ModHelper settings screen) parented to the always-on-top
/// CommonForegroundScreen so it draws above the pause menu.
/// </summary>
public static class IncomeBreakdownUI
{
    // --- Layout constants (BTD6 UI units) ---
    private const float PanelW = 1600f;
    private const float PanelH = 1540f;
    private const float Pad = 50f;
    private const float InnerW = PanelW - 2 * Pad;  // 1500
    private const float RowW = 1400f;
    private const float RowH = 84f;
    private const float AmountW = 580f;

    private static ModHelperPanel overlay;

    public static bool IsOpen => overlay != null;

    // ------------------------------------------------------------------ pause button

    /// <summary>
    /// Duplicates a native pause-menu button so it matches the game's style, relabels it
    /// "Income", and wires it to toggle our overlay. Safe to call on every pause-screen open;
    /// it only adds the button once.
    /// </summary>
    public static void AddPauseButton(PauseScreen pauseScreen)
    {
        try
        {
            if (pauseScreen == null || pauseScreen.sidePanel == null) return;

            var group = pauseScreen.sidePanel.transform.GetChild(0).gameObject;
            if (group == null) return;
            if (group.transform.Find("IncomeBreakdownBtn") != null) return; // already added this session

            // Prefer cloning the "Hotkeys" button; fall back to the last button in the group.
            var template = group.GetComponentInChildrenByName<RectTransform>("Hotkeys");
            var source = template != null
                ? template.gameObject
                : group.transform.GetChild(group.transform.childCount - 1).gameObject;

            var btn = source.Duplicate(group.transform);
            btn.name = "IncomeBreakdownBtn";

            var button = btn.GetComponent<Button>();
            if (button != null) button.SetOnClick(Toggle);

            var text = btn.GetComponentInChildren<NK_TextMeshProUGUI>();
            if (text != null)
            {
                text.AutoLocalize = false;
                text.SetText("Income");
            }

            // Swap the icon to a cash sprite (best-effort; harmless if the asset name changes).
            try
            {
                var image = btn.GetComponentInChildrenByName<Image>("Image");
                if (image != null) image.SetSprite(VanillaSprites.MoreCashIcon);
            }
            catch { /* keep the cloned icon if the cash sprite isn't found */ }
        }
        catch (Exception e)
        {
            ModHelper.Warning<IncomeBreakdown>($"Failed to add Income pause button: {e}");
        }
    }

    // ------------------------------------------------------------------ open / close

    public static void Toggle()
    {
        if (IsOpen) Hide();
        else Build();
    }

    public static void Hide()
    {
        if (overlay != null)
        {
            UnityEngine.Object.Destroy(overlay.gameObject);
            overlay = null;
        }
    }

    // ------------------------------------------------------------------ overlay

    private static void Build()
    {
        Hide();

        var parent = CommonForegroundScreen.instance;
        if (parent == null) return;

        // Full-screen dim that also catches clicks (clicking outside the panel closes it).
        overlay = ModHelperPanel.Create(new Info("IncomeBreakdownOverlay", InfoPreset.FillParent));
        overlay.SetParent(parent.transform);
        var dim = overlay.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        // Clicking the dimmed area (outside the panel) closes the overlay.
        var dimButton = overlay.AddComponent<Button>();
        dimButton.transition = Selectable.Transition.None;
        dimButton.SetOnClick(Hide);
        overlay.RectTransform.SetAsLastSibling();

        var panel = overlay.AddPanel(new Info("Main", PanelW, PanelH),
            VanillaSprites.MainBGPanelBlue, RectTransform.Axis.Vertical, 22, (int) Pad);
        // A no-op button on the panel consumes interior clicks so they don't bubble up to the
        // dim button behind it (which would otherwise close the overlay when clicking inside).
        var panelBlocker = panel.AddComponent<Button>();
        panelBlocker.transition = Selectable.Transition.None;

        BuildHeader(panel);
        BuildSummary(panel);
        BuildScroll(panel);
        BuildFooter(panel);
    }

    private static void BuildHeader(ModHelperPanel panel)
    {
        var header = panel.AddPanel(new Info("Header", InnerW, 140), null, RectTransform.Axis.Horizontal, 16);
        var title = header.AddText(new Info("Title") { Height = 140, FlexWidth = 1 },
            "Income Breakdown", 96, TextAlignmentOptions.MidlineLeft);
        title.Text.color = Color.white;
        header.AddButton(new Info("CloseX", 110, 110), VanillaSprites.CloseBtn,
            new System.Action(Hide));
    }

    private static void BuildSummary(ModHelperPanel panel)
    {
        int round = CurrentRound();
        double net = IncomeTracker.TotalEarned - IncomeTracker.TotalSpent;
        string line1 = $"Total earned this game:  {Money(IncomeTracker.TotalEarned)}";
        string line2 = (round > 0 ? $"Round {round}      " : "") +
                       $"Spent {Money(IncomeTracker.TotalSpent)}      Net {Money(net)}";

        var summary = panel.AddText(new Info("Summary", InnerW, 190), line1 + "\n" + line2,
            58, TextAlignmentOptions.Center);
        summary.Text.color = new Color(0.85f, 1f, 0.85f);
    }

    private static void BuildScroll(ModHelperPanel panel)
    {
        var scroll = panel.AddScrollPanel(new Info("Scroll", InnerW, 100) { FlexHeight = 1 },
            RectTransform.Axis.Vertical, VanillaSprites.BlueInsertPanel, 8, 28);
        var content = scroll.ScrollContent;

        if (IncomeTracker.TotalEarned <= 0)
        {
            var empty = content.AddText(new Info("Empty", RowW, 200),
                "No income recorded yet.\nStart (or play) a game and pop some bloons!",
                52, TextAlignmentOptions.Center);
            empty.Text.color = new Color(0.85f, 0.85f, 0.85f);
            return;
        }

        // --- By category ---
        AddHeaderRow(content, "By Source");
        foreach (IncomeCategory cat in Enum.GetValues(typeof(IncomeCategory)))
        {
            double value = IncomeTracker.CategoryTotals[cat];
            if (value <= 0) continue;
            AddRow(content, CategoryName(cat), value, IncomeTracker.TotalEarned, CategoryColor(cat));
        }

        // --- By tower ---
        if (IncomeTracker.TowerTotals.Count > 0)
        {
            AddSpacer(content, 24);
            AddHeaderRow(content, "By Tower");
            foreach (var kv in IncomeTracker.TowerTotals.OrderByDescending(kv => kv.Value))
            {
                if (kv.Value <= 0) continue;
                AddRow(content, Prettify(kv.Key), kv.Value, IncomeTracker.TotalEarned,
                    new Color(1f, 0.9f, 0.55f));
            }
        }
    }

    private static void BuildFooter(ModHelperPanel panel)
    {
        var footer = panel.AddPanel(new Info("Footer", InnerW, 160), null, RectTransform.Axis.Horizontal, 30);
        if (footer.LayoutGroup != null) footer.LayoutGroup.childAlignment = TextAnchor.MiddleCenter;

        var reset = footer.AddButton(new Info("Reset", 480, 130), VanillaSprites.BlueBtnLong,
            new System.Action(OnReset));
        var resetTxt = reset.AddText(new Info("ResetTxt", InfoPreset.FillParent), "Reset Totals", 50,
            TextAlignmentOptions.Center);
        resetTxt.Text.color = Color.white;
    }

    private static void OnReset()
    {
        IncomeTracker.Reset();
        Build(); // rebuild to show the cleared totals
    }

    // ------------------------------------------------------------------ row helpers

    private static void AddHeaderRow(ModHelperPanel content, string text)
    {
        var h = content.AddText(new Info(text + "Hdr", RowW, 96), text, 66, TextAlignmentOptions.MidlineLeft);
        h.Text.color = new Color(1f, 0.92f, 0.6f);
    }

    private static void AddSpacer(ModHelperPanel content, float height)
    {
        content.AddPanel(new Info("Spacer", RowW, height));
    }

    private static void AddRow(ModHelperPanel content, string label, double amount, double total, Color color)
    {
        var row = content.AddPanel(new Info(label + "Row", RowW, RowH), null, RectTransform.Axis.Horizontal, 12);
        var name = row.AddText(new Info("Name") { Height = RowH, FlexWidth = 1 }, label, 50,
            TextAlignmentOptions.MidlineLeft);
        name.Text.color = Color.white;

        double pct = total > 0 ? amount / total * 100.0 : 0;
        var amt = row.AddText(new Info("Amt", AmountW, RowH), $"{Money(amount)}    {pct:F1}%", 50,
            TextAlignmentOptions.MidlineRight);
        amt.Text.color = color;
    }

    // ------------------------------------------------------------------ misc helpers

    private static int CurrentRound()
    {
        try
        {
            var bridge = InGame.Bridge;
            if (bridge != null) return bridge.GetCurrentRound() + 1;
        }
        catch { /* not in a game */ }
        return 0;
    }

    private static string Money(double v) =>
        (v < 0 ? "-$" : "$") + Math.Abs(v).ToString("N0");

    /// <summary>"BananaFarm" -> "Banana Farm", "DartMonkey" -> "Dart Monkey".</summary>
    private static string Prettify(string baseId) =>
        Regex.Replace(baseId ?? "", "(?<=[a-z0-9])(?=[A-Z])", " ");

    private static string CategoryName(IncomeCategory cat) => cat switch
    {
        IncomeCategory.BloonPops => "Bloon Pops",
        IncomeCategory.FarmsAndBanks => "Farms & Banks",
        IncomeCategory.Abilities => "Abilities",
        IncomeCategory.Powers => "Powers",
        IncomeCategory.EndOfRound => "End of Round",
        IncomeCategory.Eco => "Eco",
        IncomeCategory.Quests => "Quests",
        IncomeCategory.CoOpTransfers => "Co-op Transfers",
        IncomeCategory.SellRefunds => "Sell Refunds",
        IncomeCategory.Geraldo => "Geraldo",
        IncomeCategory.MapSpecial => "Map / Special",
        _ => "Other"
    };

    private static Color CategoryColor(IncomeCategory cat) => cat switch
    {
        IncomeCategory.BloonPops => new Color(1f, 0.45f, 0.4f),
        IncomeCategory.FarmsAndBanks => new Color(0.5f, 0.92f, 0.45f),
        IncomeCategory.Abilities => new Color(0.78f, 0.56f, 1f),
        IncomeCategory.Powers => new Color(0.4f, 0.85f, 1f),
        IncomeCategory.EndOfRound => new Color(1f, 0.85f, 0.3f),
        IncomeCategory.Eco => new Color(0.4f, 0.95f, 0.8f),
        IncomeCategory.Quests => new Color(1f, 0.7f, 0.3f),
        IncomeCategory.CoOpTransfers => new Color(0.55f, 0.72f, 1f),
        IncomeCategory.SellRefunds => new Color(0.82f, 0.82f, 0.82f),
        IncomeCategory.Geraldo => new Color(0.85f, 0.72f, 0.5f),
        IncomeCategory.MapSpecial => new Color(1f, 0.62f, 0.85f),
        _ => new Color(0.72f, 0.72f, 0.72f)
    };
}

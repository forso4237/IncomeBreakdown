global using BTD_Mod_Helper.Extensions;
using BTD_Mod_Helper;
using MelonLoader;
using IncomeBreakdown;
using Il2CppAssets.Scripts.Simulation;
using Il2CppAssets.Scripts.Simulation.Towers;
using Il2CppAssets.Scripts.Unity.UI_New.Pause;

[assembly: MelonInfo(typeof(IncomeBreakdown.IncomeBreakdown), ModHelperData.Name, ModHelperData.Version, ModHelperData.RepoOwner)]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6")]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6-Epic")]

namespace IncomeBreakdown;

/// <summary>
/// Main mod entry point. Tracks every cash gain via the OnCashAdded hook, resets the running
/// totals when a match starts, and injects an "Income" button into the in-game pause menu that
/// opens a custom breakdown panel.
/// </summary>
public class IncomeBreakdown : BloonsTD6Mod
{
    public override void OnApplicationStart()
    {
        ModHelper.Msg<IncomeBreakdown>("Income Breakdown loaded! Pause in-game and tap \"Income\" to see where your cash came from.");
    }

    /// <summary>
    /// Postfix on Simulation.AddCash. This is the single chokepoint through which every cash
    /// change in a game flows, and the game hands us its own classification of the cash
    /// (<see cref="Simulation.CashType"/> + <see cref="Simulation.CashSource"/>) plus the tower
    /// that earned it. We forward it all to the tracker.
    /// </summary>
    public override void OnCashAdded(double amount, Simulation.CashType from, int cashIndex,
        Simulation.CashSource source, Tower tower)
    {
        IncomeTracker.Record(amount, from, cashIndex, source, tower);
    }

    /// <summary>Totals are per-game, so wipe them whenever a new match begins.</summary>
    public override void OnMatchStart()
    {
        IncomeTracker.Reset();
    }

    /// <summary>Add (once) our button to the pause menu's side panel every time it opens.</summary>
    public override void OnPauseScreenOpened(PauseScreen pauseScreen)
    {
        IncomeBreakdownUI.AddPauseButton(pauseScreen);
    }

    /// <summary>Tidy up the overlay so it never lingers on screen once play resumes.</summary>
    public override void OnPauseScreenClosed(PauseScreen pauseScreen)
    {
        IncomeBreakdownUI.Hide();
    }
}

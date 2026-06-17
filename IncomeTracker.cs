#nullable disable
using System;
using System.Collections.Generic;
using Il2CppAssets.Scripts.Simulation;
using Il2CppAssets.Scripts.Simulation.Towers;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;

namespace IncomeBreakdown;

/// <summary>
/// Friendly, player-facing income categories, listed in the order they should be displayed.
/// </summary>
public enum IncomeCategory
{
    BloonPops,
    FarmsAndBanks,
    Abilities,
    Powers,
    EndOfRound,
    Eco,
    Quests,
    CoOpTransfers,
    SellRefunds,
    Geraldo,
    MapSpecial,
    Other
}

/// <summary>
/// Accumulates cash earned during the current game, broken down both by <see cref="IncomeCategory"/>
/// and by the tower that earned it. All of this is plain managed state - it just listens to the
/// game's cash events; it never changes anything in the simulation.
/// </summary>
public static class IncomeTracker
{
    /// <summary>Positive cash in, per category, for the current game.</summary>
    public static readonly Dictionary<IncomeCategory, double> CategoryTotals = new();

    /// <summary>
    /// Positive cash in, keyed by the earning tower's baseId (e.g. "BananaFarm", "DartMonkey").
    /// In BTD6 the cash from popping a bloon is credited to the tower that popped it, so this is
    /// effectively an "income by tower" view. Only includes cash the game attributes to a tower.
    /// </summary>
    public static readonly Dictionary<string, double> TowerTotals = new();

    /// <summary>Total positive cash earned this game.</summary>
    public static double TotalEarned { get; private set; }

    /// <summary>Total cash spent this game (sum of negative cash changes, expressed as positive).</summary>
    public static double TotalSpent { get; private set; }

    static IncomeTracker()
    {
        Reset();
    }

    public static void Reset()
    {
        CategoryTotals.Clear();
        foreach (IncomeCategory cat in Enum.GetValues(typeof(IncomeCategory)))
        {
            CategoryTotals[cat] = 0d;
        }

        TowerTotals.Clear();
        TotalEarned = 0d;
        TotalSpent = 0d;
    }

    public static void Record(double amount, Simulation.CashType from, int cashIndex,
        Simulation.CashSource source, Tower tower)
    {
        // Only track the local player's cash so co-op team-mates aren't counted.
        if (cashIndex != LocalPlayerIndex()) return;

        if (amount < 0)
        {
            TotalSpent += -amount;
            return;
        }
        if (amount <= 0) return;

        var category = Categorize(from, source, tower);
        CategoryTotals[category] += amount; // every category key is initialised in Reset()
        TotalEarned += amount;

        if (tower != null && tower.towerModel != null)
        {
            string id = tower.towerModel.baseId;
            if (!string.IsNullOrEmpty(id))
            {
                TowerTotals.TryGetValue(id, out double current);
                TowerTotals[id] = current + amount;
            }
        }
    }

    /// <summary>
    /// Maps the game's own cash classification (plus the earning tower) onto a friendly category.
    /// Source-specific cases are checked first because they're unambiguous, then the broad cash
    /// type, then a tower-identity check splits passive farm/bank income from everything else.
    /// </summary>
    public static IncomeCategory Categorize(Simulation.CashType from, Simulation.CashSource source, Tower tower)
    {
        switch (source)
        {
            case Simulation.CashSource.EcoEarned: return IncomeCategory.Eco;
            case Simulation.CashSource.CoopTransferedCash: return IncomeCategory.CoOpTransfers;
            case Simulation.CashSource.TowerSold:
            case Simulation.CashSource.PropSold: return IncomeCategory.SellRefunds;
            case Simulation.CashSource.GeraldoPurchase: return IncomeCategory.Geraldo;
            case Simulation.CashSource.MapInteractableUsed:
            case Simulation.CashSource.CorvusNourishment: return IncomeCategory.MapSpecial;
            case Simulation.CashSource.BankDeposit: return IncomeCategory.FarmsAndBanks;
            case Simulation.CashSource.QuestAwarded: return IncomeCategory.Quests;
        }

        switch (from)
        {
            case Simulation.CashType.Ability: return IncomeCategory.Abilities;
            case Simulation.CashType.Powers: return IncomeCategory.Powers;
            case Simulation.CashType.EndOfRound: return IncomeCategory.EndOfRound;
            case Simulation.CashType.QuestAwarded: return IncomeCategory.Quests;
            case Simulation.CashType.CoopCash: return IncomeCategory.CoOpTransfers;
        }

        // Passive farm/bank income is credited to the Banana Farm tower line.
        if (IsCashTower(tower)) return IncomeCategory.FarmsAndBanks;

        // Everything else (the standard per-pop cash, CashType.Normal / NonTransformed) is "pops".
        return IncomeCategory.BloonPops;
    }

    /// <summary>
    /// True for the whole Banana Farm line. Monkey Bank, Banana Central, Monkey Wall Street,
    /// Banana Research Facility, etc. are all upgrades of the Banana Farm and share its baseId.
    /// </summary>
    private static bool IsCashTower(Tower tower)
    {
        if (tower == null || tower.towerModel == null) return false;
        return tower.towerModel.baseId == "BananaFarm";
    }

    private static int LocalPlayerIndex()
    {
        try
        {
            var bridge = InGame.Bridge;
            if (bridge != null) return bridge.MyPlayerNumber;
        }
        catch
        {
            // Not in a game / bridge not ready yet.
        }
        return 0;
    }
}

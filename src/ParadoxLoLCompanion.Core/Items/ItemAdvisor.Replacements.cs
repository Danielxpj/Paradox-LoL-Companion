using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Core.Items;

public sealed partial class ItemAdvisor
{
    private bool IsFullBuild(Player me)
    {
        var slots = me.Items.Where(i => i.Slot is >= 0 and <= 5).ToList();
        return slots.Count == 6 && slots.Select(i => i.Slot).Distinct().Count() == 6
            && slots.All(slot => _data.ItemById(slot.ItemID) is { } item
                && !item.Consumable && !item.HasTag("Trinket") && !item.HasTag("Lane")
                && (item.IsBoots ? item.GoldTotal >= _config.MinFinishedBootsGold
                    : !item.BuildsIntoSomething
                        && (item.GoldTotal >= _config.MinCompletedItemGold || item.HasTag("GoldPer"))));
    }

    /// <summary>
    /// Compare keeping each item with a legal replacement in the SAME five-slot
    /// context. Purchase-order bonuses and projected income cannot justify a sale.
    /// Return one swap; the next tick re-evaluates the build after any actual purchase.
    /// </summary>
    private List<ItemRecommendation> ReplacementRecommendations(GameState state, Player me,
        ChampionProfile profile, TeamThreat threat, IReadOnlyList<StaticItem> candidates,
        ChampionBuildStats? stats, int mapNumber, double currentCrit,
        double ahead, double behind, int completedCount)
    {
        var gold = state.ActivePlayer?.CurrentGold ?? 0;
        var weights = WeightsFor(profile.Archetype);
        var allies = state.AllPlayers.Where(p => p.Team == me.Team && p != me).ToList();
        var allyProfiles = allies.Select(_profiler.Profile).ToList();
        var allyGw = allies.SelectMany(p => p.Items)
            .Any(i => _data.ItemById(i.ItemID)?.AppliesGrievousWounds == true);
        var coreNames = MetaSpine.Build(_data, _config, stats, mapNumber)
            .Where(s => s.IsCore).Select(s => MetaSpine.NormalizedName(s.Item.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var originalIds = me.Items.Select(i => i.ItemID).ToList();
        var originalTree = new HashSet<int>(originalIds);
        foreach (var id in originalIds)
            AddBuildTree(id, originalTree);
        var originalNames = originalIds.Select(_data.ItemById).OfType<StaticItem>()
            .Select(i => MetaSpine.NormalizedName(i.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var protectedNames = _config.ReplacementProtectedItemIds.Select(_data.ItemById).OfType<StaticItem>()
            .Select(i => MetaSpine.NormalizedName(i.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var boughtCrit = originalIds.Sum(id => _data.ItemById(id)?.CritChance ?? 0);
        var critMultiplier = boughtCrit > 0 ? Math.Max(1, currentCrit / boughtCrit) : 1;
        ItemRecommendation? best = null;

        foreach (var slot in me.Items.Where(i => i.Slot is >= 0 and <= 5))
        {
            var old = _data.ItemById(slot.ItemID)!;
            // Permanent stacks/evolutions and build-defining amplifiers are not
            // valued fully by live data. Preserve them, boots and quest items.
            if (old.SellGold <= 0 || !old.Purchasable || old.IsBoots
                || old.HasTag("GoldPer") || old.HasTag("Jungle")
                || HasStackingInvestment(old) || _config.SnowballItemIds.Contains(old.Id)
                || _config.ReplacementProtectedItemIds.Contains(old.Id)
                || protectedNames.Contains(MetaSpine.NormalizedName(old.Name)))
                continue;

            var remaining = me.Items.Where(i => !ReferenceEquals(i, slot))
                .SelectMany(i => Enumerable.Repeat(i.ItemID, Math.Max(i.Count, 1))).ToList();
            var retained = remaining.Select(_data.ItemById).OfType<StaticItem>().ToList();
            // Remove the sold item's stats before scoring BOTH choices. Otherwise
            // its armor/crit/GW would incorrectly make keeping it seem redundant.
            var need = NeedsFrom(StatsWithout(state.ActivePlayer?.ChampionStats, old),
                me.Level, profile, allyProfiles);
            var crit = Math.Max(0, currentCrit - old.CritChance * critMultiplier);
            var teamGw = allyGw || retained.Any(i => i.AppliesGrievousWounds);
            var keep = ScoreItem(old, profile, threat, weights, teamGw, need,
                null, me.ChampionName, crit, ahead, behind, completedCount);
            var saleLoss = Math.Max(0, old.GoldTotal - old.SellGold);
            var margin = saleLoss / 1000.0 * _config.ReplacementLossPenalty
                + (coreNames.Contains(MetaSpine.NormalizedName(old.Name)) ? _config.ReplacementCoreMargin : 0);

            foreach (var candidate in candidates)
            {
                if (originalTree.Contains(candidate.Id)
                    || originalNames.Contains(MetaSpine.NormalizedName(candidate.Name))
                    || !candidate.Purchasable || candidate.IsBoots || candidate.Consumable
                    || HasStackingInvestment(candidate) || _config.SnowballItemIds.Contains(candidate.Id)
                    || !ReplacementFitsInventory(candidate, retained)
                    || LosesRequiredCoverage(old, candidate, retained, threat, profile))
                    continue;

                // A replacement changes exactly one slot. Upgrading/consuming a
                // retained finished item belongs to the normal purchase planner.
                if (BuildPathPlanner.ConsumedComponents(_data, candidate, remaining).Count > 0)
                    continue;
                var buy = ScoreItem(candidate, profile, threat, weights, teamGw, need,
                    null, me.ChampionName, crit, ahead, behind, completedCount);
                var gain = buy.Score - keep.Score;
                var situationalGain = buy.Situational - keep.Situational;
                var adjustedGain = gain - margin;
                if (adjustedGain < _config.ReplacementMinGain
                    || situationalGain < _config.ReplacementMinSituationalGain)
                    continue;

                var netCost = Math.Max(0, candidate.GoldTotal - old.SellGold);
                var missing = (int)Math.Ceiling(Math.Max(0, netCost - gold));
                var reasons = buy.Reasons.ToList();
                reasons.Insert(0, $"Replace {old.Name} — {ReplacementReason(old, candidate, threat)}");
                reasons.Add($"sale returns {GoldFmt(old.SellGold)}g; replacement costs {GoldFmt(netCost)}g net");
                if (missing > 0)
                    reasons.Add($"keep {old.Name} until you have {GoldFmt(missing)}g more; buy the replacement in full");
                // Never spend the refund on a component and leave a finished slot
                // weaker. The purchase is offered only when the whole swap is funded.
                var purchase = new PurchasePlan(candidate, candidate.GoldTotal,
                    missing == 0 ? candidate : null, missing == 0 ? candidate.GoldTotal : 0);
                var recommendation = new ItemRecommendation(candidate, adjustedGain, reasons, purchase, missing)
                {
                    Category = buy.Category,
                    Replacement = new ItemReplacement(old, slot.Slot, old.SellGold, netCost, gain, situationalGain),
                    ScoreBreakdown = new ItemScoreBreakdown(
                        buy.Breakdown.Core - keep.Breakdown.Core,
                        buy.Breakdown.Offense - keep.Breakdown.Offense,
                        buy.Breakdown.Defense - keep.Breakdown.Defense, 0,
                        buy.Breakdown.Penalty - keep.Breakdown.Penalty + margin),
                };
                if (best is null || recommendation.Score > best.Score
                    || (recommendation.Score == best.Score && netCost < best.Replacement!.NetCost))
                    best = recommendation;
            }
        }
        return best is null || _config.MaxRecommendations <= 0 ? new() : new() { best };
    }

    private bool HasStackingInvestment(StaticItem item)
    {
        var tree = new HashSet<int> { item.Id };
        AddBuildTree(item.Id, tree);
        return tree.Overlaps(_config.StackingComponentIds)
            || _config.ItemEvolutions.ContainsKey(item.Id)
            || _config.ItemEvolutions.ContainsValue(item.Id);
    }

    private bool ReplacementFitsInventory(StaticItem candidate, IReadOnlyList<StaticItem> retained)
    {
        if (BlockedByOwnedPassive(candidate, retained)
            || (candidate.AppliesGrievousWounds && retained.Any(i => i.AppliesGrievousWounds)))
            return false;
        foreach (var group in _config.ExclusiveItemGroups)
        {
            var names = group.Select(_data.ItemById).OfType<StaticItem>()
                .Select(i => MetaSpine.NormalizedName(i.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool InGroup(StaticItem item) => group.Contains(item.Id)
                || names.Contains(MetaSpine.NormalizedName(item.Name));
            if (InGroup(candidate) && retained.Any(InGroup))
                return false;
        }
        return true;
    }

    private static bool LosesRequiredCoverage(StaticItem old, StaticItem candidate,
        IReadOnlyList<StaticItem> retained, TeamThreat threat, ChampionProfile profile)
    {
        bool Loses(bool needed, Func<StaticItem, bool> covers) =>
            needed && covers(old) && !covers(candidate) && !retained.Any(covers);
        return Loses(threat.Sustain > MuGate, i => i.AppliesGrievousWounds)
            || Loses(threat.Suppression > MuGate, i => i.RemovesCc)
            || Loses(threat.ShieldThreat > MuGate, i => i.BreaksShields)
            || Loses(threat.CritThreat > MuGate, i => i.ReducesCritDamage)
            || Loses(threat.CcThreat > MuGate, i => i.GrantsTenacity || i.RemovesCc)
            || Loses(threat.ArmorStack > MuGate && profile.DealsPhysical,
                i => i.HasTag("ArmorPenetration") && !i.HasLethality)
            || Loses(threat.MrStack > MuGate && profile.DealsMagical, i => i.HasTag("MagicPenetration"));
    }

    private static ChampionStats? StatsWithout(ChampionStats? live, StaticItem item) =>
        live is null || live.MaxHealth <= 0 ? null : new ChampionStats
        {
            MaxHealth = Math.Max(1, live.MaxHealth - item.Health),
            Armor = Math.Max(0, live.Armor - item.Armor),
            MagicResist = Math.Max(0, live.MagicResist - item.SpellBlock),
            AttackDamage = Math.Max(0, live.AttackDamage - item.AttackDamage),
            AbilityPower = Math.Max(0, live.AbilityPower - item.AbilityPower),
        };

    private static string ReplacementReason(StaticItem old, StaticItem candidate, TeamThreat threat)
    {
        if (old.HasTag("SpellBlock") && !old.HasTag("Armor") && candidate.HasTag("Armor")
            && threat.PhysicalSkew > MuGate)
            return $"armor now answers {threat.TopPhysicalName} better than this magic-resist slot";
        if (old.HasTag("Armor") && !old.HasTag("SpellBlock") && candidate.HasTag("SpellBlock")
            && threat.MagicalSkew > MuGate)
            return $"magic resist now answers {threat.TopMagicalName} better than this armor slot";
        if (old.HasLethality && candidate.HasTag("ArmorPenetration") && !candidate.HasLethality)
            return "percent penetration is more useful against the enemy's stacked armor";
        return "this slot can answer the current threats more effectively";
    }
}

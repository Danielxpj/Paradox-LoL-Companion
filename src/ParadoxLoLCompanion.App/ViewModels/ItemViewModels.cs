using System.Windows.Media;
using ParadoxLoLCompanion.App.Mvvm;
using ParadoxLoLCompanion.App.Theme;
using ParadoxLoLCompanion.Core.Advice;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Util;

namespace ParadoxLoLCompanion.App.ViewModels;

/// <summary>Tarjeta tipo "scorecard": etiqueta fija + valor que se actualiza.</summary>
public sealed class ScorecardViewModel : ObservableObject
{
    private string _value = "—";

    public ScorecardViewModel(string title, Brush accent)
    {
        Title = title;
        Accent = accent;
    }

    public string Title { get; }
    public Brush Accent { get; }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

/// <summary>Un ícono de item del inventario de un jugador (con nombre para el tooltip).</summary>
public sealed record OwnedItemIconViewModel(string? IconUrl, string Name);

/// <summary>Fila de la tabla de jugadores.</summary>
public sealed class PlayerRowViewModel
{
    public PlayerRowViewModel(Player p, IStaticData catalog)
    {
        Champion = string.IsNullOrEmpty(p.ChampionName) ? "—" : p.ChampionName;
        Summoner = !string.IsNullOrEmpty(p.SummonerName) ? p.SummonerName
                 : !string.IsNullOrEmpty(p.RiotIdGameName) ? p.RiotIdGameName
                 : p.RiotId;
        Level = p.Level;
        Kda = p.Scores.Kda;
        Cs = p.Scores.CreepScore;
        TeamBrush = p.Team == "CHAOS" ? Palette.TeamChaos : Palette.TeamOrder;
        ChampionIconUrl = DdragonImages.ChampionIcon(catalog.Version,
            catalog.ResolveChampion(p.ChampionName, p.RawChampionName)?.Key);
        // Inventario como strip de íconos (tooltip = nombre); el texto queda de respaldo.
        ItemIcons = p.Items
            .Where(i => i.ItemID > 0)
            .OrderBy(i => i.Slot)
            .Select(i => new OwnedItemIconViewModel(
                DdragonImages.ItemIcon(catalog.Version, i.ItemID),
                string.IsNullOrEmpty(i.DisplayName) ? $"#{i.ItemID}" : i.DisplayName))
            .ToList();
        Items = string.Join(", ", p.Items.Where(i => !string.IsNullOrEmpty(i.DisplayName)).Select(i => i.DisplayName));
    }

    public string Champion { get; }
    public string Summoner { get; }
    public int Level { get; }
    public string Kda { get; }
    public int Cs { get; }
    public Brush TeamBrush { get; }
    public string? ChampionIconUrl { get; }
    public IReadOnlyList<OwnedItemIconViewModel> ItemIcons { get; }
    public string Items { get; }
}

/// <summary>Fila del feed de consejos, con color según severidad.</summary>
public sealed class AdviceRowViewModel
{
    public AdviceRowViewModel(AdviceItem item)
    {
        Message = item.Message;
        Category = item.Category.ToString();
        Severity = item.Severity switch
        {
            AdviceSeverity.Important => Palette.Red,
            AdviceSeverity.Warning => Palette.Amber,
            _ => Palette.Blue,
        };
    }

    public string Message { get; }
    public string Category { get; }
    public Brush Severity { get; }
}

/// <summary>Fila del panel "Asesor de items": un item recomendado con costo y razones.</summary>
public sealed class ItemRecoRowViewModel
{
    public ItemRecoRowViewModel(Core.Items.ItemRecommendation reco, string catalogVersion, double gold)
    {
        Name = reco.Item.Name;
        IconUrl = DdragonImages.ItemIcon(catalogVersion, reco.Item.Id);
        Cost = reco.Item.GoldTotal.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        // Anillo radial: progreso del oro actual hacia el costo total del item.
        GoldFraction = System.Math.Clamp(gold / System.Math.Max(reco.Item.GoldTotal, 1), 0, 1);
        GoldPercent = (int)System.Math.Round(GoldFraction * 100) + "%";

        // Barra de acción "BUY NOW ▸": qué compra procede AHORA con este oro.
        if (reco.BlockedByFullInventory)
        {
            BuyBarText = "INVENTORY FULL ▸ SELL FIRST";
            BuyBarCost = "";
            AffordBrush = Palette.Red;
        }
        else if (reco.Affordable)
        {
            // La barra "BUY NOW" muestra lo que PAGÁS ahora (faltante), no el precio de
            // lista: con componentes ya comprados terminar el item cuesta menos.
            BuyBarText = "BUY NOW ✓ FULL ITEM";
            BuyBarCost = reco.Purchase.RemainingCost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            AffordBrush = Palette.Green;
        }
        else if (reco.Purchase.NextComponent is { } component)
        {
            BuyBarText = $"BUY NOW ▸ {component.Name}";
            BuyBarCost = reco.Purchase.NextComponentCost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            AffordBrush = Palette.Green;
        }
        else
        {
            BuyBarText = $"SAVE {reco.MissingGold.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} MORE";
            BuyBarCost = "";
            AffordBrush = Palette.Amber;
        }
        // Una razón por línea: en el sub-recuadro "WHY" la lista se lee mejor que encadenada.
        Reasons = string.Join("\n", reco.Reasons.Select(r => "· " + r));

        // Prioridad + categoría: por qué está en este lugar del orden (coherencia).
        (CategoryLabel, CategoryBrush) = reco.Category switch
        {
            RecommendationCategory.Counter => ("COUNTER", Palette.Red),
            RecommendationCategory.Defense => ("DEFENSIVE", Palette.Cyan),
            RecommendationCategory.Spike => ("POWER SPIKE", Palette.Green),
            _ => ("CORE", Palette.Blue),
        };
        Priority = (int)System.Math.Round(reco.Priority * 100) + "%";
    }

    public string Name { get; }
    public string? IconUrl { get; }
    public string Cost { get; }
    /// <summary>Progreso [0,1] del oro actual hacia el costo total (anillo radial).</summary>
    public double GoldFraction { get; }
    public string GoldPercent { get; }
    public string BuyBarText { get; }
    public string BuyBarCost { get; }
    public Brush AffordBrush { get; }
    public string Reasons { get; }
    /// <summary>Etiqueta de por qué está en este lugar del orden (CORE/COUNTER/DEFENSIVE/POWER SPIKE).</summary>
    public string CategoryLabel { get; }
    public Brush CategoryBrush { get; }
    /// <summary>Prioridad relativa a la recomendación principal ("100%" = la mejor).</summary>
    public string Priority { get; }
}

/// <summary>Fila del cheat-sheet de augments de Mayhem (overlay y MATCH tab).</summary>
public sealed class AugmentRowViewModel
{
    public AugmentRowViewModel(Core.Mayhem.AugmentSuggestion suggestion)
    {
        Id = suggestion.Id;
        Name = suggestion.Name;
        TierLabel = suggestion.TierLabel;
        RarityLabel = suggestion.Rarity.ToString().ToUpperInvariant();
        IconUrl = suggestion.IconUrl.Length == 0 ? null : suggestion.IconUrl;
        FitsMyChampion = suggestion.FitsMyChampion;
        FitChip = suggestion.FitsMyChampion ? "★ YOUR CHAMP" : "";
        TierBrush = suggestion.Tier switch
        {
            1 => Palette.Gold,
            2 => Palette.Green,
            _ => Palette.Muted,
        };
        RarityBrush = suggestion.Rarity switch
        {
            Core.Augments.AugmentRarity.Prismatic => Palette.Cyan,
            Core.Augments.AugmentRarity.Gold => Palette.Gold,
            _ => Palette.Muted,
        };
    }

    public int Id { get; }
    public string Name { get; }
    public string TierLabel { get; }
    public string RarityLabel { get; }
    public string? IconUrl { get; }
    public bool FitsMyChampion { get; }
    public string FitChip { get; }
    public Brush TierBrush { get; }
    public Brush RarityBrush { get; }
}

/// <summary>Augment ofrecido detectado por OCR, con su veredicto.</summary>
public sealed class OfferedAugmentRowViewModel
{
    public OfferedAugmentRowViewModel(Core.Augments.OfferedAugment offered)
    {
        Name = offered.Name;
        TierLabel = offered.TierLabel;
        IsBest = offered.IsBest;
        Verdict = offered.IsBest ? "◆ PICK THIS" : "";
        TierBrush = offered.Tier switch
        {
            1 => Palette.Gold,
            2 => Palette.Green,
            3 => Palette.Blue,
            null => Palette.Muted,
            _ => Palette.Amber,
        };
    }

    public string Name { get; }
    public string TierLabel { get; }
    public bool IsBest { get; }
    public string Verdict { get; }
    public Brush TierBrush { get; }
}

/// <summary>Tile del panel ENEMY X-RAY: retrato, KDA, rol y estado del enemigo.</summary>
public sealed class EnemyTileViewModel
{
    public string? IconUrl { get; init; }
    public string Name { get; init; } = "";
    public string Kda { get; init; } = "";
    /// <summary>Rol táctico (BURST/TANK/MARKSMAN/CC/…); "THREAT" reemplaza al rol si es el más fed.</summary>
    public string Tag { get; init; } = "";
    public bool IsTopThreat { get; init; }
    /// <summary>"18s" mientras está muerto; vacío si está vivo.</summary>
    public string RespawnText { get; init; } = "";
}

/// <summary>Una sugerencia de venta con su ícono (fila del panel de venta).</summary>
public sealed class SellRowViewModel
{
    public SellRowViewModel(Core.Items.SellSuggestion sell, string catalogVersion)
    {
        IconUrl = DdragonImages.ItemIcon(catalogVersion, sell.Item.Id);
        Text = $"Sell {sell.Item.Name} (+{sell.SellGold.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} g) — {sell.Reason}";
    }

    public string? IconUrl { get; }
    public string Text { get; }
}

/// <summary>Fila del asesor de banca: un intercambio sugerido con sus razones.</summary>
public sealed class BenchSuggestionRowViewModel
{
    public BenchSuggestionRowViewModel(Core.Draft.BenchSuggestion suggestion, string catalogVersion)
    {
        Name = suggestion.Champion.Name;
        IconUrl = DdragonImages.ChampionIcon(catalogVersion, suggestion.Champion.Key);
        Reasons = string.Join(" · ", suggestion.Reasons);
    }

    public string Name { get; }
    public string? IconUrl { get; }
    public string Reasons { get; }
}

/// <summary>
/// Opción del selector de build del panel de items: "Auto" (detección automática,
/// Archetype null) o un arquetipo forzado a mano por el jugador.
/// </summary>
public sealed class BuildRoleOptionViewModel : ObservableObject
{
    private readonly Action<BuildRoleOptionViewModel> _onSelected;
    private bool _isChecked;

    public BuildRoleOptionViewModel(string label, BuildArchetype? archetype,
        Action<BuildRoleOptionViewModel> onSelected, bool isChecked = false)
    {
        Label = label;
        Archetype = archetype;
        _onSelected = onSelected;
        _isChecked = isChecked;
    }

    public string Label { get; }
    public BuildArchetype? Archetype { get; }

    /// <summary>Two-way con el RadioButton; solo el paso a true dispara la selección.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value) && value)
                _onSelected(this);
        }
    }
}

/// <summary>Celda de champ select (una posición del draft).</summary>
public sealed class ChampCellViewModel
{
    public ChampCellViewModel(ChampSelectCell cell, bool isLocal, string? championName = null,
        string? iconUrl = null)
    {
        Position = string.IsNullOrEmpty(cell.AssignedPosition) ? "—" : cell.AssignedPosition;
        var champ = cell.DisplayChampionId;
        Champion = champ == 0 ? "(not picked)"
                 : !string.IsNullOrEmpty(championName) ? championName
                 : $"Champion #{champ}";
        IsLocal = isLocal;
        Accent = isLocal ? Palette.Green : Palette.Muted;
        IconUrl = iconUrl;
    }

    public string Position { get; }
    public string Champion { get; }
    public bool IsLocal { get; }
    public Brush Accent { get; }
    public string? IconUrl { get; }
}

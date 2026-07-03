using System.Windows.Media;
using LoLAdvisor.App.Mvvm;
using LoLAdvisor.App.Theme;
using LoLAdvisor.Core.Advice;
using LoLAdvisor.Core.Items;
using LoLAdvisor.Core.Models;
using LoLAdvisor.Core.Objectives;
using LoLAdvisor.Core.Util;

namespace LoLAdvisor.App.ViewModels;

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

/// <summary>Tarjeta de timer de objetivo (Dragón / Barón), actualizada en sitio.</summary>
public sealed class ObjectiveTimerViewModel : ObservableObject
{
    private string _clock = "—";
    private string _detail = "";
    private Brush _accent = Palette.Muted;

    public ObjectiveTimerViewModel(string label) => Label = label;

    public string Label { get; }

    public string Clock { get => _clock; set => SetProperty(ref _clock, value); }
    public string Detail { get => _detail; set => SetProperty(ref _detail, value); }
    public Brush Accent { get => _accent; set => SetProperty(ref _accent, value); }

    public void Update(ObjectiveTiming t)
    {
        if (t.IsUp)
        {
            Clock = "Up now";
            Detail = $"since {TimeFmt.Clock(t.NextSpawn)}";
            Accent = Palette.Green;
        }
        else
        {
            Clock = TimeFmt.Clock(t.NextSpawn);
            Detail = $"in {t.Remaining:0}s (estimated)";
            Accent = t.Remaining <= 30 ? Palette.Amber : Palette.Muted;
        }
    }

}

/// <summary>Fila de la tabla de jugadores.</summary>
public sealed class PlayerRowViewModel
{
    public PlayerRowViewModel(Player p)
    {
        Champion = string.IsNullOrEmpty(p.ChampionName) ? "—" : p.ChampionName;
        Summoner = !string.IsNullOrEmpty(p.SummonerName) ? p.SummonerName
                 : !string.IsNullOrEmpty(p.RiotIdGameName) ? p.RiotIdGameName
                 : p.RiotId;
        Level = p.Level;
        Kda = p.Scores.Kda;
        Cs = p.Scores.CreepScore;
        TeamBrush = p.Team == "CHAOS" ? Palette.TeamChaos : Palette.TeamOrder;
        Items = string.Join(", ", p.Items.Where(i => !string.IsNullOrEmpty(i.DisplayName)).Select(i => i.DisplayName));
    }

    public string Champion { get; }
    public string Summoner { get; }
    public int Level { get; }
    public string Kda { get; }
    public int Cs { get; }
    public Brush TeamBrush { get; }
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
    public ItemRecoRowViewModel(Core.Items.ItemRecommendation reco)
    {
        Name = reco.Item.Name;
        Cost = reco.Item.GoldTotal.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        if (reco.Affordable)
        {
            AffordText = "✓ you can buy it now";
            AffordBrush = Palette.Green;
        }
        else
        {
            var missing = reco.MissingGold.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            AffordText = reco.Purchase.NextComponent is null
                ? $"need {missing} more gold"
                : $"need {missing} more · buy now: {reco.Purchase.NextComponent.Name}";
            AffordBrush = Palette.Amber;
        }
        Reasons = string.Join(" · ", reco.Reasons);

        // Prioridad + categoría: por qué está en este lugar del orden (coherencia).
        (CategoryLabel, CategoryBrush) = reco.Category switch
        {
            RecommendationCategory.Counter => ("COUNTER", Palette.Red),
            RecommendationCategory.Defense => ("DEFENSIVE", Palette.Blue),
            RecommendationCategory.Spike => ("POWER SPIKE", Palette.Green),
            _ => ("CORE", Palette.Muted),
        };
        Priority = (int)System.Math.Round(reco.Priority * 100) + "% match";
    }

    public string Name { get; }
    public string Cost { get; }
    public string AffordText { get; }
    public Brush AffordBrush { get; }
    public string Reasons { get; }
    /// <summary>Etiqueta de por qué está en este lugar del orden (CORE/COUNTER/DEFENSIVE/POWER SPIKE).</summary>
    public string CategoryLabel { get; }
    public Brush CategoryBrush { get; }
    /// <summary>Prioridad relativa a la recomendación principal ("100% match" = la mejor).</summary>
    public string Priority { get; }
}

/// <summary>Fila del asesor de banca: un intercambio sugerido con sus razones.</summary>
public sealed class BenchSuggestionRowViewModel
{
    public BenchSuggestionRowViewModel(Core.Draft.BenchSuggestion suggestion)
    {
        Name = suggestion.Champion.Name;
        Reasons = string.Join(" · ", suggestion.Reasons);
    }

    public string Name { get; }
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
    public ChampCellViewModel(ChampSelectCell cell, bool isLocal, string? championName = null)
    {
        Position = string.IsNullOrEmpty(cell.AssignedPosition) ? "—" : cell.AssignedPosition;
        var champ = cell.DisplayChampionId;
        Champion = champ == 0 ? "(not picked)"
                 : !string.IsNullOrEmpty(championName) ? championName
                 : $"Champion #{champ}";
        IsLocal = isLocal;
        Accent = isLocal ? Palette.Green : Palette.Muted;
    }

    public string Position { get; }
    public string Champion { get; }
    public bool IsLocal { get; }
    public Brush Accent { get; }
}

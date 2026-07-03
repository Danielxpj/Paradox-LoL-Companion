namespace LoLAdvisor.Core.Models;

/// <summary>Sesión de champ select tal como la entrega la LCU (<c>/lol-champ-select/v1/session</c>).</summary>
public sealed class ChampSelectSession
{
    public int LocalPlayerCellId { get; set; } = -1;
    public List<ChampSelectCell> MyTeam { get; set; } = new();
    public List<ChampSelectCell> TheirTeam { get; set; } = new();
    public ChampSelectBans Bans { get; set; } = new();
    public ChampSelectTimer Timer { get; set; } = new();

    /// <summary>Hay banca de campeones descartados (rerolls de ARAM).</summary>
    public bool BenchEnabled { get; set; }
    /// <summary>Campeones disponibles en la banca para intercambiar.</summary>
    public List<BenchChampion> BenchChampions { get; set; } = new();

    /// <summary>La celda del jugador local dentro de <see cref="MyTeam"/>.</summary>
    public ChampSelectCell? LocalPlayer =>
        MyTeam.Find(c => c.CellId == LocalPlayerCellId);
}

/// <summary>Un campeón descartado en la banca de ARAM.</summary>
public sealed class BenchChampion
{
    public int ChampionId { get; set; }
}

/// <summary>Una posición (celda) en el draft.</summary>
public sealed class ChampSelectCell
{
    public int CellId { get; set; }
    /// <summary>Campeón bloqueado (pick confirmado); 0 si aún no hay.</summary>
    public int ChampionId { get; set; }
    /// <summary>Campeón que el jugador está "hovereando" antes de confirmar.</summary>
    public int ChampionPickIntent { get; set; }
    public string AssignedPosition { get; set; } = "";

    /// <summary>Campeón efectivo a mostrar: el confirmado, o el intent si aún no confirma.</summary>
    public int DisplayChampionId => ChampionId != 0 ? ChampionId : ChampionPickIntent;
}

/// <summary>Baneos de ambos equipos (ids de campeón).</summary>
public sealed class ChampSelectBans
{
    public List<int> MyTeamBans { get; set; } = new();
    public List<int> TheirTeamBans { get; set; } = new();
}

/// <summary>Temporizador/fase del draft.</summary>
public sealed class ChampSelectTimer
{
    public string Phase { get; set; } = "";
    public long AdjustedTimeLeftInPhase { get; set; }
}

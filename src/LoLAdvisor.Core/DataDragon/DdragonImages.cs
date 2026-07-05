namespace LoLAdvisor.Core.DataDragon;

/// <summary>
/// URLs de los íconos del CDN de Data Dragon (verificadas 2026-07-04). WPF las
/// consume directo en <c>Image.Source</c>: WinINet descarga y cachea en disco.
/// <c>null</c> cuando falta versión o clave — mejor sin ícono que una URL rota.
/// </summary>
public static class DdragonImages
{
    private const string Cdn = "https://ddragon.leagueoflegends.com/cdn";

    /// <summary>Ícono cuadrado del campeón (por <see cref="StaticChampion.Key"/>, p.ej. "MonkeyKing").</summary>
    public static string? ChampionIcon(string version, string? championKey) =>
        string.IsNullOrEmpty(version) || string.IsNullOrEmpty(championKey)
            ? null
            : $"{Cdn}/{version}/img/champion/{championKey}.png";

    /// <summary>Ícono del item (por id numérico de ddragon).</summary>
    public static string? ItemIcon(string version, int itemId) =>
        string.IsNullOrEmpty(version) || itemId <= 0
            ? null
            : $"{Cdn}/{version}/img/item/{itemId}.png";
}

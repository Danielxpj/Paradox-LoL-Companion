using System.Text.Json;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Live;

/// <summary>
/// Convierte el JSON crudo de <c>allgamedata</c> en un <see cref="GameState"/>.
/// Aislado para poder testearlo con fixtures grabados, sin el juego corriendo.
/// </summary>
public static class LiveGameParser
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Parsea el payload. Lanza <see cref="JsonException"/> si el JSON es inválido.</summary>
    public static GameState Parse(string json)
    {
        var state = JsonSerializer.Deserialize<GameState>(json, Options);
        if (state is null)
            throw new JsonException("The allgamedata payload deserialized to null.");
        return Normalize(state);
    }

    /// <summary>Garantiza que las colecciones nunca sean null (la API puede mandar campos null/ausentes).</summary>
    private static GameState Normalize(GameState state)
    {
        state.AllPlayers ??= new();
        state.Events ??= new();
        state.Events.Events ??= new();
        state.GameData ??= new();
        foreach (var p in state.AllPlayers)
        {
            p.Items ??= new();
            p.Scores ??= new();
            p.SummonerSpells ??= new();
            p.SummonerSpells.SummonerSpellOne ??= new();
            p.SummonerSpells.SummonerSpellTwo ??= new();
        }
        return state;
    }

    /// <summary>Parseo tolerante: devuelve <c>null</c> en vez de lanzar si el JSON está corrupto o parcial.</summary>
    public static GameState? TryParse(string json)
    {
        try
        {
            return Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

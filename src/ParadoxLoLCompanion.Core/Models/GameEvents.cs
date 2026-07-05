using System.Text.Json.Serialization;

namespace ParadoxLoLCompanion.Core.Models;

/// <summary>Envoltorio de <c>events</c>: contiene la lista <c>Events</c>.</summary>
public sealed class GameEvents
{
    public List<GameEvent> Events { get; set; } = new();
}

/// <summary>
/// Un evento de partida (GameStart, DragonKill, BaronKill, etc.).
/// Los campos opcionales solo vienen según el tipo de evento.
/// </summary>
public sealed class GameEvent
{
    public int EventID { get; set; }
    public string EventName { get; set; } = "";
    /// <summary>Segundo de partida en que ocurrió el evento.</summary>
    public double EventTime { get; set; }

    // Campos opcionales según el tipo de evento.
    public string? KillerName { get; set; }
    public string? VictimName { get; set; }
    public string? DragonType { get; set; }
    public List<string>? Assisters { get; set; }

    [JsonConverter(typeof(FlexibleBoolConverter))]
    public bool Stolen { get; set; }
}

/// <summary>
/// La Live Client API entrega <c>Stolen</c> a veces como texto ("True"/"False")
/// y a veces como booleano. Este converter acepta ambos.
/// </summary>
public sealed class FlexibleBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => reader.TokenType switch
        {
            System.Text.Json.JsonTokenType.True => true,
            System.Text.Json.JsonTokenType.False => false,
            System.Text.Json.JsonTokenType.String => bool.TryParse(reader.GetString(), out var b) && b,
            _ => false,
        };

    public override void Write(System.Text.Json.Utf8JsonWriter writer, bool value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);
}

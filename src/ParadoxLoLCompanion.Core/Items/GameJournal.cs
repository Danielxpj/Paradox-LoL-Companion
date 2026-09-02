using System.Text.Json;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>
/// Diario por partida (JSON Lines): qué se recomendó, qué compró el jugador y cómo terminó.
/// Es la telemetría de adopción (¿el jugador compra lo que el top-3 le muestra?) y, de
/// paso, el corpus de partidas reales que la calibración offline necesita. Un archivo por
/// partida; nunca lanza (un diario roto no puede tirar el asesor).
/// Líneas: <c>tick</c> (cada ~30 s), <c>buy</c> (item nuevo en el inventario, con el rank
/// que tenía en el top del tick ANTERIOR: lo que el jugador vio antes de comprar) y
/// <c>end</c> (evento GameEnd con su resultado).
/// </summary>
public sealed class GameJournal
{
    private const double TickSeconds = 30;
    private readonly string _dir;
    private string? _file;
    private string? _gameKey;
    private double _lastTick = double.NegativeInfinity;
    private double _lastTime = double.NegativeInfinity;
    private HashSet<int> _owned = new();
    private List<int> _lastTop = new();
    private bool _ended;

    public GameJournal(string? dir = null) =>
        _dir = dir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParadoxLoLCompanion", "journal");

    /// <summary>Archivo de la partida en curso (null hasta el primer tick).</summary>
    public string? CurrentFile => _file;

    public void Record(GameState state, ItemAdvicePlan? plan)
    {
        try
        {
            var me = state.ActivePlayerEntry;
            if (me is null)
                return;
            var t = state.GameData.GameTime;
            var key = $"{me.ChampionName}|{state.GameData.GameMode}|{state.AllPlayers.Count}";
            // Partida nueva: cambió el campeón/modo o el reloj volvió atrás.
            if (_file is null || key != _gameKey || t < _lastTime - 5)
                Start(key, state, me);
            _lastTime = t;

            var gold = (int)(state.ActivePlayer?.CurrentGold ?? 0);
            var owned = me.Items.Select(i => i.ItemID).ToHashSet();
            foreach (var id in owned.Except(_owned))
                Write(new { kind = "buy", t, item = id, rank = _lastTop.IndexOf(id), top = _lastTop, gold });
            _owned = owned;

            var top = plan?.Recommendations.Select(r => r.Item.Id).ToList() ?? new List<int>();
            if (t - _lastTick >= TickSeconds || !top.SequenceEqual(_lastTop))
            {
                Write(new
                {
                    kind = "tick", t, gold, level = me.Level, kda = me.Scores.Kda,
                    dead = me.IsDead, owned = owned.ToList(), top,
                    archetype = plan?.MyProfile.Archetype.ToString(),
                });
                _lastTick = t;
            }
            _lastTop = top;

            if (!_ended && state.Events.Events.FirstOrDefault(e =>
                    string.Equals(e.EventName, "GameEnd", StringComparison.OrdinalIgnoreCase)) is { } end)
            {
                _ended = true;
                Write(new { kind = "end", t, result = end.Result });
            }
        }
        catch
        {
            // Telemetría: nunca interrumpe el asesor.
        }
    }

    /// <summary>Adopción sobre todos los diarios: compras totales y cuántas estaban en el top mostrado.</summary>
    public static (int Buys, int FromTop) Adoption(string dir)
    {
        int buys = 0, fromTop = 0;
        if (!Directory.Exists(dir))
            return (0, 0);
        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("\"buy\""))
                    continue;
                using var doc = JsonDocument.Parse(line);
                buys++;
                if (doc.RootElement.GetProperty("rank").GetInt32() >= 0)
                    fromTop++;
            }
        return (buys, fromTop);
    }

    private void Start(string key, GameState state, Player me)
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{me.ChampionName}.jsonl");
        _gameKey = key;
        _lastTick = double.NegativeInfinity;
        _owned = new HashSet<int>();
        _lastTop = new List<int>();
        _ended = false;
        Write(new
        {
            kind = "start", mode = state.GameData.GameMode, map = state.GameData.MapNumber,
            me = me.ChampionName,
            allies = state.AllPlayers.Where(p => p.Team == me.Team && p != me).Select(p => p.ChampionName),
            enemies = state.AllPlayers.Where(p => p.Team != me.Team).Select(p => p.ChampionName),
        });
    }

    private void Write(object line) =>
        File.AppendAllText(_file!, JsonSerializer.Serialize(line) + Environment.NewLine);
}

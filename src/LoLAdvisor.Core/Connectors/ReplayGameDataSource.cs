using System.Text.Json;
using LoLAdvisor.Core.Live;
using LoLAdvisor.Core.Models;

namespace LoLAdvisor.Core.Connectors;

/// <summary>
/// Fuente de datos falsa para desarrollo/pruebas sin LoL abierto: reproduce un
/// snapshot grabado en bucle, avanzando el reloj de partida para que los timers
/// y consejos se vean "vivos".
/// </summary>
public sealed class ReplayGameDataSource : IGameDataSource, IAsyncDisposable
{
    private readonly GameState _base;
    private readonly double _startGameTime;
    private readonly TimeSpan _interval;
    private readonly bool _advanceTime;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action<GameState, string>? GameStateUpdated;
    public event Action<ConnectionStatus, string?>? StatusChanged;
    public event Action<string>? Log;

    public ReplayGameDataSource(string baseJson, TimeSpan? interval = null, bool advanceTime = true)
    {
        _base = LiveGameParser.Parse(baseJson);
        _startGameTime = _base.GameData.GameTime;
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _advanceTime = advanceTime;
    }

    public void Start()
    {
        if (_loop is not null)
            return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;
        _cts.Cancel();
        try
        {
            if (_loop is not null)
                await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _loop = null;
            StatusChanged?.Invoke(ConnectionStatus.Disconnected, null);
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        StatusChanged?.Invoke(ConnectionStatus.Connected, "Replay mode (no LoL)");
        Log?.Invoke("[replay] playing recorded snapshot…");

        var elapsed = 0.0;
        while (!ct.IsCancellationRequested)
        {
            if (_advanceTime)
                _base.GameData.GameTime = _startGameTime + elapsed;

            var json = JsonSerializer.Serialize(_base, LiveGameParser.Options);
            GameStateUpdated?.Invoke(_base, json);

            try
            {
                await Task.Delay(_interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            elapsed += _interval.TotalSeconds;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

using System.Net.Http;
using LoLAdvisor.Core.Live;
using LoLAdvisor.Core.Models;
using LoLAdvisor.Core.Net;

namespace LoLAdvisor.Core.Connectors;

/// <summary>
/// Conector real a la Live Client Data API. Hace polling de <c>allgamedata</c>
/// en <c>https://127.0.0.1:2999</c> y publica el estado parseado. Nunca crashea:
/// si no hay partida, reporta <see cref="ConnectionStatus.WaitingForGame"/> y reintenta.
/// </summary>
public sealed class LiveClientConnector : IGameDataSource, IAsyncDisposable
{
    private const string Url = "https://127.0.0.1:2999/liveclientdata/allgamedata";

    private readonly HttpClient _http;
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private ConnectionStatus _lastStatus = ConnectionStatus.Disconnected;

    public event Action<GameState, string>? GameStateUpdated;
    public event Action<ConnectionStatus, string?>? StatusChanged;
    public event Action<string>? Log;

    public LiveClientConnector(TimeSpan? interval = null, HttpClient? http = null)
    {
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _http = http ?? LocalHttpClientFactory.Create();
    }

    public void Start()
    {
        if (_loop is not null)
            return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => PollLoopAsync(_cts.Token));
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
        catch (OperationCanceledException) { /* esperado */ }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _loop = null;
            SetStatus(ConnectionStatus.Disconnected, null);
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = await _http.GetStringAsync(Url, ct).ConfigureAwait(false);
                var state = LiveGameParser.TryParse(json);
                if (state is null)
                {
                    Log?.Invoke("[live] invalid/partial JSON, skipping tick.");
                    SetStatus(ConnectionStatus.Error, "Invalid JSON");
                }
                else
                {
                    SetStatus(ConnectionStatus.Connected, null);
                    GameStateUpdated?.Invoke(state, json);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException)
            {
                // Conexión rechazada => no hay partida activa.
                SetStatus(ConnectionStatus.WaitingForGame, "Waiting for game…");
            }
            catch (TaskCanceledException)
            {
                // Timeout de la petición: tratar como esperando.
                SetStatus(ConnectionStatus.WaitingForGame, "Waiting for game…");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[live] unexpected error: {ex.Message}");
                SetStatus(ConnectionStatus.Error, ex.Message);
            }

            try
            {
                await Task.Delay(_interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void SetStatus(ConnectionStatus status, string? message)
    {
        if (status == _lastStatus)
            return;
        _lastStatus = status;
        StatusChanged?.Invoke(status, message);
        Log?.Invoke($"[live] status: {status}{(message is null ? "" : $" — {message}")}");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _http.Dispose();
    }
}

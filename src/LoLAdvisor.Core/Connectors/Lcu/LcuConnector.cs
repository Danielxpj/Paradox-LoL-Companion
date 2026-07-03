using System.Net;
using System.Net.Http;
using System.Text.Json;
using LoLAdvisor.Core.Connectors;
using LoLAdvisor.Core.Live;
using LoLAdvisor.Core.Models;
using LoLAdvisor.Core.Net;

namespace LoLAdvisor.Core.Connectors.Lcu;

/// <summary>
/// Conector a la LCU API. Localiza el cliente vía <c>lockfile</c> y hace polling del
/// endpoint de champ select. Degrada a <see cref="ConnectionStatus.WaitingForGame"/>
/// (cliente cerrado) sin afectar al resto de la app.
/// </summary>
public sealed class LcuConnector : IAsyncDisposable
{
    private const string ChampSelectPath = "/lol-champ-select/v1/session";
    private const string GameflowPath = "/lol-gameflow/v1/session";

    private readonly LockfileLocator _locator;
    private readonly HttpClient _http;
    private readonly TimeSpan _interval;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private ConnectionStatus _lastStatus = ConnectionStatus.Disconnected;
    private bool _wasInChampSelect;
    private LcuCredentials? _cachedCreds;
    private int _lastQueueId = -1;

    /// <summary>Nueva sesión de champ select leída.</summary>
    public event Action<ChampSelectSession>? ChampSelectUpdated;

    /// <summary>El jugador salió de champ select (o cerró el cliente).</summary>
    public event Action? ChampSelectEnded;

    /// <summary>Cambió la cola actual (-1 = sin partida/lobby o cliente cerrado).</summary>
    public event Action<int>? QueueIdChanged;

    public event Action<ConnectionStatus, string?>? StatusChanged;
    public event Action<string>? Log;

    public LcuConnector(LockfileLocator? locator = null, HttpClient? http = null, TimeSpan? interval = null)
    {
        _locator = locator ?? new LockfileLocator();
        _http = http ?? LocalHttpClientFactory.Create();
        _interval = interval ?? TimeSpan.FromSeconds(1);
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
        catch (OperationCanceledException) { }
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
            // Descubrir credenciales solo cuando no las tenemos (evita correr WMI cada tick).
            _cachedCreds ??= _locator.Locate();
            if (_cachedCreds is null)
            {
                SetStatus(ConnectionStatus.WaitingForGame, "Client closed");
                EndChampSelectIfNeeded();
                SetQueueId(-1);
            }
            else
            {
                await PollChampSelectAsync(_cachedCreds, ct).ConfigureAwait(false);
                await PollGameflowAsync(_cachedCreds, ct).ConfigureAwait(false);
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

    private async Task PollChampSelectAsync(LcuCredentials creds, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, creds.BaseUrl + ChampSelectPath);
            req.Headers.TryAddWithoutValidation("Authorization", creds.BasicAuthHeader);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                // Conectado al cliente, pero no estamos en champ select.
                SetStatus(ConnectionStatus.Connected, "Client open (not in champ select)");
                EndChampSelectIfNeeded();
                return;
            }

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var session = JsonSerializer.Deserialize<ChampSelectSession>(json, LiveGameParser.Options);
            if (session is not null)
            {
                SetStatus(ConnectionStatus.Connected, "In champ select");
                _wasInChampSelect = true;
                ChampSelectUpdated?.Invoke(session);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // apagando
        }
        catch (HttpRequestException)
        {
            // El cliente se cerró o el puerto cambió: descartar credenciales y re-descubrir.
            _cachedCreds = null;
            SetStatus(ConnectionStatus.WaitingForGame, "Client closed");
            EndChampSelectIfNeeded();
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[lcu] error: {ex.Message}");
            SetStatus(ConnectionStatus.Error, ex.Message);
        }
    }

    /// <summary>
    /// Lee la cola actual de <c>/lol-gameflow/v1/session</c>. Permite distinguir modos
    /// que comparten mapa (ARAM 450 vs. Mayhem 2400). Cualquier fallo degrada a -1.
    /// </summary>
    private async Task PollGameflowAsync(LcuCredentials creds, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, creds.BaseUrl + GameflowPath);
            req.Headers.TryAddWithoutValidation("Authorization", creds.BasicAuthHeader);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                SetQueueId(-1);
                return;
            }

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var session = JsonSerializer.Deserialize<GameflowSession>(json, LiveGameParser.Options);
            SetQueueId(session?.QueueId ?? -1);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // apagando
        }
        catch (Exception)
        {
            // La cola es información auxiliar: cualquier problema se trata como "desconocida".
            SetQueueId(-1);
        }
    }

    private void SetQueueId(int queueId)
    {
        if (queueId == _lastQueueId)
            return;
        _lastQueueId = queueId;
        Log?.Invoke($"[lcu] queue: {(queueId < 0 ? "none" : queueId.ToString())}");
        QueueIdChanged?.Invoke(queueId);
    }

    private void EndChampSelectIfNeeded()
    {
        if (!_wasInChampSelect)
            return;
        _wasInChampSelect = false;
        ChampSelectEnded?.Invoke();
    }

    private void SetStatus(ConnectionStatus status, string? message)
    {
        if (status == _lastStatus)
            return;
        _lastStatus = status;
        StatusChanged?.Invoke(status, message);
        Log?.Invoke($"[lcu] status: {status}{(message is null ? "" : $" — {message}")}");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _http.Dispose();
    }
}

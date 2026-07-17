using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ParadoxLoLCompanion.App.Diagnostics;
using ParadoxLoLCompanion.App.Mvvm;
using ParadoxLoLCompanion.App.Theme;
using ParadoxLoLCompanion.Core.Advice;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.Connectors;
using ParadoxLoLCompanion.Core.Connectors.Lcu;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Draft;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Live;
using ParadoxLoLCompanion.Core.Augments;
using ParadoxLoLCompanion.Core.Mayhem;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;
using ParadoxLoLCompanion.Core.Util;

namespace ParadoxLoLCompanion.App.ViewModels;

/// <summary>Info de una actualización de datos disponible (versión actual vs. la nueva).</summary>
public sealed record DataUpdateInfo(string CachedVersion, string LatestVersion);

/// <summary>Snapshots de ejemplo para el modo replay (partida de Grieta, de ARAM: Mayhem y champ select).</summary>
public sealed record ReplaySamples(string RiftGame, string AramGame, string ChampSelect)
{
    public static ReplaySamples Empty { get; } = new("{}", "{}", "{}");
}

/// <summary>
/// ViewModel principal: conecta las fuentes de datos (Live Client + LCU), corre el
/// motor de consejos y publica todo hacia el dashboard. Todos los eventos de los
/// conectores se cablean al hilo de UI vía <see cref="Dispatcher"/>.
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly AdvisorConfig _config;
    private AdviceEngine _engine;
    private readonly LcuConnector _lcu = new();
    private readonly DataDragonClient _ddragon;
    private IStaticData _catalog = DataDragonCatalog.Empty;
    private ItemAdvisor? _itemAdvisor;
    private TeamBalanceAdvisor? _benchAdvisor;
    private MayhemAdvisor? _mayhemAdvisor;
    private int _currentQueueId = -1;
    private readonly ReplaySamples _samples;
    private BuildArchetype? _forcedArchetype;
    private GameState? _lastGameState;
    private readonly StatsProvider _statsProvider;
    private readonly LcuRuneWriter _runeWriter = new();
    private readonly LcuItemSetWriter _itemSetWriter = new();
    private string? _itemSetsWrittenKey;   // "champKey|map|patch": una escritura por contexto
    private string? _runesAppliedKey;      // "champKey|patch": una aplicación automática por campeón
    private ChampionBuildStats? _championStats;
    // Top de items del tick anterior: la histéresis del asesor lo usa para no "temblar".
    private IReadOnlyList<int>? _previousTopIds;
    private string? _statsFetchKey;   // "champKey|pos|map|patch": evita re-fetch por tick
    // Reintento con backoff cuando un fetch falla: sin esto, un timeout dejaba la key
    // fijada y la partida entera corría sin prior, botas meta, item sets ni runas.
    private readonly Dictionary<string, (int Attempts, DateTime NextRetryUtc)> _statsRetry = new();
    private const int StatsMaxAttempts = 3;
    // Tier list de augments de Mayhem (Blitz): un fetch por parche, con throttle
    // de reintento — sin él, cada tick de un fallo re-dispararía la descarga.
    private readonly AugmentProvider _augmentProvider;
    private AugmentTierList? _augmentTiers;
    private string? _augmentFetchPatch;
    private DateTime _augmentRetryAtUtc = DateTime.MinValue;

    private readonly ScorecardViewModel _clockCard = new("Time", Palette.Muted);
    private readonly ScorecardViewModel _goldCard = new("Gold", Palette.Amber);
    private readonly ScorecardViewModel _csCard = new("CS", Palette.Green);
    private readonly ScorecardViewModel _csMinCard = new("CS / min", Palette.Green);
    private readonly ScorecardViewModel _kdaCard = new("KDA", Palette.Blue);
    private readonly ScorecardViewModel _levelCard = new("Level", Palette.Purple);

    private IGameDataSource? _liveSource;
    private bool _replayMode;
    private bool _consolePaused;
    private bool _inGame;
    private bool _inChampSelect;
    private string _gameMode = "";

    public MainViewModel(Dispatcher dispatcher, AdvisorConfig config, ReplaySamples? samples = null)
    {
        _dispatcher = dispatcher;
        _samples = samples ?? ReplaySamples.Empty;
        _config = config;
        _engine = AdviceEngine.CreateDefault(config);
        _ddragon = new DataDragonClient(itemsConfig: config.Items);
        // El porqué de cada fallo de OP.GG va a la consola: "sin stats" a secas no se puede diagnosticar.
        _statsProvider = new StatsProvider(log: line => OnUi(() => AppendConsole($"[stats] {line}")));
        _augmentProvider = new AugmentProvider(log: line => OnUi(() => AppendConsole($"[augments] {line}")));

        Scorecards = new ObservableCollection<ScorecardViewModel>
        {
            _clockCard, _goldCard, _csCard, _csMinCard, _kdaCard, _levelCard,
        };

        // Selector de build del panel de items: Auto (detección) + los 7 arquetipos.
        // Los labels coinciden con ItemAdvisor.ArchetypeLabel.
        BuildRoles = new ObservableCollection<BuildRoleOptionViewModel>
        {
            new("AUTO", null, OnBuildRoleSelected, isChecked: true),
            new("MARKSMAN", BuildArchetype.Marksman, OnBuildRoleSelected),
            new("MAGE", BuildArchetype.Mage, OnBuildRoleSelected),
            new("ASSASSIN", BuildArchetype.AdAssassin, OnBuildRoleSelected),
            new("FIGHTER", BuildArchetype.AdFighter, OnBuildRoleSelected),
            new("AP FIGHTER", BuildArchetype.ApFighter, OnBuildRoleSelected),
            new("TANK", BuildArchetype.Tank, OnBuildRoleSelected),
            new("SUPPORT", BuildArchetype.Enchanter, OnBuildRoleSelected),
        };

        PauseConsoleCommand = new RelayCommand(TogglePauseConsole);
        ClearConsoleCommand = new RelayCommand(() => ConsoleLines.Clear());
        SaveConsoleCommand = new RelayCommand(SaveConsole);
        ApplyRunesCommand = new RelayCommand(OnApplyRunes);
        CheckUpdatesCommand = new RelayCommand(() =>
        {
            // El botón corre en el hilo de UI: aquí _catalog ya tiene el valor actual.
            var current = _catalog.IsLoaded ? _catalog.Version : null;
            AppendConsole("[ddragon] checking for updates…");
            _ = CheckForUpdatesAsync(userInitiated: true, current);
        });

        _lcu.StatusChanged += (s, m) => OnUi(() => ApplyLcuStatus(s, m));
        _lcu.Log += line => OnUi(() => AppendConsole(line));
        _lcu.ChampSelectUpdated += session => OnUi(() => ApplyChampSelect(session));
        _lcu.ChampSelectEnded += () => OnUi(EndChampSelect);
        _lcu.QueueIdChanged += queueId => OnUi(() => _currentQueueId = queueId);
    }

    // --- Colecciones y estado expuesto a la UI ---

    public ObservableCollection<ScorecardViewModel> Scorecards { get; }
    public ObservableCollection<PlayerRowViewModel> Players { get; } = new();
    public ObservableCollection<AdviceRowViewModel> Advice { get; } = new();
    public ObservableCollection<ItemRecoRowViewModel> ItemRecos { get; } = new();
    public ObservableCollection<BenchSuggestionRowViewModel> BenchSuggestions { get; } = new();
    public ObservableCollection<SellRowViewModel> SellRows { get; } = new();
    public ObservableCollection<EnemyTileViewModel> EnemyTiles { get; } = new();
    public ObservableCollection<ChampCellViewModel> MyTeam { get; } = new();
    public ObservableCollection<ChampCellViewModel> TheirTeam { get; } = new();
    public ObservableCollection<string> ConsoleLines { get; } = new();
    public ObservableCollection<BuildRoleOptionViewModel> BuildRoles { get; }

    public ICommand PauseConsoleCommand { get; }
    public ICommand ClearConsoleCommand { get; }
    public ICommand SaveConsoleCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand ApplyRunesCommand { get; }

    private string _contextLabel = "Idle";
    public string ContextLabel { get => _contextLabel; private set => SetProperty(ref _contextLabel, value); }

    private int _selectedTabIndex;
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }

    private string _liveStatusText = "Disconnected";
    public string LiveStatusText { get => _liveStatusText; private set => SetProperty(ref _liveStatusText, value); }

    private Brush _liveStatusBrush = Palette.Muted;
    public Brush LiveStatusBrush { get => _liveStatusBrush; private set => SetProperty(ref _liveStatusBrush, value); }

    private string _lcuStatusText = "Client closed";
    public string LcuStatusText { get => _lcuStatusText; private set => SetProperty(ref _lcuStatusText, value); }

    private Brush _lcuStatusBrush = Palette.Muted;
    public Brush LcuStatusBrush { get => _lcuStatusBrush; private set => SetProperty(ref _lcuStatusBrush, value); }

    private string _threatSummary = "";
    public string ThreatSummary { get => _threatSummary; private set => SetProperty(ref _threatSummary, value); }

    // --- Panel ENEMY X-RAY ---
    private string _physPct = "";
    public string PhysPct { get => _physPct; private set => SetProperty(ref _physPct, value); }

    private string _magicPct = "";
    public string MagicPct { get => _magicPct; private set => SetProperty(ref _magicPct, value); }

    private Brush _damageSplitBrush = Brushes.Transparent;
    /// <summary>Barra física/mágica: gradiente con corte duro en la fracción física.</summary>
    public Brush DamageSplitBrush { get => _damageSplitBrush; private set => SetProperty(ref _damageSplitBrush, value); }

    private string _recommendedLine = "";
    public string RecommendedLine { get => _recommendedLine; private set => SetProperty(ref _recommendedLine, value); }

    private string _sustainLine = "";
    public string SustainLine { get => _sustainLine; private set => SetProperty(ref _sustainLine, value); }

    private string _goldText = "";
    public string GoldText { get => _goldText; private set => SetProperty(ref _goldText, value); }

    private bool _contextIsLive;
    public bool ContextIsLive { get => _contextIsLive; private set => SetProperty(ref _contextIsLive, value); }

    private string _bootsLine = "";
    public string BootsLine { get => _bootsLine; private set => SetProperty(ref _bootsLine, value); }

    private string _bootsName = "";
    public string BootsName { get => _bootsName; private set => SetProperty(ref _bootsName, value); }

    private string _bootsChip = "";
    public string BootsChip { get => _bootsChip; private set => SetProperty(ref _bootsChip, value); }

    private string _bootsSub = "";
    public string BootsSub { get => _bootsSub; private set => SetProperty(ref _bootsSub, value); }

    private string? _bootsIconUrl;
    public string? BootsIconUrl { get => _bootsIconUrl; private set => SetProperty(ref _bootsIconUrl, value); }

    private string? _starterIconUrl;
    public string? StarterIconUrl { get => _starterIconUrl; private set => SetProperty(ref _starterIconUrl, value); }

    private string _sellLine = "";
    public string SellLine { get => _sellLine; private set => SetProperty(ref _sellLine, value); }

    private string _lateTipsLine = "";
    public string LateTipsLine { get => _lateTipsLine; private set => SetProperty(ref _lateTipsLine, value); }

    private string _starterLine = "";
    public string StarterLine { get => _starterLine; private set => SetProperty(ref _starterLine, value); }

    private string _shopAlertLine = "";
    public string ShopAlertLine { get => _shopAlertLine; private set => SetProperty(ref _shopAlertLine, value); }

    private string _runesLine = "";
    public string RunesLine { get => _runesLine; private set => SetProperty(ref _runesLine, value); }

    private string _skillOrderLine = "";
    public string SkillOrderLine { get => _skillOrderLine; private set => SetProperty(ref _skillOrderLine, value); }

    private string _skillPriorityLine = "";
    /// <summary>Versión corta ("Q › W › E") para la fila de botas del HUD.</summary>
    public string SkillPriorityLine { get => _skillPriorityLine; private set => SetProperty(ref _skillPriorityLine, value); }

    private string _runesStatus = "";
    public string RunesStatus { get => _runesStatus; private set => SetProperty(ref _runesStatus, value); }

    private string _mayhemStatus = "";
    public string MayhemStatus { get => _mayhemStatus; private set => SetProperty(ref _mayhemStatus, value); }

    private string _mayhemPickNow = "";
    public string MayhemPickNow { get => _mayhemPickNow; private set => SetProperty(ref _mayhemPickNow, value); }

    private string _mayhemGuidance = "";
    public string MayhemGuidance { get => _mayhemGuidance; private set => SetProperty(ref _mayhemGuidance, value); }

    /// <summary>Cheat-sheet de augments (tier list de Blitz), rankeado para MI campeón.</summary>
    public ObservableCollection<AugmentRowViewModel> MayhemAugments { get; } = new();

    private string _itemPanelHint = "The advisor activates once the data catalog loads and a game is detected.";
    public string ItemPanelHint { get => _itemPanelHint; private set => SetProperty(ref _itemPanelHint, value); }

    private string _benchSummary = "";
    public string BenchSummary { get => _benchSummary; private set => SetProperty(ref _benchSummary, value); }

    private string _benchVerdict = "";
    public string BenchVerdict { get => _benchVerdict; private set => SetProperty(ref _benchVerdict, value); }

    private string _benchLine = "";
    public string BenchLine { get => _benchLine; private set => SetProperty(ref _benchLine, value); }

    private string _bans = "Bans: —";
    public string Bans { get => _bans; private set => SetProperty(ref _bans, value); }

    private string _draftPhase = "";
    public string DraftPhase { get => _draftPhase; private set => SetProperty(ref _draftPhase, value); }

    public string PauseLabel => _consolePaused ? "Resume" : "Pause";

    public bool ReplayMode
    {
        get => _replayMode;
        set { if (SetProperty(ref _replayMode, value)) SwitchLiveSource(value); }
    }

    private int _replayScenarioIndex;
    /// <summary>Escenario del replay: 0 = Grieta (CLASSIC), 1 = ARAM: Mayhem.</summary>
    public int ReplayScenarioIndex
    {
        get => _replayScenarioIndex;
        set
        {
            if (SetProperty(ref _replayScenarioIndex, value) && _replayMode)
                SwitchLiveSource(replay: true);
        }
    }

    private string CurrentReplaySample =>
        ReplayScenarioIndex == 1 ? _samples.AramGame : _samples.RiftGame;

    // --- Arranque / cambio de fuente ---

    public void Start()
    {
        SwitchLiveSource(_replayMode);
        _lcu.Start();
        _ = InitStaticDataAsync();
    }

    /// <summary>Se dispara cuando hay una versión de datos más nueva que la cargada (la UI pregunta).</summary>
    public event Action<DataUpdateInfo>? DataUpdateAvailable;

    /// <summary>Al arrancar: carga el catálogo cacheado al instante y luego chequea actualizaciones.</summary>
    private async Task InitStaticDataAsync()
    {
        DataDragonCatalog? cached = null;
        try
        {
            cached = await _ddragon.LoadCachedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnUi(() => AppendConsole($"[ddragon] could not read the cache: {ex.Message}"));
        }

        if (cached is { IsLoaded: true })
            OnUi(() => ApplyCatalog(cached, $"[ddragon] cached catalog loaded (v{cached.Version}) — item recommendations active."));

        await CheckForUpdatesAsync(userInitiated: false, cached?.Version).ConfigureAwait(false);
    }

    /// <summary>
    /// Chequea si hay una versión más nueva. Si la hay, avisa para preguntar. En el primer
    /// uso (sin caché) descarga directo. <paramref name="userInitiated"/> agrega feedback
    /// cuando el usuario lo dispara desde el botón.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool userInitiated, string? currentVersion)
    {
        var latest = await _ddragon.GetLatestOnlineVersionAsync().ConfigureAwait(false);

        if (latest is null)
        {
            if (userInitiated)
                OnUi(() => AppendConsole("[ddragon] offline: could not check for updates."));
            return;
        }

        if (currentVersion is null)
        {
            OnUi(() => AppendConsole($"[ddragon] downloading data for the first time (v{latest})…"));
            await DownloadAndApplyAsync(latest).ConfigureAwait(false);
        }
        else if (latest != currentVersion)
        {
            OnUi(() =>
            {
                AppendConsole($"[ddragon] new version available: {latest} (you have {currentVersion}).");
                DataUpdateAvailable?.Invoke(new DataUpdateInfo(currentVersion, latest));
            });
        }
        else if (userInitiated)
        {
            OnUi(() => AppendConsole($"[ddragon] your data is up to date (v{currentVersion})."));
        }
    }

    /// <summary>Descarga y aplica una versión concreta del catálogo (tras confirmar el usuario).</summary>
    public async Task DownloadAndApplyAsync(string version)
    {
        OnUi(() => AppendConsole($"[ddragon] downloading v{version}…"));
        try
        {
            var catalog = await _ddragon.LoadVersionAsync(version).ConfigureAwait(false);
            OnUi(() => ApplyCatalog(catalog, $"[ddragon] data updated to v{version}."));
        }
        catch (Exception ex)
        {
            OnUi(() => AppendConsole($"[ddragon] update failed: {ex.Message}"));
        }
    }

    private ChampionProfiler? _profilerForTiles;

    private void ApplyCatalog(IStaticData catalog, string logLine)
    {
        _catalog = catalog;
        // El provider lee el override vigente en cada tick: feed y panel siempre coinciden.
        _engine = AdviceEngine.CreateWith(catalog, _config, () => _forcedArchetype, () => _championStats);
        _itemAdvisor = new ItemAdvisor(catalog, _config.Items);
        _profilerForTiles = new ChampionProfiler(catalog, _config.Items);
        _benchAdvisor = new TeamBalanceAdvisor(catalog, _config.Items);
        _mayhemAdvisor = new MayhemAdvisor(catalog, _config.Items, _config.Mayhem);
        AppendConsole(logLine);
        // Con el catálogo recién cargado, el champ select de replay resuelve nombres y consejo.
        ApplyReplayChampSelect(_replayMode);
    }

    private void SwitchLiveSource(bool replay)
    {
        DetachAndStopLiveSource();
        _lastGameState = null;
        _previousTopIds = null;
        ResetBuildRole();

        _liveSource = replay
            ? new ReplayGameDataSource(CurrentReplaySample)
            : new LiveClientConnector();

        _liveSource.GameStateUpdated += (state, raw) => OnUi(() => ApplyGameState(state, raw));
        _liveSource.StatusChanged += (s, m) => OnUi(() => ApplyLiveStatus(s, m));
        _liveSource.Log += line => OnUi(() => AppendConsole(line));

        AppendConsole(replay ? "[app] replay mode enabled." : "[app] connecting to the Live Client API…");
        _liveSource.Start();
        ApplyReplayChampSelect(replay);
    }

    /// <summary>
    /// En modo replay, puebla la pestaña Champ Select con una sesión de ejemplo (ARAM
    /// con banca) para ver el asesor de banca sin LoL abierto. Una selección real de la
    /// LCU siempre tiene prioridad.
    /// </summary>
    private void ApplyReplayChampSelect(bool replay)
    {
        if (_inChampSelect)
            return;

        if (!replay)
        {
            ClearChampSelectData();
            return;
        }

        if (string.IsNullOrWhiteSpace(_samples.ChampSelect) || _samples.ChampSelect == "{}")
            return;
        try
        {
            var session = JsonSerializer.Deserialize<ChampSelectSession>(
                _samples.ChampSelect, LiveGameParser.Options);
            if (session is not null)
            {
                PopulateChampSelect(session);
                AppendConsole("[replay] sample champ select loaded — see the Champ Select tab.");
            }
        }
        catch (JsonException ex)
        {
            AppendConsole($"[replay] could not parse the sample champ select: {ex.Message}");
        }
    }

    private void DetachAndStopLiveSource()
    {
        if (_liveSource is null)
            return;
        var old = _liveSource;
        _liveSource = null;
        _ = old.StopAsync();
        _inGame = false;
    }

    // --- Aplicar datos en vivo ---

    private void ApplyGameState(GameState state, string raw)
    {
        // Volcar el crudo ANTES de procesar: si algo falla, queda el payload que lo causó.
        FileLog.WriteRaw(raw);

        _inGame = true;
        _gameMode = state.GameData.GameMode;
        UpdateContext();
        RequestStatsIfNeeded(state);

        _clockCard.Value = TimeFmt.Clock(state.GameData.GameTime);
        _goldCard.Value = ((int)(state.ActivePlayer?.CurrentGold ?? 0)).ToString("N0", CultureInfo.InvariantCulture);
        GoldText = _goldCard.Value;
        _levelCard.Value = (state.ActivePlayer?.Level ?? 0).ToString();

        var me = state.ActivePlayerEntry;
        _csCard.Value = me is null ? "—" : me.Scores.CreepScore.ToString();
        _kdaCard.Value = me?.Scores.Kda ?? "—";
        var minutes = state.GameData.GameTimeMinutes;
        _csMinCard.Value = me is null || minutes <= 0
            ? "—"
            : (me.Scores.CreepScore / minutes).ToString("0.0", CultureInfo.InvariantCulture);

        RebuildPlayers(state);
        RebuildAdvice(state);
        _lastGameState = state;
        RebuildItemPlan(state);
        RebuildMayhemAdvice(state);

        AppendConsole(
            $"[live] t={TimeFmt.Clock(state.GameData.GameTime)} gold={_goldCard.Value} cs={_csCard.Value} " +
            $"kda={_kdaCard.Value} players={state.AllPlayers.Count} events={state.Events.Events.Count} raw={raw.Length}b");
    }

    private void RebuildPlayers(GameState state)
    {
        Players.Clear();
        foreach (var p in state.AllPlayers)
            Players.Add(new PlayerRowViewModel(p, _catalog));
    }

    private void RebuildAdvice(GameState state)
    {
        Advice.Clear();
        foreach (var item in _engine.Evaluate(state))
        {
            // Los consejos de Items duplican las tarjetas del asesor: fuera del feed.
            if (item.Category != AdviceCategory.Items)
                Advice.Add(new AdviceRowViewModel(item));
        }
    }

    /// <summary>El jugador eligió build en la UI: recalcular el consejo al instante.</summary>
    private void OnBuildRoleSelected(BuildRoleOptionViewModel option)
    {
        _forcedArchetype = option.Archetype;
        _previousTopIds = null;   // cambió la build: no arrastrar la histéresis del arquetipo viejo
        AppendConsole($"[items] build role: {option.Label}");
        if (_lastGameState is { } state)
        {
            RebuildItemPlan(state);
            RebuildMayhemAdvice(state);
            RebuildAdvice(state);
        }
    }

    /// <summary>Vuelve el selector a Auto (el override es una decisión por partida).</summary>
    private void ResetBuildRole()
    {
        foreach (var role in BuildRoles)
            role.IsChecked = role.Archetype is null;
    }

    /// <summary>
    /// Pide las stats de OP.GG cuando cambia el contexto (campeón/rol/mapa/parche).
    /// El fetch es async y opcional: el consejo sale sin prior hasta que lleguen.
    /// </summary>
    private void RequestStatsIfNeeded(GameState state)
    {
        var me = state.ActivePlayerEntry;
        if (me is null || !_catalog.IsLoaded)
            return;
        var champ = _catalog.ResolveChampion(me.ChampionName, me.RawChampionName);
        if (champ is null)
            return;

        var mapNumber = state.GameData.MapNumber == 0 ? 11 : state.GameData.MapNumber;
        // Upper: la AssignedPosition de champ select es "middle" y la Position en
        // vivo "MIDDLE" — normalizado, el pase de selección a partida no re-pide.
        var key = $"{champ.Key}|{me.Position.ToUpperInvariant()}|{mapNumber}|{_catalog.Version}";
        if (key == _statsFetchKey)
            return;
        if (_statsRetry.TryGetValue(key, out var retry)
            && (retry.Attempts >= StatsMaxAttempts || DateTime.UtcNow < retry.NextRetryUtc))
            return;   // agotó reintentos o todavía en backoff
        _statsFetchKey = key;
        _championStats = null;
        _previousTopIds = null;   // cambió el campeón/contexto: reset de la histéresis
        RebuildRunesPanel();
        _ = FetchStatsAsync(champ.Key, me.Position, mapNumber, _catalog.Version, key);
    }

    private async Task FetchStatsAsync(string champKey, string position, int mapNumber,
        string patch, string key)
    {
        ChampionBuildStats? stats = null;
        try
        {
            stats = await _statsProvider.GetAsync(champKey, position, mapNumber, patch)
                .ConfigureAwait(false);
        }
        catch
        {
            // El provider ya degrada a null; este catch es el cinturón extra del hilo async.
        }
        OnUi(() =>
        {
            if (_statsFetchKey != key)
                return;   // llegó tarde: el contexto ya cambió
            _championStats = stats;
            if (stats is null)
            {
                // Falló: liberar la key y programar backoff (45 s, 90 s, luego se rinde)
                // para que el tick de 1 s reintente — un timeout ya no silencia el prior
                // y las botas meta toda la partida.
                var attempts = _statsRetry.TryGetValue(key, out var r) ? r.Attempts + 1 : 1;
                _statsRetry[key] = (attempts, DateTime.UtcNow.AddSeconds(45 * Math.Pow(2, attempts - 1)));
                _statsFetchKey = null;
            }
            else
                _statsRetry.Remove(key);
            AppendConsole(stats is null
                ? $"[stats] no OP.GG data for {champKey} — advising without statistical priors (see lines above for why)."
                : $"[stats] OP.GG build data loaded for {champKey} ({stats.GameMode}/{stats.Position}).");
            RebuildRunesPanel();
            WriteItemSetsIfNeeded(stats);
            // Las runas solo sirven ANTES de la partida: se auto-aplican únicamente
            // durante un champ select real (nunca en replay ni con el juego andando).
            if (_inChampSelect)
                AutoApplyRunesIfNeeded(stats);
            if (_lastGameState is { } s)
                RebuildItemPlan(s);
        });
    }

    /// <summary>
    /// Champ select: aplica la página de runas más popular sin que el jugador
    /// toque nada. Deduplicado por campeón+parche; el botón manual queda como
    /// reintento. En ARAM cada swap de banca re-dispara con el campeón nuevo.
    /// </summary>
    private void AutoApplyRunesIfNeeded(ChampionBuildStats? stats)
    {
        if (stats?.Runes is not { } runes)
            return;
        var key = $"{stats.ChampionKey}|{_catalog.Version}";
        if (key == _runesAppliedKey)
            return;
        _runesAppliedKey = key;
        var champ = stats.ChampionKey;
        RunesStatus = "Applying…";
        _ = Task.Run(async () =>
        {
            var error = await _runeWriter.ApplyAsync(champ, runes).ConfigureAwait(false);
            OnUi(() =>
            {
                if (error is null)
                {
                    RunesStatus = $"Rune page \"{LcuRuneWriter.PagePrefix}{champ}\" applied.";
                    AppendConsole($"[runes] page auto-applied for {champ} (champ select).");
                    return;
                }
                if (_runesAppliedKey == key)
                    _runesAppliedKey = null;
                RunesStatus = error;
                AppendConsole($"[runes] auto-apply failed: {error}");
            });
        });
    }

    /// <summary>
    /// Escribe las 3 páginas de items en el cliente (automático). Deduplicado por
    /// campeón+mapa+parche; si falla se limpia la clave para reintentar en el
    /// próximo disparo natural (champ select o partida).
    /// </summary>
    private void WriteItemSetsIfNeeded(ChampionBuildStats? stats)
    {
        if (stats is null || _catalog.ChampionByKey(stats.ChampionKey) is not { } champ)
            return;
        var mapNumber = stats.GameMode == "aram" ? 12 : 11;
        var key = $"{stats.ChampionKey}|{mapNumber}|{_catalog.Version}";
        if (key == _itemSetsWrittenKey)
            return;
        _itemSetsWrittenKey = key;
        var pages = ItemSetBuilder.Build(stats, champ.Name, id => _catalog.ItemById(id)?.Name);
        if (pages.Count == 0)
            return;
        _ = ApplyItemSetsAsync(pages, champ, mapNumber, key);
    }

    private async Task ApplyItemSetsAsync(IReadOnlyList<ItemSetPage> pages,
        StaticChampion champ, int mapNumber, string key)
    {
        var error = await _itemSetWriter.ApplyAsync(pages, champ.Id, mapNumber).ConfigureAwait(false);
        OnUi(() =>
        {
            if (error is null)
            {
                AppendConsole($"[builds] {pages.Count} item pages written for {champ.Name}.");
                return;
            }
            if (_itemSetsWrittenKey == key)
                _itemSetsWrittenKey = null;
            AppendConsole($"[builds] item pages not written: {error}");
        });
    }

    private void RebuildRunesPanel()
    {
        if (_championStats?.Runes is not { } r)
        {
            RunesLine = "";
            SkillOrderLine = "";
            SkillPriorityLine = "";
            RunesStatus = "";
            return;
        }
        RunesLine = $"{r.PrimaryPageName}: {string.Join(" · ", r.PrimaryRuneNames)}   |   "
                  + $"{r.SecondaryPageName}: {string.Join(" · ", r.SecondaryRuneNames)}";
        var order = _championStats.Skills?.Order ?? Array.Empty<string>();
        SkillOrderLine = order.Count == 0
            ? ""
            : $"Skills: {SkillPriority(order)}   (first levels: {string.Join(" ", order.Take(6))}…)";
        SkillPriorityLine = order.Count == 0 ? "" : SkillPriority(order).Replace(" > ", " › ");
    }

    /// <summary>"Q > E > W": prioridad de maxeo por frecuencia en el orden de 15 niveles (R aparte).</summary>
    internal static string SkillPriority(IReadOnlyList<string> order) =>
        string.Join(" > ", order
            .Where(s => s is "Q" or "W" or "E")
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key));

    private async void OnApplyRunes()
    {
        if (_championStats?.Runes is not { } runes)
            return;
        RunesStatus = "Applying…";
        var champ = _championStats.ChampionKey;
        var error = await _runeWriter.ApplyAsync(champ, runes);
        RunesStatus = error ?? $"Rune page \"{LcuRuneWriter.PagePrefix}{champ}\" applied.";
        AppendConsole(error is null
            ? $"[runes] page applied for {champ}."
            : $"[runes] failed: {error}");
    }

    private void RebuildItemPlan(GameState state)
    {
        ItemRecos.Clear();
        var plan = _itemAdvisor?.Advise(state, _forcedArchetype, _championStats, _previousTopIds);
        _previousTopIds = plan?.Recommendations.Select(r => r.Item.Id).ToList();
        if (plan is null)
        {
            ThreatSummary = "";
            PhysPct = "";
            MagicPct = "";
            RecommendedLine = "";
            SustainLine = "";
            EnemyTiles.Clear();
            BootsLine = "";
            BootsName = "";
            BootsChip = "";
            BootsSub = "";
            BootsIconUrl = null;
            SellLine = "";
            SellRows.Clear();
            LateTipsLine = "";
            StarterLine = "";
            StarterIconUrl = null;
            ShopAlertLine = "";
            ItemPanelHint = _catalog.IsLoaded
                ? "Waiting for game data…"
                : "Waiting for the Data Dragon catalog…";
            return;
        }

        ItemPanelHint = "";
        ThreatSummary = plan.InventoryFull
            ? plan.ThreatSummary + "  |  Inventory FULL — sell before buying"
            : plan.ThreatSummary;
        var gold = state.ActivePlayer?.CurrentGold ?? 0;
        foreach (var reco in plan.Recommendations)
            ItemRecos.Add(new ItemRecoRowViewModel(reco, _catalog.Version, gold));
        RebuildEnemyXRay(state, plan);
        BootsLine = plan.Boots is null ? "" : FormatBoots(plan.Boots);
        if (plan.Boots is { } boots)
        {
            BootsName = boots.Boots.Name;
            BootsChip = boots.Purchase.CanFinishNow
                ? "✓ BUY NOW"
                : boots.Purchase.NextComponent is { } comp
                    ? $"▸ BUY {comp.Name.ToUpperInvariant()}"
                    : $"SAVE {boots.MissingGold.ToString("N0", CultureInfo.InvariantCulture)}";
            BootsSub = boots.Reason;
        }
        else
        {
            BootsName = "";
            BootsChip = "";
            BootsSub = "";
        }
        BootsIconUrl = plan.Boots is null
            ? null
            : DdragonImages.ItemIcon(_catalog.Version, plan.Boots.Boots.Id);
        SellLine = plan.Sells.Count == 0
            ? ""
            : "Sell: " + string.Join("  ·  ", plan.Sells.Select(s =>
                $"{s.Item.Name} (+{s.SellGold.ToString("N0", CultureInfo.InvariantCulture)} g) — {s.Reason}"));
        SellRows.Clear();
        foreach (var sell in plan.Sells)
            SellRows.Add(new SellRowViewModel(sell, _catalog.Version));
        StarterLine = plan.Starter is null
            ? ""
            : $"Start: {plan.Starter.Item.Name} ({plan.Starter.Item.GoldTotal.ToString("N0", CultureInfo.InvariantCulture)}) — {plan.Starter.Reason}";
        StarterIconUrl = plan.Starter is null
            ? null
            : DdragonImages.ItemIcon(_catalog.Version, plan.Starter.Item.Id);
        ShopAlertLine = plan.ShopAlert ?? "";
        LateTipsLine = string.Join("   ·   ", plan.LateTips);
    }

    /// <summary>
    /// Panel ENEMY X-RAY: reparto de daño, recomendación defensiva de una línea,
    /// aviso de sustain y un tile por enemigo (rol/amenaza/respawn).
    /// </summary>
    private void RebuildEnemyXRay(GameState state, ItemAdvicePlan plan)
    {
        var t = plan.Threat;
        PhysPct = ((int)Math.Round(t.PhysicalShare * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        MagicPct = ((int)Math.Round(t.MagicalShare * 100)).ToString(CultureInfo.InvariantCulture) + "%";

        // Barra partida: rojo hasta la fracción física, azul el resto (corte duro).
        var f = Fuzzy.Clamp01(t.PhysicalShare);
        var split = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop((Color)ColorConverter.ConvertFromString("#FF4A3C"), 0),
            new GradientStop((Color)ColorConverter.ConvertFromString("#FF4A3C"), f),
            new GradientStop((Color)ColorConverter.ConvertFromString("#3AA6FF"), f),
            new GradientStop((Color)ColorConverter.ConvertFromString("#3AA6FF"), 1),
        }, new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
        split.Freeze();
        DamageSplitBrush = split;

        // "Armor & anti-burst · not MR": la defensa primaria + counters activos.
        var primary = t.PhysicalShare >= 0.55 ? "Armor"
            : t.MagicalShare >= 0.55 ? "Magic resist"
            : "Mixed resists";
        var extras = new List<string>();
        if (t.Burst > 0.5) extras.Add("anti-burst");
        if (t.CritThreat > 0.5) extras.Add("anti-crit");
        if (t.EnemyTankiness > 0.5) extras.Add("pen/on-hit");
        var notHint = t.PhysicalShare >= 0.55 && t.MagicalShare < 0.2 ? " · not MR"
            : t.MagicalShare >= 0.55 && t.PhysicalShare < 0.2 ? " · not armor"
            : "";
        RecommendedLine = primary
            + (extras.Count > 0 ? " & " + string.Join(" & ", extras) : "")
            + notHint;

        SustainLine = t.Sustain > 0.3 && t.TopSustainName is not null
            ? $"Grievous wounds › {t.TopSustainName} heals"
            : "";

        EnemyTiles.Clear();
        var me = state.ActivePlayerEntry;
        if (me is null)
            return;
        foreach (var p in state.AllPlayers.Where(p => p.Team != me.Team))
        {
            var champ = _catalog.ResolveChampion(p.ChampionName, p.RawChampionName);
            var isTop = t.TopThreatName is { } top && top.StartsWith(p.ChampionName, StringComparison.Ordinal);
            EnemyTiles.Add(new EnemyTileViewModel
            {
                IconUrl = DdragonImages.ChampionIcon(_catalog.Version, champ?.Key),
                Name = p.ChampionName,
                Kda = p.Scores.Kda,
                Tag = isTop ? "THREAT" : EnemyTag(p, champ),
                IsTopThreat = isTop,
                RespawnText = p.IsDead && p.RespawnTimer > 0 ? $"{p.RespawnTimer:0}s" : "",
            });
        }
    }

    /// <summary>Rol táctico del enemigo para el tile (CC gana al arquetipo: es lo que te castiga).</summary>
    private string EnemyTag(Player p, StaticChampion? champ)
    {
        if (champ is not null && _config.Items.HeavyCcChampions.Contains(champ.Key))
            return "CC";
        var archetype = _profilerForTiles?.Profile(p).Archetype;
        return archetype switch
        {
            BuildArchetype.Marksman => "MARKSMAN",
            BuildArchetype.Tank => "TANK",
            BuildArchetype.AdAssassin => "BURST",
            BuildArchetype.Mage => "MAGE",
            BuildArchetype.ApFighter => "AP FIGHTER",
            BuildArchetype.AdFighter => "FIGHTER",
            BuildArchetype.Enchanter => "SUPPORT",
            _ => "—",
        };
    }

    /// <summary>Línea de botas accionable: qué comprar YA y cuánto falta, no solo el nombre.</summary>
    private static string FormatBoots(BootsAdvice b)
    {
        var cost = b.Boots.GoldTotal.ToString("N0", CultureInfo.InvariantCulture);
        var status = b.Purchase.CanFinishNow
            ? "✓ buy them now"
            : b.Purchase.NextComponent is { } component
                ? $"buy now: {component.Name} · need {b.MissingGold.ToString("N0", CultureInfo.InvariantCulture)} more"
                : $"need {b.MissingGold.ToString("N0", CultureInfo.InvariantCulture)} more gold";
        return $"Boots: {b.Boots.Name} ({cost}) — {status} — {b.Reason}";
    }

    /// <summary>
    /// Tarjeta de augments de ARAM: Mayhem. La cola real viene de la LCU (2400); en
    /// modo replay, un sample en el mapa 12 activa la demo.
    /// </summary>
    private void RebuildMayhemAdvice(GameState state)
    {
        var isAramMap = state.GameData.MapNumber == 12
            || string.Equals(state.GameData.GameMode, "ARAM", StringComparison.OrdinalIgnoreCase);
        var isMayhem = _config.Mayhem.QueueIds.Contains(_currentQueueId)
            || (_replayMode && isAramMap);

        if (isMayhem)
            RequestAugmentsIfNeeded();

        var advice = isMayhem ? _mayhemAdvisor?.Advise(state, _forcedArchetype, _augmentTiers) : null;
        if (advice is null)
        {
            MayhemStatus = "";
            MayhemPickNow = "";
            MayhemGuidance = "";
            MayhemAugments.Clear();
            return;
        }

        MayhemStatus = advice.StatusLine;
        MayhemPickNow = advice.PickNowLine ?? "";
        // Strip horizontal estilo HUD: guías separadas por "//" (antes bullets).
        MayhemGuidance = string.Join("   //   ", advice.Guidance);
        SyncMayhemAugments(advice.TopAugments);
    }

    /// <summary>Repobla solo si cambió (el tick es 1 s; recrear filas iguales parpadea).</summary>
    private void SyncMayhemAugments(IReadOnlyList<AugmentSuggestion> top)
    {
        if (MayhemAugments.Count == top.Count
            && MayhemAugments.Zip(top).All(p => p.First.Id == p.Second.Id))
            return;
        MayhemAugments.Clear();
        foreach (var suggestion in top)
            MayhemAugments.Add(new AugmentRowViewModel(suggestion));
    }

    /// <summary>Un fetch del tier list por parche; tras un fallo, reintento con throttle.</summary>
    private void RequestAugmentsIfNeeded()
    {
        if (!_catalog.IsLoaded || _catalog.Version == _augmentFetchPatch
            || DateTime.UtcNow < _augmentRetryAtUtc)
            return;
        _augmentFetchPatch = _catalog.Version;
        _augmentRetryAtUtc = DateTime.UtcNow.AddSeconds(60);
        _ = FetchAugmentsAsync(_catalog.Version);
    }

    private async Task FetchAugmentsAsync(string patch)
    {
        AugmentTierList? list = null;
        try
        {
            list = await _augmentProvider.GetAsync(patch).ConfigureAwait(false);
        }
        catch
        {
            // El provider ya degrada a null; cinturón extra del hilo async.
        }
        OnUi(() =>
        {
            _augmentTiers = list;
            if (list is null)
            {
                _augmentFetchPatch = null;   // reintento (throttled) en el próximo tick de Mayhem
                AppendConsole("[augments] Blitz tier list unavailable — Mayhem card shows generic guidance only.");
                return;
            }
            AppendConsole($"[augments] Blitz Mayhem tier list loaded ({list.Augments.Count} augments).");
            if (_lastGameState is { } s)
                RebuildMayhemAdvice(s);
        });
    }

    private void ApplyLiveStatus(ConnectionStatus status, string? message)
    {
        LiveStatusText = StatusText(status, message);
        LiveStatusBrush = StatusBrush(status);
        if (status != ConnectionStatus.Connected)
        {
            _inGame = false;
            _lastGameState = null; // sin partida: que el reset no recalcule sobre datos viejos
            _previousTopIds = null; // la histéresis no debe filtrarse a la próxima partida
            ResetBuildRole();
            UpdateContext();
        }
    }

    // --- Aplicar champ select (LCU) ---

    private void ApplyChampSelect(ChampSelectSession session)
    {
        _inChampSelect = true;
        UpdateContext();
        PopulateChampSelect(session);
    }

    /// <summary>Vuelca una sesión de champ select a la UI (real o de replay).</summary>
    private void PopulateChampSelect(ChampSelectSession session)
    {
        MyTeam.Clear();
        foreach (var c in session.MyTeam)
            MyTeam.Add(new ChampCellViewModel(c, c.CellId == session.LocalPlayerCellId, ChampName(c), ChampIcon(c)));

        TheirTeam.Clear();
        foreach (var c in session.TheirTeam)
            TheirTeam.Add(new ChampCellViewModel(c, isLocal: false, ChampName(c), ChampIcon(c)));

        Bans = $"Bans — us: {FormatBans(session.Bans.MyTeamBans)} | them: {FormatBans(session.Bans.TheirTeamBans)}";
        DraftPhase = string.IsNullOrEmpty(session.Timer.Phase)
            ? ""
            : $"Phase: {session.Timer.Phase} ({session.Timer.AdjustedTimeLeftInPhase / 1000}s)";

        RebuildBenchAdvice(session);
        RequestStatsFromChampSelect(session);
    }

    /// <summary>
    /// Champ select: el campeón del jugador local dispara el fetch de stats (y con
    /// él las páginas de items) ANTES de que arranque la partida. En ARAM la banca
    /// cambia el campeón: cada swap re-dispara con el nuevo.
    /// </summary>
    private void RequestStatsFromChampSelect(ChampSelectSession session)
    {
        if (!_catalog.IsLoaded)
            return;
        var me = session.MyTeam.FirstOrDefault(c => c.CellId == session.LocalPlayerCellId);
        if (me is null || me.DisplayChampionId == 0)
            return;
        var champ = _catalog.ChampionById(me.DisplayChampionId);
        if (champ is null)
            return;
        var isAram = _currentQueueId == 450 || _config.Mayhem.QueueIds.Contains(_currentQueueId);
        var mapNumber = isAram ? 12 : 11;
        var key = $"{champ.Key}|{me.AssignedPosition.ToUpperInvariant()}|{mapNumber}|{_catalog.Version}";
        if (key == _statsFetchKey)
        {
            // Stats ya cargadas (mismo campeón que antes): igual hay que aplicar
            // las runas de ESTE champ select — el dedup interno evita repetirlo.
            if (_inChampSelect)
                AutoApplyRunesIfNeeded(_championStats);
            return;
        }
        if (_statsRetry.TryGetValue(key, out var retry)
            && (retry.Attempts >= StatsMaxAttempts || DateTime.UtcNow < retry.NextRetryUtc))
            return;   // agotó reintentos o todavía en backoff
        _statsFetchKey = key;
        _championStats = null;
        RebuildRunesPanel();
        _ = FetchStatsAsync(champ.Key, me.AssignedPosition, mapNumber, _catalog.Version, key);
    }

    /// <summary>Consejo de banca (ARAM): con qué campeón descartado equilibras mejor al equipo.</summary>
    private void RebuildBenchAdvice(ChampSelectSession session)
    {
        BenchSuggestions.Clear();
        var advice = _benchAdvisor?.Advise(session);
        if (advice is null)
        {
            BenchSummary = "";
            BenchVerdict = "";
            BenchLine = "";
            return;
        }

        BenchSummary = advice.TeamSummary;
        BenchVerdict = advice.Verdict;
        BenchLine = session.BenchChampions.Count == 0
            ? "Bench: empty (reroll to fill it)"
            : "Bench: " + string.Join(", ", session.BenchChampions
                .Select(b => _catalog.ChampionNameById(b.ChampionId) ?? $"#{b.ChampionId}"));
        foreach (var suggestion in advice.Suggestions)
            BenchSuggestions.Add(new BenchSuggestionRowViewModel(suggestion, _catalog.Version));
    }

    private void EndChampSelect()
    {
        _inChampSelect = false;
        // Cada champ select aplica sus runas aunque el campeón se repita entre partidas.
        _runesAppliedKey = null;
        ClearChampSelectData();
        UpdateContext();
        // Si estamos en replay, volver a mostrar la selección de ejemplo.
        ApplyReplayChampSelect(_replayMode);
    }

    private void ClearChampSelectData()
    {
        MyTeam.Clear();
        TheirTeam.Clear();
        BenchSuggestions.Clear();
        BenchSummary = "";
        BenchVerdict = "";
        BenchLine = "";
        Bans = "Bans: —";
        DraftPhase = "";
    }

    private void ApplyLcuStatus(ConnectionStatus status, string? message)
    {
        LcuStatusText = StatusText(status, message);
        LcuStatusBrush = StatusBrush(status);
    }

    // --- Contexto / pestañas ---

    private void UpdateContext()
    {
        string label;
        int? tab = null;
        if (_inChampSelect)
        {
            label = "CHAMP SELECT";
            tab = 1;
        }
        else if (_inGame)
        {
            label = string.Equals(_gameMode, "ARAM", StringComparison.OrdinalIgnoreCase)
                ? "LIVE · ARAM"
                : "LIVE · RIFT";
            tab = 0;
        }
        else
        {
            label = "IDLE";
        }
        ContextIsLive = _inGame && !_inChampSelect;

        // Cambiar de pestaña solo al CAMBIAR de contexto: si se forzara en cada tick,
        // el usuario no podría quedarse mirando la otra pestaña.
        if (label != ContextLabel && tab is int index)
            SelectedTabIndex = index;
        ContextLabel = label;
    }

    // --- Consola ---

    private void TogglePauseConsole()
    {
        _consolePaused = !_consolePaused;
        OnPropertyChanged(nameof(PauseLabel));
        AppendConsoleForced(_consolePaused ? "[app] console paused." : "[app] console resumed.");
    }

    private void AppendConsole(string line)
    {
        if (_consolePaused)
            return;
        AppendConsoleForced(line);
    }

    private void AppendConsoleForced(string line)
    {
        // El log a archivo va primero: debe registrarse aunque la UI falle.
        FileLog.Write(line);
        try
        {
            ConsoleLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
            while (ConsoleLines.Count > 500)
                ConsoleLines.RemoveAt(0);
        }
        catch
        {
            // Nunca dejar que un hipo del generador de UI escale (evita cascadas).
        }
    }

    private string? _lastErrorMessage;

    /// <summary>Registra un error de forma visible sin tumbar la app (deduplica repeticiones).</summary>
    public void ReportError(Exception ex)
    {
        FileLog.WriteException("handler", ex);
        if (_lastErrorMessage == ex.Message)
            return;
        _lastErrorMessage = ex.Message;
        AppendConsoleForced($"[error] {ex.GetType().Name}: {ex.Message}");
    }

    private void SaveConsole()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"lol-advisor-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Filter = "Text (*.txt)|*.txt",
        };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllLines(dialog.FileName, ConsoleLines);
            AppendConsoleForced($"[app] log saved to {dialog.FileName}");
        }
    }

    // --- Helpers de estado ---

    private static string StatusText(ConnectionStatus status, string? message) => status switch
    {
        ConnectionStatus.Connected => message ?? "Connected",
        ConnectionStatus.WaitingForGame => message ?? "Waiting…",
        ConnectionStatus.Error => $"Error: {message}",
        _ => "Disconnected",
    };

    private static Brush StatusBrush(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => Palette.Green,
        ConnectionStatus.WaitingForGame => Palette.Amber,
        ConnectionStatus.Error => Palette.Red,
        _ => Palette.Muted,
    };

    private string? ChampName(ChampSelectCell cell) =>
        _catalog.ChampionNameById(cell.DisplayChampionId);

    private string? ChampIcon(ChampSelectCell cell) =>
        DdragonImages.ChampionIcon(_catalog.Version,
            _catalog.ChampionById(cell.DisplayChampionId)?.Key);

    private string FormatBans(IReadOnlyList<int> bans) =>
        bans.Count == 0 ? "—" : string.Join(", ", bans.Select(b => _catalog.ChampionNameById(b) ?? $"#{b}"));

    private void OnUi(Action action) => _dispatcher.InvokeAsync(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // Ningún fallo procesando datos debe cerrar la app: se registra y se sigue.
            ReportError(ex);
        }
    });

    public async ValueTask DisposeAsync()
    {
        DetachAndStopLiveSource();
        await _lcu.DisposeAsync().ConfigureAwait(false);
    }
}

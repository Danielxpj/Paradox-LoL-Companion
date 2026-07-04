using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LoLAdvisor.App.Diagnostics;
using LoLAdvisor.App.Mvvm;
using LoLAdvisor.App.Theme;
using LoLAdvisor.Core.Advice;
using LoLAdvisor.Core.Config;
using LoLAdvisor.Core.Connectors;
using LoLAdvisor.Core.Connectors.Lcu;
using LoLAdvisor.Core.DataDragon;
using LoLAdvisor.Core.Draft;
using LoLAdvisor.Core.Items;
using LoLAdvisor.Core.Live;
using LoLAdvisor.Core.Mayhem;
using LoLAdvisor.Core.Models;
using LoLAdvisor.Core.Objectives;
using LoLAdvisor.Core.Stats;
using LoLAdvisor.Core.Util;

namespace LoLAdvisor.App.ViewModels;

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
    private ChampionBuildStats? _championStats;
    private string? _statsFetchKey;   // "champKey|pos|map|patch": evita re-fetch por tick

    private readonly ScorecardViewModel _clockCard = new("Time", Palette.Muted);
    private readonly ScorecardViewModel _goldCard = new("Gold", Palette.Amber);
    private readonly ScorecardViewModel _csCard = new("CS", Palette.Green);
    private readonly ScorecardViewModel _csMinCard = new("CS / min", Palette.Green);
    private readonly ScorecardViewModel _kdaCard = new("KDA", Palette.Blue);
    private readonly ScorecardViewModel _levelCard = new("Level", Palette.Purple);
    private readonly ObjectiveTimerViewModel _dragon = new("Dragon");
    private readonly ObjectiveTimerViewModel _baron = new("Baron");

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

        Scorecards = new ObservableCollection<ScorecardViewModel>
        {
            _clockCard, _goldCard, _csCard, _csMinCard, _kdaCard, _levelCard,
        };
        Objectives = new ObservableCollection<ObjectiveTimerViewModel> { _dragon, _baron };

        // Selector de build del panel de items: Auto (detección) + los 7 arquetipos.
        // Los labels coinciden con ItemAdvisor.ArchetypeLabel.
        BuildRoles = new ObservableCollection<BuildRoleOptionViewModel>
        {
            new("Auto", null, OnBuildRoleSelected, isChecked: true),
            new("Marksman", BuildArchetype.Marksman, OnBuildRoleSelected),
            new("Mage", BuildArchetype.Mage, OnBuildRoleSelected),
            new("Assassin", BuildArchetype.AdAssassin, OnBuildRoleSelected),
            new("Fighter", BuildArchetype.AdFighter, OnBuildRoleSelected),
            new("AP Fighter", BuildArchetype.ApFighter, OnBuildRoleSelected),
            new("Tank", BuildArchetype.Tank, OnBuildRoleSelected),
            new("Support", BuildArchetype.Enchanter, OnBuildRoleSelected),
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
    public ObservableCollection<ObjectiveTimerViewModel> Objectives { get; }
    public ObservableCollection<PlayerRowViewModel> Players { get; } = new();
    public ObservableCollection<AdviceRowViewModel> Advice { get; } = new();
    public ObservableCollection<ItemRecoRowViewModel> ItemRecos { get; } = new();
    public ObservableCollection<BenchSuggestionRowViewModel> BenchSuggestions { get; } = new();
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

    private bool _showObjectives = true;
    /// <summary>La sección Dragón/Barón solo aplica en la Grieta (CLASSIC).</summary>
    public bool ShowObjectives { get => _showObjectives; private set => SetProperty(ref _showObjectives, value); }

    private string _threatSummary = "";
    public string ThreatSummary { get => _threatSummary; private set => SetProperty(ref _threatSummary, value); }

    private string _bootsLine = "";
    public string BootsLine { get => _bootsLine; private set => SetProperty(ref _bootsLine, value); }

    private string _sellLine = "";
    public string SellLine { get => _sellLine; private set => SetProperty(ref _sellLine, value); }

    private string _starterLine = "";
    public string StarterLine { get => _starterLine; private set => SetProperty(ref _starterLine, value); }

    private string _shopAlertLine = "";
    public string ShopAlertLine { get => _shopAlertLine; private set => SetProperty(ref _shopAlertLine, value); }

    private string _runesLine = "";
    public string RunesLine { get => _runesLine; private set => SetProperty(ref _runesLine, value); }

    private string _skillOrderLine = "";
    public string SkillOrderLine { get => _skillOrderLine; private set => SetProperty(ref _skillOrderLine, value); }

    private string _runesStatus = "";
    public string RunesStatus { get => _runesStatus; private set => SetProperty(ref _runesStatus, value); }

    private string _mayhemStatus = "";
    public string MayhemStatus { get => _mayhemStatus; private set => SetProperty(ref _mayhemStatus, value); }

    private string _mayhemPickNow = "";
    public string MayhemPickNow { get => _mayhemPickNow; private set => SetProperty(ref _mayhemPickNow, value); }

    private string _mayhemGuidance = "";
    public string MayhemGuidance { get => _mayhemGuidance; private set => SetProperty(ref _mayhemGuidance, value); }

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

    private void ApplyCatalog(IStaticData catalog, string logLine)
    {
        _catalog = catalog;
        // El provider lee el override vigente en cada tick: feed y panel siempre coinciden.
        _engine = AdviceEngine.CreateWith(catalog, _config, () => _forcedArchetype);
        _itemAdvisor = new ItemAdvisor(catalog, _config.Items);
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
        _levelCard.Value = (state.ActivePlayer?.Level ?? 0).ToString();

        var me = state.ActivePlayerEntry;
        _csCard.Value = me is null ? "—" : me.Scores.CreepScore.ToString();
        _kdaCard.Value = me?.Scores.Kda ?? "—";
        var minutes = state.GameData.GameTimeMinutes;
        _csMinCard.Value = me is null || minutes <= 0
            ? "—"
            : (me.Scores.CreepScore / minutes).ToString("0.0", CultureInfo.InvariantCulture);

        // Dragón/Barón solo existen en la Grieta: en ARAM y otros modos la sección
        // de objetivos se oculta por completo.
        var isClassic = string.Equals(state.GameData.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase);
        ShowObjectives = isClassic;
        if (isClassic)
        {
            _dragon.Update(ObjectiveTimers.Dragon(state));
            _baron.Update(ObjectiveTimers.Baron(state));
        }

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
            Players.Add(new PlayerRowViewModel(p));
    }

    private void RebuildAdvice(GameState state)
    {
        Advice.Clear();
        foreach (var item in _engine.Evaluate(state))
            Advice.Add(new AdviceRowViewModel(item));
    }

    /// <summary>El jugador eligió build en la UI: recalcular el consejo al instante.</summary>
    private void OnBuildRoleSelected(BuildRoleOptionViewModel option)
    {
        _forcedArchetype = option.Archetype;
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
        _statsFetchKey = key;
        _championStats = null;
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
            AppendConsole(stats is null
                ? $"[stats] no OP.GG data for {champKey} — advising without statistical priors (see lines above for why)."
                : $"[stats] OP.GG build data loaded for {champKey} ({stats.GameMode}/{stats.Position}).");
            RebuildRunesPanel();
            WriteItemSetsIfNeeded(stats);
            if (_lastGameState is { } s)
                RebuildItemPlan(s);
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
        var pages = ItemSetBuilder.Build(stats, champ.Name);
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
            RunesStatus = "";
            return;
        }
        RunesLine = $"{r.PrimaryPageName}: {string.Join(" · ", r.PrimaryRuneNames)}   |   "
                  + $"{r.SecondaryPageName}: {string.Join(" · ", r.SecondaryRuneNames)}";
        var order = _championStats.Skills?.Order ?? Array.Empty<string>();
        SkillOrderLine = order.Count == 0
            ? ""
            : $"Skills: {SkillPriority(order)}   (first levels: {string.Join(" ", order.Take(6))}…)";
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
        var plan = _itemAdvisor?.Advise(state, _forcedArchetype, _championStats);
        if (plan is null)
        {
            ThreatSummary = "";
            BootsLine = "";
            SellLine = "";
            StarterLine = "";
            ShopAlertLine = "";
            ItemPanelHint = _catalog.IsLoaded
                ? "Waiting for game data…"
                : "Waiting for the Data Dragon catalog…";
            return;
        }

        ItemPanelHint = "";
        ThreatSummary = plan.ThreatSummary;
        foreach (var reco in plan.Recommendations)
            ItemRecos.Add(new ItemRecoRowViewModel(reco));
        BootsLine = plan.Boots is null ? "" : $"Boots: {plan.Boots.Boots.Name} — {plan.Boots.Reason}";
        SellLine = plan.Sells.Count == 0
            ? ""
            : "Sell: " + string.Join("  ·  ", plan.Sells.Select(s =>
                $"{s.Item.Name} (+{s.SellGold.ToString("N0", CultureInfo.InvariantCulture)} g) — {s.Reason}"));
        StarterLine = plan.Starter is null
            ? ""
            : $"Start: {plan.Starter.Item.Name} ({plan.Starter.Item.GoldTotal.ToString("N0", CultureInfo.InvariantCulture)}) — {plan.Starter.Reason}";
        ShopAlertLine = plan.ShopAlert ?? "";
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

        var advice = isMayhem ? _mayhemAdvisor?.Advise(state, _forcedArchetype) : null;
        if (advice is null)
        {
            MayhemStatus = "";
            MayhemPickNow = "";
            MayhemGuidance = "";
            return;
        }

        MayhemStatus = advice.StatusLine;
        MayhemPickNow = advice.PickNowLine ?? "";
        MayhemGuidance = "• " + string.Join("\n• ", advice.Guidance);
    }

    private void ApplyLiveStatus(ConnectionStatus status, string? message)
    {
        LiveStatusText = StatusText(status, message);
        LiveStatusBrush = StatusBrush(status);
        if (status != ConnectionStatus.Connected)
        {
            _inGame = false;
            _lastGameState = null; // sin partida: que el reset no recalcule sobre datos viejos
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
            MyTeam.Add(new ChampCellViewModel(c, c.CellId == session.LocalPlayerCellId, ChampName(c)));

        TheirTeam.Clear();
        foreach (var c in session.TheirTeam)
            TheirTeam.Add(new ChampCellViewModel(c, isLocal: false, ChampName(c)));

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
            return;
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
            BenchSuggestions.Add(new BenchSuggestionRowViewModel(suggestion));
    }

    private void EndChampSelect()
    {
        _inChampSelect = false;
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
            label = "Champ Select";
            tab = 1;
        }
        else if (_inGame)
        {
            label = string.Equals(_gameMode, "ARAM", StringComparison.OrdinalIgnoreCase)
                ? "In game (ARAM)"
                : "In game";
            tab = 0;
        }
        else
        {
            label = "Idle";
        }

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

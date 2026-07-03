# Build Role Override Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Radio-button row in the Item advisor card that forces the build archetype (Auto + 7 roles), overriding auto-detection for item recommendations, boots and Mayhem augment guidance; resets to Auto per game.

**Architecture:** The core advisors stay stateless: `ItemAdvisor.Advise` and `MayhemAdvisor.Advise` gain an optional `BuildArchetype? forcedArchetype` parameter (null = current behavior). `MainViewModel` owns the selection, passes it on every tick, caches the last `GameState` to recompute instantly on radio change, and resets to Auto when the Live Client disconnects or replay toggles.

**Tech Stack:** C#/.NET 10, WPF (MVVM with hand-rolled ObservableObject), xUnit.

**Spec:** `docs/superpowers/specs/2026-07-02-build-role-override-design.md`

**NOTE — no git:** This project is intentionally NOT a git repository (user preference). Skip all commit steps; each task ends by running the test suite instead.

---

### Task 1: Core — `ItemAdvisor.Advise` accepts a forced archetype

**Files:**
- Modify: `src/LoLAdvisor.Core/Items/ItemAdvisor.cs` (method `Advise`, ~line 30, and the summary block ~line 125)
- Test: `tests/LoLAdvisor.Tests/ItemAdvisorTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `tests/LoLAdvisor.Tests/ItemAdvisorTests.cs` (inside the existing class; `None`, `Advisor()` and `TestCatalog` already exist there). Note: in TestCatalog, Soraka has tags `["Support","Mage"]` and defense 3, so her default archetype is Enchanter; item 2065 (Shurelya's) is a support item she already owns:

```csharp
[Fact]
public void ForcedArchetype_OverridesDetectionAndInventory()
{
    // Soraka (enchanter por defecto) con item de support ya comprado, forzada a maga:
    // el override manual le gana al campeón Y al inventario. Su perfil de daño no
    // cambia (sigue siendo mágico: es del kit, no de la build).
    var state = TestCatalog.State(5000,
        ("Soraka", "ORDER", 0, new[] { 2065 }),
        ("Jinx", "CHAOS", 2, None));
    var plan = Advisor().Advise(state, BuildArchetype.Mage)!;

    Assert.Equal(BuildArchetype.Mage, plan.MyProfile.Archetype);
    Assert.False(plan.MyProfile.InferredFromItems);
    Assert.Equal(DamageProfile.Magical, plan.MyProfile.Damage);
    Assert.Contains("Build override: mage", plan.ThreatSummary);
    Assert.Contains(plan.Recommendations, r => r.Item.HasTag("SpellDamage"));
}

[Fact]
public void ForcedArchetype_Null_KeepsAutoDetection()
{
    // Sin override, nada cambia: detección automática y sin banner de override.
    var state = TestCatalog.State(5000, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 2, None));
    var plan = Advisor().Advise(state)!;

    Assert.Equal(BuildArchetype.Mage, plan.MyProfile.Archetype);
    Assert.DoesNotContain("Build override", plan.ThreatSummary);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LoLAdvisor.Tests --filter "FullyQualifiedName~ForcedArchetype" --nologo -v q`
Expected: `ForcedArchetype_OverridesDetectionAndInventory` FAILS to compile? No — compile error: `Advise` has no 2-argument overload. That compile error IS the red state. (`ForcedArchetype_Null_KeepsAutoDetection` compiles fine but can't run until the other is fixed.)

- [ ] **Step 3: Implement the parameter in `ItemAdvisor`**

In `src/LoLAdvisor.Core/Items/ItemAdvisor.cs`, change the `Advise` signature and doc:

```csharp
/// <summary>
/// <c>null</c> si el catálogo no cargó o no hay datos suficientes de la partida.
/// <paramref name="forcedArchetype"/>: arquetipo elegido a mano por el jugador en la
/// UI; anula la detección por campeón e inventario (el perfil de daño no cambia:
/// es del kit del campeón, no de la build).
/// </summary>
public ItemAdvicePlan? Advise(GameState state, BuildArchetype? forcedArchetype = null)
```

Replace the profile line (`var profile = _profiler.ProfileWithInventory(me);`) with:

```csharp
// El arquetipo sale del inventario real, salvo que el jugador lo haya forzado en
// la UI: la elección manual manda sobre lo que llevas comprado.
var profile = forcedArchetype is { } forced
    ? _profiler.Profile(me) with { Archetype = forced }
    : _profiler.ProfileWithInventory(me);
```

Replace the summary block (`if (profile.InferredFromItems) summary += ...`) with:

```csharp
var summary = ThreatSummary(threat, isAram);
if (forcedArchetype is not null)
    summary += $" | Build override: {ArchetypeLabel(profile.Archetype)}";
else if (profile.InferredFromItems)
    summary += $" | Build detected from your items: {ArchetypeLabel(profile.Archetype)}";
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LoLAdvisor.Tests --filter "FullyQualifiedName~ForcedArchetype" --nologo -v q`
Expected: 2 passed.

- [ ] **Step 5: Run the whole suite (no regressions)**

Run: `dotnet test tests/LoLAdvisor.Tests --nologo -v q`
Expected: all pass (135 existing + 2 new).

---

### Task 2: Core — `MayhemAdvisor.Advise` accepts a forced archetype

**Files:**
- Modify: `src/LoLAdvisor.Core/Mayhem/MayhemAdvisor.cs` (`Advise` ~line 47, `Guidance` ~line 75)
- Test: `tests/LoLAdvisor.Tests/MayhemAdvisorTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `tests/LoLAdvisor.Tests/MayhemAdvisorTests.cs` (add `using LoLAdvisor.Core.Items;` to the file's usings for `BuildArchetype`):

```csharp
[Fact]
public void Guidance_FollowsForcedArchetype()
{
    // Jinx (tiradora) forzada a maga: la guía de augments habla de AP, no de daño sostenido.
    var advice = Advisor().Advise(State(7, enemies: ("Ahri", "CHAOS", 0, None)),
        BuildArchetype.Mage)!;

    Assert.Contains(advice.Guidance, g => g.Contains("mage"));
    Assert.DoesNotContain(advice.Guidance, g => g.Contains("marksman"));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LoLAdvisor.Tests --filter "FullyQualifiedName~Guidance_FollowsForcedArchetype" --nologo -v q`
Expected: compile error — `Advise` has no 2-argument overload. That is the red state.

- [ ] **Step 3: Implement the parameter in `MayhemAdvisor`**

In `src/LoLAdvisor.Core/Mayhem/MayhemAdvisor.cs`:

Signature (keep the existing doc line, add the param note):

```csharp
/// <summary>
/// El llamador decide si la partida es Mayhem (cola de la LCU); acá solo se calcula.
/// <c>null</c> si no hay datos del jugador activo. <paramref name="forcedArchetype"/>:
/// arquetipo forzado por el jugador en la UI (anula la detección por inventario).
/// </summary>
public MayhemAdvice? Advise(GameState state, BuildArchetype? forcedArchetype = null)
```

The `return` at the end of `Advise` passes it through:

```csharp
return new MayhemAdvice(unlocked, total, next, me.IsDead, status, pickNow,
    Guidance(state, me, forcedArchetype));
```

`Guidance` gains the parameter and uses it for the profile:

```csharp
private IReadOnlyList<string> Guidance(GameState state, Player me, BuildArchetype? forcedArchetype)
{
    var guidance = new List<string>();
    var profile = forcedArchetype is { } forced
        ? _profiler.Profile(me) with { Archetype = forced }
        : _profiler.ProfileWithInventory(me);
    guidance.Add(ArchetypeGuidance(profile.Archetype));
    // ... resto del método sin cambios (usa `profile.IsSquishy` más abajo).
```

(Only the first lines change; the threat checks below stay identical.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LoLAdvisor.Tests --filter "FullyQualifiedName~Guidance_FollowsForcedArchetype" --nologo -v q`
Expected: 1 passed.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/LoLAdvisor.Tests --nologo -v q`
Expected: all pass (138 total).

---

### Task 3: ViewModel — selection, instant recompute, reset to Auto

No unit tests here: `MainViewModel` is WPF-bound (Dispatcher) and the project has no App-side test host; the Core behavior is covered by Tasks 1–2. Verification is compile + Task 5's manual replay check.

**Files:**
- Modify: `src/LoLAdvisor.App/ViewModels/ItemViewModels.cs` (add class at the end)
- Modify: `src/LoLAdvisor.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add `BuildRoleOptionViewModel` to `ItemViewModels.cs`**

Append at the end of the file (it already has `using LoLAdvisor.App.Mvvm;`; add `using LoLAdvisor.Core.Items;` to the usings):

```csharp
/// <summary>
/// Opción del selector de build del panel de items: "Auto" (detección automática,
/// Archetype null) o un arquetipo forzado a mano por el jugador.
/// </summary>
public sealed class BuildRoleOptionViewModel : ObservableObject
{
    private readonly Action<BuildRoleOptionViewModel> _onSelected;
    private bool _isChecked;

    public BuildRoleOptionViewModel(string label, BuildArchetype? archetype,
        Action<BuildRoleOptionViewModel> onSelected, bool isChecked = false)
    {
        Label = label;
        Archetype = archetype;
        _onSelected = onSelected;
        _isChecked = isChecked;
    }

    public string Label { get; }
    public BuildArchetype? Archetype { get; }

    /// <summary>Two-way con el RadioButton; solo el paso a true dispara la selección.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value) && value)
                _onSelected(this);
        }
    }
}
```

- [ ] **Step 2: Wire state + options into `MainViewModel`**

Add fields next to `_currentQueueId` (~line 51):

```csharp
private BuildArchetype? _forcedArchetype;
private GameState? _lastGameState;
```

In the constructor, after the `Objectives = ...` line, build the options (Auto checked by default; labels match `ItemAdvisor.ArchetypeLabel`):

```csharp
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
```

Expose the collection next to the other `ObservableCollection` properties (~line 112):

```csharp
public ObservableCollection<BuildRoleOptionViewModel> BuildRoles { get; }
```

Add the handler and the reset next to `RebuildItemPlan`:

```csharp
/// <summary>El jugador eligió build en la UI: recalcular el consejo al instante.</summary>
private void OnBuildRoleSelected(BuildRoleOptionViewModel option)
{
    _forcedArchetype = option.Archetype;
    AppendConsole($"[items] build role: {option.Label}");
    if (_lastGameState is { } state)
    {
        RebuildItemPlan(state);
        RebuildMayhemAdvice(state);
    }
}

/// <summary>Vuelve el selector a Auto (el override es una decisión por partida).</summary>
private void ResetBuildRole()
{
    foreach (var role in BuildRoles)
        role.IsChecked = role.Archetype is null;
}
```

- [ ] **Step 3: Pass the override on every tick and cache the last state**

In `ApplyGameState` (~line 384), right before `RebuildItemPlan(state);` add:

```csharp
_lastGameState = state;
```

In `RebuildItemPlan` (~line 411) change the advise call:

```csharp
var plan = _itemAdvisor?.Advise(state, _forcedArchetype);
```

In `RebuildMayhemAdvice` (~line 440) change the advise call:

```csharp
var advice = isMayhem ? _mayhemAdvisor?.Advise(state, _forcedArchetype) : null;
```

- [ ] **Step 4: Reset to Auto on game end / source switch**

In `ApplyLiveStatus` (~line 458), inside the `if (status != ConnectionStatus.Connected)` block, before `UpdateContext();`:

```csharp
_inGame = false;
_lastGameState = null; // sin partida: que el reset no recalcule sobre datos viejos
ResetBuildRole();
UpdateContext();
```

In `SwitchLiveSource` (~line 291), right after `DetachAndStopLiveSource();`:

```csharp
_lastGameState = null;
ResetBuildRole();
```

- [ ] **Step 5: Verify it compiles**

Run: `dotnet build src/LoLAdvisor.Core src/LoLAdvisor.App --nologo -v q`
Expected: Build succeeded, 0 errors. (If the app is running, the copy step fails with MSB3027 — close LoLAdvisor first.)

---

### Task 4: XAML — radio row in the Item advisor card

**Files:**
- Modify: `src/LoLAdvisor.App/MainWindow.xaml` (Item advisor card, ~line 114-141)

- [ ] **Step 1: Insert the selector row**

Inside the card's `StackPanel`, right after the closing `</TextBlock>` of the `ThreatSummary` TextBlock (~line 127) and before the `ItemPanelHint` TextBlock, insert:

```xml
<!-- Selector de build: Auto (detección automática) o un rol forzado por el jugador. -->
<StackPanel Orientation="Horizontal" Margin="0,8,0,0">
    <TextBlock Text="Build:" FontSize="13" FontWeight="SemiBold"
               Foreground="{StaticResource TextMuted}"
               VerticalAlignment="Center" Margin="0,0,10,0" />
    <ItemsControl ItemsSource="{Binding BuildRoles}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <RadioButton GroupName="BuildRole" Content="{Binding Label}"
                             IsChecked="{Binding IsChecked, Mode=TwoWay}"
                             FontSize="13" Margin="0,2,14,2" VerticalAlignment="Center"
                             Foreground="{StaticResource TextPrimary}" />
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```

(WrapPanel: con 8 opciones la fila envuelve en vez de desbordar si la ventana es angosta. `GroupName` agrupa los radios entre items del ItemsControl; el two-way binding devuelve `false` al VM del radio que se desmarca.)

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/LoLAdvisor.App --nologo -v q`
Expected: Build succeeded, 0 errors.

---

### Task 5: Full verification

- [ ] **Step 1: Full test suite**

Run: `dotnet test tests/LoLAdvisor.Tests --nologo -v q`
Expected: 138 passed, 0 failed.

- [ ] **Step 2: Manual check in replay mode (app must be closed first if running)**

Run: `dotnet run --project src/LoLAdvisor.App`
Then in the app:
1. Enable "Replay mode", scenario "ARAM: Mayhem".
2. The Item advisor card shows the `Build:` row with `Auto` selected.
3. Click `Mage`: recommendations change instantly, the threat banner ends with `| Build override: mage`, and the Mayhem guidance line switches to "As a mage, …".
4. Click `Auto`: back to detected build (banner override suffix gone).
5. Toggle Replay mode off and on: the selector is back on `Auto`.

- [ ] **Step 3: Report results to the user** (which checks passed, any surprises).

# On-Card Augment Tier Badges Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pin a click-through tier-letter badge (S/A/B… + gold BEST) directly above each offered augment card in-game, positioned from the OCR bounding boxes.

**Architecture:** Windows OCR already computes word bounding boxes; we stop discarding them. The detector reports which OCR line each match came from; pure Core math maps a box from the upscaled center-crop back to native frame (= game client) pixels; `ClientToScreen` + DPI divide turns that into WPF canvas coordinates inside a new transparent click-through topmost window that covers the game client area. Lifetime mirrors the OFFERED NOW list (v2.1.6 `PickWindowTracker` grace).

**Tech Stack:** WPF, Windows.Media.Ocr, user32 interop (existing patterns in `OverlayWindow.xaml.cs` and `GameWindowCapture.cs`), xUnit.

Spec: `docs/superpowers/specs/2026-07-18-augment-card-badges-design.md`

---

### Task 1: Core geometry — `OcrBox` + `FrameTransform`

**Files:**
- Create: `src/ParadoxLoLCompanion.Core/Augments/BadgeGeometry.cs`
- Test: `tests/ParadoxLoLCompanion.Tests/BadgeGeometryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ParadoxLoLCompanion.Core.Augments;

namespace ParadoxLoLCompanion.Tests;

/// <summary>
/// Mapeo de cajas OCR del recorte central reescalado de vuelta a píxeles nativos
/// del frame (= coordenadas de cliente del juego). Calibración real: 1080p,
/// crop x 15 % (288 px), y 20 % (216 px), scale 2 (FrameOps).
/// </summary>
public class BadgeGeometryTests
{
    [Fact]
    public void CalibratedCropTransform_MapsBoxBackToNative()
    {
        var t = new FrameTransform(OffsetX: 288, OffsetY: 216, Scale: 2);
        var native = t.ToNative(new OcrBox(X: 100, Y: 50, Width: 40, Height: 20));
        Assert.Equal(new OcrBox(338, 241, 20, 10), native);
    }

    [Fact]
    public void IdentityTransform_IsPassthrough()
    {
        var box = new OcrBox(10, 20, 30, 40);
        Assert.Equal(box, FrameTransform.Identity.ToNative(box));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests --filter BadgeGeometry --nologo`
Expected: compile error CS0246 (`FrameTransform` / `OcrBox` not found).

- [ ] **Step 3: Minimal implementation**

```csharp
namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>Caja de una línea OCR en píxeles enteros (sin tipos de Windows en Core).</summary>
public readonly record struct OcrBox(int X, int Y, int Width, int Height);

/// <summary>
/// Transformación frame→recorte usada por el OCR de pase 2: invertirla lleva una
/// caja detectada en el recorte reescalado de vuelta a píxeles nativos del frame
/// (que son coordenadas de cliente de la ventana del juego).
/// </summary>
public readonly record struct FrameTransform(int OffsetX, int OffsetY, int Scale)
{
    public static readonly FrameTransform Identity = new(0, 0, 1);

    public OcrBox ToNative(OcrBox box) => new(
        OffsetX + box.X / Scale,
        OffsetY + box.Y / Scale,
        box.Width / Scale,
        box.Height / Scale);
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests --filter BadgeGeometry --nologo`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.Core/Augments/BadgeGeometry.cs tests/ParadoxLoLCompanion.Tests/BadgeGeometryTests.cs
git commit -m "feat(badges): OcrBox + FrameTransform crop-inversion math (Core, TDD)"
```

---

### Task 2: Detector reports the source line index

**Files:**
- Modify: `src/ParadoxLoLCompanion.Core/Augments/OfferedAugmentDetector.cs`
- Test: `tests/ParadoxLoLCompanion.Tests/OfferedAugmentDetectorTests.cs`

- [ ] **Step 1: Write the failing test** (append inside the existing class)

```csharp
    [Fact]
    public void Matches_ReportTheSourceLineIndex()
    {
        var offered = new OfferedAugmentDetector(List).Detect(new[]
        { "CHOOSE ONE", "Goliath", "noise", "Eureka" });

        Assert.Equal(3, offered.Single(a => a.Name == "Eureka").LineIndex);
        Assert.Equal(1, offered.Single(a => a.Name == "Goliath").LineIndex);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests --filter OfferedAugmentDetector --nologo`
Expected: compile error — `OfferedAugment` has no `LineIndex`.

- [ ] **Step 3: Implement** — full new body of `OfferedAugmentDetector.cs`:

```csharp
namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>Un augment reconocido en pantalla durante la ventana de pick.
/// <paramref name="LineIndex"/>: índice de la línea OCR donde apareció el nombre
/// (-1 si no aplica) — el App lo usa para anclar el badge sobre la carta.</summary>
public sealed record OfferedAugment(
    int Id, string Name, AugmentRarity Rarity, int? Tier, string TierLabel, bool IsBest,
    int LineIndex = -1);

/// <summary>
/// De líneas de OCR del frame completo a los augments ofrecidos. Sin geometría
/// de cartas: los nombres son evidencia suficiente y sobreviven cambios de
/// resolución e idioma. Con menos de 2 matches no se afirma nada (una sola
/// línea puede ser el tooltip de un augment ya tomado).
/// </summary>
public sealed class OfferedAugmentDetector(AugmentTierList list)
{
    /// <summary>Expuesto para inyectarle aliases localizados (cdragon).</summary>
    public AugmentNameMatcher Matcher { get; } = new(list);

    public IReadOnlyList<OfferedAugment> Detect(IReadOnlyList<string> ocrLines)
    {
        var hits = new List<(AugmentInfo Info, int Line)>();
        for (var i = 0; i < ocrLines.Count; i++)
            if (Matcher.Match(ocrLines[i]) is { } augment
                && hits.All(h => h.Info.Id != augment.Id))
                hits.Add((augment, i));

        if (hits.Count < 2)
            return Array.Empty<OfferedAugment>();

        return hits
            .OrderBy(h => h.Info.Tier ?? int.MaxValue)
            .ThenByDescending(h => h.Info.Rarity)
            .Select((h, i) => new OfferedAugment(
                h.Info.Id, h.Info.Name, h.Info.Rarity, h.Info.Tier, h.Info.TierLabel,
                IsBest: i == 0, LineIndex: h.Line))
            .ToArray();
    }
}
```

- [ ] **Step 4: Run to verify pass (full suite — record ctor call sites must still compile)**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests --nologo`
Expected: all pass, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.Core/Augments/OfferedAugmentDetector.cs tests/ParadoxLoLCompanion.Tests/OfferedAugmentDetectorTests.cs
git commit -m "feat(badges): OfferedAugment carries its source OCR line index"
```

---

### Task 3: OCR reader returns boxes; FrameOps returns its transform

App-side (WPF, not unit-testable in the Core-only test project); verified by build + Task 7 live check.

**Files:**
- Modify: `src/ParadoxLoLCompanion.App/Capture/WindowsOcrReader.cs`
- Modify: `src/ParadoxLoLCompanion.App/Capture/FrameOps.cs`

- [ ] **Step 1: `WindowsOcrReader`** — full new body:

```csharp
using System.Runtime.InteropServices.WindowsRuntime;
using ParadoxLoLCompanion.Core.Augments;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ParadoxLoLCompanion.App.Capture;

/// <summary>Línea OCR con su caja (unión de las cajas de sus palabras), en
/// píxeles del frame que se le pasó al motor.</summary>
public sealed record OcrLineResult(string Text, OcrBox Box);

/// <summary>
/// Windows.Media.Ocr sobre un frame capturado → líneas de texto con geometría.
/// El motor viene con Windows (sin dependencias); si el idioma del perfil no
/// tiene OCR instalado, cae a inglés, que además es el idioma de Blitz.
/// </summary>
public static class WindowsOcrReader
{
    public static async Task<IReadOnlyList<string>> ReadLinesAsync(CapturedFrame frame)
        => (await ReadLinesWithBoxesAsync(frame)).Select(l => l.Text).ToArray();

    public static async Task<IReadOnlyList<OcrLineResult>> ReadLinesWithBoxesAsync(
        CapturedFrame frame)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null)
            return Array.Empty<OcrLineResult>();

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            frame.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, frame.Width, frame.Height,
            BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap);
        return result.Lines.Select(line =>
        {
            double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                left = Math.Min(left, r.X);
                top = Math.Min(top, r.Y);
                right = Math.Max(right, r.X + r.Width);
                bottom = Math.Max(bottom, r.Y + r.Height);
            }
            var box = line.Words.Count == 0
                ? default
                : new OcrBox((int)left, (int)top, (int)(right - left), (int)(bottom - top));
            return new OcrLineResult(line.Text, box);
        }).ToArray();
    }
}
```

- [ ] **Step 2: `FrameOps.CenterCropUpscaled`** — change signature to also return the transform. Replace the method signature and return:

```csharp
    public static (CapturedFrame Frame, FrameTransform Transform) CenterCropUpscaled(
        CapturedFrame frame)
```

(body unchanged until the return) and:

```csharp
        return (new CapturedFrame(output, outW, outH),
            new FrameTransform(cropX, cropY, scale));
```

Add `using ParadoxLoLCompanion.Core.Augments;` at the top.

- [ ] **Step 3: Fix the one caller now so the solution builds** — in `MainViewModel.DetectOfferedAsync` (`src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs`), replace:

```csharp
                var cropped = Capture.FrameOps.CenterCropUpscaled(frame);
```

with:

```csharp
                var (cropped, cropTransform) = Capture.FrameOps.CenterCropUpscaled(frame);
```

(`cropTransform` becomes used in Task 5; an unused-variable warning in between is fine.)

- [ ] **Step 4: Build**

Run: `dotnet build --nologo`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.App/Capture/WindowsOcrReader.cs src/ParadoxLoLCompanion.App/Capture/FrameOps.cs src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs
git commit -m "feat(badges): OCR lines carry bounding boxes; crop pass exposes its transform"
```

---

### Task 4: Game client → screen origin

**Files:**
- Modify: `src/ParadoxLoLCompanion.App/Capture/GameWindowCapture.cs`

- [ ] **Step 1: Add a public origin accessor** (below `Capture()`, reusing the private interop already in the file):

```csharp
    /// <summary>
    /// Origen (screen px) del área cliente del juego: los píxeles del frame
    /// capturado son coordenadas de cliente, esto los vuelve absolutos para
    /// posicionar la ventana de badges. <c>null</c> si el juego no está.
    /// </summary>
    public static (int X, int Y)? GetClientOrigin()
    {
        var hwnd = FindGameWindow();
        if (hwnd == IntPtr.Zero)
            return null;
        var origin = new NativePoint();
        return ClientToScreen(hwnd, ref origin) ? (origin.X, origin.Y) : null;
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build --nologo` — Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ParadoxLoLCompanion.App/Capture/GameWindowCapture.cs
git commit -m "feat(badges): expose game client screen origin"
```

---

### Task 5: ViewModel — badge placements

**Files:**
- Modify: `src/ParadoxLoLCompanion.App/ViewModels/ItemViewModels.cs` (badge VM + brush reuse)
- Modify: `src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Extract the tier brush and add the badge VM.** In `ItemViewModels.cs`, inside `OfferedAugmentRowViewModel`, replace the `TierBrush = offered.Tier switch {...};` assignment with `TierBrush = BrushForTier(offered.Tier);` and add the static method + new class:

```csharp
    /// <summary>Mismo código de color de tier para la lista y los badges on-card.</summary>
    internal static Brush BrushForTier(int? tier) => tier switch
    {
        1 => Palette.Gold,
        2 => Palette.Green,
        3 => Palette.Blue,
        null => Palette.Muted,
        _ => Palette.Amber,
    };
```

```csharp
/// <summary>Badge anclado sobre una carta de augment in-game. Left/Top en DIPs
/// relativos a la ventana de badges (que cubre el área cliente del juego);
/// Left ya viene corrido -80 para centrar el contenedor de 160 DIP.</summary>
public sealed class AugmentBadgeViewModel
{
    public double Left { get; init; }
    public double Top { get; init; }
    public string TierLabel { get; init; } = "";
    public bool IsBest { get; init; }
    public Brush TierBrush { get; init; } = Palette.Muted;
}
```

- [ ] **Step 2: MainViewModel state.** Next to the `OfferedAugments` collection (`~line 275`), add:

```csharp
    /// <summary>Badges sobre las cartas (ventana click-through). Poblado junto a
    /// OfferedAugments cuando además tenemos geometría y origen de pantalla.</summary>
    public ObservableCollection<AugmentBadgeViewModel> AugmentBadges { get; } = new();

    /// <summary>Rect (screen px) que la ventana de badges debe cubrir = área
    /// cliente del juego en el momento de la última detección.</summary>
    public Int32Rect BadgeSurfacePx { get; private set; }

    /// <summary>Escala DPI del monitor (MainWindow la mantiene al día); las cajas
    /// OCR vienen en px físicos y WPF posiciona en DIPs.</summary>
    public double DpiScale { get; set; } = 1.0;
```

`Int32Rect` lives in `System.Windows` (already imported via existing usings — verify; if not, fully qualify).

- [ ] **Step 3: Populate in `DetectOfferedAsync`.** Replace the body between the capture and the `catch` with the boxes-aware version:

```csharp
            var frame = await Task.Run(Capture.GameWindowCapture.Capture).ConfigureAwait(false);
            if (frame is null)
            {
                LogOcr("game window not found or capture failed — no on-screen detection.");
                return;
            }

            // Pase 1: frame completo a resolución nativa (transform identidad).
            var pass1 = await Capture.WindowsOcrReader.ReadLinesWithBoxesAsync(frame)
                .ConfigureAwait(false);
            var texts = pass1.Select(l => l.Text).ToList();
            var boxes = pass1.Select(l => (l.Box, FrameTransform.Identity)).ToList();
            var offered = _offeredDetector!.Detect(texts);

            // Pase 2 (solo si el 1 no vio nada): recorte central reescalado x2 —
            // los títulos chicos a 1080p suelen necesitarlo.
            if (offered.Count == 0)
            {
                var (cropped, cropTransform) = Capture.FrameOps.CenterCropUpscaled(frame);
                var pass2 = await Capture.WindowsOcrReader.ReadLinesWithBoxesAsync(cropped)
                    .ConfigureAwait(false);
                texts = texts.Concat(pass2.Select(l => l.Text)).ToList();
                boxes.AddRange(pass2.Select(l => (l.Box, cropTransform)));
                offered = _offeredDetector.Detect(texts);
                LogOcr($"frame {frame.Width}x{frame.Height}: pass1 {pass1.Count} lines, " +
                       $"pass2 {pass2.Count} lines → {offered.Count} augment matches.");
                if (offered.Count == 0 && DateTime.UtcNow - _lastOcrDumpUtc > TimeSpan.FromSeconds(8))
                {
                    _lastOcrDumpUtc = DateTime.UtcNow;
                    var sample = string.Join(" | ", texts
                        .Where(l => l.Trim().Length > 2).Take(30));
                    FileLog.Write($"[ocr] no augment matches; OCR saw: {sample}");
                }
            }
            else
            {
                LogOcr($"frame {frame.Width}x{frame.Height}: {pass1.Count} lines → " +
                       $"{offered.Count} augment matches ({string.Join(", ", offered.Select(o => o.Name))}).");
            }

            var found = offered;
            var origin = Capture.GameWindowCapture.GetClientOrigin();
            OnUi(() =>
            {
                // Sin matches se conserva lo último visto: el jugador pudo abrir
                // el tab del scoreboard encima; una lectura vacía no borra la buena.
                if (found.Count == 0)
                    return;
                OfferedAugments.Clear();
                foreach (var augment in found)
                    OfferedAugments.Add(new OfferedAugmentRowViewModel(augment));
                RebuildAugmentBadges(found, boxes, frame.Width, frame.Height, origin);
            });
```

- [ ] **Step 4: The badge builder** (new private method after `DetectOfferedAsync`):

```csharp
    /// <summary>Ancla un badge centrado sobre la caja del título de cada carta.
    /// Fail-open: sin origen o sin caja para un match, ese badge no se muestra —
    /// la lista OFFERED NOW sigue siendo la red de seguridad.</summary>
    private void RebuildAugmentBadges(IReadOnlyList<OfferedAugment> offered,
        IReadOnlyList<(OcrBox Box, FrameTransform Transform)> lineBoxes,
        int frameWidth, int frameHeight, (int X, int Y)? origin)
    {
        AugmentBadges.Clear();
        if (origin is not { } o)
            return;
        BadgeSurfacePx = new Int32Rect(o.X, o.Y, frameWidth, frameHeight);
        var dpi = DpiScale > 0 ? DpiScale : 1.0;
        foreach (var augment in offered)
        {
            if (augment.LineIndex < 0 || augment.LineIndex >= lineBoxes.Count)
                continue;
            var (box, transform) = lineBoxes[augment.LineIndex];
            if (box.Width == 0)
                continue;   // línea sin palabras: no hay ancla
            var native = transform.ToNative(box);
            AugmentBadges.Add(new AugmentBadgeViewModel
            {
                Left = (native.X + native.Width / 2.0) / dpi - 80,
                Top = native.Y / dpi - 32,
                TierLabel = augment.TierLabel,
                IsBest = augment.IsBest,
                TierBrush = OfferedAugmentRowViewModel.BrushForTier(augment.Tier),
            });
        }
    }
```

- [ ] **Step 5: Clear badges wherever `OfferedAugments` clears.** In `RebuildMayhemAdvice`, both the advice-null branch and the grace-expiry branch: add `AugmentBadges.Clear();` right after each existing `OfferedAugments.Clear();`.

- [ ] **Step 6: Build**

Run: `dotnet build --nologo` — Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/ParadoxLoLCompanion.App/ViewModels/ItemViewModels.cs src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs
git commit -m "feat(badges): badge placements computed from OCR boxes in the ViewModel"
```

---

### Task 6: The badge window + MainWindow ownership

**Files:**
- Create: `src/ParadoxLoLCompanion.App/AugmentBadgeWindow.xaml`
- Create: `src/ParadoxLoLCompanion.App/AugmentBadgeWindow.xaml.cs`
- Modify: `src/ParadoxLoLCompanion.App/MainWindow.xaml.cs`

- [ ] **Step 1: XAML**

```xml
<Window x:Class="ParadoxLoLCompanion.App.AugmentBadgeWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Augment Badges"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ShowActivated="False"
        ResizeMode="NoResize" IsHitTestVisible="False">

    <!-- Ventana click-through que cubre el área cliente del juego: un badge de
         tier centrado sobre el título de cada carta ofrecida. El contenedor de
         160 DIP centra el chip sin medir su ancho real. -->
    <ItemsControl ItemsSource="{Binding AugmentBadges}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate><Canvas /></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemContainerStyle>
            <Style TargetType="ContentPresenter">
                <Setter Property="Canvas.Left" Value="{Binding Left}" />
                <Setter Property="Canvas.Top" Value="{Binding Top}" />
            </Style>
        </ItemsControl.ItemContainerStyle>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Grid Width="160">
                    <Border HorizontalAlignment="Center" BorderThickness="1"
                            CornerRadius="3" Padding="8,3">
                        <Border.Style>
                            <Style TargetType="Border">
                                <Setter Property="Background" Value="#E00B0E0C" />
                                <Setter Property="BorderBrush" Value="{Binding TierBrush}" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsBest}" Value="True">
                                        <Setter Property="Background" Value="#F0241C08" />
                                        <Setter Property="BorderBrush" Value="{StaticResource Gold}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Border.Style>
                        <StackPanel Orientation="Horizontal">
                            <TextBlock Text="{Binding TierLabel}"
                                       FontFamily="{StaticResource HudFont}" FontSize="14"
                                       FontWeight="Bold" Foreground="{Binding TierBrush}"
                                       VerticalAlignment="Center" />
                            <TextBlock Text="◆ BEST" Margin="7,0,0,0"
                                       FontFamily="{StaticResource HudFont}" FontSize="11"
                                       FontWeight="Bold" Foreground="{StaticResource Gold}"
                                       VerticalAlignment="Center">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsBest}" Value="False">
                                                <Setter Property="Visibility" Value="Collapsed" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>
                        </StackPanel>
                    </Border>
                </Grid>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</Window>
```

- [ ] **Step 2: Code-behind**

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ParadoxLoLCompanion.App;

/// <summary>
/// Superficie de badges: transparente, topmost, click-through (WS_EX_TRANSPARENT)
/// y sin foco (WS_EX_NOACTIVATE) — el juego recibe todos los clicks. Se posiciona
/// en píxeles físicos con SetWindowPos sobre el área cliente del juego.
/// </summary>
public partial class AugmentBadgeWindow : Window
{
    public AugmentBadgeWindow() => InitializeComponent();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(hwnd, GwlExstyle,
            style | WsExTransparent | WsExNoactivate | WsExToolwindow);
    }

    /// <summary>Muestra la ventana cubriendo exactamente el rect (screen px).</summary>
    public void ShowOver(Int32Rect px)
    {
        if (px.Width <= 0 || px.Height <= 0)
            return;
        if (!IsVisible)
            Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HwndTopmost, px.X, px.Y, px.Width, px.Height,
            SwpNoactivate | SwpShowwindow);
    }

    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolwindow = 0x80;
    private const int WsExNoactivate = 0x08000000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
        int x, int y, int w, int h, uint flags);
}
```

- [ ] **Step 3: MainWindow wiring.** In `MainWindow.xaml.cs`: add field `private AugmentBadgeWindow? _badges;` next to `_overlay`. In the constructor (after `InitializeComponent()` / existing init), add:

```csharp
        Loaded += (_, _) =>
        {
            if (DataContext is not ViewModels.MainViewModel vm)
                return;
            vm.DpiScale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
            vm.AugmentBadges.CollectionChanged += (_, _) => SyncBadgeWindow(vm);
        };
```

and the sync method + DPI override (class members):

```csharp
    /// <summary>Vida de la ventana de badges: existe mientras haya badges que
    /// mostrar; se re-posiciona en cada detección (el juego pudo moverse).</summary>
    private void SyncBadgeWindow(ViewModels.MainViewModel vm)
    {
        if (vm.AugmentBadges.Count == 0)
        {
            _badges?.Hide();
            return;
        }
        if (_badges is null)
        {
            _badges = new AugmentBadgeWindow { DataContext = vm };
            _badges.Closed += (_, _) => _badges = null;
        }
        _badges.ShowOver(vm.BadgeSurfacePx);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (DataContext is ViewModels.MainViewModel vm)
            vm.DpiScale = newDpi.DpiScaleX;
    }
```

Close it on exit: where `_overlay?.Close();` runs, add `_badges?.Close();`.

- [ ] **Step 4: Build + full tests**

Run: `dotnet build --nologo && dotnet test tests/ParadoxLoLCompanion.Tests --nologo`
Expected: 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.App/AugmentBadgeWindow.xaml src/ParadoxLoLCompanion.App/AugmentBadgeWindow.xaml.cs src/ParadoxLoLCompanion.App/MainWindow.xaml.cs
git commit -m "feat(badges): click-through topmost badge window pinned over the cards"
```

---

### Task 7: Verify, graph, release

- [ ] **Step 1: Full suite + build** — `dotnet build --nologo && dotnet test tests/ParadoxLoLCompanion.Tests --nologo`, expect green.
- [ ] **Step 2:** `graphify update .`
- [ ] **Step 3: Replay-mode smoke test** — run the app in replay mode if feasible; otherwise rely on tests + live game.
- [ ] **Step 4: Release** (per INSTRUCCIONES.md): bump `version.txt` + csproj to 2.2.0, `dotnet publish` (instalador.bat flags) + rename + copy, ISCC installer, `gh release create v2.2.0` with both assets, commit bump, push.
- [ ] **Step 5:** Live validation happens in the user's next real Mayhem game (Borderless); the `[ocr]` session log plus badge visibility confirm placement. Known risk to watch: pass-1 matches (native resolution) anchor to smaller title boxes than pass-2 — both map to the same native coords, so placement should be identical.

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using ParadoxLoLCompanion.App;
using ParadoxLoLCompanion.App.ViewModels;
using ParadoxLoLCompanion.Core.Config;

namespace ParadoxLoLCompanion.App.Tests;

// Exercise the real WPF timer and routed input events without showing any visible UI.
public sealed class OverlayAutoCloseTests : IClassFixture<OverlayDispatcher>
{
    private readonly OverlayDispatcher _ui;

    public OverlayAutoCloseTests(OverlayDispatcher ui) => _ui = ui;

    [Fact]
    public Task DeathOverlay_ClosesAfterDefaultFiveSeconds() => _ui.RunAsync(async () =>
    {
        using var test = new TestOverlay(new UserPreferences());
        var clock = Stopwatch.StartNew();
        test.Window.ShowForDeath();
        await Task.Delay(4500);
        Assert.True(test.Window.IsVisible);
        await WaitUntilHidden(test.Window);
        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(5));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Input_DoesNotCancelAutoClose(bool keyboard) => _ui.RunAsync(async () =>
    {
        using var test = new TestOverlay();
        test.Window.ShowForDeath();
        if (keyboard)
        {
            test.Window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(test.Window)!, Environment.TickCount, Key.A)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
        else
        {
            test.Window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,
                Environment.TickCount, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
        }

        await WaitUntilHidden(test.Window);
    });

    [Fact]
    public Task EnablingAutoClose_ArmsAlreadyOpenDeathOverlay() => _ui.RunAsync(async () =>
    {
        using var test = new TestOverlay(new UserPreferences
        { AutoCloseOnDeath = false, AutoCloseSeconds = 0.15 });
        test.Window.ShowForDeath();
        await Task.Delay(300);
        Assert.True(test.Window.IsVisible);

        test.ViewModel.OverlayAutoCloseOnDeath = true;
        await WaitUntilHidden(test.Window);
    });

    [Fact]
    public Task DisablingAutoClose_CancelsPendingClose() => _ui.RunAsync(async () =>
    {
        using var test = new TestOverlay();
        test.Window.ShowForDeath();
        test.ViewModel.OverlayAutoCloseOnDeath = false;
        await Task.Delay(350);
        Assert.True(test.Window.IsVisible);
    });

    [Fact]
    public Task ManualOverlay_StaysOpenAcrossDeath() => _ui.RunAsync(async () =>
    {
        using var test = new TestOverlay();
        test.Window.Toggle();
        test.Window.ShowForDeath();
        await Task.Delay(350);
        Assert.True(test.Window.IsVisible);
    });

    [Fact]
    public Task ManualReopen_IsNotClosedByPreviousDeathTimer() => _ui.RunAsync(async () =>
    {
        using var test = new TestOverlay();
        test.Window.ShowForDeath();
        test.Window.Toggle();
        test.Window.Toggle();
        await Task.Delay(350);
        Assert.True(test.Window.IsVisible);
    });

    [Fact]
    public Task NextDeath_StartsANewCountdown() => _ui.RunAsync(async () =>
    {
        using var test = new TestOverlay();
        test.Window.ShowForDeath();
        await WaitUntilHidden(test.Window);
        test.Window.ShowForDeath();
        Assert.True(test.Window.IsVisible);
        await WaitUntilHidden(test.Window);
    });

    private static async Task WaitUntilHidden(OverlayWindow window)
    {
        var clock = Stopwatch.StartNew();
        while (window.IsVisible && clock.Elapsed < TimeSpan.FromSeconds(2))
            await Task.Delay(25);
        Assert.False(window.IsVisible);
    }

    private sealed class TestOverlay : IDisposable
    {
        private readonly string _preferencesPath = Path.Combine(Path.GetTempPath(),
            $"paradox-overlay-test-{Guid.NewGuid():N}.json");

        public MainViewModel ViewModel { get; }
        public OverlayWindow Window { get; }

        public TestOverlay(UserPreferences? preferences = null)
        {
            ViewModel = new MainViewModel(Dispatcher.CurrentDispatcher, new AdvisorConfig(),
                preferences: preferences ?? new UserPreferences { AutoCloseSeconds = 0.15 },
                preferencesPath: _preferencesPath);
            Window = new OverlayWindow
            {
                DataContext = ViewModel,
                Opacity = 0,
                Top = -10000,
                Left = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
        }

        public void Dispose()
        {
            Window.Close();
            ViewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            File.Delete(_preferencesPath);
        }
    }
}

public sealed class OverlayDispatcher : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    public OverlayDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // Load the production theme without running App.OnStartup (updater/client/UI).
                var root = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "App.Resources.xaml")).Root!;
                XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var resources = new XElement(ns + "ResourceDictionary",
                    root.Attributes().Where(a => a.IsNamespaceDeclaration),
                    root.Element(ns + "Application.Resources")!.Elements());
                app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString().Replace(
                    "clr-namespace:ParadoxLoLCompanion.App.Theme",
                    "clr-namespace:ParadoxLoLCompanion.App.Theme;assembly=ParadoxLoLCompanion.App"));
                ready.SetResult(app.Dispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "Overlay tests" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcher = ready.Task.GetAwaiter().GetResult();
    }

    public Task RunAsync(Func<Task> test) => _dispatcher.InvokeAsync(test).Task.Unwrap();

    public void Dispose()
    {
        _dispatcher.Invoke(() => Application.Current.Shutdown());
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}

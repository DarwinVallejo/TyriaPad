using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using TyriaPad.Core;
using TyriaPad.Core.Config;
using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Game;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Overlay;

namespace TyriaPad.App.Overlay;

/// <summary>
/// Diagnostics without GW2. <c>--overlay-preview</c> shows the overlay full screen over the
/// desktop and cycles through sample states (layers, radial, cursor); <c>--overlay-snapshot
/// &lt;folder&gt;</c> saves those same states as PNGs at 1920x1080 and exits.
/// </summary>
internal static class OverlayPreview
{
    private static readonly TimeSpan CycleInterval = TimeSpan.FromSeconds(2.5);

    public static IReadOnlyList<(string Name, OverlayState State)> SampleStates(Profile profile)
    {
        ContextLayout combat = profile.Resolve(GameContext.Combat);
        ContextLayout pointer = profile.Resolve(GameContext.Pointer);
        ContextLayout mount = profile.Resolve(GameContext.Mount);
        Layer LayerOf(ContextLayout layout, GamepadButtons modifiers) => layout.Layers.FirstOrDefault(l => l.Modifiers == modifiers) ?? layout.Base;

        var states = new List<(string, OverlayState)>
        {
            ("base", new OverlayState(EngineState.Active, CameraMode.ActionCamera, GameContext.Combat, combat, combat.Base, null)),
            ("lb", new OverlayState(EngineState.Active, CameraMode.ActionCamera, GameContext.Combat, combat, LayerOf(combat, GamepadButtons.LeftBumper), null)),
            ("lt", new OverlayState(EngineState.Active, CameraMode.ActionCamera, GameContext.Combat, combat, LayerOf(combat, GamepadButtons.LeftTrigger), null)),
            ("lt-rt", new OverlayState(EngineState.Active, CameraMode.ActionCamera, GameContext.Combat, combat, LayerOf(combat, GamepadButtons.LeftTrigger | GamepadButtons.RightTrigger), null)),
            ("pointer", new OverlayState(EngineState.Active, CameraMode.Pointer, GameContext.Pointer, pointer, pointer.Base, null)),
            ("mount", new OverlayState(EngineState.Active, CameraMode.ActionCamera, GameContext.Mount, mount, mount.Base, null)),
        };
        foreach ((string name, RadialMenu menu) in profile.Radials)
        {
            states.Add(($"radial-{name}", new OverlayState(EngineState.Active, CameraMode.ActionCamera, GameContext.Combat, combat, combat.Base, new RadialState(menu, Math.Min(2, menu.Items.Count - 1)))));
        }

        return states;
    }

    /// <summary>Full-screen sample overlay. Returns what must be closed on exit.</summary>
    public static IDisposable Show(LoadedConfig config)
    {
        var window = new OverlayWindow();
        var bounds = new WindowBounds(0, 0, WindowInterop.GetSystemMetrics(WindowInterop.SmCxScreen), WindowInterop.GetSystemMetrics(WindowInterop.SmCyScreen));
        IReadOnlyList<(string Name, OverlayState State)> states = SampleStates(config.Profile);
        OverlaySettings settings = config.Settings.Overlay;
        // Same scaling GW2 would apply on this monitor.
        SkillBarLayout layout = SkillBarLayout.FromCalibration(SkillBarLayout.DefaultCalibration(bounds.Width, bounds.Height, UiSize.Normal, (float)window.DpiScale), settings.ProfessionSlots);
        int index = 0;

        void Apply()
        {
            (string name, OverlayState state) = states[index];
            window.Canvas.Model = OverlayModelBuilder.Build(state, bounds, layout, settings, showSkillBar: true, showSlotFrames: true);
            window.Place(bounds);
            Log.Write($"Overlay preview: {name}");
            index = (index + 1) % states.Count;
        }

        var timer = new DispatcherTimer(CycleInterval, DispatcherPriority.Normal, (_, _) => Apply(), Dispatcher.CurrentDispatcher);
        Apply();
        timer.Start();
        return new Closer(() =>
        {
            timer.Stop();
            window.Close();
        });
    }

    /// <summary>Saves one PNG per sample state in the given folder.</summary>
    public static void Snapshot(LoadedConfig config, string directory, int width = 1920, int height = 1080)
    {
        Directory.CreateDirectory(directory);
        var bounds = new WindowBounds(0, 0, width, height);
        OverlaySettings settings = config.Settings.Overlay;
        SkillBarLayout layout = SkillBarLayout.FromCalibration(SkillBarLayout.DefaultCalibration(width, height, UiSize.Normal), settings.ProfessionSlots);
        foreach ((string name, OverlayState state) in SampleStates(config.Profile))
        {
            var canvas = new OverlayCanvas
            {
                Model = OverlayModelBuilder.Build(state, bounds, layout, settings, showSkillBar: true, showSlotFrames: true, backdrop: true),
            };
            canvas.Measure(new Size(width, height));
            canvas.Arrange(new Rect(0, 0, width, height));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream file = File.Create(Path.Combine(directory, $"overlay-{name}.png"));
            encoder.Save(file);
        }
    }

    private sealed class Closer(Action close) : IDisposable
    {
        public void Dispose() => close();
    }
}

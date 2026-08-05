using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Yukti.Contracts;
using Yukti.Domain.ModulePlugin;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Real desktop UI automation — raw mouse/keyboard/screen-pixel control via
/// Win32 SendInput, for native apps and legacy UIs with no DOM or
/// accessibility tree to hook into. Direct port of the original TS
/// prototype's UiModule (nut.js-backed) to the formal IAutomationModule
/// contract. (Volume 1 Part III §18)
///
/// No Setup/Teardown session, matching the TS version exactly — every
/// action opens no persistent handle, so both are no-ops like ApiModule's.
/// Requires a real, unlocked display; will not run headless (same
/// documented constraint the TS version states).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DesktopUiModule : IAutomationModule
{
    private const double DefaultMatchConfidence = 0.9;

    public ModuleKind Kind => ModuleKind.DesktopUi;
    public string ContractVersion => "1.0.0";

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema
        {
            ActionName = "click",
            Description = "Moves the mouse to (x, y) and left-clicks.",
            Parameters = new[]
            {
                new ParamSpec { Name = "x", Type = ParamType.Number, Required = true },
                new ParamSpec { Name = "y", Type = ParamType.Number, Required = true },
            }
        },
        new ActionSchema
        {
            ActionName = "typeText",
            Description = "Types text at the current keyboard focus.",
            Parameters = new[]
            {
                new ParamSpec { Name = "text", Type = ParamType.String, Required = true },
            }
        },
        new ActionSchema
        {
            ActionName = "pressKey",
            Description = "Presses and releases a single named key (e.g. Enter, Tab, Escape, A, F1).",
            Parameters = new[]
            {
                new ParamSpec { Name = "key", Type = ParamType.String, Required = true },
            }
        },
        new ActionSchema
        {
            ActionName = "findImage",
            Description = "Searches the live screen for a region matching a reference image. Not-found is a normal failure, not an exception.",
            Parameters = new[]
            {
                new ParamSpec { Name = "imagePath", Type = ParamType.String, Required = true },
            }
        },
        new ActionSchema
        {
            ActionName = "screenshot",
            Description = "Captures the full screen to local disk.",
            Parameters = new[]
            {
                new ParamSpec { Name = "path", Type = ParamType.String, Required = false, Description = "Defaults to ui-screenshot-<timestamp>.png." },
            }
        },
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        try
        {
            switch (action)
            {
                case "click":
                {
                    var x = RequireInt(parameters, "x");
                    var y = RequireInt(parameters, "y");
                    Win32Input.MoveTo(x, y);
                    Win32Input.LeftClick();
                    return Task.FromResult(StepOutcome.Passed($"Clicked ({x}, {y})"));
                }
                case "typeText":
                {
                    var text = RequireString(parameters, "text");
                    Win32Input.TypeText(text);
                    return Task.FromResult(StepOutcome.Passed($"Typed {text.Length} character(s)"));
                }
                case "pressKey":
                {
                    var key = RequireString(parameters, "key");
                    Win32Input.PressKey(key);
                    return Task.FromResult(StepOutcome.Passed($"Pressed {key}"));
                }
                case "findImage":
                {
                    var imagePath = RequireString(parameters, "imagePath");
                    using var reference = new Bitmap(imagePath);
                    using var screen = CaptureScreen();
                    var match = ImageMatcher.Find(screen, reference, DefaultMatchConfidence);
                    return Task.FromResult(match is { } m
                        ? StepOutcome.Passed($"Found image at ({m.X}, {m.Y})", new { x = m.X, y = m.Y, confidence = m.Confidence })
                        : StepOutcome.Failed($"Image {imagePath} not found on screen (confidence {DefaultMatchConfidence})"));
                }
                case "screenshot":
                {
                    var path = parameters.GetValueOrDefault("path") as string ?? $"ui-screenshot-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.png";
                    using var screen = CaptureScreen();
                    screen.Save(path, ImageFormat.Png);
                    return Task.FromResult(StepOutcome.Passed($"Screenshot saved to {path}", new { path }));
                }
                default:
                    return Task.FromResult(StepOutcome.Failed($"Unknown ui action \"{action}\"."));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(StepOutcome.Failed(ex.Message));
        }
    }

    private static Bitmap CaptureScreen()
    {
        var width = Win32Input.GetSystemMetrics(Win32Input.SM_CXSCREEN);
        var height = Win32Input.GetSystemMetrics(Win32Input.SM_CYSCREEN);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(0, 0, 0, 0, new Size(width, height));
        return bitmap;
    }

    private static string RequireString(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.GetValueOrDefault(name) as string
            ?? throw new ArgumentException($"ui.{name} requires a '{name}' parameter.");

    private static int RequireInt(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value is not null
            ? Convert.ToInt32(value)
            : throw new ArgumentException($"ui action requires a '{name}' parameter.");
}

/// <summary>Raw Win32 mouse/keyboard synthesis via SendInput — no GUI-framework dependency, mirrors ApiModule's bare-HttpClient minimalism.</summary>
internal static class Win32Input
{
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    private const int InputMouse = 0;
    private const int InputKeyboard = 1;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput mi;
        [FieldOffset(0)] public KeyboardInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int type;
        public InputUnion u;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public static void MoveTo(int x, int y) => SetCursorPos(x, y);

    public static void LeftClick()
    {
        var down = new Input { type = InputMouse, u = new InputUnion { mi = new MouseInput { dwFlags = MouseEventFLeftDown } } };
        var up = new Input { type = InputMouse, u = new InputUnion { mi = new MouseInput { dwFlags = MouseEventFLeftUp } } };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<Input>());
    }

    public static void TypeText(string text)
    {
        foreach (var c in text)
        {
            var down = new Input { type = InputKeyboard, u = new InputUnion { ki = new KeyboardInput { wScan = c, dwFlags = KeyEventFUnicode } } };
            var up = new Input { type = InputKeyboard, u = new InputUnion { ki = new KeyboardInput { wScan = c, dwFlags = KeyEventFUnicode | KeyEventFKeyUp } } };
            SendInput(2, new[] { down, up }, Marshal.SizeOf<Input>());
        }
    }

    public static void PressKey(string key)
    {
        if (!VirtualKeys.TryGetValue(key, out var vk))
            throw new ArgumentException($"Unknown key '{key}'.");
        var down = new Input { type = InputKeyboard, u = new InputUnion { ki = new KeyboardInput { wVk = vk } } };
        var up = new Input { type = InputKeyboard, u = new InputUnion { ki = new KeyboardInput { wVk = vk, dwFlags = KeyEventFKeyUp } } };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<Input>());
    }

    private static readonly Dictionary<string, ushort> VirtualKeys = BuildVirtualKeyMap();

    private static Dictionary<string, ushort> BuildVirtualKeyMap()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Escape"] = 0x1B, ["Space"] = 0x20,
            ["Backspace"] = 0x08, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23,
            ["PageUp"] = 0x21, ["PageDown"] = 0x22,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["Shift"] = 0x10, ["Control"] = 0x11, ["Alt"] = 0x12,
        };
        for (var c = 'A'; c <= 'Z'; c++) map[c.ToString()] = (ushort)c;
        for (var d = '0'; d <= '9'; d++) map[d.ToString()] = (ushort)d;
        for (var f = 1; f <= 12; f++) map[$"F{f}"] = (ushort)(0x70 + f - 1);
        return map;
    }
}

[SupportedOSPlatform("windows")]
internal readonly record struct ImageMatch(int X, int Y, double Confidence);

/// <summary>
/// Straightforward, correct sample-grid image matching — not an optimized
/// computer-vision pipeline (no OpenCvSharp/native dependency added). A
/// coarse positional scan followed by a local refinement around the best
/// coarse candidate, each position scored against a fixed sample grid
/// within the reference image rather than every pixel, to keep this
/// tractable at real screen resolutions.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ImageMatcher
{
    public static ImageMatch? Find(Bitmap screen, Bitmap reference, double matchConfidence)
    {
        var maxX = screen.Width - reference.Width;
        var maxY = screen.Height - reference.Height;
        if (maxX < 0 || maxY < 0)
            return null;

        var gridStep = Math.Max(1, Math.Min(reference.Width, reference.Height) / 10);
        var samplePoints = new List<Point>();
        for (var sy = 0; sy < reference.Height; sy += gridStep)
            for (var sx = 0; sx < reference.Width; sx += gridStep)
                samplePoints.Add(new Point(sx, sy));

        var stride = Math.Max(2, gridStep);
        var (bestX, bestY, bestScore) = ScanBest(screen, reference, samplePoints, 0, maxX, 0, maxY, stride);
        (bestX, bestY, bestScore) = ScanBest(screen, reference, samplePoints,
            Math.Max(0, bestX - stride), Math.Min(maxX, bestX + stride),
            Math.Max(0, bestY - stride), Math.Min(maxY, bestY + stride), 1);

        return bestScore >= matchConfidence ? new ImageMatch(bestX, bestY, bestScore) : null;
    }

    private static (int x, int y, double score) ScanBest(
        Bitmap screen, Bitmap reference, List<Point> samplePoints, int minX, int maxX, int minY, int maxY, int stride)
    {
        var bestScore = -1.0;
        var bestX = minX;
        var bestY = minY;
        for (var y = minY; y <= maxY; y += stride)
        {
            for (var x = minX; x <= maxX; x += stride)
            {
                var score = CompareAt(screen, reference, x, y, samplePoints);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestY = y;
                }
            }
        }
        return (bestX, bestY, bestScore);
    }

    private static double CompareAt(Bitmap screen, Bitmap reference, int offsetX, int offsetY, List<Point> samplePoints)
    {
        double totalDiff = 0;
        foreach (var p in samplePoints)
        {
            var refPixel = reference.GetPixel(p.X, p.Y);
            var scrPixel = screen.GetPixel(offsetX + p.X, offsetY + p.Y);
            totalDiff += (Math.Abs(refPixel.R - scrPixel.R) + Math.Abs(refPixel.G - scrPixel.G) + Math.Abs(refPixel.B - scrPixel.B)) / (3.0 * 255.0);
        }
        return 1.0 - totalDiff / samplePoints.Count;
    }
}

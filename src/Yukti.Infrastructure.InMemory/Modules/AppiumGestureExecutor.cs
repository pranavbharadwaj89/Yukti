using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Interactions;
using Yukti.Domain.MobileTesting;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Mobile Testing Module Spec, MOB-T5: synthesizes every <see cref="Gesture"/> via the W3C
/// Actions API (<see cref="ActionSequence"/>/<see cref="PointerInputDevice"/>), the same
/// mechanism <c>MobileModule</c>'s original "swipe" action already used -- generalized here
/// rather than duplicated per gesture.
///
/// Split the same way <c>AppiumLocatorResolver</c> splits <c>Build</c> from <c>ResolveAsync</c>:
/// the coordinate math (<see cref="ToAbsolute"/>, <see cref="DirectionToPoints"/>) and the
/// bounded-attempts control flow (<see cref="TryUntil"/>) are pure -- no driver, directly
/// unit-testable -- while everything that actually moves a pointer needs a live Appium
/// server/device this environment doesn't have, the same standing constraint as every other
/// driver-bound call in this module (MOB-T1/T3/T4). <c>MobileGestureTests</c> proves the pure
/// half; the driver-bound half is built and wired in, not exercisable end-to-end here.
/// </summary>
public static class AppiumGestureExecutor
{
    private static readonly TimeSpan DefaultSwipeDuration = TimeSpan.FromMilliseconds(300);

    /// <summary>Converts a screen/element-relative point into absolute device pixels within a
    /// region starting at (<paramref name="originX"/>, <paramref name="originY"/>) and sized
    /// <paramref name="width"/> x <paramref name="height"/>. Pure -- no driver needed.</summary>
    public static (int X, int Y) ToAbsolute(RelativePoint point, int originX, int originY, int width, int height) =>
        (originX + (int)Math.Round(ClampUnit(point.X) * width), originY + (int)Math.Round(ClampUnit(point.Y) * height));

    /// <summary>Turns a <see cref="SwipeDirection"/> + distance into a relative from/to pair,
    /// centered on the target region: the swipe moves <paramref name="distance"/> (a fraction of
    /// the region's own dimension) from the center, in the given direction. Clamped to [0, 1] so
    /// a distance &gt;= 1 still produces a valid pair instead of an out-of-bounds point. Pure --
    /// no driver needed, the same reasoning as <see cref="AppiumLocatorResolver.Build"/>.</summary>
    public static (RelativePoint From, RelativePoint To) DirectionToPoints(SwipeDirection direction, double distance)
    {
        const double center = 0.5;
        var half = ClampUnit(distance) / 2;

        return direction switch
        {
            SwipeDirection.Up => (new RelativePoint(center, ClampUnit(center + half)), new RelativePoint(center, ClampUnit(center - half))),
            SwipeDirection.Down => (new RelativePoint(center, ClampUnit(center - half)), new RelativePoint(center, ClampUnit(center + half))),
            SwipeDirection.Left => (new RelativePoint(ClampUnit(center + half), center), new RelativePoint(ClampUnit(center - half), center)),
            SwipeDirection.Right => (new RelativePoint(ClampUnit(center - half), center), new RelativePoint(ClampUnit(center + half), center)),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unrecognized swipe direction."),
        };
    }

    private static double ClampUnit(double v) => Math.Clamp(v, 0, 1);

    /// <summary>Mobile Testing Module Spec, MOB-T5: a <see cref="Pinch"/>'s scale "must be
    /// strictly positive" -- a non-positive scale has no physical gesture to synthesize (zero
    /// fingers moving nowhere, or a negative spread). Pure -- no driver needed.</summary>
    public static void ValidateScale(double scale)
    {
        if (scale <= 0)
            throw new ArgumentException($"mobile.pinch requires a strictly positive 'scale' (got {scale}).");
    }

    /// <summary>Mobile Testing Module Spec, MOB-T5: "`ScrollTo` scrolls until the target is
    /// visible, with a bounded maximum number of attempts so an element that will never appear
    /// fails in seconds rather than scrolling forever." The bounded-attempts loop itself, decoupled
    /// from Appium entirely -- <paramref name="isSatisfied"/> and <paramref name="step"/> are plain
    /// async delegates, since resolving a locator is itself async; this stays properly async all
    /// the way through rather than forcing it onto a synchronous signature, so this is directly
    /// testable with fakes, no driver needed. Checks <paramref name="isSatisfied"/> BEFORE the
    /// first <paramref name="step"/> (an already-visible target needs zero scrolls) and after
    /// every subsequent one, up to <paramref name="maxAttempts"/> scrolls.</summary>
    public static async Task<bool> TryUntilAsync(Func<Task<bool>> isSatisfied, Func<Task> step, int maxAttempts, CancellationToken ct)
    {
        if (await isSatisfied())
            return true;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await step();
            if (await isSatisfied())
                return true;
        }

        return false;
    }

    /// <summary>MOB-T5's own "every Gesture type... implemented via the W3C Actions API" --
    /// the original "tap" action (MOB-T1) called <c>IWebElement.Click()</c> directly; this moves
    /// it onto the same pointer-sequence mechanism every other gesture here uses, with no change
    /// to the "tap" action's own name, params, or observable behavior.</summary>
    public static void Tap(AppiumDriver driver, IWebElement element)
    {
        var (x, y) = ElementCenter(element);
        PerformTap(driver, x, y);
    }

    public static void DoubleTap(AppiumDriver driver, IWebElement element)
    {
        var (x, y) = ElementCenter(element);
        PerformTap(driver, x, y);
        PerformTap(driver, x, y);
    }

    public static void LongPress(AppiumDriver driver, IWebElement element, TimeSpan duration)
    {
        var (x, y) = ElementCenter(element);
        var finger = new PointerInputDevice(PointerKind.Touch);
        var sequence = new ActionSequence(finger, 0);
        sequence.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, x, y, TimeSpan.Zero));
        sequence.AddAction(finger.CreatePointerDown(MouseButton.Left));
        sequence.AddAction(finger.CreatePause(duration));
        sequence.AddAction(finger.CreatePointerUp(MouseButton.Left));
        driver.PerformActions(new List<ActionSequence> { sequence });
    }

    /// <summary>The single-finger swipe/drag primitive every directional swipe, explicit
    /// coordinate-pair swipe, and drag-and-drop ultimately reduces to -- generalized from MOB-T1's
    /// original "swipe" action body rather than duplicated.</summary>
    public static void SwipeBetween(AppiumDriver driver, int fromX, int fromY, int toX, int toY, TimeSpan? duration = null)
    {
        var finger = new PointerInputDevice(PointerKind.Touch);
        var sequence = new ActionSequence(finger, 0);
        sequence.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, fromX, fromY, TimeSpan.Zero));
        sequence.AddAction(finger.CreatePointerDown(MouseButton.Left));
        sequence.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, toX, toY, duration ?? DefaultSwipeDuration));
        sequence.AddAction(finger.CreatePointerUp(MouseButton.Left));
        driver.PerformActions(new List<ActionSequence> { sequence });
    }

    /// <summary>A two-finger pinch: both fingers start <paramref name="startOffset"/> pixels from
    /// (<paramref name="centerX"/>, <paramref name="centerY"/>) on opposite sides and move to
    /// <paramref name="endOffset"/> -- spreading apart (zoom in) when end &gt; start, coming
    /// together (zoom out) when end &lt; start. Two concurrent <see cref="PointerInputDevice"/>
    /// sequences in one <see cref="IActionExecutor.PerformActions"/> call, the real multi-touch
    /// mechanism the W3C Actions spec defines -- not two sequential single-finger gestures.</summary>
    public static void Pinch(AppiumDriver driver, int centerX, int centerY, int startOffset, int endOffset)
    {
        var finger1 = new PointerInputDevice(PointerKind.Touch);
        var seq1 = new ActionSequence(finger1, 0);
        seq1.AddAction(finger1.CreatePointerMove(CoordinateOrigin.Viewport, centerX - startOffset, centerY, TimeSpan.Zero));
        seq1.AddAction(finger1.CreatePointerDown(MouseButton.Left));
        seq1.AddAction(finger1.CreatePointerMove(CoordinateOrigin.Viewport, centerX - endOffset, centerY, DefaultSwipeDuration));
        seq1.AddAction(finger1.CreatePointerUp(MouseButton.Left));

        var finger2 = new PointerInputDevice(PointerKind.Touch);
        var seq2 = new ActionSequence(finger2, 0);
        seq2.AddAction(finger2.CreatePointerMove(CoordinateOrigin.Viewport, centerX + startOffset, centerY, TimeSpan.Zero));
        seq2.AddAction(finger2.CreatePointerDown(MouseButton.Left));
        seq2.AddAction(finger2.CreatePointerMove(CoordinateOrigin.Viewport, centerX + endOffset, centerY, DefaultSwipeDuration));
        seq2.AddAction(finger2.CreatePointerUp(MouseButton.Left));

        driver.PerformActions(new List<ActionSequence> { seq1, seq2 });
    }

    /// <summary>A drag: press at the source, a short pause (lets the app's own long-press/drag
    /// recognizer engage, the real-world reason a plain swipe doesn't work as a drag), move to
    /// the destination, release.</summary>
    public static void DragAndDrop(AppiumDriver driver, int fromX, int fromY, int toX, int toY)
    {
        var finger = new PointerInputDevice(PointerKind.Touch);
        var sequence = new ActionSequence(finger, 0);
        sequence.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, fromX, fromY, TimeSpan.Zero));
        sequence.AddAction(finger.CreatePointerDown(MouseButton.Left));
        sequence.AddAction(finger.CreatePause(TimeSpan.FromMilliseconds(500)));
        sequence.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, toX, toY, TimeSpan.FromMilliseconds(500)));
        sequence.AddAction(finger.CreatePointerUp(MouseButton.Left));
        driver.PerformActions(new List<ActionSequence> { sequence });
    }

    private static void PerformTap(AppiumDriver driver, int x, int y)
    {
        var finger = new PointerInputDevice(PointerKind.Touch);
        var sequence = new ActionSequence(finger, 0);
        sequence.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, x, y, TimeSpan.Zero));
        sequence.AddAction(finger.CreatePointerDown(MouseButton.Left));
        sequence.AddAction(finger.CreatePointerUp(MouseButton.Left));
        driver.PerformActions(new List<ActionSequence> { sequence });
    }

    /// <summary>Public: dragAndDrop and pinch (MobileModule) need an element's center too, for
    /// their own resolved source/destination/target elements, not just this file's own gestures.</summary>
    public static (int X, int Y) ElementCenter(IWebElement element) =>
        (element.Location.X + element.Size.Width / 2, element.Location.Y + element.Size.Height / 2);
}

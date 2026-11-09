using Yukti.Domain.UiVocabulary;

namespace Yukti.Domain.MobileTesting;

/// <summary>
/// Mobile Testing Module Spec, MOB-T5: every gesture a mobile flow can author, implemented in
/// <c>Yukti.Infrastructure.InMemory.Modules.AppiumGestureExecutor</c> via the W3C Actions API.
/// Reuses <see cref="LocatorRef"/> from the shared vocabulary (WEB-T1/WEB-T2) for every element a
/// gesture targets -- no new locator concept, same rule MOB-T4 followed.
///
/// Every coordinate here is a <see cref="RelativePoint"/> (a 0..1 fraction of a screen or
/// element's bounds), never an absolute pixel -- the spec's own "coordinate-based gestures are
/// relative... so a flow isn't tied to one screen size."
/// </summary>
public abstract record Gesture;

public sealed record Tap(LocatorRef Target) : Gesture;

public sealed record DoubleTap(LocatorRef Target) : Gesture;

public sealed record LongPress(LocatorRef Target, TimeSpan Duration) : Gesture;

/// <summary>A direction-and-distance swipe, scoped to an element's bounds (<see cref="Within"/>)
/// or the whole screen when null.</summary>
public sealed record Swipe(SwipeDirection Direction, LocatorRef? Within, double Distance) : Gesture;

/// <summary>The explicit-coordinate-pair escape hatch for a swipe a direction can't express.</summary>
public sealed record SwipeBetween(RelativePoint From, RelativePoint To, TimeSpan Duration) : Gesture;

/// <summary>Scrolls <see cref="Container"/> (or the whole screen when null) until <see cref="Target"/>
/// becomes visible, bounded by the caller's own maximum attempt count -- never scrolls forever.</summary>
public sealed record ScrollTo(LocatorRef Target, LocatorRef? Container) : Gesture;

/// <summary>A two-finger pinch on <see cref="Target"/>. Scale &gt; 1 spreads fingers apart (zoom
/// in); scale &lt; 1 brings them together (zoom out). Scale must be strictly positive.</summary>
public sealed record Pinch(LocatorRef Target, double Scale) : Gesture;

public sealed record DragAndDropMobile(LocatorRef From, LocatorRef To) : Gesture;

public enum SwipeDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>A point relative to a screen or element's bounds: <see cref="X"/>/<see cref="Y"/> are
/// fractions in [0, 1], never absolute pixels. (0,0) is the top-left corner, (1,1) the
/// bottom-right.</summary>
public readonly record struct RelativePoint(double X, double Y);

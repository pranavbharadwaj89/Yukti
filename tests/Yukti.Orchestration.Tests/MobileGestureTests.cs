using Xunit;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.UiVocabulary;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Mobile Testing Module Spec, MOB-T5: every gesture type, implemented via the W3C Actions API.
/// No live Appium server is available in this environment (the same constraint MOB-T1/T3/T4's
/// own tests document), so the driver-bound half of <c>AppiumGestureExecutor</c> (everything
/// that actually moves a pointer) isn't exercisable end-to-end here. What IS provable without
/// one: every <see cref="Gesture"/> type constructs (the domain vocabulary itself), the pure
/// coordinate math (<see cref="AppiumGestureExecutor.ToAbsolute"/>,
/// <see cref="AppiumGestureExecutor.DirectionToPoints"/>), the bounded-attempts control flow
/// (<see cref="AppiumGestureExecutor.TryUntilAsync"/>) with fakes instead of a real driver, scale
/// validation, and the action schema shape every gesture is exposed through.
/// </summary>
public sealed class MobileGestureTests
{
    private static readonly LocatorRef SomeTarget = new InlineLocator(
        new Locator(new LocatorSpec(LocatorStrategy.AccessibilityId, "el", null), Array.Empty<LocatorSpec>(), null, null, null));

    [Fact]
    public void Every_Gesture_type_from_the_domain_model_constructs()
    {
        Gesture[] gestures =
        {
            new Tap(SomeTarget),
            new DoubleTap(SomeTarget),
            new LongPress(SomeTarget, TimeSpan.FromSeconds(1)),
            new Swipe(SwipeDirection.Up, SomeTarget, 0.5),
            new Swipe(SwipeDirection.Down, null, 0.5),
            new SwipeBetween(new RelativePoint(0.2, 0.8), new RelativePoint(0.2, 0.2), TimeSpan.FromMilliseconds(300)),
            new ScrollTo(SomeTarget, null),
            new ScrollTo(SomeTarget, SomeTarget),
            new Pinch(SomeTarget, 2.0),
            new DragAndDropMobile(SomeTarget, SomeTarget),
        };

        Assert.Equal(10, gestures.Length);
        Assert.All(gestures, g => Assert.IsAssignableFrom<Gesture>(g));
    }

    [Theory]
    [InlineData(SwipeDirection.Up)]
    [InlineData(SwipeDirection.Down)]
    [InlineData(SwipeDirection.Left)]
    [InlineData(SwipeDirection.Right)]
    public void DirectionToPoints_moves_away_from_center_in_the_given_direction(SwipeDirection direction)
    {
        var (from, to) = AppiumGestureExecutor.DirectionToPoints(direction, 0.5);

        switch (direction)
        {
            case SwipeDirection.Up:
                Assert.True(from.Y > to.Y);
                Assert.Equal(from.X, to.X, precision: 6);
                break;
            case SwipeDirection.Down:
                Assert.True(from.Y < to.Y);
                Assert.Equal(from.X, to.X, precision: 6);
                break;
            case SwipeDirection.Left:
                Assert.True(from.X > to.X);
                Assert.Equal(from.Y, to.Y, precision: 6);
                break;
            case SwipeDirection.Right:
                Assert.True(from.X < to.X);
                Assert.Equal(from.Y, to.Y, precision: 6);
                break;
        }
    }

    [Fact]
    public void DirectionToPoints_clamps_a_distance_of_1_or_more_to_the_region_bounds_instead_of_going_out_of_range()
    {
        var (from, to) = AppiumGestureExecutor.DirectionToPoints(SwipeDirection.Up, 2.0);

        Assert.InRange(from.X, 0, 1);
        Assert.InRange(from.Y, 0, 1);
        Assert.InRange(to.X, 0, 1);
        Assert.InRange(to.Y, 0, 1);
        Assert.Equal(1, from.Y, precision: 6);
        Assert.Equal(0, to.Y, precision: 6);
    }

    [Fact]
    public void ToAbsolute_converts_a_relative_center_point_into_the_middle_of_the_region()
    {
        var (x, y) = AppiumGestureExecutor.ToAbsolute(new RelativePoint(0.5, 0.5), originX: 0, originY: 0, width: 200, height: 100);
        Assert.Equal(100, x);
        Assert.Equal(50, y);
    }

    [Fact]
    public void ToAbsolute_offsets_by_the_regions_own_origin_for_a_scoped_element()
    {
        var (x, y) = AppiumGestureExecutor.ToAbsolute(new RelativePoint(0, 0), originX: 50, originY: 80, width: 200, height: 100);
        Assert.Equal(50, x);
        Assert.Equal(80, y);
    }

    [Fact]
    public void ToAbsolute_clamps_an_out_of_range_relative_point_instead_of_producing_a_point_outside_the_region()
    {
        var (x, y) = AppiumGestureExecutor.ToAbsolute(new RelativePoint(-0.5, 1.5), originX: 0, originY: 0, width: 200, height: 100);
        Assert.Equal(0, x);
        Assert.Equal(100, y);
    }

    [Fact]
    public async Task TryUntilAsync_never_scrolls_when_already_satisfied()
    {
        var stepCalls = 0;
        var result = await AppiumGestureExecutor.TryUntilAsync(
            () => Task.FromResult(true),
            () => { stepCalls++; return Task.CompletedTask; },
            maxAttempts: 5, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(0, stepCalls);
    }

    [Fact]
    public async Task TryUntilAsync_stops_as_soon_as_satisfied_after_scrolling()
    {
        var attempts = 0;
        var result = await AppiumGestureExecutor.TryUntilAsync(
            () => Task.FromResult(attempts >= 3),
            () => { attempts++; return Task.CompletedTask; },
            maxAttempts: 10, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task TryUntilAsync_gives_up_after_maxAttempts_scrolls_never_scrolling_forever()
    {
        var stepCalls = 0;
        var result = await AppiumGestureExecutor.TryUntilAsync(
            () => Task.FromResult(false),
            () => { stepCalls++; return Task.CompletedTask; },
            maxAttempts: 4, CancellationToken.None);

        Assert.False(result);
        Assert.Equal(4, stepCalls);
    }

    [Fact]
    public async Task TryUntilAsync_respects_cancellation_instead_of_scrolling_forever()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => AppiumGestureExecutor.TryUntilAsync(
            () => Task.FromResult(false),
            () =>
            {
                attempts++;
                if (attempts == 2) cts.Cancel();
                return Task.CompletedTask;
            },
            maxAttempts: 1000, cts.Token));

        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    public void ValidateScale_rejects_a_non_positive_scale(double scale)
    {
        var ex = Assert.Throws<ArgumentException>(() => AppiumGestureExecutor.ValidateScale(scale));
        Assert.Contains("scale", ex.Message);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(2.0)]
    public void ValidateScale_accepts_any_positive_scale(double scale) =>
        AppiumGestureExecutor.ValidateScale(scale); // does not throw

    [Fact]
    public void GetSupportedActions_exposes_every_gesture_action_schema_shaped_as_the_spec_describes()
    {
        var actions = new MobileModule().GetSupportedActions().ToDictionary(a => a.ActionName);

        Assert.True(actions.ContainsKey("doubleTap"));
        Assert.True(actions.ContainsKey("longPress"));
        Assert.True(actions.ContainsKey("swipeBetween"));
        Assert.True(actions.ContainsKey("scrollTo"));
        Assert.True(actions.ContainsKey("pinch"));
        Assert.True(actions.ContainsKey("dragAndDrop"));

        var swipe = actions["swipe"];
        AssertParam(swipe, "direction", required: false);
        AssertParam(swipe, "distance", required: false);
        AssertParam(swipe, "within", required: false);
        AssertParam(swipe, "fromX", required: false); // legacy, now optional alongside direction

        var swipeBetween = actions["swipeBetween"];
        AssertParam(swipeBetween, "fromX", required: true);
        AssertParam(swipeBetween, "toY", required: true);
        AssertParam(swipeBetween, "within", required: false);

        var scrollTo = actions["scrollTo"];
        AssertParam(scrollTo, "selector", required: true);
        AssertParam(scrollTo, "container", required: false);
        AssertParam(scrollTo, "maxAttempts", required: false);

        var pinch = actions["pinch"];
        AssertParam(pinch, "selector", required: true);
        AssertParam(pinch, "scale", required: true);

        var dragAndDrop = actions["dragAndDrop"];
        AssertParam(dragAndDrop, "from", required: true);
        AssertParam(dragAndDrop, "to", required: true);
    }

    [Fact]
    public void Every_gesture_param_has_a_non_empty_description_matching_the_rest_of_this_codebase()
    {
        foreach (var action in new MobileModule().GetSupportedActions())
        {
            foreach (var param in action.Parameters)
            {
                Assert.False(string.IsNullOrWhiteSpace(param.Description),
                    $"{action.ActionName}.{param.Name} is missing a Description.");
            }
        }
    }

    private static void AssertParam(ActionSchema action, string paramName, bool required)
    {
        var param = Assert.Single(action.Parameters, p => p.Name == paramName);
        Assert.Equal(required, param.Required);
    }
}

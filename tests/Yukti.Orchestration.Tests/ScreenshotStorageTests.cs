using Xunit;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// D6-T5: the path-safety half of screenshot serving, proven in isolation from the heavier
/// Playwright/FixtureApp engine test in WebModuleTests — a flow author controls the raw "path"
/// param, so this must never let them write (or later, via the serving endpoint, read) outside
/// ScreenshotStorage's own per-run directory.
/// </summary>
public sealed class ScreenshotStorageTests
{
    [Theory]
    [InlineData("shot.png", "shot.png")]
    [InlineData("../../escape-attempt.png", "escape-attempt.png")]
    [InlineData("a/b/c/nested.png", "nested.png")]
    public void A_requested_name_is_reduced_to_its_bare_file_name(string requested, string expected) =>
        Assert.Equal(expected, ScreenshotStorage.SafeFileName(requested, "prefix"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("shots/")] // Sarvaghna review finding: a trailing separator makes Path.GetFileName return "".
    public void A_name_that_reduces_to_empty_falls_back_to_a_generated_default(string? requested)
    {
        var fileName = ScreenshotStorage.SafeFileName(requested, "prefix");
        Assert.StartsWith("prefix-", fileName);
        Assert.EndsWith(".png", fileName);
    }
}

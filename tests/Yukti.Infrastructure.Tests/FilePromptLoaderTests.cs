using Microsoft.Extensions.Configuration;
using Xunit;
using Yukti.Application.Ai;
using Yukti.Infrastructure.Ai;

namespace Yukti.Infrastructure.Tests;

/// <summary>D5-T3: "Prompts live in Sarvagna/Prompts/&lt;capability&gt;.v1.md, loaded by name and version."</summary>
public sealed class FilePromptLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "yukti-prompt-test-" + Guid.NewGuid());

    public FilePromptLoaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FilePromptLoader BuildLoader()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:PromptsBasePath"] = _tempDir })
            .Build();
        return new FilePromptLoader(config);
    }

    [Fact]
    public async Task Loads_a_prompt_file_by_capability_and_version()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "coder.v1.md"), "You are the Coder.\n{{CONTEXT}}");

        var content = await BuildLoader().LoadAsync("coder", "v1", CancellationToken.None);

        Assert.Equal("You are the Coder.\n{{CONTEXT}}", content);
    }

    [Fact]
    public async Task Missing_prompt_file_raises_PromptNotFoundException_naming_capability_and_version()
    {
        var ex = await Assert.ThrowsAsync<PromptNotFoundException>(
            () => BuildLoader().LoadAsync("ghost", "v3", CancellationToken.None));

        Assert.Contains("ghost", ex.Message);
        Assert.Contains("v3", ex.Message);
    }

    [Fact]
    public async Task A_different_version_of_the_same_capability_is_a_different_file_never_the_same_one_edited()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "analyst.v1.md"), "v1 text");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "analyst.v2.md"), "v2 text");

        var loader = BuildLoader();
        Assert.Equal("v1 text", await loader.LoadAsync("analyst", "v1", CancellationToken.None));
        Assert.Equal("v2 text", await loader.LoadAsync("analyst", "v2", CancellationToken.None));
    }
}

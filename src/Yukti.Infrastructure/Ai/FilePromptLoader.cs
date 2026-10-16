using Microsoft.Extensions.Configuration;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// D5-T3: reads <c>{BasePath}/{capability}.{version}.md</c> from disk.
/// Default base path is <c>Sarvagna/Prompts</c> (repo root), matching the
/// spec's own path literally — configurable so tests can point it at a
/// temp fixture directory without needing a real capability prompt to
/// exist yet (Analyst/Coder are D5-T4/T5, not this task).
/// </summary>
public sealed class FilePromptLoader : IPromptLoader
{
    private readonly string _basePath;

    public FilePromptLoader(IConfiguration config)
    {
        _basePath = config["Ai:PromptsBasePath"] ?? Path.Combine(AppContext.BaseDirectory, "Sarvagna", "Prompts");
    }

    public async Task<string> LoadAsync(string capability, string version, CancellationToken ct)
    {
        var path = Path.Combine(_basePath, $"{capability}.{version}.md");
        if (!File.Exists(path))
        {
            throw new PromptNotFoundException(capability, version);
        }

        return await File.ReadAllTextAsync(path, ct);
    }
}

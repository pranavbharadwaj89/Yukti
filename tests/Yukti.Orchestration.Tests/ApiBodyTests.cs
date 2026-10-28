using System.Net;
using Xunit;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// API Testing Module Spec, API-T3: JSON/form/multipart body encoding. Cookie jar and redirect
/// policy coverage lives in ApiCookieTests — this file is body shapes only.
/// </summary>
public sealed class ApiBodyTests
{
    private static ExecutionContext NewContext() => new()
    {
        RunId = FlowRunId.New(),
        Variables = new Dictionary<string, object?>(),
        Credentials = new InMemoryCredentialResolver(),
        RunCancellation = CancellationToken.None,
    };

    [Fact]
    public async Task Json_object_body_sets_content_type_automatically()
    {
        string? receivedContentType = null;
        string? receivedBody = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedContentType = ctx.Request.ContentType;
            using var reader = new StreamReader(ctx.Request.InputStream);
            receivedBody = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["method"] = "POST",
            ["body"] = new Dictionary<string, object?> { ["name"] = "widget" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.StartsWith("application/json", receivedContentType);
        Assert.Contains("\"name\"", receivedBody);
    }

    [Fact]
    public async Task An_explicit_Content_Type_header_overrides_the_automatic_one_for_a_json_body()
    {
        string? receivedContentType = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedContentType = ctx.Request.ContentType;
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["method"] = "POST",
            ["headers"] = new Dictionary<string, object?> { ["Content-Type"] = "application/vnd.custom+json" },
            ["body"] = new Dictionary<string, object?> { ["name"] = "widget" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.StartsWith("application/vnd.custom+json", receivedContentType);
    }

    [Fact]
    public async Task Form_body_encodes_as_application_x_www_form_urlencoded()
    {
        string? receivedContentType = null;
        string? receivedBody = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedContentType = ctx.Request.ContentType;
            using var reader = new StreamReader(ctx.Request.InputStream);
            receivedBody = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["method"] = "POST",
            ["headers"] = new Dictionary<string, object?> { ["Content-Type"] = "application/x-www-form-urlencoded" },
            ["body"] = new Dictionary<string, object?> { ["username"] = "alice", ["password"] = "s3cret" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.StartsWith("application/x-www-form-urlencoded", receivedContentType);
        Assert.Equal("username=alice&password=s3cret", receivedBody);
    }

    [Fact]
    public async Task Multipart_body_sends_a_text_part_and_a_file_part()
    {
        var filesDir = Path.Combine(Path.GetTempPath(), $"yukti-api-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(filesDir);
        var fileBytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(Path.Combine(filesDir, "photo.png"), fileBytes);

        var previousEnv = Environment.GetEnvironmentVariable("YUKTI_API_FILES_DIR");
        Environment.SetEnvironmentVariable("YUKTI_API_FILES_DIR", filesDir);
        try
        {
            string? receivedContentType = null;
            byte[]? receivedBody = null;
            using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
            {
                receivedContentType = ctx.Request.ContentType;
                using var ms = new MemoryStream();
                await ctx.Request.InputStream.CopyToAsync(ms);
                receivedBody = ms.ToArray();
                ctx.Response.StatusCode = 200;
                await ApiModuleAssertionTests.WriteBody(ctx, "{}");
            });

            var module = new ApiModule();
            var outcome = await module.Run("request", new Dictionary<string, object?>
            {
                ["url"] = server.BaseUrl,
                ["method"] = "POST",
                ["body"] = new Dictionary<string, object?>
                {
                    ["parts"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["name"] = "caption", ["value"] = "hello" },
                        new Dictionary<string, object?> { ["name"] = "avatar", ["fileName"] = "photo.png", ["contentType"] = "image/png" },
                    },
                },
            }, NewContext(), CancellationToken.None);

            Assert.Equal(StepStatus.Passed, outcome.Status);
            Assert.StartsWith("multipart/form-data; boundary=", receivedContentType);
            var bodyText = System.Text.Encoding.Latin1.GetString(receivedBody!);
            Assert.Contains("name=caption", bodyText);
            Assert.Contains("name=avatar; filename=photo.png", bodyText);
            Assert.Contains("Content-Type: image/png", bodyText);
        }
        finally
        {
            Environment.SetEnvironmentVariable("YUKTI_API_FILES_DIR", previousEnv);
            Directory.Delete(filesDir, recursive: true);
        }
    }

    [Fact]
    public async Task Multipart_missing_file_fails_the_step_naming_the_file()
    {
        var filesDir = Path.Combine(Path.GetTempPath(), $"yukti-api-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(filesDir);

        var previousEnv = Environment.GetEnvironmentVariable("YUKTI_API_FILES_DIR");
        Environment.SetEnvironmentVariable("YUKTI_API_FILES_DIR", filesDir);
        try
        {
            var module = new ApiModule();
            var outcome = await module.Run("request", new Dictionary<string, object?>
            {
                ["url"] = "http://127.0.0.1:1/",
                ["method"] = "POST",
                ["body"] = new Dictionary<string, object?>
                {
                    ["parts"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["name"] = "avatar", ["fileName"] = "missing.png" },
                    },
                },
            }, NewContext(), CancellationToken.None);

            Assert.Equal(StepStatus.Failed, outcome.Status);
            Assert.Contains("missing.png", outcome.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("YUKTI_API_FILES_DIR", previousEnv);
            Directory.Delete(filesDir, recursive: true);
        }
    }

    [Fact]
    public async Task Multipart_file_part_cannot_escape_the_configured_directory_via_traversal()
    {
        var filesDir = Path.Combine(Path.GetTempPath(), $"yukti-api-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(filesDir);
        var secretDir = Path.Combine(Path.GetTempPath(), $"yukti-secret-{Guid.NewGuid():N}");
        Directory.CreateDirectory(secretDir);
        await File.WriteAllTextAsync(Path.Combine(secretDir, "secret.txt"), "do-not-leak");

        var previousEnv = Environment.GetEnvironmentVariable("YUKTI_API_FILES_DIR");
        Environment.SetEnvironmentVariable("YUKTI_API_FILES_DIR", filesDir);
        try
        {
            var module = new ApiModule();
            var outcome = await module.Run("request", new Dictionary<string, object?>
            {
                ["url"] = "http://127.0.0.1:1/",
                ["method"] = "POST",
                ["body"] = new Dictionary<string, object?>
                {
                    ["parts"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["name"] = "avatar", ["fileName"] = "../" + Path.GetFileName(secretDir) + "/secret.txt" },
                    },
                },
            }, NewContext(), CancellationToken.None);

            // Path.GetFileName strips any directory component, so the traversal segment is
            // gone entirely -- the step fails as "file not found" (there's no such bare-named
            // file in filesDir), never by reading the file outside it.
            Assert.Equal(StepStatus.Failed, outcome.Status);
            Assert.DoesNotContain("do-not-leak", outcome.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("YUKTI_API_FILES_DIR", previousEnv);
            Directory.Delete(filesDir, recursive: true);
            Directory.Delete(secretDir, recursive: true);
        }
    }
}

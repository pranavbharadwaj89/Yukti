using Xunit;

namespace Yukti.Analyzers.Tests;

public sealed class NoUnauditedCommandHandlerAnalyzerTests
{
    private const string Scaffold = """
        interface ICommand<TResult> { }
        interface ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
        {
            System.Threading.Tasks.Task<TResult> Handle(TCommand command, System.Threading.CancellationToken ct);
        }
        abstract class AuditableCommandHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
            where TCommand : ICommand<TResult>
        {
            public System.Threading.Tasks.Task<TResult> Handle(TCommand command, System.Threading.CancellationToken ct) => HandleCore(command, ct);
            protected abstract System.Threading.Tasks.Task<TResult> HandleCore(TCommand command, System.Threading.CancellationToken ct);
        }
        class MyCommand : ICommand<bool> { }

        """;

    [Fact]
    public async Task Flags_a_command_handler_implementing_ICommandHandler_directly()
    {
        var source = Scaffold + """
            class MyCommandHandler : ICommandHandler<MyCommand, bool>
            {
                public System.Threading.Tasks.Task<bool> Handle(MyCommand command, System.Threading.CancellationToken ct) => null;
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoUnauditedCommandHandlerAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == NoUnauditedCommandHandlerAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Allows_a_handler_inheriting_AuditableCommandHandler()
    {
        var source = Scaffold + """
            class MyCommandHandler : AuditableCommandHandler<MyCommand, bool>
            {
                protected override System.Threading.Tasks.Task<bool> HandleCore(MyCommand command, System.Threading.CancellationToken ct) => null;
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoUnauditedCommandHandlerAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == NoUnauditedCommandHandlerAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Allows_a_handler_with_a_leading_audit_exempt_comment()
    {
        var source = Scaffold + """
            // audit-exempt: read-only diagnostic command, never mutates state
            class MyCommandHandler : ICommandHandler<MyCommand, bool>
            {
                public System.Threading.Tasks.Task<bool> Handle(MyCommand command, System.Threading.CancellationToken ct) => null;
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoUnauditedCommandHandlerAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == NoUnauditedCommandHandlerAnalyzer.DiagnosticId);
    }
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Yukti.Orchestration.Tests;

/// <summary>Everything a captured log call carried: the rendered message, every
/// structured state key/value pair, and every ambient scope's key/value pairs
/// (as BeginScope attaches them) — flattened so a test can assert over the
/// whole surface a real structured sink would receive, not just the message text.</summary>
internal sealed record CapturedLogRecord(LogLevel Level, string Category, string Message, IReadOnlyList<string> AllValues);

/// <summary>
/// An in-memory ILoggerProvider that records every log call's full
/// structured surface (message, state key/values, active scope key/values)
/// so FR-LOG-04's "adversarial log-scraping test" has something concrete to
/// scrape: a real captured stream of what a production sink would have
/// received, rather than re-implementing a sink.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentBag<CapturedLogRecord> Records { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void Dispose() { }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _category;
        private readonly CapturingLoggerProvider _owner;

        public CapturingLogger(string category, CapturingLoggerProvider owner)
        {
            _category = category;
            _owner = owner;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            ScopeStack.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = new List<string>();

            if (state is IEnumerable<KeyValuePair<string, object?>> stateValues)
                foreach (var kv in stateValues)
                    values.Add($"{kv.Key}={kv.Value}");

            foreach (var scope in ScopeStack.Current)
                if (scope is IEnumerable<KeyValuePair<string, object>> scopeValues)
                    foreach (var kv in scopeValues)
                        values.Add($"{kv.Key}={kv.Value}");

            var message = formatter(state, exception);
            values.Add(message);
            if (exception is not null)
                values.Add(exception.ToString());

            _owner.Records.Add(new CapturedLogRecord(logLevel, _category, message, values));
        }
    }

    /// <summary>Thread-local ambient scope stack — BeginScope/Dispose push/pop, Log reads it.</summary>
    private static class ScopeStack
    {
        [ThreadStatic] private static List<object>? _stack;

        public static IReadOnlyList<object> Current => _stack ?? (IReadOnlyList<object>)Array.Empty<object>();

        public static IDisposable Push(object state)
        {
            _stack ??= new List<object>();
            _stack.Add(state);
            return new Popper(_stack);
        }

        private sealed class Popper : IDisposable
        {
            private readonly List<object> _stack;
            public Popper(List<object> stack) => _stack = stack;
            public void Dispose() => _stack.RemoveAt(_stack.Count - 1);
        }
    }
}

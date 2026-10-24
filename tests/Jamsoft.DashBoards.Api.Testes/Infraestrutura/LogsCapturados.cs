using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>Guarda tudo o que a API registra em log (mensagem, valores estruturados e exceção), em qualquer nível.</summary>
public sealed class LogsCapturados : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _linhas = new();

    public IReadOnlyCollection<string> Linhas => _linhas;

    public ILogger CreateLogger(string categoria) => new Registrador(categoria, _linhas);

    public void Dispose() { }

    private sealed class Registrador(string categoria, ConcurrentQueue<string> linhas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var valores = state is IEnumerable<KeyValuePair<string, object?>> pares
                ? string.Join(" ", pares.Select(p => $"{p.Key}={p.Value}"))
                : state?.ToString();
            linhas.Enqueue($"{logLevel} {categoria}: {formatter(state, exception)} | {valores} | {exception}");
        }
    }
}

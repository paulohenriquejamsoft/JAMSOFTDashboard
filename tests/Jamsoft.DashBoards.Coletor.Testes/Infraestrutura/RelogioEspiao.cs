using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;

/// <summary>
/// <see cref="FakeTimeProvider"/> que anota o prazo de cada timer criado: o teste avança o relógio só depois que a
/// espera foi armada, sem corrida entre o avanço e o código que vai esperar.
/// </summary>
public sealed class RelogioEspiao(FakeTimeProvider falso) : TimeProvider
{
    private readonly List<TimeSpan> _prazos = [];

    public FakeTimeProvider Falso { get; } = falso;

    public IReadOnlyList<TimeSpan> Prazos
    {
        get
        {
            lock (_prazos)
            {
                return [.. _prazos];
            }
        }
    }

    public override DateTimeOffset GetUtcNow() => Falso.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => Falso.LocalTimeZone;

    public override long TimestampFrequency => Falso.TimestampFrequency;

    public override long GetTimestamp() => Falso.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_prazos)
        {
            _prazos.Add(dueTime);
        }

        return Falso.CreateTimer(callback, state, dueTime, period);
    }
}

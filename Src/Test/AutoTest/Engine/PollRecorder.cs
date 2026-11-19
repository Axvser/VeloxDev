using System.Diagnostics;

namespace VeloxDev.AT.Engine;

/// <summary>
/// Reads a demo's observation payload in a loop and remembers every sample it took.
/// </summary>
/// <remarks>
/// Two things make this more than a <c>while</c> loop. A UI automation read has latency, so the interval is a floor
/// and never a promise — every wait is bounded by the wall clock rather than by a count of iterations. And a timeout
/// carries the last payload verbatim, which is usually enough to see which field stopped moving.
/// </remarks>
internal sealed class PollRecorder
{
    /// <summary>How often to read. Polling faster than the demos' own 40ms readout would not sharpen what they report.</summary>
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(10);

    private readonly Func<StatePayload> _read;
    private readonly List<StatePayload> _samples = [];

    /// <summary>Create a recorder over a payload reader.</summary>
    internal PollRecorder(Func<StatePayload> read) => _read = read;

    /// <summary>Every payload read since the last <see cref="Reset"/>, oldest first.</summary>
    internal IReadOnlyList<StatePayload> Samples => _samples;

    /// <summary>Forget the samples taken so far — used once a click is known to have landed.</summary>
    internal void Reset() => _samples.Clear();

    /// <summary>Read once and remember it.</summary>
    internal StatePayload Read()
    {
        var payload = _read();
        _samples.Add(payload);
        return payload;
    }

    /// <summary>Poll until the predicate holds, and return the payload that satisfied it.</summary>
    /// <exception cref="TimeoutException">The predicate never held, with the last payload in the message.</exception>
    internal StatePayload Until(Func<StatePayload, bool> satisfied, TimeSpan timeout, string expectation, TimeSpan? interval = null)
    {
        var poll = interval ?? DefaultInterval;
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

        while (true)
        {
            var payload = Read();
            if (satisfied(payload)) return payload;

            if (Stopwatch.GetTimestamp() >= deadline)
            {
                throw new TimeoutException(
                    $"Waited {timeout.TotalSeconds:F1}s for {expectation}, but the demo never reported it. Last payload: {payload.Raw}");
            }

            Thread.Sleep(poll);
        }
    }
}

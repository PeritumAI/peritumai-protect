using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Clipboard;

/// <summary>
/// Remembers quiet clipboard scans by sequence number and decides when a warning is due.
/// </summary>
public sealed class ClipboardAlertGate
{
    private readonly Dictionary<uint, ScanSession> _scans = new();
    private readonly HashSet<uint> _decided = new();
    private uint? _shown;

    public uint? LatestSequence { get; private set; }

    public void Store(uint sequence, ScanSession session)
    {
        _scans[sequence] = session;
        LatestSequence = sequence;
        if (_scans.Count > 8)
        {
            foreach (var old in _scans.Keys.Where(key => key != sequence && _decided.Contains(key)).Take(4).ToList())
                _scans.Remove(old);
        }
    }

    public ScanSession? Peek(uint sequence) =>
        _scans.TryGetValue(sequence, out var session) ? session : null;

    public bool IsDecided(uint sequence) => _decided.Contains(sequence);

    public bool WasShown(uint sequence) => _shown == sequence;

    public bool ShouldWarn(uint sequence, bool targetInFront, int minHits)
    {
        if (!targetInFront || _decided.Contains(sequence) || _shown == sequence)
            return false;
        if (!_scans.TryGetValue(sequence, out var session))
            return false;
        if (session.Result.Error is not null)
            return false;
        return session.Result.HitCount >= minHits;
    }

    public void MarkShown(uint sequence) => _shown = sequence;

    public void MarkDecided(uint sequence)
    {
        _decided.Add(sequence);
        if (_shown == sequence)
            _shown = null;
    }
}

using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Redaction;

public sealed class CopyRedaction
{
    public static CopyRedaction Permanent { get; } = new(null);

    public CopyRedaction(ReversibleSession? session) => Session = session;

    public ReversibleSession? Session { get; }

    public bool Reversible => Session is not null;

    public string Apply(string text, IReadOnlyList<Finding> findings) =>
        Session is null ? TextRedactor.Apply(text, findings) : Session.Apply(text, findings);

    public string WithMapLine(string text) =>
        Session is null ? text : ReversibleSession.AppendMapLine(text, Session.MapLine);
}

using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Redaction;

public interface IRedactor
{
    string Apply(string text, IReadOnlyList<Finding> findings);
}

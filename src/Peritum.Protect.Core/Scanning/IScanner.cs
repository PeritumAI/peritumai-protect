using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.Core.Scanning;

public interface IScanner
{
    ScanResult Scan(ScanRequest request, IReadOnlyList<Rule> rules);
}

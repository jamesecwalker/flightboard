using FlightBoard.Core.Interest.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlightBoard.Core.Interest;

public sealed record InterestResult(InterestTag? Best, IReadOnlyList<InterestTag> All)
{
    public static readonly InterestResult None = new(null, []);
    public string TagsCsv => string.Join(",", All.Select(t => $"{t.Category}:{t.Label}"));
}

/// <summary>Runs every rule, keeps every tag (for history), and picks the highest-scoring one for the board.</summary>
public sealed class InterestEvaluator
{
    private readonly IReadOnlyList<IInterestRule> _rules;
    private readonly ILogger _log;

    public InterestEvaluator(IEnumerable<IInterestRule> rules, ILogger<InterestEvaluator>? log = null)
    {
        _rules = rules.ToList();
        _log = log ?? NullLogger<InterestEvaluator>.Instance;
    }

    public static IReadOnlyList<IInterestRule> DefaultRules() =>
    [
        new EmergencyRule(),
        new WatchListRule(),
        new MilitaryGovRule(),
        new FirstSightingRule(),
        new UnusualTypeRule(),
        new OdditiesRule(),
        new PrivateJetRule(),
    ];

    public InterestResult Evaluate(InterestContext ctx)
    {
        var all = new List<InterestTag>();
        foreach (var rule in _rules)
        {
            try { all.AddRange(rule.Evaluate(ctx)); }
            catch (Exception ex) { _log.LogWarning(ex, "Interest rule {Rule} threw", rule.Name); }
        }
        if (all.Count == 0) return InterestResult.None;
        var best = all.OrderByDescending(t => t.Score).First();
        return new InterestResult(best, all);
    }
}

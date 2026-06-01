using System.Text.RegularExpressions;

namespace ZoneBalancer;

/// <summary>
/// Resolves a per-map weight (a relative player-load estimate). Most maps see
/// 1-2 players; towns/cities can see 100+, so the operator bumps those. The
/// weight is expressed in the same "load units" as a mob group, so a city with
/// no mobs but heavy population might be weighted ~200-400 to share a zone
/// fairly with grind maps.
///
/// Precedence (highest first): exact MapID override, then the first matching
/// regex pattern (in the order supplied), then the base weight.
/// </summary>
public sealed class WeightResolver
{
    private readonly double _base;
    private readonly Dictionary<string, double> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Regex Pattern, double Weight)> _patterns = new();

    public WeightResolver(double baseWeight) => _base = baseWeight;

    public void AddExact(string mapId, double weight) => _exact[mapId] = weight;

    public void AddPattern(string regex, double weight)
        => _patterns.Add((new Regex(regex, RegexOptions.IgnoreCase), weight));

    /// <summary>Parse a "key = value" weights file. A key prefixed with '~' is a
    /// regex; otherwise it's an exact MapID. '#' or ';' begin comments.</summary>
    public void LoadFile(string path)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (!double.TryParse(line[(eq + 1)..].Trim(),
                    System.Globalization.CultureInfo.InvariantCulture, out var w))
                continue;
            if (key.StartsWith('~')) AddPattern(key[1..].Trim(), w);
            else AddExact(key, w);
        }
    }

    public double Resolve(string mapId)
    {
        if (_exact.TryGetValue(mapId, out var w)) return w;
        foreach (var (pattern, weight) in _patterns)
            if (pattern.IsMatch(mapId)) return weight;
        return _base;
    }
}

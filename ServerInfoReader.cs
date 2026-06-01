namespace ZoneBalancer;

/// <summary>
/// Minimal ServerInfo.txt reader: returns the distinct Zone ids configured for
/// world 0 (ServerType 6). Used only to cross-check the target zone count
/// against what the stack is actually wired to run.
/// </summary>
public static class ServerInfoReader
{
    public static List<int> ZoneIds(string path)
    {
        var zones = new SortedSet<int>();
        bool inDefine = false;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("#DEFINE", StringComparison.OrdinalIgnoreCase)) { inDefine = true; continue; }
            if (line.StartsWith("#ENDDEFINE", StringComparison.OrdinalIgnoreCase)) { inDefine = false; continue; }
            if (inDefine) continue;
            if (!line.StartsWith("SERVER_INFO", StringComparison.OrdinalIgnoreCase)) continue;

            // SERVER_INFO "name", type, world, zone, fromType, "ip", port, ...
            var csv = line["SERVER_INFO".Length..];
            var semi = csv.IndexOf(';');
            if (semi >= 0) csv = csv[..semi];
            var parts = csv.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 4) continue;
            if (!int.TryParse(parts[1], out var type) || type != 6) continue;
            if (int.TryParse(parts[3], out var zone)) zones.Add(zone);
        }
        return zones.ToList();
    }
}

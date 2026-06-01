using System.Text;

namespace ZoneBalancer;

/// <summary>
/// Counts active mob regen groups per map. Each map's groups live in
/// MobRegen/&lt;MapID&gt;.txt (a #table MobRegenGroup file); an active group is a
/// line beginning with "#record". Commented templates (";#record", ";  ...")
/// don't count. Maps with no file (instanced / Lua-spawned dungeons that don't
/// pre-load into the engine's mh_Array) count as 0.
/// </summary>
public static class MobRegenCounter
{
    private static readonly Encoding Raw = Encoding.Latin1;

    /// <summary>Returns groups-per-MapID for the given MobRegen directory.</summary>
    public static Dictionary<string, int> CountAll(string mobRegenDir)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(mobRegenDir)) return result;

        foreach (var file in Directory.EnumerateFiles(mobRegenDir, "*.txt"))
        {
            var mapId = Path.GetFileNameWithoutExtension(file);
            result[mapId] = CountFile(file);
        }
        return result;
    }

    public static int CountFile(string path)
    {
        int count = 0;
        foreach (var line in File.ReadLines(path, Raw))
        {
            // A real record line starts with '#record'; a commented one starts
            // with ';' (which TrimStart does not remove), so it's excluded.
            if (line.TrimStart().StartsWith("#record", StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }
}

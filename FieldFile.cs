using System.Text;

namespace ZoneBalancer;

/// <summary>
/// One physical map (keyed by MapIDClient). A MapID may appear on several
/// Field.txt #Record rows (variants); they always share a zone and the mob
/// regen data is counted once.
/// </summary>
public sealed class MapEntry
{
    public required string MapId { get; init; }
    public int OriginalZone { get; set; }
    public int RowCount { get; set; }
    public int MobGroups { get; set; }
    public double Weight { get; set; } = 1.0;
    public double Load { get; set; }
    public int AssignedZone { get; set; } = -1;
}

/// <summary>
/// Parses Field.txt (tab-delimited, CRLF, EUC-KR comment lines) and rewrites
/// the trailing "Fiesta" zone-assignment column while preserving every other
/// byte. Round-trips through Latin-1 so non-ASCII comment bytes are untouched.
/// </summary>
public sealed class FieldFile
{
    // Latin-1 maps bytes 0x00..0xFF <-> chars 1:1, so read+write is byte-exact.
    private static readonly Encoding Raw = Encoding.Latin1;

    private readonly string[] _lines;      // split on '\n'; each may keep a trailing '\r'
    private readonly int _fiestaIdx;       // 0-based field index of the Fiesta column

    public IReadOnlyList<MapEntry> Maps { get; }

    private FieldFile(string[] lines, int fiestaIdx, IReadOnlyList<MapEntry> maps)
    {
        _lines = lines;
        _fiestaIdx = fiestaIdx;
        Maps = maps;
    }

    public static FieldFile Load(string path)
    {
        var raw = File.ReadAllText(path, Raw);
        var lines = raw.Split('\n');

        int fiestaIdx = -1;
        foreach (var line in lines)
        {
            var content = StripCr(line);
            if (!content.StartsWith("#ColumnName", StringComparison.OrdinalIgnoreCase)) continue;
            var fields = content.Split('\t');
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].Trim().Equals("Fiesta", StringComparison.OrdinalIgnoreCase))
                {
                    fiestaIdx = i;
                    break;
                }
            }
            break;
        }
        if (fiestaIdx < 0)
            throw new InvalidDataException("Field.txt: could not find the 'Fiesta' column in #ColumnName.");

        // Distinct maps, first-seen order.
        var byId = new Dictionary<string, MapEntry>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<MapEntry>();
        foreach (var line in lines)
        {
            var content = StripCr(line);
            if (!content.StartsWith("#Record", StringComparison.OrdinalIgnoreCase)) continue;
            var fields = content.Split('\t');
            if (fields.Length <= fiestaIdx || fields.Length < 2) continue;
            var mapId = fields[1].Trim();
            if (mapId.Length == 0) continue;

            if (!byId.TryGetValue(mapId, out var e))
            {
                e = new MapEntry
                {
                    MapId = mapId,
                    OriginalZone = ParseIntLoose(fields[fiestaIdx]),
                };
                byId[mapId] = e;
                ordered.Add(e);
            }
            e.RowCount++;
        }

        return new FieldFile(lines, fiestaIdx, ordered);
    }

    /// <summary>Write a copy with each #Record row's Fiesta column set to its
    /// map's assigned zone. Rows whose MapID has no assignment keep their
    /// original value. Output is byte-identical except the rewritten column.</summary>
    public void Write(string path, IReadOnlyDictionary<string, int> assignment)
    {
        var sb = new StringBuilder();
        for (int li = 0; li < _lines.Length; li++)
        {
            var line = _lines[li];
            bool cr = line.EndsWith('\r');
            var content = cr ? line[..^1] : line;

            if (content.StartsWith("#Record", StringComparison.OrdinalIgnoreCase))
            {
                var fields = content.Split('\t');
                if (fields.Length > _fiestaIdx && fields.Length >= 2)
                {
                    var mapId = fields[1].Trim();
                    if (assignment.TryGetValue(mapId, out var zone))
                    {
                        fields[_fiestaIdx] = zone.ToString();
                        content = string.Join('\t', fields);
                    }
                }
            }

            sb.Append(content);
            if (cr) sb.Append('\r');
            if (li < _lines.Length - 1) sb.Append('\n');
        }
        File.WriteAllText(path, sb.ToString(), Raw);
    }

    private static string StripCr(string s) => s.EndsWith('\r') ? s[..^1] : s;

    private static int ParseIntLoose(string s)
        => int.TryParse(s.Trim(), out var v) ? v : 0;
}

using System.Globalization;
using ZoneBalancer;

const int DefaultCap = 4096;   // engine ceiling: MobHatchery mh_Array[4096] per zone

var argList = args.ToList();
if (argList.Count == 0 || argList.Contains("-h") || argList.Contains("--help"))
{
    PrintUsage();
    return argList.Count == 0 ? 1 : 0;
}

// ---- parse args ----
string? serverSource = null, fieldPath = null, mobRegenDir = null, serverInfoPath = null, outPath = null;
int? zones = null;
int cap = DefaultCap, zoneBase = 0;
double baseWeight = 1.0, playerFactor = 1.0, mobFactor = 1.0;
var resolver = new WeightResolver(1.0);
var exactWeights = new List<(string, double)>();
var patternWeights = new List<(string, double)>();
string? weightsFile = null;

try
{
    for (int i = 0; i < argList.Count; i++)
    {
        string a = argList[i];
        string Next() => ++i < argList.Count ? argList[i] : throw new ArgEx($"{a} needs a value");
        switch (a)
        {
            case "--server-source": serverSource = Next(); break;
            case "--field": fieldPath = Next(); break;
            case "--mobregen": mobRegenDir = Next(); break;
            case "--server-info": serverInfoPath = Next(); break;
            case "--zones": zones = ParseInt(Next(), "--zones"); break;
            case "--max-groups": cap = ParseInt(Next(), "--max-groups"); break;
            case "--zone-base": zoneBase = ParseInt(Next(), "--zone-base"); break;
            case "--base-weight": baseWeight = ParseDouble(Next(), "--base-weight"); break;
            case "--player-factor": playerFactor = ParseDouble(Next(), "--player-factor"); break;
            case "--mob-factor": mobFactor = ParseDouble(Next(), "--mob-factor"); break;
            case "--weight": exactWeights.Add(ParseKv(Next(), "--weight")); break;
            case "--weight-pattern": patternWeights.Add(ParseKv(Next(), "--weight-pattern")); break;
            case "--weights": weightsFile = Next(); break;
            case "--out": outPath = Next(); break;
            default: throw new ArgEx($"unknown argument: {a}");
        }
    }

    // Resolve Field.txt / MobRegen from --server-source unless given explicitly.
    if (serverSource is not null)
    {
        fieldPath ??= Path.Combine(serverSource, "9Data", "Shine", "World", "Field.txt");
        mobRegenDir ??= Path.Combine(serverSource, "9Data", "Shine", "MobRegen");
    }
    if (fieldPath is null) throw new ArgEx("need --field or --server-source");
    if (mobRegenDir is null) throw new ArgEx("need --mobregen or --server-source");
    if (!File.Exists(fieldPath)) throw new ArgEx($"no such file: {fieldPath}");
    if (!Directory.Exists(mobRegenDir)) throw new ArgEx($"no such directory: {mobRegenDir}");
    if (cap < 1) throw new ArgEx("--max-groups must be >= 1");
    if (zones is < 1) throw new ArgEx("--zones must be >= 1");
}
catch (ArgEx ex)
{
    Console.Error.WriteLine($"zone-balancer: {ex.Message}");
    Console.Error.WriteLine("try --help");
    return 2;
}

// ---- load data ----
resolver = new WeightResolver(baseWeight);
if (weightsFile is not null)
{
    if (!File.Exists(weightsFile)) { Console.Error.WriteLine($"zone-balancer: no such weights file: {weightsFile}"); return 2; }
    resolver.LoadFile(weightsFile);
}
foreach (var (k, w) in patternWeights) resolver.AddPattern(k, w);
foreach (var (k, w) in exactWeights) resolver.AddExact(k, w);   // exact wins over pattern

var field = FieldFile.Load(fieldPath);
var groups = MobRegenCounter.CountAll(mobRegenDir);

foreach (var m in field.Maps)
{
    m.MobGroups = groups.TryGetValue(m.MapId, out var g) ? g : 0;
    m.Weight = resolver.Resolve(m.MapId);
    m.Load = m.MobGroups * mobFactor + m.Weight * playerFactor;
}

// ---- analysis report ----
int totalRows = field.Maps.Sum(m => m.RowCount);
int totalGroups = field.Maps.Sum(m => m.MobGroups);
int withMobs = field.Maps.Count(m => m.MobGroups > 0);
var biggest = field.Maps.MaxBy(m => m.MobGroups);
int minZones = Balancer.MinFeasibleZones(field.Maps, cap);

Console.WriteLine($"Field.txt : {fieldPath}");
Console.WriteLine($"MobRegen  : {mobRegenDir}");
Console.WriteLine();
Console.WriteLine($"  distinct maps        : {field.Maps.Count}  ({totalRows} #Record rows)");
Console.WriteLine($"  maps with mob groups : {withMobs}  ({field.Maps.Count - withMobs} with 0 — towns / instanced)");
Console.WriteLine($"  total mob groups     : {totalGroups}");
if (biggest is not null)
    Console.WriteLine($"  biggest single map   : {biggest.MapId} ({biggest.MobGroups} groups)");
Console.WriteLine($"  per-zone cap         : {cap}");
Console.WriteLine($"  MINIMUM zones needed : {minZones}   (ceil {totalGroups} / {cap})");

var overCap = Balancer.OverCapMaps(field.Maps, cap).ToList();
if (overCap.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"  !! {overCap.Count} map(s) exceed the cap on their own and can never load:");
    foreach (var m in overCap) Console.WriteLine($"       {m.MapId}: {m.MobGroups} > {cap}");
}

// current distribution
Console.WriteLine();
Console.WriteLine("  current Fiesta distribution:");
foreach (var grp in field.Maps.GroupBy(m => m.OriginalZone).OrderBy(g => g.Key))
    Console.WriteLine($"     zone {grp.Key,2}: {grp.Count(),3} maps, {grp.Sum(m => m.MobGroups),5} groups");

if (serverInfoPath is not null && File.Exists(serverInfoPath))
{
    var ids = ServerInfoReader.ZoneIds(serverInfoPath);
    Console.WriteLine();
    Console.WriteLine($"  ServerInfo zones     : {ids.Count} configured (ids {string.Join(",", ids)})");
}

if (zones is null)
{
    Console.WriteLine();
    Console.WriteLine("No --zones given (analysis only). Top maps by load:");
    foreach (var m in field.Maps.OrderByDescending(m => m.Load).Take(12))
        Console.WriteLine($"     {m.MapId,-16} groups={m.MobGroups,4}  weight={m.Weight,6:0.#}  load={m.Load,7:0.#}");
    Console.WriteLine();
    Console.WriteLine($"Re-run with --zones N (N >= {minZones}) to compute a balanced split.");
    return 0;
}

// ---- balance ----
BalanceResult result;
try
{
    result = Balancer.Balance(field.Maps, zones.Value, cap, zoneBase);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"zone-balancer: {ex.Message}");
    return 3;
}

Console.WriteLine();
Console.WriteLine($"Balanced across {zones} zones (ids {zoneBase}..{zoneBase + zones - 1}):");
Console.WriteLine();
Console.WriteLine($"  {"zone",4}  {"maps",4}  {"groups",7}  {"cap%",6}  {"load",9}");
Console.WriteLine($"  {new string('-', 4)}  {new string('-', 4)}  {new string('-', 7)}  {new string('-', 6)}  {new string('-', 9)}");
foreach (var z in result.Zones)
{
    double pct = 100.0 * z.MobGroups / cap;
    Console.WriteLine($"  {z.Id,4}  {z.Maps.Count,4}  {z.MobGroups,7}  {pct,5:0.0}%  {z.Load,9:0.#}");
}

double maxLoad = result.Zones.Max(z => z.Load);
double minLoad = result.Zones.Min(z => z.Load);
int maxGroups = result.Zones.Max(z => z.MobGroups);
Console.WriteLine();
Console.WriteLine($"  load spread : min {minLoad:0.#} .. max {maxLoad:0.#}  (imbalance {(maxLoad > 0 ? (maxLoad - minLoad) / maxLoad * 100 : 0):0.0}%)");
Console.WriteLine($"  peak mob groups in a zone : {maxGroups} / {cap}  ({100.0 * maxGroups / cap:0.0}%)");

if (serverInfoPath is not null && File.Exists(serverInfoPath))
{
    var ids = ServerInfoReader.ZoneIds(serverInfoPath);
    if (ids.Count != zones)
        Console.WriteLine($"  note: ServerInfo has {ids.Count} zone(s) but you targeted {zones} — wire up {zones} Zone services to match.");
}

// ---- write ----
if (outPath is not null)
{
    var assignment = field.Maps
        .Where(m => m.AssignedZone >= 0)
        .ToDictionary(m => m.MapId, m => m.AssignedZone, StringComparer.OrdinalIgnoreCase);
    field.Write(outPath, assignment);
    Console.WriteLine();
    Console.WriteLine($"Wrote {outPath} ({assignment.Count} maps reassigned; all other bytes preserved).");
}
else
{
    Console.WriteLine();
    Console.WriteLine("(dry run — pass --out PATH to write the new Field.txt)");
}
return 0;

// ---- helpers ----
static int ParseInt(string s, string flag)
    => int.TryParse(s, out var v) ? v : throw new ArgEx($"{flag}: '{s}' is not an integer");
static double ParseDouble(string s, string flag)
    => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
        ? v : throw new ArgEx($"{flag}: '{s}' is not a number");
static (string, double) ParseKv(string s, string flag)
{
    int eq = s.LastIndexOf('=');
    if (eq <= 0) throw new ArgEx($"{flag}: expected KEY=VALUE, got '{s}'");
    return (s[..eq].Trim(), ParseDouble(s[(eq + 1)..].Trim(), flag));
}

static void PrintUsage()
{
    Console.WriteLine(
@"zone-balancer — auto-size Fiesta zones from Field.txt + MobRegen data.

Reads the maps in Field.txt and the per-map mob regen group counts, then packs
maps into a target number of zones: spreading player/mob load as evenly as
possible while keeping each zone under the engine's hard cap of 4096 mob spawn
groups (MobHatchery mh_Array[4096]). Writes a new Field.txt with the trailing
'Fiesta' (zone id) column rewritten.

USAGE
  zone-balancer --server-source DIR [--zones N] [--out FILE] [options]
  zone-balancer --field Field.txt --mobregen MobRegen/ [--zones N] [options]

INPUTS
  --server-source DIR   ServerSource root; derives Field.txt + MobRegen/ paths.
  --field PATH          Field.txt (overrides the derived path).
  --mobregen DIR        MobRegen directory (overrides the derived path).
  --server-info PATH    Optional ServerInfo.txt, to cross-check the zone count.

ZONES
  --zones N             Target number of zones. Omit for analysis only
                        (reports the minimum feasible count and exits).
  --max-groups N        Per-zone mob-group cap (default 4096 — the engine limit).
  --zone-base N         First zone id written to the Fiesta column (default 0).
  --out PATH            Write the rebalanced Field.txt here (else dry run).

WEIGHTS (relative player load, in mob-group-equivalent units; default 1)
  --base-weight W       Default weight for every map (default 1).
  --weight MAPID=W      Override one map (e.g. --weight Eld=300 for a city).
  --weight-pattern RE=W Override maps whose MapID matches regex RE.
  --weights FILE        Load 'KEY = W' lines; '~' prefix on KEY means regex.
  --player-factor F     Scale weight's contribution to load (default 1).
  --mob-factor F        Scale mob groups' contribution to load (default 1).
                        load = mobGroups*mob-factor + weight*player-factor

EXAMPLES
  # Just analyse (how many zones do I actually need?)
  zone-balancer --server-source /srv/ServerSource

  # Pack into 4 zones, treat the three towns as heavy, write the file
  zone-balancer --server-source /srv/ServerSource --zones 4 \
    --weight Rou=300 --weight Eld=300 --weight Bera=200 \
    --out Field.balanced.txt");
}

file sealed class ArgEx(string message) : Exception(message);

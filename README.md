# ik-fiesta-zone-balancer

A small .NET CLI that **auto-sizes the zones of a Fiesta Online server** from its
own data files. It reads `Field.txt` (the map list) and the per-map mob-spawn
data in `MobRegen/`, then packs the maps into a target number of zones — spreading
load as evenly as possible while respecting the engine's hard ceiling of
**4096 mob spawn groups per zone**. It writes a new `Field.txt` with the trailing
`Fiesta` (zone-id) column rewritten, leaving every other byte untouched.

Companion to [ik-fiesta-docker](https://github.com/IkaronClaude/ik-fiesta-docker) /
[ik-fiesta-proxy](https://github.com/IkaronClaude/ik-fiesta-proxy): once you've split
your maps across N zones, run N `Zone` services (the `Fiesta` column tells each
Zone exe which maps to load).

## Why

A single Fiesta `Zone` process loads every map assigned to it and registers each
map's mob regen groups into a fixed array — `MobHatchery`'s `mh_Array[4096]`. Go
over 4096 total and the zone asserts on boot (*"Too many MobRegenGroup"*). So a
real server **must** split its maps across multiple zones, and that split should
be balanced — not just by mob count (server AI/regen cost) but by **expected
players**: most maps see 1–2 players, but towns can see 100+. This tool accounts
for both.

## What it does

1. Counts active mob regen groups per map (`MobRegen/<MapID>.txt`, lines starting
   with `#record`; commented templates don't count). Maps with no file —
   instanced / Lua-spawned dungeons that don't pre-load into `mh_Array` — count
   as 0.
2. Computes the **minimum feasible zone count** = `ceil(total groups / 4096)`,
   and flags any single map that already exceeds the cap (impossible to load).
3. Assigns each map a **load** = `mobGroups × mob-factor + weight × player-factor`,
   where `weight` is a per-map player-load estimate you can bump (towns!).
4. Packs maps into your target zone count with a longest-processing-time greedy:
   heaviest map first into the least-loaded zone that still has mob-group
   headroom. Even load, hard cap never exceeded.
5. Writes a new `Field.txt` (only the `Fiesta` column changes; CRLF, encoding,
   and all other columns preserved byte-for-byte).

## Build

```bash
dotnet build -c Release
# or run directly:
dotnet run -- --help
```

Requires the .NET SDK (built/tested on .NET 10).

## Usage

```
zone-balancer --server-source DIR [--zones N] [--out FILE] [options]
zone-balancer --field Field.txt --mobregen MobRegen/ [--zones N] [options]
```

**Analysis only** (how many zones do I actually need?):

```bash
zone-balancer --server-source /srv/ServerSource
```

```
  distinct maps        : 148  (174 #Record rows)
  maps with mob groups : 74  (74 with 0 — towns / instanced)
  total mob groups     : 14472
  biggest single map   : UrgDark01 (782 groups)
  per-zone cap         : 4096
  MINIMUM zones needed : 4   (ceil 14472 / 4096)
```

**Pack into 4 zones**, treating the three starter towns as heavy, and write it:

```bash
zone-balancer --server-source /srv/ServerSource --zones 4 \
  --weight Rou=300 --weight Eld=300 --weight Bera=200 \
  --out Field.balanced.txt
```

```
  zone  maps   groups    cap%       load
  ----  ----  -------  ------  ---------
     0    36     3668   89.6%       3903
     1    35     3569   87.1%       3903
     2    36     3668   89.6%       3903
     3    41     3567   87.1%       3903

  load spread : min 3903 .. max 3903  (imbalance 0.0%)
  peak mob groups in a zone : 3668 / 4096  (89.6%)
```

Without `--out` it's a dry run (reports only).

## Options

| Option | Meaning |
| --- | --- |
| `--server-source DIR` | ServerSource root; derives `9Data/Shine/World/Field.txt` + `9Data/Shine/MobRegen/`. |
| `--field PATH` / `--mobregen DIR` | Point at the two inputs explicitly (override the derived paths). |
| `--server-info PATH` | Optional `ServerInfo.txt`; cross-checks the target against the configured zone count. |
| `--zones N` | Target zone count. Omit for analysis only. |
| `--max-groups N` | Per-zone mob-group cap (default **4096** — the engine limit). |
| `--zone-base N` | First zone id written to the `Fiesta` column (default `0`). |
| `--out PATH` | Write the rebalanced `Field.txt` (else dry run). |
| `--base-weight W` | Default per-map weight (default `1`). |
| `--weight MAPID=W` | Override one map's weight (e.g. a town). Repeatable. |
| `--weight-pattern RE=W` | Override maps whose `MapID` matches regex `RE`. Repeatable. |
| `--weights FILE` | Load `KEY = W` lines; a `~` prefix on `KEY` means regex. |
| `--player-factor F` / `--mob-factor F` | Scale each term of `load = mobGroups×mob-factor + weight×player-factor` (both default `1`). |

### The weight model

`weight` is a relative player-load estimate expressed in *mob-group-equivalent
units*. A normal grind map (weight 1) is dominated by its mob-group count. A town
has ~0 mob groups but heavy population, so give it a weight comparable to a busy
grind map (e.g. 200–400) to make the packer treat it as real load. Tune
`--player-factor` to dial how much population matters relative to mob cost.

A weights file lets you keep this in version control:

```ini
# towns — heavy population, ~no mobs
Rou  = 300
Eld  = 300
Bera = 200
Adl  = 200
# all guild-territory maps: near-zero live load
~^Guild = 0
```

## Notes

- **No game content** ships here — you point it at your own files.
- Only the `Fiesta` column is rewritten; the output is otherwise byte-identical
  to your input (verified against real 2016-NA `Field.txt`).
- After splitting, make sure your `ServerInfo.txt` actually defines that many
  `Zone` services (ids `0..N-1`) and your stack runs them.

## License

[Apache License 2.0](LICENSE) — same as `ik-fiesta-docker`. Permissive, with an
explicit patent grant.

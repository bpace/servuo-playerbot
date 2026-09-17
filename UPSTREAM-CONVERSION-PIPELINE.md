# UO Offline conversion pipeline

`bpace/uo-offline` is the tracking fork for Klein187's ModernUO project. `bpace/servuo-playerbot` is an independent ServUO implementation. Never merge one into the other.

When UO Offline changes, run this from the PlayerBot repository:

```powershell
.\tools\Sync-Upstream.ps1 -Fetch
```

The script fetches the tracking checkout, compares the upstream revision against the last reviewed revision, and creates a timestamped report under `research/upstream-diffs`. It never writes PlayerBot source, shard data, or a Git remote.

Triage every changed file in the report into one of these outcomes:

| Outcome | Meaning |
| --- | --- |
| Port | The behavior can be implemented against ServUO APIs and tests. |
| Adapt | The feature is useful but needs a ServUO-native data model or UI. |
| Reference | Preserve its design or data idea without copying engine code. |
| Decline | It depends on ModernUO internals or conflicts with shard policy. |

For a port or adaptation, create a focused branch in this repository, record the upstream commit and source files in the commit body, compile against ServUO pub57, and test in the isolated full ServUO worktree. Do not deploy the result until it has been reviewed and the shard owner authorizes a restart.

After every item in a report has been triaged, run:

```powershell
.\tools\Sync-Upstream.ps1 -MarkReviewed
```

That advances only the reviewed marker. It does not claim that every upstream change was ported.

## Editor-specific rule

The ModernUO browser editor is a reference implementation. Its `tools/map` server, `Data/Live` file bridge, JSON registries, and engine hooks are not dependencies of this mod. The ServUO editor writes only `Data/PlayerBots/world-data.xml`, retains a `.bak` copy on every change, and must expose world-writing controls only through the dashboard's allowlisted shard listener.

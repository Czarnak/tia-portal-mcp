# Graph Report - tia-portal-mcp  (2026-06-14)

## Corpus Check

- 84 files · ~25,985 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary

- 681 nodes · 1001 edges · 86 communities (6 shown, 80 thin omitted)
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS · INFERRED: 2 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness

- Built from commit: `94e93582`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)

- [[_COMMUNITY_Community 0|Community 0]]
- [[_COMMUNITY_Community 1|Community 1]]
- [[_COMMUNITY_Community 2|Community 2]]
- [[_COMMUNITY_Community 3|Community 3]]
- [[_COMMUNITY_Community 4|Community 4]]
- [[_COMMUNITY_Community 5|Community 5]]
- [[_COMMUNITY_Community 6|Community 6]]
- [[_COMMUNITY_Community 7|Community 7]]
- [[_COMMUNITY_Community 8|Community 8]]
- [[_COMMUNITY_Community 9|Community 9]]
- [[_COMMUNITY_Community 10|Community 10]]
- [[_COMMUNITY_Community 11|Community 11]]
- [[_COMMUNITY_Community 12|Community 12]]
- [[_COMMUNITY_Community 13|Community 13]]
- [[_COMMUNITY_Community 14|Community 14]]
- [[_COMMUNITY_Community 15|Community 15]]
- [[_COMMUNITY_Community 16|Community 16]]
- [[_COMMUNITY_Community 17|Community 17]]
- [[_COMMUNITY_Community 18|Community 18]]
- [[_COMMUNITY_Community 19|Community 19]]
- [[_COMMUNITY_Community 20|Community 20]]
- [[_COMMUNITY_Community 21|Community 21]]
- [[_COMMUNITY_Community 22|Community 22]]
- [[_COMMUNITY_Community 23|Community 23]]
- [[_COMMUNITY_Community 24|Community 24]]
- [[_COMMUNITY_Community 25|Community 25]]
- [[_COMMUNITY_Community 26|Community 26]]
- [[_COMMUNITY_Community 27|Community 27]]
- [[_COMMUNITY_Community 28|Community 28]]
- [[_COMMUNITY_Community 29|Community 29]]
- [[_COMMUNITY_Community 30|Community 30]]
- [[_COMMUNITY_Community 31|Community 31]]
- [[_COMMUNITY_Community 32|Community 32]]
- [[_COMMUNITY_Community 33|Community 33]]
- [[_COMMUNITY_Community 34|Community 34]]
- [[_COMMUNITY_Community 35|Community 35]]
- [[_COMMUNITY_Community 36|Community 36]]
- [[_COMMUNITY_Community 37|Community 37]]
- [[_COMMUNITY_Community 38|Community 38]]
- [[_COMMUNITY_Community 39|Community 39]]
- [[_COMMUNITY_Community 40|Community 40]]
- [[_COMMUNITY_Community 41|Community 41]]
- [[_COMMUNITY_Community 42|Community 42]]
- [[_COMMUNITY_Community 43|Community 43]]
- [[_COMMUNITY_Community 44|Community 44]]
- [[_COMMUNITY_Community 45|Community 45]]
- [[_COMMUNITY_Community 46|Community 46]]
- [[_COMMUNITY_Community 47|Community 47]]
- [[_COMMUNITY_Community 48|Community 48]]
- [[_COMMUNITY_Community 49|Community 49]]
- [[_COMMUNITY_Community 50|Community 50]]
- [[_COMMUNITY_Community 51|Community 51]]
- [[_COMMUNITY_Community 52|Community 52]]
- [[_COMMUNITY_Community 53|Community 53]]
- [[_COMMUNITY_Community 54|Community 54]]
- [[_COMMUNITY_Community 55|Community 55]]
- [[_COMMUNITY_Community 56|Community 56]]
- [[_COMMUNITY_Community 57|Community 57]]
- [[_COMMUNITY_Community 58|Community 58]]
- [[_COMMUNITY_Community 59|Community 59]]
- [[_COMMUNITY_Community 60|Community 60]]
- [[_COMMUNITY_Community 61|Community 61]]
- [[_COMMUNITY_Community 62|Community 62]]
- [[_COMMUNITY_Community 63|Community 63]]
- [[_COMMUNITY_Community 64|Community 64]]
- [[_COMMUNITY_Community 65|Community 65]]
- [[_COMMUNITY_Community 66|Community 66]]
- [[_COMMUNITY_Community 67|Community 67]]
- [[_COMMUNITY_Community 68|Community 68]]
- [[_COMMUNITY_Community 69|Community 69]]
- [[_COMMUNITY_Community 70|Community 70]]
- [[_COMMUNITY_Community 71|Community 71]]
- [[_COMMUNITY_Community 72|Community 72]]
- [[_COMMUNITY_Community 73|Community 73]]
- [[_COMMUNITY_Community 74|Community 74]]
- [[_COMMUNITY_Community 75|Community 75]]
- [[_COMMUNITY_Community 76|Community 76]]
- [[_COMMUNITY_Community 77|Community 77]]
- [[_COMMUNITY_Community 78|Community 78]]
- [[_COMMUNITY_Community 79|Community 79]]
- [[_COMMUNITY_Community 80|Community 80]]
- [[_COMMUNITY_Community 81|Community 81]]
- [[_COMMUNITY_Community 82|Community 82]]
- [[_COMMUNITY_Community 83|Community 83]]
- [[_COMMUNITY_Community 84|Community 84]]
- [[_COMMUNITY_Community 85|Community 85]]

## God Nodes (most connected - your core abstractions)

1. `Program` - 37 edges
2. `OpennessWorkerClient` - 36 edges
3. `HardwareConfigReader` - 20 edges
4. `CompileChecker` - 16 edges
5. `ProjectLifecycleService` - 16 edges
6. `TagMutationService` - 15 edges
7. `ProjectLifecycleTools` - 14 edges
8. `CrossReferenceReader` - 14 edges
9. `NetworkDeviceConfigurator` - 14 edges
10. `EquipmentCatalogSearcher` - 13 edges

## Surprising Connections (you probably didn't know these)

- `OpennessWorkerClient` --references--> `JsonSerializerOptions`  [EXTRACTED]
  TiaMcpServer/Worker/OpennessWorkerClient.cs → TiaMcpServer.OpennessWorker/Program.cs
- `WriteSafetyService` --references--> `JsonSerializerOptions`  [EXTRACTED]
  TiaMcpServer/Safety/WriteSafetyService.cs → TiaMcpServer.OpennessWorker/Program.cs
- `WriteSafetyTooling` --references--> `JsonSerializerOptions`  [EXTRACTED]
  TiaMcpServer/Safety/WriteSafetyTooling.cs → TiaMcpServer.OpennessWorker/Program.cs
- `WriteSafetyService` --references--> `TimeSpan`  [EXTRACTED]
  TiaMcpServer/Safety/WriteSafetyService.cs → TiaMcpServer/Worker/OpennessWorkerClient.cs
- `ArchiveModeNames` --references--> `string`  [EXTRACTED]
  TiaMcpServer.Contracts/ArchiveModeNames.cs → TiaMcpServer.Contracts/ProjectSessionBinding.cs

## Communities (86 total, 80 thin omitted)

### Community 0 - "Community 0"

Cohesion: 0.04
Nodes (45): Architecture, Block Paths, Build From Source, code:text (C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21), code:powershell (dotnet run --project TiaMcpServer), code:powershell ('{ "method": "browse_project_tree", "projectPath": null }' |), code:json ({"success":true,"payload":"[...]"}), code:json ({"success":false,"error":"No running TIA Portal V21 instance) (+37 more)

### Community 4 - "Community 4"

Cohesion: 0.13
Nodes (10): ConcurrentDictionary, Func, JsonSerializerOptions, Invalid(), Valid(), WriteSafetyService, Invalid(), Valid() (+2 more)

### Community 5 - "Community 5"

Cohesion: 0.1
Nodes (4): TagOperationsTool, TagTableOperationsTool, TiaMcpServer.Tools, UserConstantOperationsTool

### Community 14 - "Community 14"

Cohesion: 0.22
Nodes (4): bool, IDisposable, TiaPortalSession, TiaPortal

### Community 18 - "Community 18"

Cohesion: 0.27
Nodes (3): string, ArchiveModeNames, ProjectSessionBinding

### Community 32 - "Community 32"

Cohesion: 0.29
Nodes (7): Model Context Protocol, Siemens TIA Openness User Group, Phase 1: Implementation, Phase 2: Universal Block Support, Phase 3: Hardware and Network Discovery, Phase 4: Advanced Diagnostics, tia-portal-mcp

## Knowledge Gaps

- **58 isolated node(s):** `ConcurrentDictionary`, `Func`, `TiaMcpServer.Tools`, `TiaMcpServer.Tools`, `ProjectSessionBinding` (+53 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **80 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions

_Questions this graph is uniquely positioned to answer:_

- **Why does `JsonSerializerOptions` connect `Community 4` to `Community 1`, `Community 2`?**
  _High betweenness centrality (0.016) - this node is a cross-community bridge._
- **Why does `OpennessWorkerClient` connect `Community 2` to `Community 4`?**
  _High betweenness centrality (0.015) - this node is a cross-community bridge._
- **Why does `Program` connect `Community 1` to `Community 4`?**
  _High betweenness centrality (0.013) - this node is a cross-community bridge._
- **What connects `ConcurrentDictionary`, `Func`, `TiaMcpServer.Tools` to the rest of the system?**
  _58 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.04 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._
- **Should `Community 4` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._

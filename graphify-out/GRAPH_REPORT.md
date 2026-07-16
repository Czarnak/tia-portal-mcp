# Graph Report - tia-portal-mcp  (2026-07-17)

## Corpus Check
- 101 files · ~34,420 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1025 nodes · 1601 edges · 109 communities (11 shown, 98 thin omitted)
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS · INFERRED: 2 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `52e114ae`
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
- [[_COMMUNITY_Community 86|Community 86]]
- [[_COMMUNITY_Community 87|Community 87]]
- [[_COMMUNITY_Community 88|Community 88]]
- [[_COMMUNITY_Community 89|Community 89]]
- [[_COMMUNITY_Community 90|Community 90]]
- [[_COMMUNITY_Community 91|Community 91]]
- [[_COMMUNITY_Community 92|Community 92]]
- [[_COMMUNITY_Community 93|Community 93]]
- [[_COMMUNITY_Community 94|Community 94]]
- [[_COMMUNITY_Community 95|Community 95]]
- [[_COMMUNITY_Community 96|Community 96]]
- [[_COMMUNITY_Community 97|Community 97]]
- [[_COMMUNITY_Community 98|Community 98]]
- [[_COMMUNITY_Community 99|Community 99]]
- [[_COMMUNITY_Community 100|Community 100]]
- [[_COMMUNITY_Community 101|Community 101]]
- [[_COMMUNITY_Community 102|Community 102]]
- [[_COMMUNITY_Community 103|Community 103]]
- [[_COMMUNITY_Community 104|Community 104]]
- [[_COMMUNITY_Community 105|Community 105]]
- [[_COMMUNITY_Community 107|Community 107]]
- [[_COMMUNITY_Community 108|Community 108]]

## God Nodes (most connected - your core abstractions)
1. `OpennessWorkerClient` - 56 edges
2. `Program` - 46 edges
3. `BatchOperationCatalogTests` - 28 edges
4. `HardwareConfigReader` - 20 edges
5. `OpennessWorkerClientIntegrationTests` - 20 edges
6. `ProjectLifecycleTools` - 18 edges
7. `PersistentWorkerTransport` - 18 edges
8. `BatchPayloadBudget` - 16 edges
9. `WriteSafetyService` - 16 edges
10. `CompileChecker` - 16 edges

## Surprising Connections (you probably didn't know these)
- `BatchPayloadBudget` --references--> `int`  [EXTRACTED]
  TiaMcpServer/Batch/BatchPayloadBudget.cs → TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs
- `OpennessWorkerClient` --references--> `int`  [EXTRACTED]
  TiaMcpServer/Worker/OpennessWorkerClient.cs → TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs
- `PersistentWorkerTransport` --references--> `int`  [EXTRACTED]
  TiaMcpServer/Worker/PersistentWorkerTransport.cs → TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs
- `BatchOperationStatus` --references--> `string`  [EXTRACTED]
  TiaMcpServer/Batch/BatchOperationResult.cs → TiaMcpServer.Tests/BatchSafetyTokenTests.cs
- `WriteSafetyService` --references--> `string`  [EXTRACTED]
  TiaMcpServer/Safety/WriteSafetyService.cs → TiaMcpServer.Tests/BatchSafetyTokenTests.cs

## Communities (109 total, 98 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.09
Nodes (4): object, PersistentWorkerTransport, ProjectSessionBinding, OpennessWorkerClient

### Community 1 - "Community 1"
Cohesion: 0.08
Nodes (8): BatchOperationStatus, BatchSafetySnapshot, BatchTools, BlockImporter, string, ArchiveModeNames, ProjectSessionBinding, BatchSafetyTokenTests

### Community 3 - "Community 3"
Cohesion: 0.04
Nodes (45): Architecture, Block Paths, Build From Source, code:text (C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21), code:powershell (dotnet run --project TiaMcpServer), code:powershell ('{ "method": "browse_project_tree", "projectPath": null }' |), code:json ({"success":true,"payload":"[...]"}), code:json ({"success":false,"error":"No running TIA Portal V21 instance) (+37 more)

### Community 4 - "Community 4"
Cohesion: 0.08
Nodes (10): BatchResultFormatter, ConcurrentDictionary, Func, JsonSerializerOptions, Invalid(), Valid(), WriteSafetyService, BatchOperationRequestJsonTests (+2 more)

### Community 5 - "Community 5"
Cohesion: 0.12
Nodes (8): BatchOperationCatalog, Invalid(), Valid(), int, IReadOnlyDictionary, IReadOnlyList, IReadOnlySet, EquipmentCatalogSearcher

### Community 6 - "Community 6"
Cohesion: 0.1
Nodes (11): bool, ConcurrentQueue, IDisposable, ILogger, TiaPortalSession, Process, SemaphoreSlim, Task (+3 more)

### Community 10 - "Community 10"
Cohesion: 0.1
Nodes (4): TagOperationsTool, TagTableOperationsTool, TiaMcpServer.Tools, UserConstantOperationsTool

### Community 29 - "Community 29"
Cohesion: 0.24
Nodes (3): Invalid(), Valid(), WriteSafetyTooling

### Community 45 - "Community 45"
Cohesion: 0.29
Nodes (7): Model Context Protocol, Siemens TIA Openness User Group, Phase 1: Implementation, Phase 2: Universal Block Support, Phase 3: Hardware and Network Discovery, Phase 4: Advanced Diagnostics, tia-portal-mcp

## Knowledge Gaps
- **68 isolated node(s):** `IReadOnlyList`, `IReadOnlyDictionary`, `IReadOnlySet`, `BatchOperationRequest`, `ConcurrentDictionary` (+63 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **98 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `JsonSerializerOptions` connect `Community 4` to `Community 0`, `Community 2`, `Community 29`, `Community 6`?**
  _High betweenness centrality (0.053) - this node is a cross-community bridge._
- **Why does `string` connect `Community 1` to `Community 0`, `Community 4`, `Community 5`, `Community 6`, `Community 11`?**
  _High betweenness centrality (0.039) - this node is a cross-community bridge._
- **Why does `OpennessWorkerClient` connect `Community 0` to `Community 1`, `Community 4`, `Community 5`, `Community 6`?**
  _High betweenness centrality (0.039) - this node is a cross-community bridge._
- **What connects `IReadOnlyList`, `IReadOnlyDictionary`, `IReadOnlySet` to the rest of the system?**
  _68 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.09 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.08 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.04 - nodes in this community are weakly interconnected._
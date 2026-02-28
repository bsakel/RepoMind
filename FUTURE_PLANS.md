# RepoMind — Future Plans

This document outlines planned improvements and ideas for RepoMind, organized by priority and impact.

---

## 🔥 High Priority — Onboarding & Reliability

### 1. `repomind init` — Zero-friction onboarding ✅ Implemented
A single `repomind --init` command that validates the environment (.NET, git), detects repo structure under the root path, runs the first scan, and outputs a human-readable summary. The "npm init" moment for codebase intelligence.

### 2. Progress feedback during scanning ✅ Implemented
Per-project progress logging (`[3/12] Scanning Acme.Core... 142 types found (2.1s)`) written to stderr so it appears in VS Code's MCP output panel and CLI terminals alike.

### 3. Resilient scanning — never fail entirely ✅ Implemented
If one project has a malformed csproj or Roslyn can't parse a file, the scan wraps each project in error handling, collects failures, continues scanning remaining projects, and reports what failed at the end.

---

## 🧠 High Impact — Deeper Intelligence

### 4. Semantic summaries ✅ Implemented
Move beyond structural data (types, methods, parameters) to capture *intent*. Uses XML doc comments, method counts, interface relationships, DI dependency counts, and naming conventions to generate natural-language summaries via `get_project_summary` and `get_type_summary` MCP tools.

### 5. Architecture pattern detection
Automatically detect and label common patterns in the codebase:
- Repository pattern (`IRepository<T>` implementations)
- CQRS (MediatR handlers, command/query separation)
- Event sourcing (`IEventStore`, domain events)
- Mediator, decorator, factory patterns

The signals are already in the scanned data — interface names, DI registrations, naming conventions. Label them so AI agents get instant architectural context.

### 6. Mermaid flow diagrams ✅ Implemented
The `trace_flow`, `analyze_impact`, and `get_dependency_graph` tools now include Mermaid diagram blocks alongside their markdown output. Every AI agent and modern dev tool can render Mermaid for visual dependency chains.

---

## 🚀 Medium Impact — Reach & Usability

### 7. Watch mode — always-fresh memory
Add a `--watch` flag that uses `FileSystemWatcher` to detect file changes and auto-rescan affected projects. The memory database stays current without manual rescans. Especially powerful for the MCP server — AI agents always have up-to-date knowledge.

### 8. Multi-language support (TypeScript first)
.NET-only limits the audience. TypeScript/JavaScript would dramatically expand adoption. The architecture supports it — add a `TypeScriptScanner` (using the TS compiler API or tree-sitter) that feeds the same `TypeInfo`/`MethodInfo` models. The MCP server, query layer, and flat files are already language-agnostic.

### 9. Docker image + GitHub Action
Two distribution channels:
- **Docker**: `docker run -v /path/to/repos:/repos repomind` — no .NET SDK required on host
- **GitHub Action**: Scan on push, publish memory as artifact. Fresh codebase intelligence on every commit. "Set it and forget it" for teams.

### 10. Structured tool results
Tools currently return markdown strings. Add structured JSON responses with metadata:
```json
{ "results": [...], "count": 47, "truncated": false, "query_ms": 12 }
```
Let AI agents decide formatting. Enables pagination, smarter follow-up queries, and programmatic consumption.

---

## 💡 Nice to Have — Polish & Delight

### 11. `repomind doctor` — self-diagnostics ✅ Implemented
A `--doctor` CLI flag that checks: database exists, schema tables present, data counts, scan freshness, flat file presence. Supports `--type=TypeName` to investigate why a specific type is missing (checks visibility, project inclusion, test/benchmark filtering).

### 12. Cross-repo change impact stories
Instead of blast radius lists, generate narrative:
> "Changing `IUserRepository` affects: `UserService` (direct consumer in Acme.Core), `AuthController` (indirect via UserService in Acme.Web.Api), and 3 test fixtures. The interface is also referenced as a NuGet package by the Billing team's repo."

### 13. Enhanced AGENTS.md generation
Level up `generate_agents_md` to produce a true onboarding document:
- Key architecture patterns detected in the codebase
- Common development flows ("to add a new endpoint, modify these files")
- Known gotchas ("all DB access goes through IRepository, never use DbContext directly")
- Team conventions inferred from code patterns

### 14. Query caching with TTL
Cache MCP tool query results with a short TTL (30s). The database is read-only between scans, so results won't change. Reduces repeated query overhead when an AI agent iterates on the same question.

---

## Implementation Priority

| # | Feature | Effort | Status |
|---|---------|--------|--------|
| 1 | `repomind init` | Small | ✅ Done |
| 2 | Progress feedback | Small | ✅ Done |
| 3 | Resilient scanning | Small | ✅ Done |
| 4 | Semantic summaries | Medium | ✅ Done |
| 5 | Pattern detection | Medium | Planned |
| 6 | Mermaid diagrams | Medium | ✅ Done |
| 7 | Watch mode | Medium | Planned |
| 8 | TypeScript support | Large | Planned |
| 9 | Docker + CI | Medium | Planned |
| 10 | Structured results | Medium | Planned |
| 11 | Doctor command | Small | ✅ Done |
| 12 | Impact stories | Medium | Planned |
| 13 | Enhanced AGENTS.md | Medium | Planned |
| 14 | Query caching | Small | Planned |

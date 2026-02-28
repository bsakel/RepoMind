# RepoMind

An AI memory layer for multi-repo codebases. RepoMind gives GitHub Copilot cross-project intelligence — types, dependencies, interfaces, and constructor injection — across any number of independent .NET repositories.

## What's Included

- **RepoMind.Mcp** — An MCP server (STDIO transport) that exposes 22 tools for querying types, dependencies, endpoints, configuration, impact analysis, version alignment, test coverage, git status, and more across all projects in a codebase
- **RepoMind.Scanner** — A Roslyn-based scanner that catalogs all projects, types, methods, REST/GraphQL endpoints, configuration keys, interfaces, and constructor-injected dependencies into a SQLite database and/or flat files

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0 or later)
- Git (for repo operations)
- Your multi-repo codebase cloned into a single directory (e.g., `C:\repos\MyProduct`)

## Build & Test

```bash
dotnet build RepoMind.slnx
dotnet test RepoMind.slnx
```

## Two Ways to Use RepoMind

RepoMind supports two workflows depending on how your AI agent (or you) prefers to consume codebase intelligence.

---

### Workflow A: MCP Server

**For AI agents that use structured tool calls** (e.g., GitHub Copilot with MCP support). The MCP server exposes 22 specialized query tools backed by a SQLite database. Agents invoke tools like `search_types`, `trace_flow`, or `analyze_impact` to get precise, filtered answers.

#### Setup

Add to your VS Code workspace `.vscode/mcp.json`:

```json
{
  "servers": {
    "repomind": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/RepoMind/src/RepoMind.Mcp"
      ],
      "env": {
        "REPOMIND_ROOT": "${workspaceFolder}"
      }
    }
  }
}
```

Or system-wide via VS Code user settings (`settings.json`):

```json
{
  "mcp": {
    "servers": {
      "repomind": {
        "type": "stdio",
        "command": "dotnet",
        "args": ["run", "--project", "/path/to/RepoMind/src/RepoMind.Mcp"],
        "env": { "REPOMIND_ROOT": "/path/to/your/codebase" }
      }
    }
  }
}
```

For faster startup, publish first:

```bash
dotnet publish src/RepoMind.Mcp -c Release -o ~/.repomind
# Then use: "command": "/path/to/.repomind/RepoMind.Mcp.exe"
```

Or install as a dotnet tool:

```bash
dotnet pack src/RepoMind.Mcp -c Release -o ./nupkg
dotnet tool install --global RepoMind --add-source ./nupkg
# Then use: "command": "repomind"
```

#### Scanning

The first time, ask Copilot to scan your codebase:
> "Use the `rescan_memory` tool to scan the codebase"

Or use the CLI directly:
```bash
dotnet run --project src/RepoMind.Mcp -- --init --root=/path/to/your/codebase
```

For subsequent updates (only rescan changed projects):
> "Use the `rescan_memory` tool with incremental=true"

The MCP server will guide you if the database hasn't been created yet.

#### Diagnostics

If something isn't working, run the built-in health check:
```bash
dotnet run --project src/RepoMind.Mcp -- --doctor --root=/path/to/your/codebase

# Investigate why a specific type is missing:
dotnet run --project src/RepoMind.Mcp -- --doctor --type=MyService --root=/path/to/your/codebase
```

---

### Workflow B: Flat Files

**For AI agents and developers that prefer reading files directly** and using standard CLI tools (`grep`, `cat`, `find`) to locate information. No MCP server needed — the scanner produces structured JSON and Markdown files that *are* the memory.

#### Scanning

Run the scanner CLI with `--flat-only`:

```bash
# Full scan
dotnet run --project src/RepoMind.Scanner.Cli -- --root /path/to/your/codebase --flat-only

# Incremental scan (only changed projects)
dotnet run --project src/RepoMind.Scanner.Cli -- --root /path/to/your/codebase --flat-only --incremental
```

#### Output

Files are written to `<root>/memory/`:

| File | Contents |
|------|----------|
| `projects.json` | Full project catalog with assemblies and frameworks |
| `dependency-graph.json` | Cross-project internal dependency map |
| `types-index.json` | All public types with methods, parameters, and endpoints |
| `projects/<name>.md` | Per-project detail: assemblies, types, methods, endpoints, config keys |

#### Querying with CLI Tools

```bash
# Find a type
grep -r "UserService" memory/types-index.json

# Find all REST endpoints
grep -rE "\[GET\]|\[POST\]|\[PUT\]|\[DELETE\]" memory/projects/

# Find config keys across projects
grep -r "ConnectionString" memory/projects/

# List all projects
cat memory/projects.json | python -m json.tool

# Find types implementing an interface
grep -r "implements.*IRepository" memory/projects/

# Show dependency graph
cat memory/dependency-graph.json | python -m json.tool
```

## How Internal Packages Are Detected

RepoMind automatically determines which NuGet packages are "internal" (i.e., references to other projects in the same codebase) by matching package names against all assembly names found during the scan. No configuration or prefixes needed.

## Available MCP Tools

| Tool | Description |
|------|-------------|
| `list_projects` | List all scanned projects |
| `get_project_info` | Detailed info for a specific project |
| `get_dependency_graph` | NuGet dependency relationships (with Mermaid diagram) |
| `search_types` | Find types by name pattern |
| `find_implementors` | Find types implementing an interface |
| `find_type_details` | Full type info (interfaces, DI deps) |
| `search_injections` | Find constructor-injected dependencies |
| `search_endpoints` | Find REST/GraphQL endpoints by route |
| `search_methods` | Find public methods by name pattern |
| `search_config` | Find config keys (appsettings, env vars, IConfiguration) |
| `trace_flow` | Trace type usage chains across projects (with Mermaid diagram) |
| `analyze_impact` | Blast radius analysis for type changes (with Mermaid diagram) |
| `get_package_versions` | NuGet package version usage |
| `update_repos` | Git pull all repos (with optional auto-rescan) |
| `get_repo_status` | Git status across all repos |
| `rescan_memory` | Re-run the Roslyn scanner (supports incremental) |
| `rescan_project` | Rescan a single project by name |
| `generate_agents_md` | Auto-generate AGENTS.md for the codebase |
| `check_version_alignment` | Detect NuGet version mismatches (MAJOR/MINOR) |
| `find_untested_types` | Find production types without test classes |
| `get_project_summary` | Natural-language project summary with role analysis |
| `get_type_summary` | Natural-language type summary with complexity assessment |
| `detect_patterns` | Auto-detect architecture patterns (Repository, CQRS, Factory, etc.) |

## Project Structure

```
RepoMind.slnx
Directory.Build.props               # Shared net10.0 settings

src/
  RepoMind.Mcp/                     # MCP server
  RepoMind.Scanner/                  # Roslyn scanner (library)
  RepoMind.Scanner.Cli/              # Scanner CLI entry point

tests/
  RepoMind.Mcp.Tests/               # xUnit tests (99 tests)
  RepoMind.Scanner.Tests/           # xUnit tests (18 tests)

memory/                              # Generated scanner output
```

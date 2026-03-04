# Migration Analyzer — Agent Runbook

> **Purpose**: This document is designed to be fed directly to an AI coding agent (Claude Sonnet 4, Cursor, etc.) so it can understand, build, and run this solution autonomously on a Mac with .NET 8.

---

## What This Tool Does

This is a **Roslyn-based .NET Migration Analyzer** that scans a .NET solution and produces reports identifying Windows-specific code, configurations, and dependencies that would block migration from **Windows VMs → Linux ECS containers**. It runs **24 analyzers** covering:

- Windows APIs, P/Invoke, COM Interop, Platform Detection
- File system paths, environment variables
- Authentication (LDAP, Windows Auth, Integrated Security, CyberArk)
- Cryptography (DPAPI, X509Store, certificates)
- IIS compatibility (System.Web, HttpContext.Current, web.config)
- WCF services, Windows Services, Container Readiness
- MSMQ, Named Pipes, Synchronization primitives
- Data access (OLEDB, ODBC, SQL Express, EF6)
- Distributed transactions (TransactionScope, MSDTC)
- Configuration (ConfigurationManager, Machine.config)
- NuGet packages, Quartz.NET jobs, scheduled tasks
- Logging (EventLog, ETW, log4net/NLog Windows targets)

Reports are generated in **HTML** (interactive dashboard), **Excel** (multi-sheet), **JSON** (CI/CD), and **Markdown** formats. Reports include a section showing **every check that was performed**, even those with 0 findings, so you can confirm nothing was missed.

---

## Prerequisites

- **.NET 8 SDK** (or later) installed — verify with `dotnet --version`
- **MSBuild** is bundled with .NET SDK, no separate install needed
- macOS, Linux, or Windows

---

## Solution Structure

```
MigrationAnalyzer.sln
├── MigrationAnalyzer.Core/           # Models, interfaces (IMigrationAnalyzer)
├── MigrationAnalyzer.Analyzers/      # 24 Roslyn-based analyzers
├── MigrationAnalyzer.Reports/        # HTML, Excel, JSON, Markdown generators
├── MigrationAnalyzer.CLI/            # CLI entry point (Program.cs)
├── MigrationAnalyzer.Tests/          # xUnit tests
└── SampleProject/LegacySolution.sln  # Sample legacy app for testing
```

---

## How to Build

```bash
cd /Users/pranavpandey/migrationtool
dotnet build MigrationAnalyzer.sln
```

Expected: `Build succeeded` with some NuGet vulnerability warnings (safe to ignore — they're Microsoft.Build runtime dependencies).

---

## How to Run

### Against the included sample project

```bash
dotnet run --project MigrationAnalyzer.CLI -- \
  --solution SampleProject/LegacySolution.sln \
  --output ./reports \
  --format all \
  --verbose
```

### Against any target solution

```bash
dotnet run --project MigrationAnalyzer.CLI -- \
  --solution /absolute/path/to/YourSolution.sln \
  --output ./reports \
  --format all \
  --verbose
```

### CLI Options Reference

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--solution` | `-s` | Path to .sln file **(required)** | — |
| `--output` | `-o` | Output directory for reports | `./reports` |
| `--format` | `-f` | Report formats: `html`, `excel`, `json`, `markdown`, `all` | `all` |
| `--severity` | — | Minimum severity: `critical`, `high`, `medium`, `low`, `info` | `info` |
| `--exclude` | — | Exclude file patterns (comma-separated regex) | — |
| `--verbose` | `-v` | Detailed console logging | `false` |
| `--rules` | — | Custom rules JSON file path | — |

### Common usage patterns

```bash
# Only critical/high issues, exclude test projects
dotnet run --project MigrationAnalyzer.CLI -- \
  -s /path/to/Solution.sln \
  --severity high \
  --exclude ".*Tests.*,.*\.Test" \
  --format html,markdown

# JSON only for CI/CD pipeline
dotnet run --project MigrationAnalyzer.CLI -- \
  -s /path/to/Solution.sln \
  --format json \
  --output ./build/reports
```

---

## How to View Reports

```bash
# Open interactive HTML dashboard
open reports/migration-report.html

# Read markdown summary
cat reports/migration-summary.md

# Parse JSON
cat reports/migration-report.json | python3 -m json.tool

# Excel report
open reports/migration-report.xlsx
```

---

## How to Run Tests

```bash
dotnet test MigrationAnalyzer.Tests
```

> **Note**: 3 tests in `CryptographyAnalyzerTests` are known to fail due to test setup issues. All other tests pass.

---

## Exit Codes (CI/CD)

| Code | Meaning |
|------|---------|
| `0` | No critical or high issues |
| `1` | Critical issues found (migration blockers) |
| `2` | High issues found (significant changes needed) |

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| `No MSBuild instances found` | Install .NET SDK: `brew install dotnet-sdk` |
| `Solution file not found` | Use absolute path to .sln file |
| Workspace warnings during analysis | Normal — MSBuild emits warnings for missing packages. Use `--verbose` to see details |
| `ClosedXML` package error | Run `dotnet restore MigrationAnalyzer.sln` first |
| Reports directory doesn't exist | It's auto-created. Check `--output` path is writable |

---

## Key Files to Understand

| File | Purpose |
|------|---------|
| `MigrationAnalyzer.Core/Models.cs` | `DiagnosticFinding`, `AnalyzerCheckSummary`, `AnalysisResult`, severity/category enums |
| `MigrationAnalyzer.CLI/Program.cs` | Entry point — registers all 24 analyzers, orchestrates analysis, generates reports |
| `MigrationAnalyzer.Analyzers/*.cs` | Individual analyzer implementations (one per file) |
| `MigrationAnalyzer.Reports/ReportGenerators.cs` | All 4 report generators (HTML, Excel, JSON, Markdown) |

---

## Adding a New Analyzer

1. Create `MigrationAnalyzer.Analyzers/MyAnalyzer.cs` implementing `IMigrationAnalyzer`
2. Register it in `MigrationAnalyzer.CLI/Program.cs` in the `analyzers` list
3. Add tests in `MigrationAnalyzer.Tests/MyAnalyzerTests.cs`
4. Build and run

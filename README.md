# .NET Migration Analyzer

A comprehensive .NET 8 console application that analyzes enterprise .NET codebases to assess migration readiness from Windows VMs to Linux ECS containers on AWS. Built using Roslyn analyzers for deep code inspection.

## 🎯 Overview

This tool performs automated analysis of .NET solutions to identify Windows-specific dependencies, configuration issues, and potential migration blockers when moving from Windows-hosted applications to Linux containers.

### Key Capabilities

- **Windows API Detection**: Identifies usage of Windows-specific namespaces (`Microsoft.Win32`, `System.Management`, `System.ServiceProcess`)
- **P/Invoke Analysis**: Detects native Windows DLL calls that won't work on Linux
- **File System Analysis**: Flags hardcoded Windows paths, case-sensitivity issues, and path separator problems
- **Authentication Patterns**: Detects Windows Authentication, NTLM, Kerberos, and Integrated Security
- **Configuration Audit**: Analyzes web.config/app.config for IIS-specific settings
- **Package Assessment**: Reviews NuGet packages for platform-specific dependencies
- **Application-Specific**: Specialized analyzers for Quartz.NET, CyberArk, and LDAP integrations
- **Comprehensive Reporting**: Generates HTML, Excel, JSON, and Markdown reports with effort estimates

---

## 🏗️ Architecture

The solution follows a modular, extensible architecture:

```
MigrationAnalyzer/
├── MigrationAnalyzer.Core/          # Core models and interfaces
│   ├── Models.cs                    # AnalysisResult, DiagnosticFinding
│   └── Interfaces/IMigrationAnalyzer
├── MigrationAnalyzer.Analyzers/     # Roslyn-based analyzers
│   ├── WindowsApiAnalyzer.cs        # Windows API detection
│   ├── PInvokeAnalyzer.cs           # P/Invoke detection
│   ├── FileSystemAnalyzer.cs        # Path and file system issues
│   ├── AuthenticationAnalyzer.cs    # Auth patterns (Windows/LDAP)
│   ├── ConfigurationAnalyzer.cs     # Config file analysis
│   ├── PackageAnalyzer.cs           # NuGet package assessment
│   ├── QuartzAnalyzer.cs            # Quartz.NET configuration
│   └── CyberArkAnalyzer.cs          # CyberArk integration
├── MigrationAnalyzer.Reports/       # Report generators
│   ├── HtmlReportGenerator.cs       # Interactive HTML with charts
│   ├── ExcelReportGenerator.cs      # Multi-sheet Excel workbook
│   ├── JsonReportGenerator.cs       # Machine-readable JSON
│   └── MarkdownReportGenerator.cs   # Executive summary
├── MigrationAnalyzer.CLI/           # Command-line interface
│   └── Program.cs                   # Entry point with options
└── MigrationAnalyzer.Tests/         # Unit tests
    └── WindowsApiAnalyzerTests.cs
```

### Design Patterns

- **Strategy Pattern**: Each analyzer implements `IMigrationAnalyzer` interface
- **Visitor Pattern**: Uses Roslyn's `CSharpSyntaxWalker` for AST traversal
- **Builder Pattern**: Report generators build complex output formats
- **Template Method**: Base analyzer interface with customizable implementations

---

## 📋 Prerequisites

- **.NET 8.0 SDK** or later
- **MSBuild** (included with Visual Studio or .NET SDK)
- Windows, macOS, or Linux development environment

---

## 🚀 Installation & Usage

### Build the Solution

```bash
dotnet build MigrationAnalyzer.sln
```

### Run Analysis

**Basic usage:**
```bash
dotnet run --project MigrationAnalyzer.CLI -- --solution /path/to/YourSolution.sln
```

**With options:**
```bash
dotnet run --project MigrationAnalyzer.CLI -- \
  --solution /path/to/YourSolution.sln \
  --output ./migration-reports \
  --format html,excel,markdown \
  --severity high \
  --verbose
```

### Command-Line Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--solution` | `-s` | Path to .sln file or directory | *Required* |
| `--output` | `-o` | Output directory for reports | `./reports` |
| `--format` | `-f` | Report formats: `html`, `excel`, `json`, `markdown`, `all` | `all` |
| `--severity` | | Minimum severity: `critical`, `high`, `medium`, `low`, `info` | `info` |
| `--exclude` | | Exclude paths (comma-separated regex patterns) | None |
| `--verbose` | `-v` | Enable detailed console logging | `false` |
| `--rules` | | Path to custom rules JSON file | None |

### Examples

**Analyze with only critical and high severity:**
```bash
dotnet run --project MigrationAnalyzer.CLI -- -s MyApp.sln --severity high
```

**Exclude test projects:**
```bash
dotnet run --project MigrationAnalyzer.CLI -- -s MyApp.sln --exclude ".*Test.*,.*\.Tests"
```

**Generate only HTML report:**
```bash
dotnet run --project MigrationAnalyzer.CLI -- -s MyApp.sln --format html
```

---

## 📊 Reports Generated

### 1. HTML Report (`migration-report.html`)

Interactive dashboard with:
- Executive summary with key metrics
- Pie/doughnut charts for severity and category distribution
- Sortable and filterable findings table
- Prioritized action plan with effort estimates
- Real-time search and filtering

**Features:**
- Click column headers to sort
- Use search box to filter findings
- Filter by severity or category dropdowns
- Color-coded severity indicators

### 2. Excel Report (`migration-report.xlsx`)

Multi-sheet workbook:
- **Summary Sheet**: Key metrics, severity breakdown, category analysis
- **Findings Sheet**: Complete findings with autofilter enabled
- **Package Inventory**: NuGet packages and versions
- **Configuration Audit**: Config file issues isolated

### 3. JSON Report (`migration-report.json`)

Machine-readable format for CI/CD integration:
```json
{
  "analysisDate": "2025-11-22T10:30:00",
  "solutionPath": "/path/to/solution.sln",
  "totalFilesScanned": 150,
  "findings": [ /* array of findings */ ]
}
```

### 4. Markdown Summary (`migration-summary.md`)

Executive summary with:
- High-level statistics
- Prioritized action plan
- Top issues by category
- Next steps recommendations

---

## 🔍 Analyzers in Detail

### WindowsApiAnalyzer (WIN001)

Detects usage of Windows-specific namespaces and types:
- `Microsoft.Win32.*` (Registry access)
- `System.Management.*` (WMI)
- `System.ServiceProcess.*` (Windows Services)
- `WindowsIdentity`, `WindowsPrincipal`, `EventLog`, etc.

**Example Finding:**
```
Severity: High
File: UserService.cs:45
Issue: Usage of Windows-specific namespace 'Microsoft.Win32' detected.
Recommendation: Replace with cross-platform alternatives (e.g., Microsoft.Extensions.Configuration)
```

### PInvokeAnalyzer (WIN002)

Identifies P/Invoke calls to native Windows DLLs:
- `kernel32.dll`, `user32.dll`, `advapi32.dll`, etc.

**Critical** because these will fail on Linux.

### FileSystemAnalyzer (FS001)

Flags file system issues:
- Hardcoded paths: `C:\Temp\`, `D:\Data\`
- UNC paths: `\\server\share`
- Backslash separators in string literals
- Case-sensitivity concerns

### AuthenticationAnalyzer (AUTH001)

Detects authentication patterns:
- Windows Authentication in `[Authorize]` attributes
- `Integrated Security=true` in connection strings
- `DirectoryEntry`, `DirectorySearcher` (LDAP)
- NTLM/Kerberos references

### ConfigurationAnalyzer (CFG001-CFG007)

Analyzes configuration files:
- `web.config` with IIS-specific settings
- `app.config` needing migration to `appsettings.json`
- Legacy target frameworks
- Windows-specific packages in `.csproj`

### PackageAnalyzer (PKG001-PKG004)

Reviews NuGet packages:
- Known problematic packages (e.g., `System.Management`)
- Packages with "Windows" in name
- Old versions needing updates
- Native library dependencies

### QuartzAnalyzer (QTZ001)

Checks Quartz.NET configuration:
- In-memory vs. persistent storage
- Clustering configuration for ECS
- Database connectivity requirements

### CyberArkAnalyzer (CYB001)

Analyzes CyberArk integration:
- Windows Credential Provider usage
- Hardcoded paths to CyberArk binaries
- AppID configuration patterns

---

## 🎯 Severity Levels & Effort Estimation

| Severity | Description | Effort (days) | Impact |
|----------|-------------|---------------|--------|
| **Critical** | Blocks migration completely | 5 | P/Invoke, COM, Windows Services |
| **High** | Requires significant code changes | 3 | Windows Auth, Registry, Event Log |
| **Medium** | Configuration/moderate changes | 1 | Paths, connection strings |
| **Low** | Best practice improvements | 0.5 | Case-sensitivity warnings |
| **Info** | Recommendations | 0.1 | Optimization suggestions |

**Total Effort** = Sum of (Finding Count × Effort per Severity)

---

## 🧪 Testing

Run unit tests:
```bash
dotnet test MigrationAnalyzer.Tests/
```

Run with coverage:
```bash
dotnet test MigrationAnalyzer.Tests/ --collect:"XPlat Code Coverage"
```

### Test Sample Project

A sample legacy project is included for testing:
```bash
dotnet run --project MigrationAnalyzer.CLI -- --solution SampleProject/LegacySolution.sln
```

---

## 🔧 Extending the Analyzer

### Adding a New Analyzer

1. **Create analyzer class** in `MigrationAnalyzer.Analyzers/`:

```csharp
public class MyCustomAnalyzer : IMigrationAnalyzer
{
    public string Id => "CUS001";
    public string Name => "My Custom Analyzer";
    public AnalyzerCategory Category => AnalyzerCategory.General;

    public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(
        Solution solution, CancellationToken ct)
    {
        var findings = new List<DiagnosticFinding>();
        
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                var root = await syntaxTree.GetRootAsync(ct);
                var semanticModel = await document.GetSemanticModelAsync(ct);
                
                var walker = new MyWalker(semanticModel, document.FilePath);
                walker.Visit(root);
                findings.AddRange(walker.Findings);
            }
        }
        
        return findings;
    }
    
    private class MyWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly string _filePath;
        public List<DiagnosticFinding> Findings { get; } = new();
        
        public MyWalker(SemanticModel model, string path)
        {
            _semanticModel = model;
            _filePath = path;
        }
        
        // Override Visit methods to detect patterns
        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            // Your custom logic here
            base.VisitMethodDeclaration(node);
        }
    }
}
```

2. **Register in Program.cs**:

```csharp
var analyzers = new List<IMigrationAnalyzer>
{
    new WindowsApiAnalyzer(),
    // ... other analyzers
    new MyCustomAnalyzer()  // Add your analyzer
};
```

3. **Write unit tests**:

```csharp
public class MyCustomAnalyzerTests : AnalyzerTestBase
{
    [Fact]
    public async Task Detects_MyPattern()
    {
        var source = @"/* test code */";
        var analyzer = new MyCustomAnalyzer();
        var findings = await RunAnalyzerAsync(analyzer, source);
        
        Assert.NotEmpty(findings);
    }
}
```

---

## 📈 Success Metrics

- **Performance**: Analyzes solutions with 50+ projects in under 5 minutes
- **Accuracy**: < 5% false positive rate
- **Coverage**: Detects 20+ categories of Windows-specific patterns
- **Actionability**: Every finding includes specific recommendations

---

## 🐛 Troubleshooting

### "No MSBuild instances found"
- Install .NET 8 SDK from https://dot.net

### Workspace load errors
- Ensure solution builds successfully before analysis
- Use `--verbose` flag to see detailed errors

### Missing findings
- Check `--severity` and `--exclude` filters
- Verify analyzers are registered in `Program.cs`

---

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Add/modify analyzers with unit tests
4. Submit pull request with clear description

---

## 📄 License

MIT License - See LICENSE file for details

---

## 🔗 Related Resources

- [.NET on Linux Documentation](https://docs.microsoft.com/dotnet/core/linux)
- [Roslyn API Documentation](https://docs.microsoft.com/dotnet/csharp/roslyn-sdk/)
- [AWS ECS Best Practices](https://docs.aws.amazon.com/ecs/)
- [ASP.NET Core Migration Guide](https://docs.microsoft.com/aspnet/core/migration/)

---

## 📞 Support

For issues, questions, or contributions, please use the GitHub Issues page.

---

**Built with ❤️ for enterprise .NET migrations**

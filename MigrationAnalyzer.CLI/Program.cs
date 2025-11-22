using CommandLine;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;
using MigrationAnalyzer.Analyzers;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;
using MigrationAnalyzer.Reports;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MigrationAnalyzer.CLI
{
    public class Options
    {
        [Option('s', "solution", Required = true, HelpText = "Path to the solution file (.sln) or directory")]
        public string SolutionPath { get; set; } = string.Empty;

        [Option('o', "output", Required = false, HelpText = "Output directory for reports", Default = "./reports")]
        public string OutputPath { get; set; } = "./reports";

        [Option('f', "format", Required = false, HelpText = "Report formats: html, excel, json, markdown, all", Default = "all")]
        public string Format { get; set; } = "all";

        [Option("severity", Required = false, HelpText = "Minimum severity to report: critical, high, medium, low, info", Default = "info")]
        public string MinimumSeverity { get; set; } = "info";

        [Option("exclude", Required = false, HelpText = "Exclude paths matching patterns (comma-separated)")]
        public string? ExcludePatterns { get; set; }

        [Option('v', "verbose", Required = false, HelpText = "Enable detailed console logging")]
        public bool Verbose { get; set; }

        [Option("rules", Required = false, HelpText = "Path to custom rules JSON file")]
        public string? CustomRulesPath { get; set; }
    }

    class Program
    {
        private static bool _verbose = false;
        private static Severity _minimumSeverity = Severity.Info;
        private static List<Regex> _excludePatterns = new();

        static async Task Main(string[] args)
        {
            Console.WriteLine("╔═══════════════════════════════════════════════╗");
            Console.WriteLine("║   .NET Migration Analyzer v1.0                ║");
            Console.WriteLine("║   Windows VM → Linux Container Assessment     ║");
            Console.WriteLine("╚═══════════════════════════════════════════════╝");
            Console.WriteLine();

            // Register MSBuild
            if (!MSBuildLocator.IsRegistered)
            {
                var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
                if (instances.Length == 0)
                {
                    Console.WriteLine("❌ No MSBuild instances found. Please install .NET SDK.");
                    return;
                }
                MSBuildLocator.RegisterInstance(instances.OrderByDescending(x => x.Version).First());
            }

            await Parser.Default.ParseArguments<Options>(args)
                .WithParsedAsync(RunAnalysisAsync);
        }

        static async Task RunAnalysisAsync(Options opts)
        {
            _verbose = opts.Verbose;
            
            // Parse minimum severity
            if (!Enum.TryParse<Severity>(opts.MinimumSeverity, true, out _minimumSeverity))
            {
                Console.WriteLine($"❌ Invalid severity level: {opts.MinimumSeverity}");
                return;
            }

            // Parse exclude patterns
            if (!string.IsNullOrEmpty(opts.ExcludePatterns))
            {
                foreach (var pattern in opts.ExcludePatterns.Split(','))
                {
                    try
                    {
                        _excludePatterns.Add(new Regex(pattern.Trim(), RegexOptions.IgnoreCase));
                        LogVerbose($"Excluding pattern: {pattern.Trim()}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Invalid exclude pattern '{pattern}': {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"📂 Analyzing: {opts.SolutionPath}");
            Console.WriteLine($"📊 Minimum severity: {_minimumSeverity}");
            Console.WriteLine();
            
            var sw = Stopwatch.StartNew();

            if (!File.Exists(opts.SolutionPath))
            {
                Console.WriteLine($"❌ Solution file not found at {opts.SolutionPath}");
                return;
            }

            Console.WriteLine("🔄 Loading solution...");
            using var workspace = MSBuildWorkspace.Create();
            workspace.WorkspaceFailed += (sender, e) =>
            {
                if (_verbose)
                    Console.WriteLine($"⚠️  Workspace: {e.Diagnostic.Message}");
            };

            var solution = await workspace.OpenSolutionAsync(opts.SolutionPath);
            Console.WriteLine($"✓ Loaded {solution.Projects.Count()} projects");
            Console.WriteLine();

            var analyzers = new List<IMigrationAnalyzer>
            {
                new WindowsApiAnalyzer(),
                new PInvokeAnalyzer(),
                new FileSystemAnalyzer(),
                new AuthenticationAnalyzer(),
                new ConfigurationAnalyzer(),
                new PackageAnalyzer(),
                new QuartzAnalyzer(),
                new CyberArkAnalyzer()
            };

            var allFindings = new List<DiagnosticFinding>();
            var severityCountsLive = new Dictionary<Severity, int>();
            
            foreach (var analyzer in analyzers)
            {
                Console.Write($"🔍 Running {analyzer.Name}... ");
                var analyzerSw = Stopwatch.StartNew();
                
                try
                {
                    var findings = await analyzer.AnalyzeAsync(solution, CancellationToken.None);
                    var filteredFindings = FilterFindings(findings.ToList());
                    allFindings.AddRange(filteredFindings);
                    
                    analyzerSw.Stop();
                    Console.WriteLine($"✓ ({filteredFindings.Count} findings in {analyzerSw.ElapsedMilliseconds}ms)");
                    
                    // Update live counts
                    foreach (var finding in filteredFindings)
                    {
                        severityCountsLive[finding.Severity] = severityCountsLive.GetValueOrDefault(finding.Severity, 0) + 1;
                    }
                    
                    LogVerbose($"  Details: {string.Join(", ", filteredFindings.GroupBy(f => f.Severity).Select(g => $"{g.Key}={g.Count()}"))}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed: {ex.Message}");
                    LogVerbose($"  Stack: {ex.StackTrace}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════");
            Console.WriteLine("📊 Analysis Summary");
            Console.WriteLine("═══════════════════════════════════════════════");
            
            var result = new AnalysisResult
            {
                AnalysisDate = DateTime.Now,
                SolutionPath = opts.SolutionPath,
                Findings = allFindings,
                Duration = sw.Elapsed,
                TotalFilesScanned = solution.Projects.Sum(p => p.Documents.Count())
            };

            var severityCounts = result.GetSeverityCounts();
            Console.WriteLine($"Total Findings:     {allFindings.Count}");
            Console.WriteLine();
            
            foreach (var severity in Enum.GetValues<Severity>())
            {
                var count = severityCounts.GetValueOrDefault(severity, 0);
                if (count > 0)
                {
                    var icon = severity switch
                    {
                        Severity.Critical => "🔴",
                        Severity.High => "🟠",
                        Severity.Medium => "🟡",
                        Severity.Low => "🟢",
                        Severity.Info => "🔵",
                        _ => "⚪"
                    };
                    Console.WriteLine($"  {icon} {severity,-10}: {count,4}");
                }
            }
            
            Console.WriteLine();
            Console.WriteLine($"Estimated Effort:   {result.CalculateEffortDays():F1} developer-days");
            Console.WriteLine($"Analysis Duration:  {sw.Elapsed.TotalSeconds:F2} seconds");
            Console.WriteLine("═══════════════════════════════════════════════");
            Console.WriteLine();

            // Generate Reports
            Console.WriteLine("📝 Generating reports...");
            Directory.CreateDirectory(opts.OutputPath);
            
            var formats = opts.Format.ToLower().Split(',').Select(f => f.Trim()).ToList();
            
            if (formats.Contains("json") || formats.Contains("all"))
            {
                Console.Write("  - JSON report... ");
                new JsonReportGenerator().Generate(result, opts.OutputPath);
                Console.WriteLine("✓");
            }
            
            if (formats.Contains("html") || formats.Contains("all"))
            {
                Console.Write("  - HTML report... ");
                new HtmlReportGenerator().Generate(result, opts.OutputPath);
                Console.WriteLine("✓");
            }
            
            if (formats.Contains("excel") || formats.Contains("all"))
            {
                Console.Write("  - Excel report... ");
                new ExcelReportGenerator().Generate(result, opts.OutputPath);
                Console.WriteLine("✓");
            }

            if (formats.Contains("markdown") || formats.Contains("all"))
            {
                Console.Write("  - Markdown summary... ");
                new MarkdownReportGenerator().Generate(result, opts.OutputPath);
                Console.WriteLine("✓");
            }

            Console.WriteLine();
            Console.WriteLine($"✅ Reports generated in: {Path.GetFullPath(opts.OutputPath)}");
            Console.WriteLine();
            
            if (severityCounts.GetValueOrDefault(Severity.Critical, 0) > 0)
            {
                Console.WriteLine("⚠️  WARNING: Critical issues found! These will block Linux migration.");
            }
        }

        private static List<DiagnosticFinding> FilterFindings(List<DiagnosticFinding> findings)
        {
            return findings.Where(f =>
            {
                // Filter by severity
                if (f.Severity > _minimumSeverity)
                    return false;

                // Filter by exclude patterns
                if (_excludePatterns.Any(pattern => pattern.IsMatch(f.FilePath)))
                {
                    LogVerbose($"Excluded: {f.FilePath}");
                    return false;
                }

                return true;
            }).ToList();
        }

        private static void LogVerbose(string message)
        {
            if (_verbose)
                Console.WriteLine(message);
        }
    }
}

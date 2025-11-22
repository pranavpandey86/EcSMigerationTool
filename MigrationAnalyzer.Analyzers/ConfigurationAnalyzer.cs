using Microsoft.CodeAnalysis;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;
using System.Xml.Linq;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Analyzes configuration files (web.config, app.config) and project files for migration issues
    /// </summary>
    public class ConfigurationAnalyzer : IMigrationAnalyzer
    {
        public string Id => "CFG001";
        public string Name => "Configuration File Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Configuration;

        public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            foreach (var project in solution.Projects)
            {
                // Analyze project file
                var projectFindings = await AnalyzeProjectFile(project, ct);
                findings.AddRange(projectFindings);

                // Look for config files in project directory
                var projectDir = Path.GetDirectoryName(project.FilePath);
                if (!string.IsNullOrEmpty(projectDir))
                {
                    var webConfig = Path.Combine(projectDir, "web.config");
                    if (File.Exists(webConfig))
                    {
                        findings.AddRange(AnalyzeWebConfig(webConfig));
                    }

                    var appConfig = Path.Combine(projectDir, "app.config");
                    if (File.Exists(appConfig))
                    {
                        findings.AddRange(AnalyzeAppConfig(appConfig));
                    }
                }
            }

            return findings;
        }

        private async Task<IEnumerable<DiagnosticFinding>> AnalyzeProjectFile(Project project, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            if (string.IsNullOrEmpty(project.FilePath) || !File.Exists(project.FilePath))
                return findings;

            try
            {
                var doc = XDocument.Load(project.FilePath);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                // Check OutputType
                var outputType = doc.Descendants(ns + "OutputType").FirstOrDefault()?.Value;
                if (outputType == "WinExe")
                {
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = project.FilePath,
                        LineNumber = 1,
                        Severity = Severity.High,
                        Message = "Project OutputType is 'WinExe' (Windows GUI application).",
                        Recommendation = "Change OutputType to 'Exe' for console apps or ensure GUI is cross-platform (e.g., Avalonia, MAUI).",
                        Category = AnalyzerCategory.Configuration,
                        RuleId = "CFG001"
                    });
                }

                // Check TargetFramework
                var targetFramework = doc.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value;
                if (!string.IsNullOrEmpty(targetFramework) && !targetFramework.StartsWith("net") && !targetFramework.StartsWith("netstandard"))
                {
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = project.FilePath,
                        LineNumber = 1,
                        Severity = Severity.Critical,
                        Message = $"Legacy target framework detected: '{targetFramework}'",
                        Recommendation = "Upgrade to .NET 8 (net8.0) for Linux container support.",
                        Category = AnalyzerCategory.Configuration,
                        RuleId = "CFG001"
                    });
                }

                // Check for platform-specific packages
                var packageReferences = doc.Descendants(ns + "PackageReference");
                foreach (var pkgRef in packageReferences)
                {
                    var packageId = pkgRef.Attribute("Include")?.Value;
                    var version = pkgRef.Attribute("Version")?.Value;

                    if (!string.IsNullOrEmpty(packageId))
                    {
                        // Check for Windows-specific packages
                        if (packageId.Contains("Win32", StringComparison.OrdinalIgnoreCase) ||
                            packageId.Contains("Windows", StringComparison.OrdinalIgnoreCase))
                        {
                            findings.Add(new DiagnosticFinding
                            {
                                FilePath = project.FilePath,
                                LineNumber = 1,
                                Severity = Severity.High,
                                Message = $"Windows-specific package detected: '{packageId}' version '{version}'",
                                Recommendation = "Review package documentation for Linux compatibility or find cross-platform alternatives.",
                                Category = AnalyzerCategory.Dependencies,
                                RuleId = "CFG002"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                findings.Add(new DiagnosticFinding
                {
                    FilePath = project.FilePath,
                    LineNumber = 1,
                    Severity = Severity.Info,
                    Message = $"Could not analyze project file: {ex.Message}",
                    Recommendation = "Manually review project file for Windows-specific settings.",
                    Category = AnalyzerCategory.Configuration,
                    RuleId = "CFG001"
                });
            }

            return findings;
        }

        private IEnumerable<DiagnosticFinding> AnalyzeWebConfig(string filePath)
        {
            var findings = new List<DiagnosticFinding>();

            try
            {
                var doc = XDocument.Load(filePath);

                findings.Add(new DiagnosticFinding
                {
                    FilePath = filePath,
                    LineNumber = 1,
                    Severity = Severity.High,
                    Message = "web.config file detected. IIS-specific configuration.",
                    Recommendation = "Migrate settings to appsettings.json and use Kestrel as the web server. Remove system.webServer sections.",
                    Category = AnalyzerCategory.Configuration,
                    RuleId = "CFG003"
                });

                // Check authentication mode
                var authMode = doc.Descendants("authentication").FirstOrDefault()?.Attribute("mode")?.Value;
                if (authMode == "Windows")
                {
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = filePath,
                        LineNumber = 1,
                        Severity = Severity.Critical,
                        Message = "Windows authentication mode configured in web.config.",
                        Recommendation = "Switch to Forms, JWT, or OAuth2 authentication compatible with Linux containers.",
                        Category = AnalyzerCategory.Security,
                        RuleId = "CFG004"
                    });
                }

                // Check connection strings
                var connStrings = doc.Descendants("connectionStrings").Descendants("add");
                foreach (var conn in connStrings)
                {
                    var connString = conn.Attribute("connectionString")?.Value ?? "";
                    if (connString.Contains("Integrated Security", StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new DiagnosticFinding
                        {
                            FilePath = filePath,
                            LineNumber = 1,
                            Severity = Severity.High,
                            Message = $"Connection string with Integrated Security: '{conn.Attribute("name")?.Value}'",
                            Recommendation = "Replace with SQL authentication using environment variables or secrets manager.",
                            Category = AnalyzerCategory.Configuration,
                            RuleId = "CFG005"
                        });
                    }
                }

                // Check for system.webServer sections
                if (doc.Descendants("system.webServer").Any())
                {
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = filePath,
                        LineNumber = 1,
                        Severity = Severity.Medium,
                        Message = "IIS-specific system.webServer configuration detected.",
                        Recommendation = "Remove IIS-specific settings. Configure middleware in Startup.cs/Program.cs instead.",
                        Category = AnalyzerCategory.Configuration,
                        RuleId = "CFG006"
                    });
                }
            }
            catch (Exception ex)
            {
                findings.Add(new DiagnosticFinding
                {
                    FilePath = filePath,
                    LineNumber = 1,
                    Severity = Severity.Info,
                    Message = $"Could not analyze web.config: {ex.Message}",
                    Recommendation = "Manually review configuration file.",
                    Category = AnalyzerCategory.Configuration,
                    RuleId = "CFG003"
                });
            }

            return findings;
        }

        private IEnumerable<DiagnosticFinding> AnalyzeAppConfig(string filePath)
        {
            var findings = new List<DiagnosticFinding>();

            findings.Add(new DiagnosticFinding
            {
                FilePath = filePath,
                LineNumber = 1,
                Severity = Severity.Medium,
                Message = "app.config file detected.",
                Recommendation = "Migrate settings to appsettings.json for .NET 8 compatibility.",
                Category = AnalyzerCategory.Configuration,
                RuleId = "CFG007"
            });

            return findings;
        }
    }
}

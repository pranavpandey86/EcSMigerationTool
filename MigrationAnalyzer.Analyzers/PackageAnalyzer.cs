using Microsoft.CodeAnalysis;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;
using System.Xml.Linq;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Analyzes NuGet packages for platform-specific dependencies and compatibility issues
    /// </summary>
    public class PackageAnalyzer : IMigrationAnalyzer
    {
        public string Id => "PKG001";
        public string Name => "NuGet Package Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Dependencies;

        // Known problematic or Windows-specific packages
        private static readonly Dictionary<string, string> ProblematicPackages = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.Win32.Registry", "Windows-specific registry access" },
            { "System.Management", "WMI is Windows-only" },
            { "System.ServiceProcess.ServiceController", "Windows Services only" },
            { "System.DirectoryServices", "Use Novell.Directory.Ldap.NETStandard instead" },
            { "System.DirectoryServices.AccountManagement", "Use Novell.Directory.Ldap.NETStandard instead" },
            { "System.Drawing", "Consider SkiaSharp or ImageSharp for cross-platform graphics" },
            { "System.Drawing.Common", "Limited Linux support; use SkiaSharp or ImageSharp" }
        };

        public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            foreach (var project in solution.Projects)
            {
                if (string.IsNullOrEmpty(project.FilePath) || !File.Exists(project.FilePath))
                    continue;

                try
                {
                    var doc = XDocument.Load(project.FilePath);
                    var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                    var packageReferences = doc.Descendants(ns + "PackageReference");
                    
                    foreach (var pkgRef in packageReferences)
                    {
                        var packageId = pkgRef.Attribute("Include")?.Value;
                        var version = pkgRef.Attribute("Version")?.Value;

                        if (string.IsNullOrEmpty(packageId))
                            continue;

                        // Check against known problematic packages
                        if (ProblematicPackages.TryGetValue(packageId, out var issue))
                        {
                            findings.Add(new DiagnosticFinding
                            {
                                FilePath = project.FilePath,
                                LineNumber = 1,
                                Severity = Severity.High,
                                Message = $"Package '{packageId}' v{version}: {issue}",
                                Recommendation = ProblematicPackages[packageId],
                                Category = AnalyzerCategory.Dependencies,
                                RuleId = "PKG001"
                            });
                        }

                        // Flag packages with "Windows" in name
                        if (packageId.Contains("Windows", StringComparison.OrdinalIgnoreCase) &&
                            !ProblematicPackages.ContainsKey(packageId))
                        {
                            findings.Add(new DiagnosticFinding
                            {
                                FilePath = project.FilePath,
                                LineNumber = 1,
                                Severity = Severity.Medium,
                                Message = $"Potentially Windows-specific package: '{packageId}' v{version}",
                                Recommendation = "Review package documentation for Linux compatibility.",
                                Category = AnalyzerCategory.Dependencies,
                                RuleId = "PKG002"
                            });
                        }

                        // Check for very old versions (might indicate legacy code)
                        if (!string.IsNullOrEmpty(version) && Version.TryParse(version, out var v))
                        {
                            if (v.Major < 3)
                            {
                                findings.Add(new DiagnosticFinding
                                {
                                    FilePath = project.FilePath,
                                    LineNumber = 1,
                                    Severity = Severity.Low,
                                    Message = $"Old package version detected: '{packageId}' v{version}",
                                    Recommendation = "Consider updating to latest stable version for better cross-platform support.",
                                    Category = AnalyzerCategory.Dependencies,
                                    RuleId = "PKG003"
                                });
                            }
                        }
                    }

                    // Also check for project references to Windows-specific projects
                    var projectReferences = doc.Descendants(ns + "ProjectReference");
                    foreach (var projRef in projectReferences)
                    {
                        var includePath = projRef.Attribute("Include")?.Value;
                        if (!string.IsNullOrEmpty(includePath) && 
                            (includePath.Contains("Windows", StringComparison.OrdinalIgnoreCase) ||
                             includePath.Contains("Win32", StringComparison.OrdinalIgnoreCase)))
                        {
                            findings.Add(new DiagnosticFinding
                            {
                                FilePath = project.FilePath,
                                LineNumber = 1,
                                Severity = Severity.Medium,
                                Message = $"Reference to potentially Windows-specific project: '{Path.GetFileName(includePath)}'",
                                Recommendation = "Review referenced project for Windows dependencies.",
                                Category = AnalyzerCategory.Dependencies,
                                RuleId = "PKG004"
                            });
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
                        Message = $"Could not analyze packages: {ex.Message}",
                        Recommendation = "Manually review package references.",
                        Category = AnalyzerCategory.Dependencies,
                        RuleId = "PKG001"
                    });
                }
            }

            return findings;
        }
    }
}

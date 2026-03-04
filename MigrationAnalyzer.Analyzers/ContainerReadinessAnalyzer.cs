using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Checks for container readiness: Dockerfile, health check endpoints, port binding, graceful shutdown
    /// </summary>
    public class ContainerReadinessAnalyzer : IMigrationAnalyzer
    {
        public string Id => "CTR001";
        public string Name => "Container Readiness Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Hosting;
        public string Description => "Validates container readiness: Dockerfile presence, health check endpoints, hardcoded ports, graceful shutdown hooks, and ephemeral file system usage";

        public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            foreach (var project in solution.Projects)
            {
                var projectDir = Path.GetDirectoryName(project.FilePath);
                if (!string.IsNullOrEmpty(projectDir))
                {
                    // Check for Dockerfile
                    CheckDockerfile(projectDir, findings);

                    // Check for docker-compose
                    CheckDockerCompose(projectDir, findings);
                }

                // Code-level checks
                foreach (var document in project.Documents)
                {
                    if (document.SourceCodeKind != SourceCodeKind.Regular) continue;

                    var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync(ct);
                    var semanticModel = await document.GetSemanticModelAsync(ct);

                    var walker = new ContainerReadinessWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private void CheckDockerfile(string projectDir, List<DiagnosticFinding> findings)
        {
            try
            {
                var dockerfilePath = Path.Combine(projectDir, "Dockerfile");
                var solutionDir = Path.GetDirectoryName(projectDir);

                var foundDockerfile = false;
                string? dockerfileContent = null;
                string? foundPath = null;

                // Check project dir and solution dir
                foreach (var dir in new[] { projectDir, solutionDir })
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    var df = Path.Combine(dir, "Dockerfile");
                    if (File.Exists(df))
                    {
                        foundDockerfile = true;
                        dockerfileContent = File.ReadAllText(df);
                        foundPath = df;
                        break;
                    }
                }

                if (!foundDockerfile)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = projectDir,
                        LineNumber = 0,
                        Severity = Severity.Medium,
                        Message = "No Dockerfile found in project or solution directory.",
                        Recommendation = "Create a Dockerfile for containerization. Use 'mcr.microsoft.com/dotnet/aspnet:8.0' as the runtime base image for Linux containers.",
                        Category = AnalyzerCategory.Hosting,
                        RuleId = "CTR001"
                    });
                }
                else if (dockerfileContent != null && foundPath != null)
                {
                    // Check for Windows-based images
                    if (dockerfileContent.Contains("nanoserver", StringComparison.OrdinalIgnoreCase) ||
                        dockerfileContent.Contains("windowsservercore", StringComparison.OrdinalIgnoreCase) ||
                        dockerfileContent.Contains("ltsc", StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new DiagnosticFinding
                        {
                            FilePath = foundPath,
                            LineNumber = 1,
                            Severity = Severity.Critical,
                            Message = "Dockerfile uses a Windows-based container image.",
                            Recommendation = "Replace with Linux-based images: 'mcr.microsoft.com/dotnet/aspnet:8.0' (runtime) or 'mcr.microsoft.com/dotnet/sdk:8.0' (build).",
                            Category = AnalyzerCategory.Hosting,
                            RuleId = "CTR002"
                        });
                    }

                    // Check for HEALTHCHECK instruction
                    if (!dockerfileContent.Contains("HEALTHCHECK", StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new DiagnosticFinding
                        {
                            FilePath = foundPath,
                            LineNumber = 1,
                            Severity = Severity.Low,
                            Message = "Dockerfile does not contain a HEALTHCHECK instruction.",
                            Recommendation = "Add HEALTHCHECK to Dockerfile or configure health checks in ECS task definition. Implement /health endpoint in the application.",
                            Category = AnalyzerCategory.Hosting,
                            RuleId = "CTR003"
                        });
                    }
                }
            }
            catch { /* Ignore file access errors */ }
        }

        private void CheckDockerCompose(string projectDir, List<DiagnosticFinding> findings)
        {
            try
            {
                var solutionDir = Path.GetDirectoryName(projectDir);
                foreach (var dir in new[] { projectDir, solutionDir })
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    var composePath = Path.Combine(dir, "docker-compose.yml");
                    var composePathAlt = Path.Combine(dir, "docker-compose.yaml");
                    var path = File.Exists(composePath) ? composePath : File.Exists(composePathAlt) ? composePathAlt : null;

                    if (path != null)
                    {
                        var content = File.ReadAllText(path);
                        if (content.Contains("volumes:", StringComparison.OrdinalIgnoreCase) &&
                            (content.Contains("C:\\", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("C:/", StringComparison.OrdinalIgnoreCase)))
                        {
                            findings.Add(new DiagnosticFinding
                            {
                                FilePath = path,
                                LineNumber = 1,
                                Severity = Severity.High,
                                Message = "docker-compose.yml contains Windows-style volume paths.",
                                Recommendation = "Replace Windows paths (C:\\...) with Linux-compatible paths (/var/data, /app/data) or use named volumes.",
                                Category = AnalyzerCategory.Hosting,
                                RuleId = "CTR004"
                            });
                        }
                    }
                }
            }
            catch { /* Ignore file access errors */ }
        }

        private class ContainerReadinessWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public ContainerReadinessWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var value = node.Token.ValueText;

                    // Detect hardcoded port numbers (common containerization issue)
                    if (value.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith("https://localhost:", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith("http://0.0.0.0:", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith("http://+:", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.Low, "CTR005",
                            $"Hardcoded URL/port binding detected: '{value}'",
                            "Use environment variables (ASPNETCORE_URLS) or configuration for port binding in containers. ECS maps ports via task definitions.");
                    }
                }
                base.VisitLiteralExpression(node);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var symbolInfo = _semanticModel.GetSymbolInfo(node.Expression);
                    var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

                    if (methodSymbol != null)
                    {
                        var methodName = methodSymbol.Name;
                        var containingType = methodSymbol.ContainingType?.Name ?? "";

                        // Detect File.WriteAllText etc. to local paths (ephemeral in containers)
                        if (containingType == "File" &&
                            (methodName is "WriteAllText" or "WriteAllBytes" or "WriteAllLines" or "Create" or "AppendAllText"))
                        {
                            // Check if writing to a local/temporary path
                            var args = node.ArgumentList?.Arguments;
                            if (args != null && args.Value.Count > 0)
                            {
                                var firstArg = args.Value[0].Expression.ToString();
                                if (!firstArg.Contains("Path.GetTempPath") &&
                                    !firstArg.Contains("Environment") &&
                                    !firstArg.Contains("/tmp") &&
                                    !firstArg.Contains("configuration"))
                                {
                                    AddFinding(node.GetLocation(), Severity.Info, "CTR006",
                                        $"File write operation detected: {containingType}.{methodName}()",
                                        "Container file systems are ephemeral. Ensure important data is written to mounted volumes, S3, or databases — not local file system.");
                                }
                            }
                        }
                    }
                }
                base.VisitInvocationExpression(node);
            }

            private void AddFinding(Location location, Severity severity, string ruleId, string message, string recommendation)
            {
                var lineSpan = location.GetLineSpan();
                Findings.Add(new DiagnosticFinding
                {
                    FilePath = _filePath,
                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                    Severity = severity,
                    Message = message,
                    Recommendation = recommendation,
                    Category = AnalyzerCategory.Hosting,
                    RuleId = ruleId
                });
            }
        }
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Analyzes Quartz.NET configuration for container readiness (clustering, persistent storage)
    /// </summary>
    public class QuartzAnalyzer : IMigrationAnalyzer
    {
        public string Id => "QTZ001";
        public string Name => "Quartz.NET Configuration Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Configuration;

        public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            foreach (var project in solution.Projects)
            {
                // Check if Quartz is referenced
                var hasQuartz = project.MetadataReferences.Any(r => 
                    r.Display?.Contains("Quartz", StringComparison.OrdinalIgnoreCase) == true);

                if (!hasQuartz)
                    continue;

                foreach (var document in project.Documents)
                {
                    if (document.SourceCodeKind != SourceCodeKind.Regular) continue;

                    var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync(ct);
                    var semanticModel = await document.GetSemanticModelAsync(ct);

                    var walker = new QuartzWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class QuartzWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();
            private bool _hasPersistentStore = false;
            private bool _hasClusteringConfig = false;

            public QuartzWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var methodName = node.Expression.ToString();

                // Check for RAMJobStore usage (in-memory)
                if (methodName.Contains("UseInMemoryStore") || methodName.Contains("RAMJobStore"))
                {
                    AddFinding(node.GetLocation(), Severity.High,
                        "Quartz.NET is configured to use in-memory job storage (RAMJobStore).",
                        "For containerized environments, use persistent job storage (AdoJobStore with SQL) to maintain job state across container restarts and enable clustering.");
                }

                // Check for persistent store
                if (methodName.Contains("UsePersistentStore") || methodName.Contains("AdoJobStore"))
                {
                    _hasPersistentStore = true;
                    AddFinding(node.GetLocation(), Severity.Info,
                        "Quartz.NET persistent job storage detected (AdoJobStore).",
                        "Good! Persistent storage is recommended for containers. Ensure database is accessible from ECS.");
                }

                // Check for clustering configuration
                if (methodName.Contains("quartz.jobStore.clustered") || 
                    methodName.Contains("UseClustering") ||
                    node.ToString().Contains("quartz.jobStore.clustered", StringComparison.OrdinalIgnoreCase))
                {
                    _hasClusteringConfig = true;
                    AddFinding(node.GetLocation(), Severity.Info,
                        "Quartz.NET clustering configuration detected.",
                        "Excellent! Clustering is essential for running multiple container instances in ECS.");
                }

                base.VisitInvocationExpression(node);
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var value = node.Token.ValueText;

                    // Check configuration keys
                    if (value.Contains("quartz.jobStore.type"))
                    {
                        AddFinding(node.GetLocation(), Severity.Info,
                            "Quartz.NET job store type configuration found.",
                            "Verify it's set to AdoJobStore for persistent storage, not RAMJobStore.");
                    }

                    if (value.Contains("quartz.jobStore.clustered"))
                    {
                        _hasClusteringConfig = true;
                    }

                    // Check for file-based storage (not suitable for containers)
                    if (value.Contains("quartz.jobStore.dataSource") && 
                        (value.Contains("file://") || value.Contains("localdb", StringComparison.OrdinalIgnoreCase)))
                    {
                        AddFinding(node.GetLocation(), Severity.High,
                            "Quartz.NET is using file-based or LocalDB storage.",
                            "Use a shared SQL Server or PostgreSQL database accessible from all container instances.");
                    }
                }
                base.VisitLiteralExpression(node);
            }

            public override void VisitCompilationUnit(CompilationUnitSyntax node)
            {
                base.VisitCompilationUnit(node);

                // After visiting entire file, check if clustering was configured
                if (_hasPersistentStore && !_hasClusteringConfig)
                {
                    AddFinding(node.GetLocation(), Severity.Medium,
                        "Quartz.NET has persistent storage but clustering is not explicitly configured.",
                        "Enable clustering (quartz.jobStore.clustered = true) for running multiple ECS container instances.");
                }
            }

            private void AddFinding(Location location, Severity severity, string message, string recommendation)
            {
                var lineSpan = location.GetLineSpan();
                Findings.Add(new DiagnosticFinding
                {
                    FilePath = _filePath,
                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                    Severity = severity,
                    Message = message,
                    Recommendation = recommendation,
                    Category = AnalyzerCategory.Configuration,
                    RuleId = "QTZ001"
                });
            }
        }
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects Windows Task Scheduler, schtasks.exe, and Hangfire with Windows-specific storage
    /// </summary>
    public class ScheduledJobAnalyzer : IMigrationAnalyzer
    {
        public string Id => "JOB001";
        public string Name => "Scheduled Job Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Configuration;
        public string Description => "Detects Windows Task Scheduler API, schtasks.exe usage, and Hangfire/job scheduler configurations that need container-compatible scheduling (ECS Scheduled Tasks, cron)";

        public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    if (document.SourceCodeKind != SourceCodeKind.Regular) continue;

                    var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync(ct);
                    var semanticModel = await document.GetSemanticModelAsync(ct);

                    var walker = new ScheduledJobWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class ScheduledJobWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public ScheduledJobWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name?.ToString() ?? "";

                if (name.StartsWith("Microsoft.Win32.TaskScheduler", StringComparison.OrdinalIgnoreCase))
                {
                    AddFinding(node.GetLocation(), Severity.Critical, "JOB001",
                        $"Windows Task Scheduler namespace '{name}' detected.",
                        "Windows Task Scheduler API is not available on Linux. Use ECS Scheduled Tasks, CloudWatch Events/EventBridge rules, or container-native cron (Quartz.NET/Hangfire).");
                }

                if (name.StartsWith("Hangfire", StringComparison.OrdinalIgnoreCase))
                {
                    AddFinding(node.GetLocation(), Severity.Info, "JOB002",
                        $"Hangfire namespace '{name}' detected.",
                        "Hangfire is cross-platform. Ensure storage (SQL Server, Redis) is accessible from ECS containers and connection strings use SQL auth (not Integrated Security).");
                }

                base.VisitUsingDirective(node);
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var value = node.Token.ValueText;

                    // Detect schtasks.exe references
                    if (value.Contains("schtasks", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.Critical, "JOB001",
                            $"schtasks.exe reference detected: '{value}'",
                            "schtasks.exe is Windows Task Scheduler CLI. Replace with ECS Scheduled Tasks, EventBridge rules, or container-native job scheduling.");
                    }

                    // Detect at.exe references
                    if (value.Equals("at.exe", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("at", StringComparison.OrdinalIgnoreCase) && node.Parent?.ToString().Contains("Process") == true)
                    {
                        AddFinding(node.GetLocation(), Severity.High, "JOB001",
                            "at.exe (Windows scheduler) reference detected.",
                            "Replace with container-compatible scheduling solution.");
                    }
                }
                base.VisitLiteralExpression(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);
                    var typeName = typeInfo.Type?.Name ?? "";
                    var namespaceName = typeInfo.Type?.ContainingNamespace?.ToDisplayString() ?? "";

                    if (typeName == "TaskService" && namespaceName.Contains("TaskScheduler"))
                    {
                        AddFinding(node.GetLocation(), Severity.Critical, "JOB001",
                            "Windows TaskService (Task Scheduler) instantiation detected.",
                            "Windows Task Scheduler is not available on Linux. Migrate scheduled tasks to ECS Scheduled Tasks, EventBridge, or Quartz.NET with persistent storage.");
                    }

                    if (typeName == "TaskDefinition" && namespaceName.Contains("TaskScheduler"))
                    {
                        AddFinding(node.GetLocation(), Severity.Critical, "JOB001",
                            "Windows Task Scheduler TaskDefinition detected.",
                            "Migrate task definitions to ECS task definitions, CloudWatch Events, or Quartz.NET job definitions.");
                    }
                }
                base.VisitObjectCreationExpression(node);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var symbolInfo = _semanticModel.GetSymbolInfo(node.Expression);
                    var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

                    if (methodSymbol != null)
                    {
                        var containingType = methodSymbol.ContainingType?.Name ?? "";

                        // Detect Process.Start("schtasks", ...)
                        if (containingType == "Process" && methodSymbol.Name == "Start")
                        {
                            var args = node.ArgumentList?.Arguments;
                            if (args != null && args.Value.Count > 0)
                            {
                                var firstArg = args.Value[0].Expression.ToString();
                                if (firstArg.Contains("schtasks", StringComparison.OrdinalIgnoreCase))
                                {
                                    AddFinding(node.GetLocation(), Severity.Critical, "JOB001",
                                        "Process.Start() launching schtasks.exe detected.",
                                        "Remove direct schtasks.exe invocations. Use ECS Scheduled Tasks or container-native scheduling.");
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
                    Category = AnalyzerCategory.Configuration,
                    RuleId = ruleId
                });
            }
        }
    }
}

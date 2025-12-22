using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects MSMQ (Microsoft Message Queuing) usage which is Windows-only
    /// </summary>
    public class MSMQAnalyzer : IMigrationAnalyzer
    {
        public string Id => "MSMQ001";
        public string Name => "MSMQ Detector";
        public AnalyzerCategory Category => AnalyzerCategory.WindowsApi;

        private static readonly HashSet<string> MSMQNamespaces = new()
        {
            "System.Messaging",
            "Experimental.System.Messaging"
        };

        private static readonly HashSet<string> MSMQTypes = new()
        {
            "MessageQueue",
            "Message",
            "MessageQueueTransaction",
            "MessageQueueTransactionType",
            "MessageEnumerator",
            "MessageQueueCriteria"
        };

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

                    var walker = new MSMQWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class MSMQWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public MSMQWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name?.ToString() ?? "";
                if (MSMQNamespaces.Any(ns => name.StartsWith(ns, StringComparison.OrdinalIgnoreCase)))
                {
                    AddFinding(node.GetLocation(), Severity.Critical, "MSMQ001",
                        $"MSMQ namespace '{name}' detected.",
                        "MSMQ (Microsoft Message Queuing) is Windows-only. Migrate to cross-platform message brokers like RabbitMQ, Amazon SQS, or Azure Service Bus.");
                }
                base.VisitUsingDirective(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);
                    var typeName = typeInfo.Type?.Name;

                    if (typeName == "MessageQueue")
                    {
                        AddFinding(node.GetLocation(), Severity.Critical, "MSMQ002",
                            "MessageQueue instantiation detected - MSMQ is Windows-only.",
                            "Replace MSMQ with RabbitMQ (using RabbitMQ.Client NuGet), Amazon SQS, or Azure Service Bus for cross-platform messaging.");
                    }
                }
                base.VisitObjectCreationExpression(node);
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (_semanticModel != null)
                {
                    var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                    if (symbol != null)
                    {
                        var typeName = symbol.ContainingType?.Name ?? symbol.Name;
                        var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? "";

                        if (MSMQTypes.Contains(typeName) && namespaceName.Contains("System.Messaging"))
                        {
                            AddFinding(node.GetLocation(), Severity.Critical, "MSMQ001",
                                $"MSMQ type '{typeName}' detected.",
                                "MSMQ is Windows-only. Migrate to cross-platform message brokers (RabbitMQ, SQS, Kafka).");
                        }
                    }
                }
                base.VisitIdentifierName(node);
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var value = node.Token.ValueText;

                    // Detect MSMQ queue paths
                    if (value.StartsWith(@".\private$\", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith(@".\public$\", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith("FormatName:DIRECT=", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains(@"\private$\", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.Critical, "MSMQ003",
                            $"MSMQ queue path detected: '{value}'",
                            "MSMQ queue paths are Windows-specific. Replace with connection strings for cross-platform brokers.");
                    }
                }
                base.VisitLiteralExpression(node);
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
                    Category = AnalyzerCategory.WindowsApi,
                    RuleId = ruleId
                });
            }
        }
    }
}

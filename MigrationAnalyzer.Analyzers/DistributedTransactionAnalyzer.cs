using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects distributed transaction patterns (TransactionScope, MSDTC) that won't work on Linux
    /// </summary>
    public class DistributedTransactionAnalyzer : IMigrationAnalyzer
    {
        public string Id => "DTC001";
        public string Name => "Distributed Transaction Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Transactions;
        public string Description => "Detects distributed transactions (TransactionScope, MSDTC, two-phase commit) that rely on Windows DTC service unavailable in Linux containers";

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

                    var walker = new TransactionWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class TransactionWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public TransactionWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name?.ToString() ?? "";
                if (name == "System.Transactions" || name.StartsWith("System.Transactions."))
                {
                    AddFinding(node.GetLocation(), Severity.High, "DTC001",
                        $"System.Transactions namespace '{name}' detected.",
                        "TransactionScope with distributed transactions requires MSDTC (Windows-only). Use local transactions or implement the Saga pattern for cross-service transactions.");
                }

                if (name == "System.EnterpriseServices" || name.StartsWith("System.EnterpriseServices."))
                {
                    AddFinding(node.GetLocation(), Severity.Critical, "DTC002",
                        $"Enterprise Services (COM+) namespace '{name}' detected.",
                        "System.EnterpriseServices (COM+ transactions) is Windows-only. Migrate to local database transactions or event-driven eventual consistency.");
                }

                base.VisitUsingDirective(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);
                    var typeName = typeInfo.Type?.Name ?? "";

                    if (typeName == "TransactionScope")
                    {
                        // Check for TransactionScopeOption.Required — implies distributed transactions
                        var args = node.ArgumentList?.Arguments;
                        var isDistributed = false;

                        if (args != null)
                        {
                            foreach (var arg in args)
                            {
                                var argText = arg.ToString();
                                if (argText.Contains("TransactionScopeOption.Required") ||
                                    argText.Contains("TransactionScopeOption.RequiresNew"))
                                {
                                    isDistributed = true;
                                }
                            }
                        }

                        AddFinding(node.GetLocation(),
                            isDistributed ? Severity.High : Severity.Medium,
                            "DTC001",
                            $"TransactionScope instantiation detected{(isDistributed ? " with distributed transaction option" : "")}.",
                            "TransactionScope may escalate to MSDTC for cross-database operations. MSDTC is not available on Linux. Use local transactions per database or implement Saga/Outbox pattern.");
                    }

                    if (typeName == "CommittableTransaction")
                    {
                        AddFinding(node.GetLocation(), Severity.High, "DTC001",
                            "CommittableTransaction usage detected — explicit transaction management.",
                            "CommittableTransaction with distributed resources requires MSDTC. Use local database transactions instead.");
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

                        if (typeName == "TransactionManager" && namespaceName.Contains("System.Transactions"))
                        {
                            AddFinding(node.GetLocation(), Severity.High, "DTC001",
                                "TransactionManager usage detected — distributed transaction management.",
                                "TransactionManager coordinates distributed transactions via MSDTC. Not available on Linux containers.");
                        }
                    }
                }
                base.VisitIdentifierName(node);
            }

            public override void VisitAttribute(AttributeSyntax node)
            {
                var attributeName = node.Name.ToString();

                if (attributeName.Contains("Transaction") && attributeName.Contains("Attribute"))
                {
                    AddFinding(node.GetLocation(), Severity.High, "DTC002",
                        $"Transaction attribute [{attributeName}] detected (possibly COM+ / Enterprise Services).",
                        "COM+ transaction attributes are Windows-only. Migrate to standard database transactions or distributed transaction alternatives.");
                }

                base.VisitAttribute(node);
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
                    Category = AnalyzerCategory.Transactions,
                    RuleId = ruleId
                });
            }
        }
    }
}

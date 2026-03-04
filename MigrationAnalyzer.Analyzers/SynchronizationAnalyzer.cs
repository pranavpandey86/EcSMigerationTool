using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects cross-platform synchronization issues: Named Mutex/Semaphore with Global\ prefix
    /// </summary>
    public class SynchronizationAnalyzer : IMigrationAnalyzer
    {
        public string Id => "SYNC001";
        public string Name => "Synchronization Primitives Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.IPC;
        public string Description => "Detects cross-platform synchronization issues: Named Mutex/Semaphore with Windows-specific 'Global\\' prefix, cross-process locking patterns that differ on Linux";

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

                    var walker = new SynchronizationWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class SynchronizationWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public SynchronizationWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);
                    var typeName = typeInfo.Type?.Name ?? "";

                    if (typeName == "Mutex")
                    {
                        var args = node.ArgumentList?.Arguments;
                        bool hasName = false;
                        bool hasGlobalPrefix = false;

                        if (args != null)
                        {
                            foreach (var arg in args)
                            {
                                var argText = arg.Expression.ToString();
                                if (arg.Expression is LiteralExpressionSyntax literal &&
                                    literal.IsKind(SyntaxKind.StringLiteralExpression))
                                {
                                    hasName = true;
                                    if (literal.Token.ValueText.StartsWith(@"Global\", StringComparison.OrdinalIgnoreCase))
                                    {
                                        hasGlobalPrefix = true;
                                    }
                                }
                                else if (argText.Contains("Global\\"))
                                {
                                    hasName = true;
                                    hasGlobalPrefix = true;
                                }
                            }
                        }

                        if (hasGlobalPrefix)
                        {
                            AddFinding(node.GetLocation(), Severity.High, "SYNC001",
                                "Named Mutex with 'Global\\' prefix detected — Windows-specific naming.",
                                "The 'Global\\' prefix is Windows-specific (for cross-session visibility). On Linux, named mutexes use different naming. Remove the 'Global\\' prefix for cross-platform compatibility.");
                        }
                        else if (hasName)
                        {
                            AddFinding(node.GetLocation(), Severity.Medium, "SYNC001",
                                "Named Mutex detected — potential cross-platform naming concern.",
                                "Named Mutex behavior differs between Windows and Linux. Backslashes in names may cause issues on Linux. Test inter-process synchronization in containers.");
                        }
                    }

                    if (typeName == "Semaphore")
                    {
                        var args = node.ArgumentList?.Arguments;
                        if (args != null)
                        {
                            foreach (var arg in args)
                            {
                                if (arg.Expression is LiteralExpressionSyntax literal &&
                                    literal.IsKind(SyntaxKind.StringLiteralExpression))
                                {
                                    var value = literal.Token.ValueText;
                                    if (value.StartsWith(@"Global\", StringComparison.OrdinalIgnoreCase))
                                    {
                                        AddFinding(node.GetLocation(), Severity.High, "SYNC002",
                                            "Named Semaphore with 'Global\\' prefix detected — Windows-specific naming.",
                                            "The 'Global\\' prefix is Windows-specific. Remove for cross-platform compatibility. Use distributed locks (Redis, database) in container environments.");
                                    }
                                    else if (!string.IsNullOrEmpty(value))
                                    {
                                        AddFinding(node.GetLocation(), Severity.Medium, "SYNC002",
                                            "Named Semaphore detected — verify cross-platform naming compatibility.",
                                            "Named synchronization primitives behave differently on Linux. For cross-container coordination, use distributed locks (Redis, database advisory locks).");
                                    }
                                }
                            }
                        }
                    }

                    if (typeName == "EventWaitHandle")
                    {
                        var args = node.ArgumentList?.Arguments;
                        if (args != null)
                        {
                            foreach (var arg in args)
                            {
                                if (arg.Expression is LiteralExpressionSyntax literal &&
                                    literal.IsKind(SyntaxKind.StringLiteralExpression))
                                {
                                    var value = literal.Token.ValueText;
                                    if (!string.IsNullOrEmpty(value))
                                    {
                                        AddFinding(node.GetLocation(), Severity.Medium, "SYNC003",
                                            $"Named EventWaitHandle detected: '{value}'",
                                            "Named event handles work differently on Linux. In containers, each container has its own namespace — cross-container signaling requires distributed mechanisms.");
                                    }
                                }
                            }
                        }
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
                        var methodName = methodSymbol.Name;
                        var containingType = methodSymbol.ContainingType?.Name ?? "";

                        // Detect Mutex.OpenExisting / Semaphore.OpenExisting
                        if ((containingType == "Mutex" || containingType == "Semaphore" || containingType == "EventWaitHandle") &&
                            methodName == "OpenExisting")
                        {
                            AddFinding(node.GetLocation(), Severity.High, "SYNC001",
                                $"{containingType}.OpenExisting() detected — opens existing named synchronization primitive.",
                                "Named synchronization primitives are per-OS and per-container on Linux. In ECS, containers are isolated. Use distributed locking for cross-container coordination.");
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
                    Category = AnalyzerCategory.IPC,
                    RuleId = ruleId
                });
            }
        }
    }
}

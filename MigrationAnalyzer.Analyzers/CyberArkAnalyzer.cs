using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Analyzes CyberArk integration patterns for container compatibility
    /// </summary>
    public class CyberArkAnalyzer : IMigrationAnalyzer
    {
        public string Id => "CYB001";
        public string Name => "CyberArk Integration Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Security;

        private static readonly HashSet<string> CyberArkNamespaces = new()
        {
            "CyberArk",
            "PACyberArk",
            "CyberArk.AIM",
            "CyberArk.PasswordVault"
        };

        public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            foreach (var project in solution.Projects)
            {
                // Check if CyberArk is referenced
                var hasCyberArk = project.MetadataReferences.Any(r =>
                    r.Display?.Contains("CyberArk", StringComparison.OrdinalIgnoreCase) == true);

                if (!hasCyberArk)
                    continue;

                foreach (var document in project.Documents)
                {
                    if (document.SourceCodeKind != SourceCodeKind.Regular) continue;

                    var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync(ct);
                    var semanticModel = await document.GetSemanticModelAsync(ct);

                    var walker = new CyberArkWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class CyberArkWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public CyberArkWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name.ToString();
                if (CyberArkNamespaces.Any(ns => name.StartsWith(ns, StringComparison.OrdinalIgnoreCase)))
                {
                    AddFinding(node.GetLocation(), Severity.Medium,
                        $"CyberArk namespace '{name}' detected.",
                        "Verify CyberArk SDK/API is compatible with Linux containers. Prefer REST API over native Windows credential providers.");
                }
                base.VisitUsingDirective(node);
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (_semanticModel != null)
                {
                    var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                    if (symbol != null)
                    {
                        var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? "";
                        
                        // Check for Windows Credential Provider usage
                        if (namespaceName.Contains("CyberArk") && 
                            (symbol.Name.Contains("CredentialProvider") || 
                             symbol.Name.Contains("WindowsCredential")))
                        {
                            AddFinding(node.GetLocation(), Severity.High,
                                $"CyberArk Windows Credential Provider usage detected: '{symbol.Name}'",
                                "Replace with CyberArk REST API (Central Credential Provider) for cross-platform compatibility.");
                        }

                        // Check for AIM CLIPasswordSDK (command-line interface)
                        if (symbol.Name.Contains("CLIPasswordSDK") || symbol.Name.Contains("AIM"))
                        {
                            AddFinding(node.GetLocation(), Severity.Medium,
                                "CyberArk AIM CLI Password SDK detected.",
                                "Ensure AIM binary is available in container image or use REST API instead. Mount credentials securely.");
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

                    // Check for Windows paths to CyberArk binaries
                    if (value.Contains("CLIPasswordSDK", StringComparison.OrdinalIgnoreCase) &&
                        (value.Contains("C:\\") || value.Contains("\\\\")))
                    {
                        AddFinding(node.GetLocation(), Severity.High,
                            $"Hardcoded Windows path to CyberArk binary: '{value}'",
                            "Remove hardcoded paths. Use environment variables or mount CyberArk binaries in container at runtime.");
                    }

                    // Check for app ID configuration
                    if (value.StartsWith("AppID=", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.Info,
                            "CyberArk AppID configuration detected.",
                            "Ensure AppID is configured via environment variables or secrets, not hardcoded in source.");
                    }
                }
                base.VisitLiteralExpression(node);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var methodName = node.Expression.ToString();

                // Check for credential retrieval calls
                if (methodName.Contains("GetPassword") || 
                    methodName.Contains("RetrievePassword") ||
                    methodName.Contains("FetchCredential"))
                {
                    // Check if it's from CyberArk namespace
                    if (_semanticModel != null)
                    {
                        var symbolInfo = _semanticModel.GetSymbolInfo(node.Expression);
                        if (symbolInfo.Symbol != null)
                        {
                            var namespaceName = symbolInfo.Symbol.ContainingNamespace?.ToDisplayString() ?? "";
                            if (namespaceName.Contains("CyberArk", StringComparison.OrdinalIgnoreCase))
                            {
                                AddFinding(node.GetLocation(), Severity.Info,
                                    "CyberArk credential retrieval detected.",
                                    "Verify network connectivity from ECS containers to CyberArk vault. Configure proper security groups and IAM roles.");
                            }
                        }
                    }
                }

                base.VisitInvocationExpression(node);
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
                    Category = AnalyzerCategory.Security,
                    RuleId = "CYB001"
                });
            }
        }
    }
}

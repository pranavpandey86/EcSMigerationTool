using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects Windows Named Pipes usage which may have cross-platform compatibility issues
    /// </summary>
    public class NamedPipesAnalyzer : IMigrationAnalyzer
    {
        public string Id => "PIPE001";
        public string Name => "Named Pipes Detector";
        public AnalyzerCategory Category => AnalyzerCategory.WindowsApi;

        private static readonly HashSet<string> PipeTypes = new()
        {
            "NamedPipeServerStream",
            "NamedPipeClientStream",
            "AnonymousPipeServerStream",
            "AnonymousPipeClientStream",
            "PipeStream"
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

                    var walker = new NamedPipesWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class NamedPipesWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public NamedPipesWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name?.ToString() ?? "";
                if (name == "System.IO.Pipes" || name.StartsWith("System.IO.Pipes."))
                {
                    AddFinding(node.GetLocation(), Severity.High, "PIPE001",
                        $"Named Pipes namespace '{name}' detected.",
                        "Named Pipes behavior differs between Windows and Linux. Ensure pipe names work on Linux (no \\\\.\\pipe\\ prefix) and test IPC in containers.");
                }
                base.VisitUsingDirective(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);
                    var typeName = typeInfo.Type?.Name;

                    if (typeName == "NamedPipeServerStream" || typeName == "NamedPipeClientStream")
                    {
                        // Check for Windows-specific pipe options
                        var arguments = node.ArgumentList?.Arguments;
                        var hasWindowsSpecificOptions = false;

                        if (arguments != null)
                        {
                            foreach (var arg in arguments)
                            {
                                var argText = arg.ToString();
                                // Check for security-related or Windows-specific options
                                if (argText.Contains("PipeSecurity") ||
                                    argText.Contains("TokenImpersonationLevel") ||
                                    argText.Contains("HandleInheritability"))
                                {
                                    hasWindowsSpecificOptions = true;
                                    break;
                                }
                            }
                        }

                        var severity = hasWindowsSpecificOptions ? Severity.High : Severity.Medium;
                        var extraMessage = hasWindowsSpecificOptions ? " with Windows-specific security options" : "";

                        AddFinding(node.GetLocation(), severity, "PIPE002",
                            $"{typeName} instantiation detected{extraMessage}.",
                            "Named pipes work on Linux but with differences. Remove PipeSecurity (use filesystem permissions on Linux). Test pipe connectivity in containerized environment.");
                    }
                }
                base.VisitObjectCreationExpression(node);
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var value = node.Token.ValueText;

                    // Detect Windows-specific pipe path format
                    if (value.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith(@"\\.\Pipe\", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.High, "PIPE003",
                            $"Windows-specific pipe path detected: '{value}'",
                            "The \\\\.\\pipe\\ prefix is Windows-specific. On Linux, named pipes are created in the filesystem. Use just the pipe name without the prefix.");
                    }
                }
                base.VisitLiteralExpression(node);
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

                        if (PipeTypes.Contains(typeName) && namespaceName.Contains("System.IO.Pipes"))
                        {
                            // Only report once per unique location (avoid duplicates with object creation)
                            if (!(node.Parent is ObjectCreationExpressionSyntax))
                            {
                                AddFinding(node.GetLocation(), Severity.Medium, "PIPE001",
                                    $"Named Pipes type '{typeName}' usage detected.",
                                    "Test Named Pipes IPC in Linux containers. Consider using Unix Domain Sockets or gRPC for better cross-platform support.");
                            }
                        }
                    }
                }
                base.VisitIdentifierName(node);
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

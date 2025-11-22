using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    public class WindowsApiAnalyzer : IMigrationAnalyzer
    {
        public string Id => "WIN001";
        public string Name => "Windows API Usage Detector";
        public AnalyzerCategory Category => AnalyzerCategory.WindowsApi;

        private static readonly HashSet<string> WindowsNamespaces = new()
        {
            "Microsoft.Win32",
            "System.Management",
            "System.ServiceProcess",
            "System.Diagnostics.EventLog",
            "System.DirectoryServices"
        };

        private static readonly HashSet<string> WindowsTypes = new()
        {
            "Registry", "RegistryKey", "EventLog", "ServiceController", "WindowsIdentity", "WindowsPrincipal"
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

                    var walker = new WindowsApiWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class WindowsApiWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public WindowsApiWalker(SemanticModel semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name.ToString();
                if (WindowsNamespaces.Any(ns => name.StartsWith(ns)))
                {
                    AddFinding(node.GetLocation(), Severity.High, 
                        $"Usage of Windows-specific namespace '{name}' detected.",
                        "Remove Windows-specific dependencies and use cross-platform alternatives (e.g., Microsoft.Extensions.Configuration instead of Registry).");
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
                        var typeName = symbol.ContainingType?.Name ?? "";
                        
                        // Check for Windows-specific types
                        if (WindowsTypes.Contains(typeName))
                        {
                            AddFinding(node.GetLocation(), Severity.High,
                                $"Usage of Windows-specific type '{typeName}' detected.",
                                "Replace with cross-platform alternatives. Use ClaimsIdentity for WindowsIdentity, configuration for Registry, and logging for EventLog.");
                        }

                        // Check namespace of the symbol
                        var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? "";
                        if (WindowsNamespaces.Any(ns => namespaceName.StartsWith(ns)))
                        {
                            AddFinding(node.GetLocation(), Severity.High,
                                $"Usage of type '{symbol.Name}' from Windows-specific namespace '{namespaceName}'.",
                                "Replace with cross-platform alternatives compatible with Linux containers.");
                        }
                    }
                }
                base.VisitIdentifierName(node);
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
                    Category = AnalyzerCategory.WindowsApi,
                    RuleId = "WIN001"
                });
            }
        }
    }
}

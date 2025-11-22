using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects COM Interop and ActiveX usage which are Windows-only technologies
    /// </summary>
    public class ComInteropAnalyzer : IMigrationAnalyzer
    {
        public string Id => "COM001";
        public string Name => "COM Interop Detector";
        public AnalyzerCategory Category => AnalyzerCategory.WindowsApi;

        private static readonly HashSet<string> ComNamespaces = new()
        {
            "System.Runtime.InteropServices.ComTypes",
            "System.EnterpriseServices"
        };

        private static readonly HashSet<string> ComAttributes = new()
        {
            "ComImport",
            "ComVisible",
            "ProgId",
            "ClassInterface",
            "InterfaceType",
            "ComSourceInterfaces",
            "DispId"
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

                    var walker = new ComInteropWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class ComInteropWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public ComInteropWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name.ToString();
                if (ComNamespaces.Any(ns => name.StartsWith(ns, StringComparison.OrdinalIgnoreCase)))
                {
                    AddFinding(node.GetLocation(), Severity.Critical, "COM001",
                        $"COM Interop namespace '{name}' detected.",
                        "COM is Windows-only. Replace with cross-platform alternatives or use gRPC/REST APIs for inter-process communication.");
                }
                base.VisitUsingDirective(node);
            }

            public override void VisitAttribute(AttributeSyntax node)
            {
                var attributeName = node.Name.ToString();
                
                // Remove "Attribute" suffix if present for matching
                var nameWithoutSuffix = attributeName.EndsWith("Attribute") 
                    ? attributeName.Substring(0, attributeName.Length - "Attribute".Length) 
                    : attributeName;

                if (ComAttributes.Contains(nameWithoutSuffix))
                {
                    var severity = nameWithoutSuffix == "ComImport" ? Severity.Critical : Severity.High;
                    var ruleId = nameWithoutSuffix == "ComImport" ? "COM001" : "COM002";
                    
                    AddFinding(node.GetLocation(), severity, ruleId,
                        $"COM attribute '[{nameWithoutSuffix}]' detected.",
                        "COM Interop attributes indicate Windows-specific COM dependencies. Remove COM usage or isolate in Windows-specific assemblies with runtime checks.");
                }
                base.VisitAttribute(node);
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

                        // Detect Type.GetTypeFromProgID()
                        if (containingType == "Type" && methodName == "GetTypeFromProgID")
                        {
                            AddFinding(node.GetLocation(), Severity.Critical, "COM003",
                                "Type.GetTypeFromProgID() call detected - creates COM objects via ProgID.",
                                "COM objects cannot be created on Linux. Replace with managed .NET libraries or use REST/gRPC APIs.");
                        }

                        // Detect Type.GetTypeFromCLSID()
                        if (containingType == "Type" && methodName == "GetTypeFromCLSID")
                        {
                            AddFinding(node.GetLocation(), Severity.Critical, "COM003",
                                "Type.GetTypeFromCLSID() call detected - creates COM objects via CLSID.",
                                "COM objects cannot be created on Linux. Replace with managed .NET libraries or use REST/gRPC APIs.");
                        }

                        // Detect Marshal.ReleaseComObject()
                        if (containingType == "Marshal" && methodName == "ReleaseComObject")
                        {
                            AddFinding(node.GetLocation(), Severity.High, "COM004",
                                "Marshal.ReleaseComObject() call detected - COM object lifetime management.",
                                "Indicates COM Interop usage. Remove COM dependencies for Linux compatibility.");
                        }

                        // Detect Marshal.GetActiveObject()
                        if (containingType == "Marshal" && methodName == "GetActiveObject")
                        {
                            AddFinding(node.GetLocation(), Severity.Critical, "COM003",
                                "Marshal.GetActiveObject() call detected - retrieves running COM object.",
                                "COM ROT (Running Object Table) is Windows-only. Use alternative IPC mechanisms.");
                        }

                        // Detect Activator.CreateInstance with COM types
                        if (containingType == "Activator" && methodName == "CreateInstance")
                        {
                            // Check if the argument is Type.GetTypeFromProgID or similar
                            var arguments = node.ArgumentList?.Arguments;
                            if (arguments != null && arguments.Value.Any())
                            {
                                var firstArg = arguments.Value.First().Expression;
                                if (firstArg is InvocationExpressionSyntax invocation)
                                {
                                    var argSymbolInfo = _semanticModel.GetSymbolInfo(invocation.Expression);
                                    var argMethodSymbol = argSymbolInfo.Symbol as IMethodSymbol;
                                    if (argMethodSymbol?.Name == "GetTypeFromProgID" || 
                                        argMethodSymbol?.Name == "GetTypeFromCLSID")
                                    {
                                        AddFinding(node.GetLocation(), Severity.Critical, "COM003",
                                            "Activator.CreateInstance() with COM type detected.",
                                            "Creating COM objects will fail on Linux. Replace with managed alternatives.");
                                    }
                                }
                            }
                        }
                    }
                }
                base.VisitInvocationExpression(node);
            }

            public override void VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                // Check if class has COM attributes or implements COM interfaces
                var hasComAttribute = node.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr =>
                    {
                        var name = attr.Name.ToString();
                        var nameWithoutSuffix = name.EndsWith("Attribute") 
                            ? name.Substring(0, name.Length - "Attribute".Length) 
                            : name;
                        return ComAttributes.Contains(nameWithoutSuffix);
                    });

                if (hasComAttribute && _semanticModel != null)
                {
                    AddFinding(node.Identifier.GetLocation(), Severity.High, "COM002",
                        $"Class '{node.Identifier.Text}' has COM attributes.",
                        "COM-enabled classes cannot be used on Linux. Isolate COM functionality or replace with cross-platform alternatives.");
                }

                base.VisitClassDeclaration(node);
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

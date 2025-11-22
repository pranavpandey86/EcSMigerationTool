using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects platform-specific code and conditional compilation that needs review for cross-platform compatibility
    /// </summary>
    public class PlatformDetectionAnalyzer : IMigrationAnalyzer
    {
        public string Id => "PLT001";
        public string Name => "Platform Detection Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.General;

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

                    var walker = new PlatformDetectionWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);

                    // Also check for preprocessor directives
                    CheckPreprocessorDirectives(syntaxTree, document.FilePath ?? document.Name, findings);
                }
            }

            return findings;
        }

        private void CheckPreprocessorDirectives(SyntaxTree syntaxTree, string filePath, List<DiagnosticFinding> findings)
        {
            var root = syntaxTree.GetRoot();
            var directives = root.DescendantTrivia()
                .Where(t => t.IsDirective)
                .Select(t => t.GetStructure())
                .OfType<IfDirectiveTriviaSyntax>();

            foreach (var directive in directives)
            {
                var condition = directive.Condition.ToString();
                
                // Check for Windows-specific conditional compilation
                if (condition.Contains("WINDOWS", StringComparison.OrdinalIgnoreCase) ||
                    condition.Contains("WIN32", StringComparison.OrdinalIgnoreCase) ||
                    condition.Contains("WIN64", StringComparison.OrdinalIgnoreCase))
                {
                    var lineSpan = directive.GetLocation().GetLineSpan();
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = filePath,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        Severity = Severity.Medium,
                        Message = $"Windows-specific preprocessor directive detected: '#if {condition}'",
                        Recommendation = "Review code within Windows-specific conditional blocks. Ensure Linux-compatible alternative code paths exist or migrate to runtime checks.",
                        Category = AnalyzerCategory.General,
                        RuleId = "PLT004"
                    });
                }
            }
        }

        private class PlatformDetectionWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public PlatformDetectionWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var nodeText = node.ToString();

                    // Detect Environment.OSVersion usage
                    if (nodeText.Contains("Environment.OSVersion") || 
                        nodeText == "Environment.OSVersion")
                    {
                        AddFinding(node.GetLocation(), Severity.Medium, "PLT001",
                            "Environment.OSVersion usage detected.",
                            "Environment.OSVersion returns different values on Windows vs Linux. Use RuntimeInformation.IsOSPlatform() for platform checks or avoid OS-specific logic.");
                    }

                    // Check for specific Platform property access
                    var symbolInfo = _semanticModel.GetSymbolInfo(node);
                    var symbol = symbolInfo.Symbol;

                    if (symbol != null)
                    {
                        var memberName = symbol.Name;
                        var containingType = symbol.ContainingType?.Name ?? "";

                        // Detect OSVersion.Platform
                        if (containingType == "OperatingSystem" && memberName == "Platform")
                        {
                            AddFinding(node.GetLocation(), Severity.Medium, "PLT001",
                                "OperatingSystem.Platform property access detected.",
                                "Platform checks should use RuntimeInformation.IsOSPlatform() for better cross-platform compatibility.");
                        }
                    }
                }
                base.VisitMemberAccessExpression(node);
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
                        var namespaceName = methodSymbol.ContainingNamespace?.ToDisplayString() ?? "";

                        // Detect RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        if (containingType == "RuntimeInformation" && methodName == "IsOSPlatform")
                        {
                            // Check if the argument is OSPlatform.Windows
                            var args = node.ArgumentList?.Arguments;
                            if (args != null && args.Value.Count > 0)
                            {
                                var argText = args.Value[0].ToString();
                                if (argText.Contains("OSPlatform.Windows"))
                                {
                                    AddFinding(node.GetLocation(), Severity.Info, "PLT002",
                                        "RuntimeInformation.IsOSPlatform(OSPlatform.Windows) detected.",
                                        "Good practice! Verify that Linux code path exists and is tested. Ensure this code branch has cross-platform alternatives.");
                                }
                            }
                        }

                        // Detect OperatingSystem.IsWindows() (.NET 5+)
                        if (containingType == "OperatingSystem" && methodName == "IsWindows")
                        {
                            AddFinding(node.GetLocation(), Severity.Info, "PLT002",
                                "OperatingSystem.IsWindows() detected (.NET 5+).",
                                "Good practice for platform detection! Ensure Linux code path exists and is tested.");
                        }

                        // Detect OperatingSystem.IsLinux()
                        if (containingType == "OperatingSystem" && methodName == "IsLinux")
                        {
                            AddFinding(node.GetLocation(), Severity.Info, "PLT003",
                                "OperatingSystem.IsLinux() detected - Linux-specific code path.",
                                "Excellent! Linux-specific code path exists. Ensure it's properly tested.");
                        }

                        // Detect other platform checks
                        if (containingType == "OperatingSystem" && 
                            (methodName == "IsMacOS" || methodName == "IsFreeBSD" || methodName == "IsAndroid"))
                        {
                            AddFinding(node.GetLocation(), Severity.Info, "PLT003",
                                $"OperatingSystem.{methodName}() detected - platform-specific code.",
                                "Platform-specific code path detected. Ensure all target platforms are handled.");
                        }
                    }
                }
                base.VisitInvocationExpression(node);
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (_semanticModel != null)
                {
                    var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                    if (symbol != null)
                    {
                        var symbolName = symbol.Name;

                        // Detect PlatformID enum usage
                        if (symbolName == "Win32NT" || symbolName == "Win32Windows" || 
                            symbolName == "Win32S" || symbolName == "WinCE")
                        {
                            // Check if it's from System.PlatformID
                            var typeInfo = _semanticModel.GetTypeInfo(node);
                            if (typeInfo.Type?.Name == "PlatformID")
                            {
                                AddFinding(node.GetLocation(), Severity.Medium, "PLT001",
                                    $"PlatformID.{symbolName} detected - Windows platform check.",
                                    "PlatformID is legacy approach. Use RuntimeInformation.IsOSPlatform() or OperatingSystem.IsWindows() instead.");
                            }
                        }

                        // Detect OSPlatform.Windows
                        if (symbolName == "Windows" || symbolName == "Linux" || symbolName == "OSX")
                        {
                            var typeInfo = _semanticModel.GetTypeInfo(node);
                            if (typeInfo.Type?.Name == "OSPlatform")
                            {
                                if (symbolName == "Windows")
                                {
                                    AddFinding(node.GetLocation(), Severity.Info, "PLT002",
                                        "OSPlatform.Windows detected in platform check.",
                                        "Platform detection found. Ensure cross-platform alternatives are implemented for Linux migration.");
                                }
                            }
                        }
                    }
                }
                base.VisitIdentifierName(node);
            }

            public override void VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                // Detect comparisons with PlatformID
                if (_semanticModel != null)
                {
                    var nodeText = node.ToString();
                    
                    // Check for Environment.OSVersion.Platform == PlatformID.Win32NT
                    if (nodeText.Contains("Environment.OSVersion.Platform") && 
                        (nodeText.Contains("Win32NT") || nodeText.Contains("Win32Windows")))
                    {
                        AddFinding(node.GetLocation(), Severity.Medium, "PLT001",
                            "Platform check using Environment.OSVersion.Platform == PlatformID.Win32NT detected.",
                            "Use RuntimeInformation.IsOSPlatform(OSPlatform.Windows) or OperatingSystem.IsWindows() for modern platform detection.");
                    }
                }
                base.VisitBinaryExpression(node);
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
                    Category = AnalyzerCategory.General,
                    RuleId = ruleId
                });
            }
        }
    }
}

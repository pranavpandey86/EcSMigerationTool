using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects Windows Service patterns (ServiceBase, Topshelf, InstallUtil) that need container migration
    /// </summary>
    public class WindowsServiceAnalyzer : IMigrationAnalyzer
    {
        public string Id => "SVC001";
        public string Name => "Windows Service Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Hosting;
        public string Description => "Detects Windows Service patterns (ServiceBase, Topshelf, InstallUtil, sc.exe) that must become IHostedService/BackgroundService for ECS containers";

        private static readonly HashSet<string> ServiceTypes = new()
        {
            "ServiceBase",
            "ServiceInstaller",
            "ServiceProcessInstaller",
            "ServiceController"
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

                    var walker = new WindowsServiceWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class WindowsServiceWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public WindowsServiceWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name?.ToString() ?? "";

                if (name == "System.ServiceProcess" || name.StartsWith("System.ServiceProcess."))
                {
                    AddFinding(node.GetLocation(), Severity.High, "SVC001",
                        $"Windows Service namespace '{name}' detected.",
                        "System.ServiceProcess is Windows-only. Migrate to IHostedService/BackgroundService for cross-platform long-running services in containers.");
                }

                if (name.StartsWith("Topshelf", StringComparison.OrdinalIgnoreCase))
                {
                    AddFinding(node.GetLocation(), Severity.High, "SVC002",
                        $"Topshelf namespace '{name}' detected — Windows Service hosting library.",
                        "Topshelf is designed for Windows Services. Migrate to .NET Generic Host with IHostedService for container deployment.");
                }

                base.VisitUsingDirective(node);
            }

            public override void VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                if (_semanticModel != null)
                {
                    var declaredSymbol = _semanticModel.GetDeclaredSymbol(node);
                    if (declaredSymbol != null)
                    {
                        var baseType = declaredSymbol.BaseType;
                        while (baseType != null)
                        {
                            if (baseType.Name == "ServiceBase" &&
                                (baseType.ContainingNamespace?.ToDisplayString() ?? "").Contains("System.ServiceProcess"))
                            {
                                AddFinding(node.Identifier.GetLocation(), Severity.Critical, "SVC001",
                                    $"Class '{node.Identifier.Text}' inherits from ServiceBase — Windows Service implementation.",
                                    "Refactor to inherit from BackgroundService or implement IHostedService. Use .NET Generic Host (Host.CreateDefaultBuilder) as the entry point instead of ServiceBase.Run().");
                            }

                            if (baseType.Name == "ServiceInstaller" || baseType.Name == "ServiceProcessInstaller")
                            {
                                AddFinding(node.Identifier.GetLocation(), Severity.High, "SVC003",
                                    $"Class '{node.Identifier.Text}' is a Windows Service installer ({baseType.Name}).",
                                    "Service installers are not needed for containers. Remove installer classes and use Dockerfile + ECS task definitions instead.");
                            }

                            baseType = baseType.BaseType;
                        }
                    }
                }
                base.VisitClassDeclaration(node);
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

                        // Detect ServiceBase.Run()
                        if (containingType == "ServiceBase" && methodName == "Run")
                        {
                            AddFinding(node.GetLocation(), Severity.Critical, "SVC001",
                                "ServiceBase.Run() call detected — Windows Service entry point.",
                                "Replace with Host.CreateDefaultBuilder().ConfigureServices(...).Build().RunAsync() for container-compatible hosting.");
                        }

                        // Detect Topshelf HostFactory
                        if (containingType == "HostFactory" && methodName == "Run")
                        {
                            AddFinding(node.GetLocation(), Severity.High, "SVC002",
                                "Topshelf HostFactory.Run() detected — Windows Service hosting.",
                                "Replace Topshelf with .NET Generic Host. Use Host.CreateDefaultBuilder() with AddHostedService<T>().");
                        }
                    }
                }

                // Check for sc.exe or installutil references in string literals
                var nodeText = node.ToString();
                if (nodeText.Contains("sc.exe", StringComparison.OrdinalIgnoreCase) ||
                    nodeText.Contains("installutil", StringComparison.OrdinalIgnoreCase))
                {
                    AddFinding(node.GetLocation(), Severity.High, "SVC003",
                        "Windows Service installation utility reference detected.",
                        "sc.exe and installutil are Windows-only. Use Docker/ECS for service deployment instead.");
                }

                base.VisitInvocationExpression(node);
            }

            public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var methodName = node.Identifier.Text;

                // Detect OnStart/OnStop overrides in ServiceBase subclass
                if (methodName is "OnStart" or "OnStop" or "OnPause" or "OnContinue")
                {
                    if (node.Modifiers.Any(SyntaxKind.OverrideKeyword))
                    {
                        AddFinding(node.GetLocation(), Severity.High, "SVC001",
                            $"Windows Service lifecycle method '{methodName}' override detected.",
                            $"Map '{methodName}' logic to IHostedService.StartAsync/StopAsync or BackgroundService.ExecuteAsync for container lifecycle management.");
                    }
                }

                base.VisitMethodDeclaration(node);
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
                    Category = AnalyzerCategory.Hosting,
                    RuleId = ruleId
                });
            }
        }
    }
}

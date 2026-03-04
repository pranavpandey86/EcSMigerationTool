using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects WCF (Windows Communication Foundation) usage including ServiceHost, bindings, and .svc files
    /// </summary>
    public class WCFAnalyzer : IMigrationAnalyzer
    {
        public string Id => "WCF001";
        public string Name => "WCF Service Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Hosting;
        public string Description => "Detects WCF services (ServiceHost, bindings, .svc files, ChannelFactory) that require migration to gRPC, REST APIs, or CoreWCF";

        private static readonly HashSet<string> WCFNamespaces = new()
        {
            "System.ServiceModel",
            "System.ServiceModel.Web",
            "System.ServiceModel.Channels",
            "System.ServiceModel.Description",
            "System.ServiceModel.Dispatcher",
            "System.ServiceModel.Security",
            "CoreWCF"
        };

        private static readonly HashSet<string> WCFBindings = new()
        {
            "BasicHttpBinding",
            "WSHttpBinding",
            "WSDualHttpBinding",
            "NetTcpBinding",
            "NetNamedPipeBinding",
            "NetMsmqBinding",
            "CustomBinding",
            "WebHttpBinding"
        };

        private static readonly HashSet<string> WCFTypes = new()
        {
            "ServiceHost",
            "ServiceHostBase",
            "ChannelFactory",
            "DuplexChannelFactory",
            "ServiceBehaviorAttribute",
            "OperationContractAttribute",
            "ServiceContractAttribute",
            "DataContractAttribute",
            "FaultContractAttribute",
            "ClientBase"
        };

        public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            foreach (var project in solution.Projects)
            {
                // Check for .svc files in project directory
                var projectDir = Path.GetDirectoryName(project.FilePath);
                if (!string.IsNullOrEmpty(projectDir))
                {
                    try
                    {
                        var svcFiles = Directory.GetFiles(projectDir, "*.svc", SearchOption.AllDirectories);
                        foreach (var svcFile in svcFiles)
                        {
                            findings.Add(new DiagnosticFinding
                            {
                                FilePath = svcFile,
                                LineNumber = 1,
                                Severity = Severity.Critical,
                                Message = $"WCF service file detected: '{Path.GetFileName(svcFile)}'",
                                Recommendation = "WCF .svc files are IIS-hosted services. Migrate to ASP.NET Core Web API (REST), gRPC, or CoreWCF for container compatibility.",
                                Category = AnalyzerCategory.Hosting,
                                RuleId = "WCF003"
                            });
                        }
                    }
                    catch { /* Directory access errors */ }
                }

                foreach (var document in project.Documents)
                {
                    if (document.SourceCodeKind != SourceCodeKind.Regular) continue;

                    var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync(ct);
                    var semanticModel = await document.GetSemanticModelAsync(ct);

                    var walker = new WCFWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class WCFWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public WCFWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name?.ToString() ?? "";
                if (WCFNamespaces.Any(ns => name.StartsWith(ns, StringComparison.OrdinalIgnoreCase)))
                {
                    var isCoreWcf = name.StartsWith("CoreWCF", StringComparison.OrdinalIgnoreCase);
                    AddFinding(node.GetLocation(),
                        isCoreWcf ? Severity.Info : Severity.High,
                        isCoreWcf ? "WCF001" : "WCF001",
                        isCoreWcf
                            ? $"CoreWCF namespace '{name}' detected — already using cross-platform WCF."
                            : $"WCF namespace '{name}' detected.",
                        isCoreWcf
                            ? "CoreWCF is the cross-platform WCF port. Verify it runs on Linux containers."
                            : "System.ServiceModel (WCF) has limited Linux support. Migrate to ASP.NET Core Web API (REST), gRPC, or CoreWCF.");
                }
                base.VisitUsingDirective(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);
                    var typeName = typeInfo.Type?.Name ?? "";

                    if (WCFBindings.Contains(typeName))
                    {
                        var severity = typeName is "NetNamedPipeBinding" or "NetMsmqBinding"
                            ? Severity.Critical
                            : Severity.High;

                        AddFinding(node.GetLocation(), severity, "WCF002",
                            $"WCF binding '{typeName}' instantiation detected.",
                            typeName switch
                            {
                                "NetNamedPipeBinding" => "NetNamedPipeBinding is Windows-only IPC. Replace with gRPC or Unix Domain Sockets for Linux.",
                                "NetMsmqBinding" => "NetMsmqBinding requires MSMQ (Windows-only). Replace with RabbitMQ, SQS, or Kafka.",
                                "NetTcpBinding" => "NetTcpBinding may work via CoreWCF on Linux. Consider gRPC as a modern alternative.",
                                _ => "Migrate WCF bindings to ASP.NET Core Web API or gRPC for cross-platform support."
                            });
                    }

                    if (typeName == "ServiceHost" || typeName == "ServiceHostBase")
                    {
                        AddFinding(node.GetLocation(), Severity.Critical, "WCF003",
                            $"WCF ServiceHost instantiation detected.",
                            "ServiceHost is the WCF hosting model. Migrate to ASP.NET Core Kestrel web server with Web API or gRPC endpoints.");
                    }
                }
                base.VisitObjectCreationExpression(node);
            }

            public override void VisitAttribute(AttributeSyntax node)
            {
                var attributeName = node.Name.ToString();

                if (attributeName.Contains("ServiceContract") || attributeName.Contains("OperationContract"))
                {
                    AddFinding(node.GetLocation(), Severity.High, "WCF001",
                        $"WCF attribute [{attributeName}] detected — WCF service/operation contract.",
                        "Migrate WCF contracts to ASP.NET Core controllers (REST) or gRPC proto definitions. Consider CoreWCF for direct migration.");
                }

                if (attributeName.Contains("ServiceBehavior"))
                {
                    AddFinding(node.GetLocation(), Severity.Medium, "WCF001",
                        $"WCF [{attributeName}] attribute detected.",
                        "WCF service behaviors need migration. Map concurrency, instancing, and transaction behaviors to ASP.NET Core equivalents.");
                }

                base.VisitAttribute(node);
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

                        if (namespaceName.StartsWith("System.ServiceModel") &&
                            WCFTypes.Contains(typeName) &&
                            !(node.Parent is ObjectCreationExpressionSyntax))
                        {
                            AddFinding(node.GetLocation(), Severity.High, "WCF001",
                                $"WCF type '{typeName}' usage detected.",
                                "Migrate WCF types to ASP.NET Core Web API, gRPC, or CoreWCF equivalents.");
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
                    Category = AnalyzerCategory.Hosting,
                    RuleId = ruleId
                });
            }
        }
    }
}

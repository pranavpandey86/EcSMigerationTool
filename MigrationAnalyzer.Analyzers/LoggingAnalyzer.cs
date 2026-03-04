using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects Windows-specific logging patterns: EventLog, ETW, log4net/NLog Windows targets
    /// </summary>
    public class LoggingAnalyzer : IMigrationAnalyzer
    {
        public string Id => "LOG001";
        public string Name => "Logging Framework Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Logging;
        public string Description => "Detects Windows-specific logging (EventLog.WriteEntry, ETW EventSource, log4net/NLog Windows targets, Trace listeners) that must migrate to stdout/CloudWatch for containers";

        public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            foreach (var project in solution.Projects)
            {
                // Check for log4net/NLog config in project directory
                var projectDir = Path.GetDirectoryName(project.FilePath);
                if (!string.IsNullOrEmpty(projectDir))
                {
                    CheckLoggingConfigs(projectDir, findings);
                }

                foreach (var document in project.Documents)
                {
                    if (document.SourceCodeKind != SourceCodeKind.Regular) continue;

                    var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync(ct);
                    var semanticModel = await document.GetSemanticModelAsync(ct);

                    var walker = new LoggingWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private void CheckLoggingConfigs(string projectDir, List<DiagnosticFinding> findings)
        {
            try
            {
                // Check for log4net config
                var log4netConfig = Path.Combine(projectDir, "log4net.config");
                if (File.Exists(log4netConfig))
                {
                    var content = File.ReadAllText(log4netConfig);
                    if (content.Contains("EventLogAppender", StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new DiagnosticFinding
                        {
                            FilePath = log4netConfig, LineNumber = 1, Severity = Severity.High,
                            Message = "log4net EventLogAppender configured — writes to Windows Event Log.",
                            Recommendation = "Replace EventLogAppender with ConsoleAppender or FileAppender. Use CloudWatch agent to collect logs from stdout in ECS.",
                            Category = AnalyzerCategory.Logging, RuleId = "LOG003"
                        });
                    }
                }

                // Check for NLog config
                var nlogConfig = Path.Combine(projectDir, "NLog.config");
                if (File.Exists(nlogConfig))
                {
                    var content = File.ReadAllText(nlogConfig);
                    if (content.Contains("EventLog", StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new DiagnosticFinding
                        {
                            FilePath = nlogConfig, LineNumber = 1, Severity = Severity.High,
                            Message = "NLog EventLog target configured — writes to Windows Event Log.",
                            Recommendation = "Replace EventLog target with Console or File target. Use CloudWatch agent to collect logs from stdout in ECS.",
                            Category = AnalyzerCategory.Logging, RuleId = "LOG003"
                        });
                    }
                }
            }
            catch { /* Ignore file access errors */ }
        }

        private class LoggingWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public LoggingWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name?.ToString() ?? "";

                if (name == "System.Diagnostics.Eventing" || name.StartsWith("System.Diagnostics.Eventing."))
                {
                    AddFinding(node.GetLocation(), Severity.High, "LOG002",
                        $"ETW (Event Tracing for Windows) namespace '{name}' detected.",
                        "ETW is Windows-only. Replace with structured logging (Serilog, NLog) writing to stdout for CloudWatch collection in ECS.");
                }

                if (name == "System.Diagnostics.Tracing" || name.StartsWith("System.Diagnostics.Tracing."))
                {
                    AddFinding(node.GetLocation(), Severity.Medium, "LOG002",
                        $"EventSource namespace '{name}' detected.",
                        "EventSource-based tracing works on Linux but may have different behavior. Verify listeners are configured for Linux. Consider OpenTelemetry.");
                }

                base.VisitUsingDirective(node);
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

                        // Detect EventLog.WriteEntry
                        if (containingType == "EventLog" && methodName == "WriteEntry")
                        {
                            AddFinding(node.GetLocation(), Severity.High, "LOG001",
                                "EventLog.WriteEntry() detected — writes to Windows Event Log.",
                                "Windows Event Log is not available in Linux containers. Replace with ILogger, Serilog, or NLog writing to stdout/stderr for CloudWatch.");
                        }

                        // Detect EventLog.CreateEventSource
                        if (containingType == "EventLog" && methodName == "CreateEventSource")
                        {
                            AddFinding(node.GetLocation(), Severity.High, "LOG001",
                                "EventLog.CreateEventSource() detected — registers Windows Event Source.",
                                "Event sources are Windows-only. Remove and replace with structured logging framework.");
                        }

                        // Detect Trace.Write / Debug.Write
                        if ((containingType == "Trace" || containingType == "Debug") &&
                            (methodName is "Write" or "WriteLine" or "TraceError" or "TraceWarning" or "TraceInformation"))
                        {
                            AddFinding(node.GetLocation(), Severity.Low, "LOG004",
                                $"{containingType}.{methodName}() detected — System.Diagnostics tracing.",
                                "Trace/Debug output may not be captured in containers. Use ILogger or a structured logging framework that writes to stdout.");
                        }
                    }
                }
                base.VisitInvocationExpression(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);
                    var typeName = typeInfo.Type?.Name ?? "";

                    if (typeName == "EventLog")
                    {
                        AddFinding(node.GetLocation(), Severity.High, "LOG001",
                            "EventLog instantiation detected.",
                            "Windows Event Log is not available on Linux. Use ILogger/Serilog/NLog writing to stdout for container log collection.");
                    }

                    if (typeName == "EventLogTraceListener")
                    {
                        AddFinding(node.GetLocation(), Severity.High, "LOG001",
                            "EventLogTraceListener detected — routes Trace output to Windows Event Log.",
                            "Replace with ConsoleTraceListener or use a structured logging framework for container compatibility.");
                    }
                }
                base.VisitObjectCreationExpression(node);
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
                    Category = AnalyzerCategory.Logging,
                    RuleId = ruleId
                });
            }
        }
    }
}

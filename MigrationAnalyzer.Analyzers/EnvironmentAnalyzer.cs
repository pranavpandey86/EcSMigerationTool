using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects Windows-specific environment variable usage and machine-level configuration access
    /// </summary>
    public class EnvironmentAnalyzer : IMigrationAnalyzer
    {
        public string Id => "ENV001";
        public string Name => "Environment Variable Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Configuration;
        public string Description => "Detects Windows-specific environment variables, machine-level registry environment access, and hardcoded machine names that won't exist in containers";

        private static readonly HashSet<string> WindowsEnvVars = new(StringComparer.OrdinalIgnoreCase)
        {
            "COMPUTERNAME", "USERDOMAIN", "LOGONSERVER", "SESSIONNAME",
            "OS", "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER",
            "NUMBER_OF_PROCESSORS", "PATHEXT", "COMSPEC",
            "SystemDrive", "SystemRoot", "windir",
            "ProgramW6432", "CommonProgramW6432",
            "ALLUSERSPROFILE", "ProgramData",
            "LOCALAPPDATA", "APPDATA", "HOMEDRIVE", "HOMEPATH"
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

                    var walker = new EnvironmentWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class EnvironmentWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public EnvironmentWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
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

                        // Detect Environment.GetEnvironmentVariable with Windows-specific vars
                        if (containingType == "Environment" && methodName == "GetEnvironmentVariable")
                        {
                            var args = node.ArgumentList?.Arguments;
                            if (args != null && args.Value.Count > 0)
                            {
                                var firstArg = args.Value[0].Expression;
                                if (firstArg is LiteralExpressionSyntax literal &&
                                    literal.IsKind(SyntaxKind.StringLiteralExpression))
                                {
                                    var envVarName = literal.Token.ValueText;
                                    if (WindowsEnvVars.Contains(envVarName))
                                    {
                                        AddFinding(node.GetLocation(), Severity.Medium, "ENV001",
                                            $"Windows-specific environment variable '{envVarName}' accessed.",
                                            $"'{envVarName}' may not exist or have different values in Linux containers. Use container-specific environment variables set via ECS task definitions.");
                                    }
                                }
                            }

                            // Check for EnvironmentVariableTarget.Machine
                            if (args != null && args.Value.Count > 1)
                            {
                                var secondArg = args.Value[1].Expression.ToString();
                                if (secondArg.Contains("EnvironmentVariableTarget.Machine"))
                                {
                                    AddFinding(node.GetLocation(), Severity.High, "ENV002",
                                        "Environment.GetEnvironmentVariable() with Machine target detected.",
                                        "Machine-level environment variables are set differently on Linux. In containers, use ECS task definition environment variables or AWS Secrets Manager.");
                                }
                            }
                        }

                        // Detect Environment.SetEnvironmentVariable with Machine target
                        if (containingType == "Environment" && methodName == "SetEnvironmentVariable")
                        {
                            var args = node.ArgumentList?.Arguments;
                            if (args != null && args.Value.Count > 2)
                            {
                                var thirdArg = args.Value[2].Expression.ToString();
                                if (thirdArg.Contains("EnvironmentVariableTarget.Machine"))
                                {
                                    AddFinding(node.GetLocation(), Severity.High, "ENV002",
                                        "Environment.SetEnvironmentVariable() with Machine target detected.",
                                        "Setting machine-level environment variables requires admin rights and is not appropriate for containers. Use ECS task definitions for environment configuration.");
                                }
                            }
                        }

                        // Detect Environment.MachineName usage that might be used for identification
                        if (containingType == "Environment" && methodName == "get_MachineName")
                        {
                            AddFinding(node.GetLocation(), Severity.Low, "ENV003",
                                "Environment.MachineName accessed.",
                                "In containers, MachineName returns the container hostname (random by default). Configure hostname via ECS task definition if needed for identification.");
                        }

                        // Detect Environment.UserDomainName
                        if (containingType == "Environment" && methodName == "get_UserDomainName")
                        {
                            AddFinding(node.GetLocation(), Severity.Medium, "ENV003",
                                "Environment.UserDomainName accessed.",
                                "UserDomainName is Windows domain-specific. Linux containers don't have domain membership. Use alternative identification methods.");
                        }
                    }
                }
                base.VisitInvocationExpression(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var nodeText = node.ToString();

                if (nodeText == "Environment.MachineName")
                {
                    AddFinding(node.GetLocation(), Severity.Low, "ENV003",
                        "Environment.MachineName accessed.",
                        "In containers, MachineName returns the container hostname (random by default). Use ECS metadata endpoint for container identification.");
                }

                if (nodeText == "Environment.UserDomainName")
                {
                    AddFinding(node.GetLocation(), Severity.Medium, "ENV003",
                        "Environment.UserDomainName accessed.",
                        "UserDomainName is Windows domain-specific. Linux containers are not domain-joined. Use alternative identification.");
                }

                if (nodeText == "Environment.UserName")
                {
                    AddFinding(node.GetLocation(), Severity.Info, "ENV003",
                        "Environment.UserName accessed.",
                        "In containers, the user is typically 'root' or a configured non-root user. Behavior differs from Windows domain environments.");
                }

                base.VisitMemberAccessExpression(node);
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
                    Category = AnalyzerCategory.Configuration,
                    RuleId = ruleId
                });
            }
        }
    }
}

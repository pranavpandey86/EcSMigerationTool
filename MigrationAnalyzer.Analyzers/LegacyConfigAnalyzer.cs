using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects legacy configuration patterns: ConfigurationManager, Machine.config, appSettings
    /// </summary>
    public class LegacyConfigAnalyzer : IMigrationAnalyzer
    {
        public string Id => "LCFG001";
        public string Name => "Legacy Configuration Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Configuration;
        public string Description => "Detects legacy configuration patterns (ConfigurationManager.AppSettings, Machine.config references, System.Configuration) that need migration to IConfiguration/appsettings.json";

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

                    var walker = new LegacyConfigWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class LegacyConfigWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public LegacyConfigWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name?.ToString() ?? "";

                if (name == "System.Configuration" || name.StartsWith("System.Configuration."))
                {
                    AddFinding(node.GetLocation(), Severity.Medium, "LCFG001",
                        $"Legacy configuration namespace '{name}' detected.",
                        "System.Configuration is the legacy .NET Framework configuration system. Migrate to Microsoft.Extensions.Configuration with appsettings.json and environment variables.");
                }

                base.VisitUsingDirective(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var nodeText = node.ToString();

                // Detect ConfigurationManager.AppSettings
                if (nodeText.Contains("ConfigurationManager.AppSettings"))
                {
                    AddFinding(node.GetLocation(), Severity.Medium, "LCFG001",
                        "ConfigurationManager.AppSettings usage detected.",
                        "Migrate to IConfiguration with appsettings.json. Use IOptions<T> pattern for strongly-typed configuration. Environment variables work well in containers.");
                }

                // Detect ConfigurationManager.ConnectionStrings
                if (nodeText.Contains("ConfigurationManager.ConnectionStrings"))
                {
                    AddFinding(node.GetLocation(), Severity.Medium, "LCFG002",
                        "ConfigurationManager.ConnectionStrings usage detected.",
                        "Migrate to IConfiguration.GetConnectionString(). Store connection strings in environment variables or AWS Secrets Manager for containers.");
                }

                // Detect ConfigurationManager.GetSection
                if (nodeText.Contains("ConfigurationManager.GetSection"))
                {
                    AddFinding(node.GetLocation(), Severity.Medium, "LCFG001",
                        "ConfigurationManager.GetSection() usage detected.",
                        "Custom config sections need migration to IConfiguration sections or IOptions<T> pattern with appsettings.json.");
                }

                // Detect WebConfigurationManager
                if (nodeText.Contains("WebConfigurationManager"))
                {
                    AddFinding(node.GetLocation(), Severity.High, "LCFG003",
                        "WebConfigurationManager usage detected — ASP.NET Classic configuration.",
                        "WebConfigurationManager is IIS/System.Web specific. Migrate to IConfiguration with appsettings.json in ASP.NET Core.");
                }

                base.VisitMemberAccessExpression(node);
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var value = node.Token.ValueText;

                    // Detect Machine.config references
                    if (value.Contains("machine.config", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("Machine.config", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.High, "LCFG004",
                            "Machine.config reference detected.",
                            "Machine.config is a Windows-level configuration file. Move all settings to appsettings.json, environment variables, or AWS Parameter Store.");
                    }

                    // Detect references to Windows config paths
                    if (value.Contains(@"C:\Windows\Microsoft.NET\Framework", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains(@"%WINDIR%\Microsoft.NET", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.High, "LCFG004",
                            $"Windows .NET Framework config path detected: '{value}'",
                            "Framework-level config paths don't exist on Linux. Use appsettings.json and environment variables.");
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

                        if (typeName == "ConfigurationManager" && namespaceName.Contains("System.Configuration"))
                        {
                            // Covered by MemberAccessExpression above in most cases
                            // This catches direct references not via member access
                        }

                        if (typeName == "ConfigurationSettings" && namespaceName.Contains("System.Configuration"))
                        {
                            AddFinding(node.GetLocation(), Severity.Medium, "LCFG001",
                                "Obsolete ConfigurationSettings usage detected.",
                                "ConfigurationSettings is deprecated. Migrate to IConfiguration with appsettings.json.");
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
                    Category = AnalyzerCategory.Configuration,
                    RuleId = ruleId
                });
            }
        }
    }
}

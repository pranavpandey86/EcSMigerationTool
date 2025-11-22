using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;
using System.Xml.Linq;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Analyzes IIS-specific features and ASP.NET Classic dependencies
    /// </summary>
    public class IISCompatibilityAnalyzer : IMigrationAnalyzer
    {
        public string Id => "IIS001";
        public string Name => "IIS Compatibility Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Configuration;

        private static readonly HashSet<string> SystemWebTypes = new()
        {
            "HttpContext",
            "HttpRequest",
            "HttpResponse",
            "HttpServerUtility",
            "HttpApplication",
            "HttpModule",
            "HttpHandler",
            "SessionState",
            "HttpSessionState"
        };

        public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            foreach (var project in solution.Projects)
            {
                // Check for web.config files
                var projectDir = Path.GetDirectoryName(project.FilePath);
                if (!string.IsNullOrEmpty(projectDir))
                {
                    var webConfigPath = Path.Combine(projectDir, "web.config");
                    if (File.Exists(webConfigPath))
                    {
                        findings.AddRange(AnalyzeWebConfig(webConfigPath));
                    }

                    // Check for Global.asax
                    var globalAsaxPath = Path.Combine(projectDir, "Global.asax");
                    var globalAsaxCsPath = Path.Combine(projectDir, "Global.asax.cs");
                    if (File.Exists(globalAsaxPath) || File.Exists(globalAsaxCsPath))
                    {
                        findings.Add(new DiagnosticFinding
                        {
                            FilePath = File.Exists(globalAsaxPath) ? globalAsaxPath : globalAsaxCsPath,
                            LineNumber = 1,
                            Severity = Severity.High,
                            Message = "Global.asax file detected - ASP.NET Classic application.",
                            Recommendation = "Migrate to ASP.NET Core. Replace Global.asax logic with Startup.cs or Program.cs middleware configuration.",
                            Category = AnalyzerCategory.Configuration,
                            RuleId = "IIS006"
                        });
                    }
                }

                // Analyze code files
                foreach (var document in project.Documents)
                {
                    if (document.SourceCodeKind != SourceCodeKind.Regular) continue;

                    var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync(ct);
                    var semanticModel = await document.GetSemanticModelAsync(ct);

                    var walker = new IISCompatibilityWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private List<DiagnosticFinding> AnalyzeWebConfig(string webConfigPath)
        {
            var findings = new List<DiagnosticFinding>();

            try
            {
                var doc = XDocument.Load(webConfigPath);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                // Check for system.web section (ASP.NET Classic)
                var systemWeb = doc.Descendants("system.web").FirstOrDefault();
                if (systemWeb != null)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = webConfigPath,
                        LineNumber = 1,
                        Severity = Severity.High,
                        Message = "<system.web> section detected in web.config - ASP.NET Classic configuration.",
                        Recommendation = "Migrate to ASP.NET Core which uses appsettings.json and does not require system.web configuration.",
                        Category = AnalyzerCategory.Configuration,
                        RuleId = "IIS001"
                    });
                }

                // Check for HTTP Modules
                var httpModules = doc.Descendants("httpModules").FirstOrDefault();
                if (httpModules != null)
                {
                    var modules = httpModules.Descendants("add").Select(e => e.Attribute("name")?.Value ?? "Unknown").ToList();
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = webConfigPath,
                        LineNumber = 1,
                        Severity = Severity.High,
                        Message = $"HTTP Modules detected in web.config: {string.Join(", ", modules)}",
                        Recommendation = "HTTP Modules are IIS-specific. Migrate to ASP.NET Core middleware. Replace modules with middleware in Startup.cs/Program.cs.",
                        Category = AnalyzerCategory.Configuration,
                        RuleId = "IIS002"
                    });
                }

                // Check for HTTP Handlers
                var httpHandlers = doc.Descendants("httpHandlers").FirstOrDefault();
                if (httpHandlers != null)
                {
                    var handlers = httpHandlers.Descendants("add").Select(e => e.Attribute("path")?.Value ?? "Unknown").ToList();
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = webConfigPath,
                        LineNumber = 1,
                        Severity = Severity.High,
                        Message = $"HTTP Handlers detected in web.config: {string.Join(", ", handlers)}",
                        Recommendation = "HTTP Handlers are IIS-specific. Migrate to ASP.NET Core endpoint routing or middleware.",
                        Category = AnalyzerCategory.Configuration,
                        RuleId = "IIS003"
                    });
                }

                // Check for system.webServer modules (IIS 7+)
                var webServerModules = doc.Descendants("system.webServer")
                    .Descendants("modules")
                    .FirstOrDefault();
                if (webServerModules != null)
                {
                    var modules = webServerModules.Descendants("add").Select(e => e.Attribute("name")?.Value ?? "Unknown").ToList();
                    if (modules.Any())
                    {
                        findings.Add(new DiagnosticFinding
                        {
                            FilePath = webConfigPath,
                            LineNumber = 1,
                            Severity = Severity.High,
                            Message = $"IIS modules detected in system.webServer: {string.Join(", ", modules)}",
                            Recommendation = "IIS modules need migration to ASP.NET Core middleware or Kestrel configuration.",
                            Category = AnalyzerCategory.Configuration,
                            RuleId = "IIS004"
                        });
                    }
                }

                // Check for URL Rewrite rules
                var rewriteRules = doc.Descendants("system.webServer")
                    .Descendants("rewrite")
                    .Descendants("rules")
                    .FirstOrDefault();
                if (rewriteRules != null && rewriteRules.Elements().Any())
                {
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = webConfigPath,
                        LineNumber = 1,
                        Severity = Severity.Medium,
                        Message = "IIS URL Rewrite rules detected in web.config.",
                        Recommendation = "Migrate URL rewrite rules to ASP.NET Core URL Rewriting middleware or use reverse proxy (nginx) rules.",
                        Category = AnalyzerCategory.Configuration,
                        RuleId = "IIS005"
                    });
                }

                // Check for handlers in system.webServer
                var webServerHandlers = doc.Descendants("system.webServer")
                    .Descendants("handlers")
                    .FirstOrDefault();
                if (webServerHandlers != null && webServerHandlers.Elements().Any())
                {
                    findings.Add(new DiagnosticFinding
                    {
                        FilePath = webConfigPath,
                        LineNumber = 1,
                        Severity = Severity.High,
                        Message = "IIS handlers detected in system.webServer configuration.",
                        Recommendation = "Migrate IIS handlers to ASP.NET Core endpoint routing.",
                        Category = AnalyzerCategory.Configuration,
                        RuleId = "IIS003"
                    });
                }
            }
            catch (Exception ex)
            {
                findings.Add(new DiagnosticFinding
                {
                    FilePath = webConfigPath,
                    LineNumber = 1,
                    Severity = Severity.Info,
                    Message = $"Could not fully parse web.config: {ex.Message}",
                    Recommendation = "Manually review web.config for IIS-specific settings.",
                    Category = AnalyzerCategory.Configuration,
                    RuleId = "IIS001"
                });
            }

            return findings;
        }

        private class IISCompatibilityWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public IISCompatibilityWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name.ToString();

                // Detect System.Web namespace (ASP.NET Classic)
                if (name.StartsWith("System.Web", StringComparison.Ordinal) && 
                    !name.Contains("AspNetCore"))
                {
                    AddFinding(node.GetLocation(), Severity.High, "IIS001",
                        $"System.Web namespace '{name}' detected - ASP.NET Classic dependency.",
                        "System.Web is not available in .NET Core/5+. Migrate to ASP.NET Core equivalents (Microsoft.AspNetCore.Http for HttpContext, etc.).");
                }

                base.VisitUsingDirective(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var nodeText = node.ToString();

                    // Detect HttpContext.Current
                    if (nodeText.Contains("HttpContext.Current"))
                    {
                        AddFinding(node.GetLocation(), Severity.High, "IIS001",
                            "HttpContext.Current usage detected - ASP.NET Classic pattern.",
                            "HttpContext.Current is not available in ASP.NET Core. Use dependency injection to pass HttpContext to services, or access via IHttpContextAccessor.");
                    }

                    // Detect Server.MapPath
                    if (nodeText.Contains("Server.MapPath"))
                    {
                        AddFinding(node.GetLocation(), Severity.High, "IIS001",
                            "Server.MapPath() usage detected - ASP.NET Classic HttpServerUtility.",
                            "Use IWebHostEnvironment.ContentRootPath or WebRootPath in ASP.NET Core instead of Server.MapPath().");
                    }

                    // Check symbol for System.Web types
                    var symbolInfo = _semanticModel.GetSymbolInfo(node);
                    var symbol = symbolInfo.Symbol;

                    if (symbol != null)
                    {
                        var containingType = symbol.ContainingType?.Name ?? "";
                        var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? "";

                        // Detect System.Web.HttpContext types
                        if (namespaceName.StartsWith("System.Web") && 
                            !namespaceName.Contains("AspNetCore") &&
                            SystemWebTypes.Contains(containingType))
                        {
                            AddFinding(node.GetLocation(), Severity.High, "IIS001",
                                $"System.Web.{containingType} usage detected.",
                                $"Replace with ASP.NET Core equivalent. Use Microsoft.AspNetCore.Http.HttpContext instead of System.Web.HttpContext.");
                        }
                    }
                }
                base.VisitMemberAccessExpression(node);
            }

            public override void VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                if (_semanticModel != null)
                {
                    var declaredSymbol = _semanticModel.GetDeclaredSymbol(node);
                    if (declaredSymbol != null)
                    {
                        // Check if class inherits from System.Web types
                        var baseType = declaredSymbol.BaseType;
                        while (baseType != null)
                        {
                            var baseTypeName = baseType.Name;
                            var baseNamespace = baseType.ContainingNamespace?.ToDisplayString() ?? "";

                            if (baseNamespace.StartsWith("System.Web") && !baseNamespace.Contains("AspNetCore"))
                            {
                                if (baseTypeName == "HttpApplication")
                                {
                                    AddFinding(node.Identifier.GetLocation(), Severity.High, "IIS006",
                                        $"Class '{node.Identifier.Text}' inherits from System.Web.HttpApplication (Global.asax).",
                                        "HttpApplication is ASP.NET Classic. Migrate application events to ASP.NET Core Startup.cs or Program.cs with middleware.");
                                }
                                else if (baseTypeName == "HttpModule" || baseTypeName.EndsWith("HttpModule"))
                                {
                                    AddFinding(node.Identifier.GetLocation(), Severity.High, "IIS002",
                                        $"Class '{node.Identifier.Text}' implements IHttpModule or inherits HttpModule.",
                                        "HTTP Modules are IIS-specific. Migrate to ASP.NET Core middleware.");
                                }
                                else if (baseTypeName == "HttpHandler" || baseTypeName.EndsWith("HttpHandler"))
                                {
                                    AddFinding(node.Identifier.GetLocation(), Severity.High, "IIS003",
                                        $"Class '{node.Identifier.Text}' implements IHttpHandler.",
                                        "HTTP Handlers are IIS-specific. Migrate to ASP.NET Core endpoint routing or middleware.");
                                }
                            }

                            baseType = baseType.BaseType;
                        }

                        // Check implemented interfaces
                        foreach (var iface in declaredSymbol.Interfaces)
                        {
                            var ifaceName = iface.Name;
                            var ifaceNamespace = iface.ContainingNamespace?.ToDisplayString() ?? "";

                            if (ifaceNamespace.StartsWith("System.Web") && !ifaceNamespace.Contains("AspNetCore"))
                            {
                                if (ifaceName == "IHttpModule")
                                {
                                    AddFinding(node.Identifier.GetLocation(), Severity.High, "IIS002",
                                        $"Class '{node.Identifier.Text}' implements IHttpModule.",
                                        "IHttpModule is IIS-specific. Migrate to ASP.NET Core middleware pattern.");
                                }
                                else if (ifaceName == "IHttpHandler")
                                {
                                    AddFinding(node.Identifier.GetLocation(), Severity.High, "IIS003",
                                        $"Class '{node.Identifier.Text}' implements IHttpHandler.",
                                        "IHttpHandler is IIS-specific. Migrate to ASP.NET Core endpoint routing.");
                                }
                            }
                        }
                    }
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
                    Category = AnalyzerCategory.Configuration,
                    RuleId = ruleId
                });
            }
        }
    }
}

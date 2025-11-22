using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects Windows Authentication patterns, LDAP usage, and SSO integration points
    /// </summary>
    public class AuthenticationAnalyzer : IMigrationAnalyzer
    {
        public string Id => "AUTH001";
        public string Name => "Authentication & Security Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Security;

        private static readonly HashSet<string> AuthenticationNamespaces = new()
        {
            "System.DirectoryServices",
            "System.DirectoryServices.AccountManagement",
            "System.DirectoryServices.Protocols",
            "System.Security.Principal.Windows"
        };

        private static readonly HashSet<string> WindowsIdentityTypes = new()
        {
            "WindowsIdentity",
            "WindowsPrincipal",
            "NTAccount",
            "SecurityIdentifier"
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

                    var walker = new AuthenticationWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class AuthenticationWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public AuthenticationWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name.ToString();
                if (AuthenticationNamespaces.Any(ns => name.StartsWith(ns)))
                {
                    AddFinding(node.GetLocation(), Severity.High,
                        $"Windows-specific authentication namespace '{name}' detected.",
                        "Replace with cross-platform LDAP libraries (Novell.Directory.Ldap.NETStandard) or use Azure AD/OIDC for authentication.");
                }
                base.VisitUsingDirective(node);
            }

            public override void VisitAttribute(AttributeSyntax node)
            {
                var attributeName = node.Name.ToString();
                
                // Check for [Authorize] attribute with Windows authentication
                if (attributeName.Contains("Authorize"))
                {
                    var args = node.ArgumentList?.Arguments;
                    if (args != null)
                    {
                        foreach (var arg in args)
                        {
                            var argText = arg.ToString();
                            if (argText.Contains("Windows") || argText.Contains("NTLM") || argText.Contains("Negotiate"))
                            {
                                AddFinding(node.GetLocation(), Severity.High,
                                    $"Windows authentication scheme detected in [Authorize] attribute.",
                                    "Replace with JWT Bearer authentication, OpenID Connect, or other cross-platform authentication schemes.");
                            }
                        }
                    }
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
                        
                        if (WindowsIdentityTypes.Contains(typeName))
                        {
                            AddFinding(node.GetLocation(), Severity.High,
                                $"Windows-specific identity type '{typeName}' detected.",
                                "Replace with ClaimsIdentity/ClaimsPrincipal for cross-platform authentication.");
                        }

                        // Detect DirectoryEntry and DirectorySearcher
                        if (typeName == "DirectoryEntry" || typeName == "DirectorySearcher")
                        {
                            AddFinding(node.GetLocation(), Severity.High,
                                $"LDAP type '{typeName}' from System.DirectoryServices detected.",
                                "Replace with Novell.Directory.Ldap.NETStandard for cross-platform LDAP support.");
                        }
                    }
                }
                base.VisitIdentifierName(node);
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var value = node.Token.ValueText;

                    // Check for Integrated Security in connection strings
                    if (value.Contains("Integrated Security", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("Trusted_Connection", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.High,
                            "Connection string with Windows Integrated Security detected.",
                            "Replace with SQL authentication using username/password from secure configuration (e.g., environment variables or secrets manager).");
                    }

                    // Check for NTLM/Kerberos references
                    if (value.Contains("NTLM", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("Kerberos", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("Negotiate", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.Medium,
                            $"Windows authentication protocol reference detected: '{value}'",
                            "Ensure authentication mechanisms are compatible with Linux containers. Consider OAuth2/OIDC.");
                    }

                    // Check for LDAP:// URLs
                    if (value.StartsWith("LDAP://", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.Info,
                            "LDAP URL detected. Verify LDAP server is accessible from Linux containers.",
                            "Ensure proper network configuration and DNS resolution for LDAP servers in containerized environment.");
                    }
                }
                base.VisitLiteralExpression(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);
                    var typeName = typeInfo.Type?.Name;

                    if (typeName == "WindowsIdentity" || typeName == "WindowsPrincipal")
                    {
                        AddFinding(node.GetLocation(), Severity.High,
                            $"Instantiation of Windows-specific type '{typeName}' detected.",
                            "Refactor to use ClaimsIdentity/ClaimsPrincipal with appropriate claims transformation.");
                    }
                }
                base.VisitObjectCreationExpression(node);
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
                    Category = AnalyzerCategory.Security,
                    RuleId = "AUTH001"
                });
            }
        }
    }
}

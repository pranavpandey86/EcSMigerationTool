using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects Windows-specific cryptography APIs including DPAPI and Certificate Store
    /// </summary>
    public class CryptographyAnalyzer : IMigrationAnalyzer
    {
        public string Id => "CRY001";
        public string Name => "Cryptography Compatibility Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.Security;

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

                    var walker = new CryptographyWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class CryptographyWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public CryptographyWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name.ToString();
                
                // Detect DPAPI namespace
                if (name.Contains("System.Security.Cryptography") && 
                    (name.Contains("ProtectedData") || name.Contains("DataProtection")))
                {
                    AddFinding(node.GetLocation(), Severity.Info, "CRY001",
                        $"Cryptography namespace '{name}' detected.",
                        "If using DPAPI (ProtectedData), migrate to ASP.NET Core Data Protection API or use cross-platform encryption (AES).");
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
                        var typeName = symbol.ContainingType?.Name ?? symbol.Name;
                        var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? "";

                        // Detect ProtectedData class (DPAPI)
                        if (typeName == "ProtectedData" && 
                            namespaceName.Contains("System.Security.Cryptography"))
                        {
                            AddFinding(node.GetLocation(), Severity.Critical, "CRY001",
                                "ProtectedData class usage detected (DPAPI - Data Protection API).",
                                "DPAPI is Windows-only. Use ASP.NET Core Data Protection API for cross-platform or encrypt with AES + secure key storage (Azure Key Vault, AWS Secrets Manager).");
                        }

                        // Detect DataProtectionScope enum
                        if (typeName == "DataProtectionScope")
                        {
                            AddFinding(node.GetLocation(), Severity.Critical, "CRY001",
                                "DataProtectionScope usage detected (DPAPI).",
                                "DPAPI scopes (CurrentUser/LocalMachine) are Windows-only. Replace with cross-platform encryption.");
                        }

                        // Detect X509Store
                        if (typeName == "X509Store")
                        {
                            AddFinding(node.GetLocation(), Severity.High, "CRY002",
                                "X509Store usage detected (Windows Certificate Store).",
                                "Windows Certificate Store differs from Linux. Use PEM files or containerized certificate management. Ensure certificates are mounted in containers.");
                        }

                        // Detect StoreLocation enum
                        if (typeName == "StoreLocation")
                        {
                            AddFinding(node.GetLocation(), Severity.Medium, "CRY002",
                                "StoreLocation usage detected (Certificate Store location).",
                                "StoreLocation.LocalMachine and CurrentUser are Windows-specific. Use file-based certificates or environment-specific certificate stores.");
                        }

                        // Detect CspParameters (Cryptographic Service Provider)
                        if (typeName == "CspParameters" && 
                            namespaceName.Contains("System.Security.Cryptography"))
                        {
                            AddFinding(node.GetLocation(), Severity.High, "CRY003",
                                "CspParameters usage detected (Windows CSP).",
                                "Windows Cryptographic Service Providers are not available on Linux. Use standard .NET cryptography classes or OpenSSL.");
                        }

                        // Detect RSACryptoServiceProvider (older CSP-based)
                        if (typeName == "RSACryptoServiceProvider")
                        {
                            AddFinding(node.GetLocation(), Severity.Medium, "CRY003",
                                "RSACryptoServiceProvider usage detected (CSP-based).",
                                "Prefer RSA.Create() for cross-platform compatibility instead of RSACryptoServiceProvider.");
                        }

                        // Detect DSACryptoServiceProvider (older CSP-based)
                        if (typeName == "DSACryptoServiceProvider")
                        {
                            AddFinding(node.GetLocation(), Severity.Medium, "CRY003",
                                "DSACryptoServiceProvider usage detected (CSP-based).",
                                "Prefer DSA.Create() for cross-platform compatibility instead of DSACryptoServiceProvider.");
                        }
                    }
                }
                base.VisitIdentifierName(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var symbolInfo = _semanticModel.GetSymbolInfo(node.Type);
                    var typeSymbol = symbolInfo.Symbol as INamedTypeSymbol;

                    if (typeSymbol != null)
                    {
                        var typeName = typeSymbol.Name;

                        // Detect X509Store instantiation with arguments
                        if (typeName == "X509Store")
                        {
                            // Check if StoreLocation is specified
                            var args = node.ArgumentList?.Arguments;
                            if (args != null && args.Value.Count > 1)
                            {
                                AddFinding(node.GetLocation(), Severity.High, "CRY002",
                                    "X509Store instantiated with StoreLocation parameter.",
                                    "Certificate store locations differ between Windows and Linux. Store certificates as files in container or use cloud-based certificate management.");
                            }
                        }

                        // Detect CspParameters instantiation
                        if (typeName == "CspParameters")
                        {
                            AddFinding(node.GetLocation(), Severity.High, "CRY003",
                                "CspParameters object creation detected.",
                                "CSP (Cryptographic Service Provider) is Windows-specific. Use standard cryptography classes without CSP parameters.");
                        }
                    }
                }
                base.VisitObjectCreationExpression(node);
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

                        // Detect ProtectedData.Protect()
                        if (containingType == "ProtectedData" && methodName == "Protect")
                        {
                            AddFinding(node.GetLocation(), Severity.Critical, "CRY001",
                                "ProtectedData.Protect() call detected (DPAPI encryption).",
                                "DPAPI encryption is Windows-only. Migrate to ASP.NET Core Data Protection or use AES with secure key management (Key Vault, Secrets Manager).");
                        }

                        // Detect ProtectedData.Unprotect()
                        if (containingType == "ProtectedData" && methodName == "Unprotect")
                        {
                            AddFinding(node.GetLocation(), Severity.Critical, "CRY001",
                                "ProtectedData.Unprotect() call detected (DPAPI decryption).",
                                "DPAPI decryption is Windows-only. Data encrypted with DPAPI must be decrypted before migration and re-encrypted with cross-platform method.");
                        }

                        // Detect X509Store.Open()
                        if (containingType == "X509Store" && methodName == "Open")
                        {
                            AddFinding(node.GetLocation(), Severity.Medium, "CRY002",
                                "X509Store.Open() call detected.",
                                "Ensure certificate access works on Linux. Consider file-based certificates loaded via X509Certificate2 from PEM files.");
                        }
                    }
                }
                base.VisitInvocationExpression(node);
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
                    Category = AnalyzerCategory.Security,
                    RuleId = ruleId
                });
            }
        }
    }
}

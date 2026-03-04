using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Detects legacy data access patterns: OLEDB, ODBC DSN, SQL Express, EF6
    /// </summary>
    public class DataAccessAnalyzer : IMigrationAnalyzer
    {
        public string Id => "DAL001";
        public string Name => "Data Access Layer Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.DataAccess;
        public string Description => "Detects legacy data access patterns (OleDb, ODBC DSN, SQL Express, Entity Framework 6) that are Windows-specific or require migration for Linux containers";

        private static readonly HashSet<string> LegacyDataTypes = new()
        {
            "OleDbConnection",
            "OleDbCommand",
            "OleDbDataAdapter",
            "OleDbDataReader",
            "OdbcConnection",
            "OdbcCommand",
            "OdbcDataAdapter",
            "OdbcDataReader"
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

                    var walker = new DataAccessWalker(semanticModel, document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class DataAccessWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel? _semanticModel;
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public DataAccessWalker(SemanticModel? semanticModel, string filePath)
            {
                _semanticModel = semanticModel;
                _filePath = filePath;
            }

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var name = node.Name?.ToString() ?? "";

                if (name == "System.Data.OleDb" || name.StartsWith("System.Data.OleDb."))
                {
                    AddFinding(node.GetLocation(), Severity.Critical, "DAL001",
                        $"OLE DB namespace '{name}' detected.",
                        "OLE DB providers are Windows-only (uses COM). Replace with ADO.NET SqlClient, Npgsql, or MySQL Connector for cross-platform data access.");
                }

                if (name == "System.Data.Odbc" || name.StartsWith("System.Data.Odbc."))
                {
                    AddFinding(node.GetLocation(), Severity.High, "DAL002",
                        $"ODBC namespace '{name}' detected.",
                        "ODBC requires driver configuration on the host OS. Ensure Linux-compatible ODBC drivers are installed in container or switch to native ADO.NET providers.");
                }

                if (name == "System.Data.Entity" || name.StartsWith("System.Data.Entity."))
                {
                    AddFinding(node.GetLocation(), Severity.High, "DAL004",
                        $"Entity Framework 6 namespace '{name}' detected.",
                        "Entity Framework 6 has limited Linux support. Migrate to Entity Framework Core (Microsoft.EntityFrameworkCore) for full cross-platform compatibility.");
                }

                base.VisitUsingDirective(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_semanticModel != null)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);
                    var typeName = typeInfo.Type?.Name ?? "";

                    if (LegacyDataTypes.Contains(typeName))
                    {
                        var isOleDb = typeName.StartsWith("OleDb");
                        AddFinding(node.GetLocation(),
                            isOleDb ? Severity.Critical : Severity.High,
                            isOleDb ? "DAL001" : "DAL002",
                            $"{typeName} instantiation detected — {(isOleDb ? "OLE DB" : "ODBC")} data access.",
                            isOleDb
                                ? "OLE DB is Windows-only (COM-based). Replace with SqlClient, Npgsql, or EF Core."
                                : "ODBC requires OS-level driver configuration. Use native ADO.NET providers or ensure Linux ODBC drivers are installed in the container.");
                    }
                }
                base.VisitObjectCreationExpression(node);
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var value = node.Token.ValueText;

                    // Detect SQL Server Express / named instances
                    if (value.Contains(@".\SQLEXPRESS", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains(@".\MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains(@"(local)\", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.High, "DAL003",
                            $"SQL Server named instance/Express detected: '{value}'",
                            "SQL Express and named instances are Windows-specific. Use a full SQL Server instance, Azure SQL, or containerized SQL Server with a proper connection string.");
                    }

                    // Detect OLE DB connection strings
                    if (value.Contains("Provider=", StringComparison.OrdinalIgnoreCase) &&
                        (value.Contains("Microsoft.ACE", StringComparison.OrdinalIgnoreCase) ||
                         value.Contains("Microsoft.Jet", StringComparison.OrdinalIgnoreCase) ||
                         value.Contains("SQLOLEDB", StringComparison.OrdinalIgnoreCase) ||
                         value.Contains("SQLNCLI", StringComparison.OrdinalIgnoreCase)))
                    {
                        AddFinding(node.GetLocation(), Severity.Critical, "DAL001",
                            $"OLE DB provider connection string detected.",
                            "OLE DB providers (ACE, Jet, SQLOLEDB, SQLNCLI) are Windows-only. Replace with ADO.NET SqlClient or EF Core.");
                    }

                    // Detect ODBC DSN references
                    if (value.StartsWith("DSN=", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("DRIVER=", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(node.GetLocation(), Severity.High, "DAL002",
                            "ODBC DSN or driver reference detected in connection string.",
                            "ODBC DSN configurations are OS-specific. Configure Linux-compatible DSN entries in /etc/odbc.ini within the container or use native providers.");
                    }
                }
                base.VisitLiteralExpression(node);
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
                    Category = AnalyzerCategory.DataAccess,
                    RuleId = ruleId
                });
            }
        }
    }
}

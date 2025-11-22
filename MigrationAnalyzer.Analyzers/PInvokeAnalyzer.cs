using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;

namespace MigrationAnalyzer.Analyzers
{
    public class PInvokeAnalyzer : IMigrationAnalyzer
    {
        public string Id => "WIN002";
        public string Name => "P/Invoke Detector";
        public AnalyzerCategory Category => AnalyzerCategory.WindowsApi;

        private static readonly HashSet<string> WindowsDlls = new(StringComparer.OrdinalIgnoreCase)
        {
            "kernel32.dll", "user32.dll", "gdi32.dll", "advapi32.dll", "shell32.dll", "ntdll.dll"
        };

        public async Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct)
        {
            var findings = new List<DiagnosticFinding>();

            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync(ct);
                    var walker = new PInvokeWalker(document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class PInvokeWalker : CSharpSyntaxWalker
        {
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public PInvokeWalker(string filePath)
            {
                _filePath = filePath;
            }

            public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var dllImport = node.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .FirstOrDefault(a => a.Name.ToString().Contains("DllImport"));

                if (dllImport != null)
                {
                    var arg = dllImport.ArgumentList?.Arguments.FirstOrDefault();
                    if (arg != null && arg.Expression is LiteralExpressionSyntax literal)
                    {
                        var dllName = literal.Token.ValueText;
                        if (WindowsDlls.Contains(dllName) || dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            AddFinding(node.GetLocation(), Severity.Critical,
                                $"P/Invoke to native Windows DLL '{dllName}' detected.",
                                "This code will fail on Linux. Replace with a managed equivalent or use conditional compilation with platform checks.");
                        }
                    }
                }
                base.VisitMethodDeclaration(node);
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
                    Category = AnalyzerCategory.WindowsApi,
                    RuleId = "WIN002"
                });
            }
        }
    }
}

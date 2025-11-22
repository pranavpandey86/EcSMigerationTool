using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;
using System.Text.RegularExpressions;

namespace MigrationAnalyzer.Analyzers
{
    public class FileSystemAnalyzer : IMigrationAnalyzer
    {
        public string Id => "FS001";
        public string Name => "File System Analyzer";
        public AnalyzerCategory Category => AnalyzerCategory.FileSystem;

        private static readonly Regex WindowsPathRegex = new Regex(@"^[a-zA-Z]:\\", RegexOptions.Compiled);
        private static readonly Regex UncPathRegex = new Regex(@"^\\\\[a-zA-Z0-9]", RegexOptions.Compiled);

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
                    var walker = new FileSystemWalker(document.FilePath ?? document.Name);
                    walker.Visit(root);
                    findings.AddRange(walker.Findings);
                }
            }

            return findings;
        }

        private class FileSystemWalker : CSharpSyntaxWalker
        {
            private readonly string _filePath;
            public List<DiagnosticFinding> Findings { get; } = new();

            public FileSystemWalker(string filePath)
            {
                _filePath = filePath;
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var value = node.Token.ValueText;
                    
                    if (WindowsPathRegex.IsMatch(value))
                    {
                        AddFinding(node.GetLocation(), Severity.High,
                            $"Hardcoded Windows path detected: '{value}'",
                            "Use Path.Combine and relative paths, or configuration settings for paths. Avoid drive letters.");
                    }
                    else if (UncPathRegex.IsMatch(value))
                    {
                        AddFinding(node.GetLocation(), Severity.High,
                            $"UNC path detected: '{value}'",
                            "UNC paths may not be accessible from Linux containers. Ensure the target share is mounted or accessible via network.");
                    }
                    // Check for Windows environment variables
                    else if (value.Contains("%APPDATA%", StringComparison.OrdinalIgnoreCase) ||
                             value.Contains("%PROGRAMFILES%", StringComparison.OrdinalIgnoreCase) ||
                             value.Contains("%WINDIR%", StringComparison.OrdinalIgnoreCase) ||
                             value.Contains("%SYSTEMROOT%", StringComparison.OrdinalIgnoreCase) ||
                             value.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase) ||
                             value.Contains("%TEMP%", StringComparison.OrdinalIgnoreCase) && value.Contains(":\\"))
                    {
                        AddFinding(node.GetLocation(), Severity.High,
                            $"Windows environment variable detected in path: '{value}'",
                            "Windows-specific environment variables do not exist on Linux. Use configuration settings or Linux-compatible paths.");
                    }
                    else if (value.Contains('\\') && !value.Contains('/'))
                    {
                        // Heuristic: if it contains backslashes but no forward slashes, it might be a path
                        // We need to be careful not to flag regex or other escaped strings too aggressively
                        if (value.Contains(":\\") || value.StartsWith("\\\\")) 
                        {
                             // Already caught above
                        }
                        else if (value.Count(c => c == '\\') > 1 && !value.Contains(" "))
                        {
                             AddFinding(node.GetLocation(), Severity.Medium,
                                $"Potential Windows-style path separator usage: '{value}'",
                                "Use Path.DirectorySeparatorChar or forward slashes '/' which work on both Windows and Linux.");
                        }
                    }
                }
                base.VisitLiteralExpression(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var nodeText = node.ToString();
                
                if (nodeText == "Path.GetTempPath()")
                {
                    AddFinding(node.GetLocation(), Severity.Medium,
                        "Path.GetTempPath() usage detected",
                        "Ensure the temporary directory environment variables are correctly set in the container.");
                }
                
                // Detect Environment.GetFolderPath with Windows-specific special folders
                if (nodeText.Contains("Environment.GetFolderPath"))
                {
                    AddFinding(node.GetLocation(), Severity.Medium,
                        "Environment.GetFolderPath() usage detected",
                        "Special folder paths differ between Windows and Linux. Use IWebHostEnvironment or configuration-based paths instead.");
                }
                
                base.VisitMemberAccessExpression(node);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var nodeText = node.ToString();
                
                // Check for specific SpecialFolder enum values that are Windows-specific
                if (nodeText.Contains("SpecialFolder.ProgramFiles") ||
                    nodeText.Contains("SpecialFolder.ApplicationData") ||
                    nodeText.Contains("SpecialFolder.LocalApplicationData") ||
                    nodeText.Contains("SpecialFolder.CommonProgramFiles") ||
                    nodeText.Contains("SpecialFolder.Windows") ||
                    nodeText.Contains("SpecialFolder.System"))
                {
                    AddFinding(node.GetLocation(), Severity.High,
                        $"Windows-specific SpecialFolder usage detected in: {nodeText}",
                        "SpecialFolder values like ProgramFiles, ApplicationData are Windows-specific. Use configuration files or relative paths for cross-platform compatibility.");
                }
                
                base.VisitInvocationExpression(node);
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
                    Category = AnalyzerCategory.FileSystem,
                    RuleId = "FS001"
                });
            }
        }
    }
}

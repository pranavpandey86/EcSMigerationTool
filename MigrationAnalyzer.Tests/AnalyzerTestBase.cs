using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using MigrationAnalyzer.Core.Interfaces;
using MigrationAnalyzer.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

namespace MigrationAnalyzer.Tests
{
    public abstract class AnalyzerTestBase
    {
        private static readonly MetadataReference[] DefaultReferences = GetDefaultReferences();

        private static MetadataReference[] GetDefaultReferences()
        {
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Win32.Registry).Assembly.Location),
            };

            // Try to add additional references for common Windows types
            // These may not be available on all platforms, so we try-catch
            TryAddReference(references, "System.Runtime");
            TryAddReference(references, "System.Security.Principal.Windows");
            TryAddReference(references, "System.Security.Cryptography");
            TryAddReference(references, "System.Security.Cryptography.ProtectedData");
            TryAddReference(references, "System.Security.Cryptography.X509Certificates");
            TryAddReference(references, "System.Runtime.InteropServices");
            TryAddReference(references, "System.DirectoryServices");
            TryAddReference(references, "System.IO.Pipes");
            TryAddReference(references, "netstandard");

            return references.ToArray();
        }

        private static void TryAddReference(List<MetadataReference> references, string assemblyName)
        {
            try
            {
                var assembly = Assembly.Load(assemblyName);
                if (assembly?.Location != null && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            catch
            {
                // Ignore - some assemblies may not be available on all platforms
            }
        }

        protected async Task<List<DiagnosticFinding>> RunAnalyzerAsync(IMigrationAnalyzer analyzer, string source)
        {
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            var solution = new AdhocWorkspace()
                .CurrentSolution
                .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp);

            foreach (var reference in DefaultReferences)
            {
                solution = solution.AddMetadataReference(projectId, reference);
            }

            solution = solution.AddDocument(documentId, "TestFile.cs", SourceText.From(source));

            return (await analyzer.AnalyzeAsync(solution, CancellationToken.None)).ToList();
        }
    }
}

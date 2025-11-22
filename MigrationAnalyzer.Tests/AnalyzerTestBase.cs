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

namespace MigrationAnalyzer.Tests
{
    public abstract class AnalyzerTestBase
    {
        protected async Task<List<DiagnosticFinding>> RunAnalyzerAsync(IMigrationAnalyzer analyzer, string source)
        {
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            var solution = new AdhocWorkspace()
                .CurrentSolution
                .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
                .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(Console).Assembly.Location))
                .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(Microsoft.Win32.Registry).Assembly.Location))
                .AddDocument(documentId, "TestFile.cs", SourceText.From(source));

            return (await analyzer.AnalyzeAsync(solution, CancellationToken.None)).ToList();
        }
    }
}

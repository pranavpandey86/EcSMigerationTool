using Microsoft.CodeAnalysis;

namespace MigrationAnalyzer.Core.Models
{
    public enum Severity
    {
        Critical,
        High,
        Medium,
        Low,
        Info
    }

    public enum AnalyzerCategory
    {
        WindowsApi,
        FileSystem,
        Security,
        Configuration,
        Dependencies,
        General
    }

    public class DiagnosticFinding
    {
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public Severity Severity { get; set; }
        public AnalyzerCategory Category { get; set; }
        public string RuleId { get; set; } = string.Empty;
    }

    public class AnalysisResult
    {
        public DateTime AnalysisDate { get; set; }
        public string SolutionPath { get; set; } = string.Empty;
        public List<DiagnosticFinding> Findings { get; set; } = new();
        public TimeSpan Duration { get; set; }
        public int TotalFilesScanned { get; set; }
        public Dictionary<string, int> PackageInventory { get; set; } = new();
        
        /// <summary>
        /// Calculates total effort in developer-days based on severity weights
        /// </summary>
        public double CalculateEffortDays()
        {
            return Findings.Sum(f => f.Severity switch
            {
                Severity.Critical => 5.0,
                Severity.High => 3.0,
                Severity.Medium => 1.0,
                Severity.Low => 0.5,
                Severity.Info => 0.1,
                _ => 0
            });
        }
        
        public Dictionary<Severity, int> GetSeverityCounts()
        {
            return Findings.GroupBy(f => f.Severity)
                          .ToDictionary(g => g.Key, g => g.Count());
        }
        
        public Dictionary<AnalyzerCategory, int> GetCategoryCounts()
        {
            return Findings.GroupBy(f => f.Category)
                          .ToDictionary(g => g.Key, g => g.Count());
        }
    }
}

namespace MigrationAnalyzer.Core.Interfaces
{
    using MigrationAnalyzer.Core.Models;

    public interface IMigrationAnalyzer
    {
        string Id { get; }
        string Name { get; }
        AnalyzerCategory Category { get; }
        Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct);
    }
}

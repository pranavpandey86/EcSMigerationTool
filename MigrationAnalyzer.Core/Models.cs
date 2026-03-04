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
        General,
        Messaging,
        Logging,
        DataAccess,
        Hosting,
        IPC,
        Transactions
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

    /// <summary>
    /// Tracks whether a particular analyzer ran and what it checked for,
    /// even if it found no issues — so reports can show "checked but clear".
    /// </summary>
    public class AnalyzerCheckSummary
    {
        public string AnalyzerId { get; set; } = string.Empty;
        public string AnalyzerName { get; set; } = string.Empty;
        public AnalyzerCategory Category { get; set; }
        public string Description { get; set; } = string.Empty;
        public int FindingsCount { get; set; }
        public long ElapsedMs { get; set; }

        /// <summary>Human-readable status: "X issues found" or "✅ No issues found"</summary>
        public string Status => FindingsCount > 0
            ? $"{FindingsCount} issue(s) found"
            : "✅ No issues found — no changes required";
    }

    public class AnalysisResult
    {
        public DateTime AnalysisDate { get; set; }
        public string SolutionPath { get; set; } = string.Empty;
        public List<DiagnosticFinding> Findings { get; set; } = new();
        public TimeSpan Duration { get; set; }
        public int TotalFilesScanned { get; set; }
        public Dictionary<string, string> PackageInventory { get; set; } = new();

        /// <summary>
        /// Summary of every analyzer that was executed, including those with 0 findings.
        /// </summary>
        public List<AnalyzerCheckSummary> CheckSummaries { get; set; } = new();
        
        /// <summary>
        /// Removes duplicate findings based on file path, line number, and rule ID
        /// </summary>
        public void DeduplicateFindings()
        {
            Findings = Findings
                .GroupBy(f => (f.FilePath, f.LineNumber, f.RuleId))
                .Select(g => g.First())
                .ToList();
        }
        
        /// <summary>
        /// Calculates total effort in developer-days based on severity weights
        /// Uses improved estimation that considers unique rule types (batch fixes)
        /// </summary>
        public double CalculateEffortDays()
        {
            // Base effort per individual finding
            var baseEffort = Findings.Sum(f => f.Severity switch
            {
                Severity.Critical => 5.0,
                Severity.High => 3.0,
                Severity.Medium => 1.0,
                Severity.Low => 0.5,
                Severity.Info => 0.1,
                _ => 0
            });
            
            // Apply batch fix discount: many findings of same type can be fixed together
            // Discount is based on unique rule types - reduces effort by up to 30%
            var uniqueRules = Findings.Select(f => f.RuleId).Distinct().Count();
            var totalFindings = Findings.Count;
            
            if (totalFindings > 0 && uniqueRules > 0)
            {
                var batchFactor = Math.Min(1.0, 0.7 + (0.3 * uniqueRules / totalFindings));
                return baseEffort * batchFactor;
            }
            
            return baseEffort;
        }
        
        /// <summary>
        /// Calculates raw effort without batch fix discount
        /// </summary>
        public double CalculateRawEffortDays()
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
        /// <summary>Human-readable description of what this analyzer checks for.</summary>
        string Description => $"Checks for {Category} related migration issues";
        Task<IEnumerable<DiagnosticFinding>> AnalyzeAsync(Solution solution, CancellationToken ct);
    }
}

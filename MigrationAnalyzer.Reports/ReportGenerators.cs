using ClosedXML.Excel;
using MigrationAnalyzer.Core.Models;
using System.Text.Json;
using System.Text;

namespace MigrationAnalyzer.Reports
{
    public interface IReportGenerator
    {
        void Generate(AnalysisResult result, string outputPath);
    }

    public class JsonReportGenerator : IReportGenerator
    {
        public void Generate(AnalysisResult result, string outputPath)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(result, options);
            File.WriteAllText(Path.Combine(outputPath, "migration-report.json"), json);
        }
    }

    public class HtmlReportGenerator : IReportGenerator
    {
        public void Generate(AnalysisResult result, string outputPath)
        {
            var sb = new StringBuilder();
            var severityCounts = result.GetSeverityCounts();
            var categoryCounts = result.GetCategoryCounts();
            var effortDays = result.CalculateEffortDays();
            
            sb.AppendLine("<!DOCTYPE html><html><head><title>Migration Analysis Report</title>");
            sb.AppendLine("<meta charset='UTF-8'>");
            sb.AppendLine(@"<style>
                body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background: #f5f5f5; }
                .container { max-width: 1400px; margin: 0 auto; background: white; padding: 30px; box-shadow: 0 0 10px rgba(0,0,0,0.1); }
                h1 { color: #333; border-bottom: 3px solid #0066cc; padding-bottom: 10px; }
                h2 { color: #0066cc; margin-top: 30px; }
                .executive-summary { background: #e3f2fd; padding: 20px; border-radius: 8px; margin: 20px 0; }
                .metric { display: inline-block; margin: 10px 20px; }
                .metric-label { font-weight: bold; color: #666; }
                .metric-value { font-size: 24px; color: #0066cc; }
                .critical { color: #d32f2f; font-weight: bold; }
                .high { color: #f57c00; font-weight: bold; }
                .medium { color: #fbc02d; font-weight: bold; }
                .low { color: #388e3c; font-weight: bold; }
                .info { color: #1976d2; }
                table { border-collapse: collapse; width: 100%; margin-top: 20px; }
                th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }
                th { background-color: #0066cc; color: white; cursor: pointer; user-select: none; }
                th:hover { background-color: #0052a3; }
                tr:nth-child(even) { background-color: #f9f9f9; }
                tr:hover { background-color: #e3f2fd; }
                .chart-container { display: inline-block; width: 45%; margin: 20px 2%; vertical-align: top; }
                .bar { background: #0066cc; color: white; padding: 8px; margin: 5px 0; border-radius: 4px; }
                .filter-box { margin: 20px 0; padding: 15px; background: #f5f5f5; border-radius: 5px; }
                .filter-box input, .filter-box select { padding: 8px; margin: 5px; border: 1px solid #ddd; border-radius: 4px; }
                .action-plan { background: #fff3e0; padding: 20px; border-left: 4px solid #f57c00; margin: 20px 0; }
                .priority-item { margin: 10px 0; padding: 10px; background: white; border-radius: 4px; }
            </style>");
            sb.AppendLine("<script src='https://cdn.jsdelivr.net/npm/chart.js@3.9.1/dist/chart.min.js'></script>");
            sb.AppendLine("</head><body><div class='container'>");
            
            // Header
            sb.AppendLine($"<h1>🔍 .NET Migration Analysis Report</h1>");
            sb.AppendLine($"<p><strong>Analysis Date:</strong> {result.AnalysisDate:yyyy-MM-dd HH:mm:ss}</p>");
            sb.AppendLine($"<p><strong>Solution:</strong> {Path.GetFileName(result.SolutionPath)}</p>");
            sb.AppendLine($"<p><strong>Duration:</strong> {result.Duration.TotalSeconds:F2} seconds</p>");
            
            // Executive Summary
            sb.AppendLine("<div class='executive-summary'>");
            sb.AppendLine("<h2>📊 Executive Summary</h2>");
            sb.AppendLine($"<div class='metric'><div class='metric-label'>Total Findings</div><div class='metric-value'>{result.Findings.Count}</div></div>");
            sb.AppendLine($"<div class='metric'><div class='metric-label'>Files Scanned</div><div class='metric-value'>{result.TotalFilesScanned}</div></div>");
            sb.AppendLine($"<div class='metric'><div class='metric-label'>Estimated Effort</div><div class='metric-value'>{effortDays:F1} days</div></div>");
            sb.AppendLine("<h3>Findings by Severity</h3>");
            foreach (var severity in Enum.GetValues<Severity>())
            {
                var count = severityCounts.GetValueOrDefault(severity, 0);
                if (count > 0)
                    sb.AppendLine($"<div class='metric'><span class='{severity.ToString().ToLower()}'>{severity}</span>: <strong>{count}</strong></div>");
            }
            sb.AppendLine("</div>");
            
            // Charts
            sb.AppendLine("<div class='chart-container'><canvas id='severityChart'></canvas></div>");
            sb.AppendLine("<div class='chart-container'><canvas id='categoryChart'></canvas></div>");
            
            // Action Plan
            sb.AppendLine("<div class='action-plan'>");
            sb.AppendLine("<h2>🎯 Prioritized Action Plan</h2>");
            var criticalCount = severityCounts.GetValueOrDefault(Severity.Critical, 0);
            var highCount = severityCounts.GetValueOrDefault(Severity.High, 0);
            
            if (criticalCount > 0)
            {
                sb.AppendLine($"<div class='priority-item'><strong>PRIORITY 1 - BLOCKERS:</strong> Fix {criticalCount} critical issues immediately. These will prevent migration. Estimated: {criticalCount * 5} days.</div>");
            }
            if (highCount > 0)
            {
                sb.AppendLine($"<div class='priority-item'><strong>PRIORITY 2 - HIGH IMPACT:</strong> Address {highCount} high-severity issues. These require significant code changes. Estimated: {highCount * 3} days.</div>");
            }
            sb.AppendLine("<div class='priority-item'><strong>PRIORITY 3:</strong> Update configurations and resolve medium/low priority issues.</div>");
            sb.AppendLine("<div class='priority-item'><strong>PRIORITY 4:</strong> Implement best practices and optimization recommendations.</div>");
            sb.AppendLine("</div>");
            
            // Detailed Findings Table
            sb.AppendLine("<h2>📋 Detailed Findings</h2>");
            sb.AppendLine("<div class='filter-box'>");
            sb.AppendLine("<input type='text' id='searchBox' placeholder='Search findings...' onkeyup='filterTable()'>");
            sb.AppendLine("<select id='severityFilter' onchange='filterTable()'><option value=''>All Severities</option>");
            foreach (var severity in Enum.GetValues<Severity>())
                sb.AppendLine($"<option value='{severity}'>{severity}</option>");
            sb.AppendLine("</select>");
            sb.AppendLine("<select id='categoryFilter' onchange='filterTable()'><option value=''>All Categories</option>");
            foreach (var category in Enum.GetValues<AnalyzerCategory>())
                sb.AppendLine($"<option value='{category}'>{category}</option>");
            sb.AppendLine("</select>");
            sb.AppendLine("</div>");
            
            sb.AppendLine("<table id='findingsTable'><thead><tr>");
            sb.AppendLine("<th onclick='sortTable(0)'>Severity</th>");
            sb.AppendLine("<th onclick='sortTable(1)'>Category</th>");
            sb.AppendLine("<th onclick='sortTable(2)'>Rule ID</th>");
            sb.AppendLine("<th onclick='sortTable(3)'>File</th>");
            sb.AppendLine("<th onclick='sortTable(4)'>Line</th>");
            sb.AppendLine("<th onclick='sortTable(5)'>Issue</th>");
            sb.AppendLine("<th onclick='sortTable(6)'>Recommendation</th>");
            sb.AppendLine("</tr></thead><tbody>");
            
            foreach (var finding in result.Findings.OrderBy(f => f.Severity))
            {
                sb.AppendLine($"<tr class='{finding.Severity.ToString().ToLower()}-row'>");
                sb.AppendLine($"<td><span class='{finding.Severity.ToString().ToLower()}'>{finding.Severity}</span></td>");
                sb.AppendLine($"<td>{finding.Category}</td>");
                sb.AppendLine($"<td>{finding.RuleId}</td>");
                sb.AppendLine($"<td title='{finding.FilePath}'>{Path.GetFileName(finding.FilePath)}</td>");
                sb.AppendLine($"<td>{finding.LineNumber}</td>");
                sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(finding.Message)}</td>");
                sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(finding.Recommendation)}</td>");
                sb.AppendLine("</tr>");
            }
            
            sb.AppendLine("</tbody></table>");
            
            // JavaScript for charts and interactivity
            sb.AppendLine("<script>");
            
            // Severity chart data
            sb.AppendLine("const severityData = {");
            sb.AppendLine($"  labels: [{string.Join(", ", severityCounts.Keys.Select(k => $"'{k}'"))}],");
            sb.AppendLine($"  datasets: [{{");
            sb.AppendLine($"    data: [{string.Join(", ", severityCounts.Values)}],");
            sb.AppendLine("    backgroundColor: ['#d32f2f', '#f57c00', '#fbc02d', '#388e3c', '#1976d2']");
            sb.AppendLine("  }]");
            sb.AppendLine("};");
            
            // Category chart data
            sb.AppendLine("const categoryData = {");
            sb.AppendLine($"  labels: [{string.Join(", ", categoryCounts.Keys.Select(k => $"'{k}'"))}],");
            sb.AppendLine($"  datasets: [{{");
            sb.AppendLine($"    data: [{string.Join(", ", categoryCounts.Values)}],");
            sb.AppendLine("    backgroundColor: ['#FF6384', '#36A2EB', '#FFCE56', '#4BC0C0', '#9966FF', '#FF9F40']");
            sb.AppendLine("  }]");
            sb.AppendLine("};");
            
            sb.AppendLine(@"
new Chart(document.getElementById('severityChart'), {
    type: 'pie',
    data: severityData,
    options: { responsive: true, plugins: { title: { display: true, text: 'Findings by Severity' }, legend: { position: 'bottom' } } }
});

new Chart(document.getElementById('categoryChart'), {
    type: 'doughnut',
    data: categoryData,
    options: { responsive: true, plugins: { title: { display: true, text: 'Findings by Category' }, legend: { position: 'bottom' } } }
});

function filterTable() {
    const searchText = document.getElementById('searchBox').value.toLowerCase();
    const severityFilter = document.getElementById('severityFilter').value;
    const categoryFilter = document.getElementById('categoryFilter').value;
    const table = document.getElementById('findingsTable');
    const rows = table.getElementsByTagName('tr');
    
    for (let i = 1; i < rows.length; i++) {
        const row = rows[i];
        const cells = row.getElementsByTagName('td');
        if (cells.length === 0) continue;
        
        const severity = cells[0].textContent.trim();
        const category = cells[1].textContent.trim();
        const rowText = row.textContent.toLowerCase();
        
        let show = true;
        if (searchText && !rowText.includes(searchText)) show = false;
        if (severityFilter && severity !== severityFilter) show = false;
        if (categoryFilter && category !== categoryFilter) show = false;
        
        row.style.display = show ? '' : 'none';
    }
}

function sortTable(columnIndex) {
    const table = document.getElementById('findingsTable');
    const rows = Array.from(table.rows).slice(1);
    const isAscending = table.getAttribute('data-sort-dir') === 'asc';
    
    rows.sort((a, b) => {
        const aText = a.cells[columnIndex].textContent.trim();
        const bText = b.cells[columnIndex].textContent.trim();
        return isAscending ? aText.localeCompare(bText) : bText.localeCompare(aText);
    });
    
    table.setAttribute('data-sort-dir', isAscending ? 'desc' : 'asc');
    rows.forEach(row => table.tBodies[0].appendChild(row));
}
</script>");
            
            sb.AppendLine("</div></body></html>");

            File.WriteAllText(Path.Combine(outputPath, "migration-report.html"), sb.ToString());
        }
    }

    public class ExcelReportGenerator : IReportGenerator
    {
        public void Generate(AnalysisResult result, string outputPath)
        {
            using var workbook = new XLWorkbook();
            
            // Summary Sheet
            CreateSummarySheet(workbook, result);
            
            // Findings Sheet
            CreateFindingsSheet(workbook, result);
            
            // Package Inventory Sheet
            CreatePackageInventorySheet(workbook, result);
            
            // Configuration Audit Sheet
            CreateConfigurationAuditSheet(workbook, result);

            workbook.SaveAs(Path.Combine(outputPath, "migration-report.xlsx"));
        }

        private void CreateSummarySheet(XLWorkbook workbook, AnalysisResult result)
        {
            var sheet = workbook.Worksheets.Add("Summary");
            var severityCounts = result.GetSeverityCounts();
            var categoryCounts = result.GetCategoryCounts();
            var effortDays = result.CalculateEffortDays();
            
            // Header
            sheet.Cell(1, 1).Value = "Migration Analysis Summary";
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 16;
            sheet.Range(1, 1, 1, 4).Merge();
            
            // Metrics
            sheet.Cell(3, 1).Value = "Analysis Date:";
            sheet.Cell(3, 2).Value = result.AnalysisDate.ToString("yyyy-MM-dd HH:mm:ss");
            
            sheet.Cell(4, 1).Value = "Solution Path:";
            sheet.Cell(4, 2).Value = result.SolutionPath;
            
            sheet.Cell(5, 1).Value = "Total Findings:";
            sheet.Cell(5, 2).Value = result.Findings.Count;
            sheet.Cell(5, 2).Style.Font.Bold = true;
            
            sheet.Cell(6, 1).Value = "Files Scanned:";
            sheet.Cell(6, 2).Value = result.TotalFilesScanned;
            
            sheet.Cell(7, 1).Value = "Analysis Duration:";
            sheet.Cell(7, 2).Value = $"{result.Duration.TotalSeconds:F2} seconds";
            
            sheet.Cell(8, 1).Value = "Estimated Effort:";
            sheet.Cell(8, 2).Value = $"{effortDays:F1} developer-days";
            sheet.Cell(8, 2).Style.Font.Bold = true;
            sheet.Cell(8, 2).Style.Font.FontColor = XLColor.DarkRed;
            
            // Severity breakdown
            sheet.Cell(10, 1).Value = "Findings by Severity";
            sheet.Cell(10, 1).Style.Font.Bold = true;
            sheet.Cell(10, 1).Style.Font.FontSize = 14;
            
            int row = 11;
            sheet.Cell(row, 1).Value = "Severity";
            sheet.Cell(row, 2).Value = "Count";
            sheet.Cell(row, 3).Value = "Effort (days)";
            sheet.Range(row, 1, row, 3).Style.Font.Bold = true;
            sheet.Range(row, 1, row, 3).Style.Fill.BackgroundColor = XLColor.LightGray;
            row++;
            
            foreach (var severity in Enum.GetValues<Severity>())
            {
                var count = severityCounts.GetValueOrDefault(severity, 0);
                if (count > 0)
                {
                    var effort = count * (severity switch
                    {
                        Severity.Critical => 5.0,
                        Severity.High => 3.0,
                        Severity.Medium => 1.0,
                        Severity.Low => 0.5,
                        Severity.Info => 0.1,
                        _ => 0
                    });
                    
                    sheet.Cell(row, 1).Value = severity.ToString();
                    sheet.Cell(row, 2).Value = count;
                    sheet.Cell(row, 3).Value = effort;
                    
                    // Color coding
                    switch (severity)
                    {
                        case Severity.Critical:
                            sheet.Cell(row, 1).Style.Font.FontColor = XLColor.DarkRed;
                            break;
                        case Severity.High:
                            sheet.Cell(row, 1).Style.Font.FontColor = XLColor.DarkOrange;
                            break;
                    }
                    row++;
                }
            }
            
            // Category breakdown
            row += 2;
            sheet.Cell(row, 1).Value = "Findings by Category";
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 1).Style.Font.FontSize = 14;
            row++;
            
            sheet.Cell(row, 1).Value = "Category";
            sheet.Cell(row, 2).Value = "Count";
            sheet.Range(row, 1, row, 2).Style.Font.Bold = true;
            sheet.Range(row, 1, row, 2).Style.Fill.BackgroundColor = XLColor.LightGray;
            row++;
            
            foreach (var category in categoryCounts.OrderByDescending(c => c.Value))
            {
                sheet.Cell(row, 1).Value = category.Key.ToString();
                sheet.Cell(row, 2).Value = category.Value;
                row++;
            }
            
            sheet.Columns().AdjustToContents();
        }

        private void CreateFindingsSheet(XLWorkbook workbook, AnalysisResult result)
        {
            var sheet = workbook.Worksheets.Add("Findings");
            
            // Headers
            sheet.Cell(1, 1).Value = "Severity";
            sheet.Cell(1, 2).Value = "Category";
            sheet.Cell(1, 3).Value = "Rule ID";
            sheet.Cell(1, 4).Value = "File Path";
            sheet.Cell(1, 5).Value = "Line";
            sheet.Cell(1, 6).Value = "Issue Description";
            sheet.Cell(1, 7).Value = "Recommendation";
            sheet.Cell(1, 8).Value = "Effort (days)";
            
            sheet.Range(1, 1, 1, 8).Style.Font.Bold = true;
            sheet.Range(1, 1, 1, 8).Style.Fill.BackgroundColor = XLColor.DarkBlue;
            sheet.Range(1, 1, 1, 8).Style.Font.FontColor = XLColor.White;

            int row = 2;
            foreach (var finding in result.Findings.OrderBy(f => f.Severity).ThenBy(f => f.Category))
            {
                var effort = finding.Severity switch
                {
                    Severity.Critical => 5.0,
                    Severity.High => 3.0,
                    Severity.Medium => 1.0,
                    Severity.Low => 0.5,
                    Severity.Info => 0.1,
                    _ => 0
                };
                
                sheet.Cell(row, 1).Value = finding.Severity.ToString();
                sheet.Cell(row, 2).Value = finding.Category.ToString();
                sheet.Cell(row, 3).Value = finding.RuleId;
                sheet.Cell(row, 4).Value = finding.FilePath;
                sheet.Cell(row, 5).Value = finding.LineNumber;
                sheet.Cell(row, 6).Value = finding.Message;
                sheet.Cell(row, 7).Value = finding.Recommendation;
                sheet.Cell(row, 8).Value = effort;
                
                // Color coding for severity
                switch (finding.Severity)
                {
                    case Severity.Critical:
                        sheet.Range(row, 1, row, 8).Style.Font.FontColor = XLColor.DarkRed;
                        sheet.Range(row, 1, row, 8).Style.Font.Bold = true;
                        break;
                    case Severity.High:
                        sheet.Range(row, 1, row, 8).Style.Font.FontColor = XLColor.DarkOrange;
                        break;
                }
                
                row++;
            }

            // Add autofilter
            var dataRange = sheet.Range(1, 1, row - 1, 8);
            dataRange.SetAutoFilter();
            
            sheet.Columns().AdjustToContents();
        }

        private void CreatePackageInventorySheet(XLWorkbook workbook, AnalysisResult result)
        {
            var sheet = workbook.Worksheets.Add("Package Inventory");
            
            sheet.Cell(1, 1).Value = "Package Name";
            sheet.Cell(1, 2).Value = "Version";
            sheet.Cell(1, 3).Value = "Usage Count";
            sheet.Range(1, 1, 1, 3).Style.Font.Bold = true;
            sheet.Range(1, 1, 1, 3).Style.Fill.BackgroundColor = XLColor.LightGray;
            
            int row = 2;
            foreach (var package in result.PackageInventory.OrderBy(p => p.Key))
            {
                sheet.Cell(row, 1).Value = package.Key;
                sheet.Cell(row, 3).Value = package.Value;
                row++;
            }
            
            if (row == 2)
            {
                sheet.Cell(2, 1).Value = "No package inventory data collected";
            }
            
            sheet.Columns().AdjustToContents();
        }

        private void CreateConfigurationAuditSheet(XLWorkbook workbook, AnalysisResult result)
        {
            var sheet = workbook.Worksheets.Add("Configuration Audit");
            
            var configFindings = result.Findings
                .Where(f => f.Category == AnalyzerCategory.Configuration)
                .ToList();
            
            sheet.Cell(1, 1).Value = "File";
            sheet.Cell(1, 2).Value = "Issue";
            sheet.Cell(1, 3).Value = "Recommendation";
            sheet.Cell(1, 4).Value = "Severity";
            sheet.Range(1, 1, 1, 4).Style.Font.Bold = true;
            sheet.Range(1, 1, 1, 4).Style.Fill.BackgroundColor = XLColor.LightGray;
            
            int row = 2;
            foreach (var finding in configFindings)
            {
                sheet.Cell(row, 1).Value = Path.GetFileName(finding.FilePath);
                sheet.Cell(row, 2).Value = finding.Message;
                sheet.Cell(row, 3).Value = finding.Recommendation;
                sheet.Cell(row, 4).Value = finding.Severity.ToString();
                row++;
            }
            
            if (row == 2)
            {
                sheet.Cell(2, 1).Value = "No configuration issues found";
            }
            
            sheet.Columns().AdjustToContents();
        }
    }

    public class MarkdownReportGenerator : IReportGenerator
    {
        public void Generate(AnalysisResult result, string outputPath)
        {
            var sb = new StringBuilder();
            var severityCounts = result.GetSeverityCounts();
            var categoryCounts = result.GetCategoryCounts();
            var effortDays = result.CalculateEffortDays();
            
            sb.AppendLine("# Migration Analysis Summary");
            sb.AppendLine();
            sb.AppendLine($"**Analysis Date:** {result.AnalysisDate:yyyy-MM-dd HH:mm:ss}  ");
            sb.AppendLine($"**Solution:** `{result.SolutionPath}`  ");
            sb.AppendLine($"**Duration:** {result.Duration.TotalSeconds:F2} seconds  ");
            sb.AppendLine($"**Files Scanned:** {result.TotalFilesScanned}  ");
            sb.AppendLine();
            
            sb.AppendLine("## 📊 Executive Summary");
            sb.AppendLine();
            sb.AppendLine($"- **Total Findings:** {result.Findings.Count}");
            sb.AppendLine($"- **Estimated Migration Effort:** {effortDays:F1} developer-days");
            sb.AppendLine();
            
            sb.AppendLine("### Findings by Severity");
            sb.AppendLine();
            foreach (var severity in Enum.GetValues<Severity>())
            {
                var count = severityCounts.GetValueOrDefault(severity, 0);
                if (count > 0)
                {
                    var icon = severity switch
                    {
                        Severity.Critical => "🔴",
                        Severity.High => "🟠",
                        Severity.Medium => "🟡",
                        Severity.Low => "🟢",
                        Severity.Info => "🔵",
                        _ => "⚪"
                    };
                    sb.AppendLine($"- {icon} **{severity}:** {count}");
                }
            }
            sb.AppendLine();
            
            sb.AppendLine("### Findings by Category");
            sb.AppendLine();
            foreach (var category in categoryCounts.OrderByDescending(c => c.Value))
            {
                sb.AppendLine($"- **{category.Key}:** {category.Value}");
            }
            sb.AppendLine();
            
            sb.AppendLine("## 🎯 Prioritized Action Plan");
            sb.AppendLine();
            
            var criticalCount = severityCounts.GetValueOrDefault(Severity.Critical, 0);
            var highCount = severityCounts.GetValueOrDefault(Severity.High, 0);
            var mediumCount = severityCounts.GetValueOrDefault(Severity.Medium, 0);
            
            if (criticalCount > 0)
            {
                sb.AppendLine($"### Priority 1: Critical Issues ({criticalCount} items, ~{criticalCount * 5} days)");
                sb.AppendLine();
                sb.AppendLine("These issues will **block migration** and must be addressed first:");
                sb.AppendLine();
                var criticalFindings = result.Findings.Where(f => f.Severity == Severity.Critical).Take(10);
                foreach (var finding in criticalFindings)
                {
                    sb.AppendLine($"- `{Path.GetFileName(finding.FilePath)}:{finding.LineNumber}` - {finding.Message}");
                }
                if (criticalCount > 10)
                    sb.AppendLine($"- _(... and {criticalCount - 10} more)_");
                sb.AppendLine();
            }
            
            if (highCount > 0)
            {
                sb.AppendLine($"### Priority 2: High Priority Issues ({highCount} items, ~{highCount * 3} days)");
                sb.AppendLine();
                sb.AppendLine("These require significant code changes:");
                sb.AppendLine();
                var highFindings = result.Findings.Where(f => f.Severity == Severity.High).GroupBy(f => f.Category);
                foreach (var group in highFindings)
                {
                    sb.AppendLine($"- **{group.Key}:** {group.Count()} issues");
                }
                sb.AppendLine();
            }
            
            if (mediumCount > 0)
            {
                sb.AppendLine($"### Priority 3: Medium Priority Issues ({mediumCount} items, ~{mediumCount} days)");
                sb.AppendLine();
                sb.AppendLine("Configuration and moderate code changes needed.");
                sb.AppendLine();
            }
            
            sb.AppendLine("## 📋 Top Issues by Category");
            sb.AppendLine();
            
            foreach (var category in categoryCounts.OrderByDescending(c => c.Value).Take(5))
            {
                sb.AppendLine($"### {category.Key} ({category.Value} issues)");
                sb.AppendLine();
                
                var topFindings = result.Findings
                    .Where(f => f.Category == category.Key)
                    .OrderBy(f => f.Severity)
                    .Take(5);
                
                foreach (var finding in topFindings)
                {
                    sb.AppendLine($"**{finding.Severity}** - `{Path.GetFileName(finding.FilePath)}:{finding.LineNumber}`");
                    sb.AppendLine($"> {finding.Message}");
                    sb.AppendLine($"> **Recommendation:** {finding.Recommendation}");
                    sb.AppendLine();
                }
            }
            
            sb.AppendLine("## 📝 Next Steps");
            sb.AppendLine();
            sb.AppendLine("1. Review the detailed HTML or Excel report for complete findings");
            sb.AppendLine("2. Address all Critical issues before proceeding");
            sb.AppendLine("3. Create migration tasks in your project management tool");
            sb.AppendLine("4. Set up a Linux container test environment");
            sb.AppendLine("5. Implement changes incrementally with testing");
            sb.AppendLine("6. Re-run this analyzer after changes to track progress");
            sb.AppendLine();
            
            sb.AppendLine("---");
            sb.AppendLine($"*Report generated by MigrationAnalyzer on {result.AnalysisDate:yyyy-MM-dd HH:mm:ss}*");
            
            File.WriteAllText(Path.Combine(outputPath, "migration-summary.md"), sb.ToString());
        }
    }
}

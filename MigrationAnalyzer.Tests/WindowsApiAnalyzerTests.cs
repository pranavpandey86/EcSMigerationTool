using MigrationAnalyzer.Analyzers;
using MigrationAnalyzer.Core.Models;
using Xunit;

namespace MigrationAnalyzer.Tests
{
    public class WindowsApiAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_Registry_Usage()
        {
            var source = @"
                using Microsoft.Win32;
                
                class Program {
                    void Main() {
                        var key = Registry.LocalMachine;
                    }
                }";

            var analyzer = new WindowsApiAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("Microsoft.Win32"));
            Assert.Equal(Severity.High, findings[0].Severity);
        }

        [Fact]
        public async Task No_Findings_For_Clean_Code()
        {
            var source = @"
                using System;
                
                class Program {
                    void Main() {
                        Console.WriteLine(""Hello"");
                    }
                }";

            var analyzer = new WindowsApiAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.Empty(findings);
        }
    }
}

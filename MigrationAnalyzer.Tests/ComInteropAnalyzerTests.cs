using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MigrationAnalyzer.Analyzers;
using Xunit;

namespace MigrationAnalyzer.Tests
{
    public class ComInteropAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_ComImport_Attribute()
        {
            var source = @"
                using System.Runtime.InteropServices;
                
                [ComImport]
                [Guid(""00000000-0000-0000-0000-000000000000"")]
                interface IExcelApplication
                {
                    void DoSomething();
                }";

            var analyzer = new ComInteropAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("ComImport"));
            Assert.Contains(findings, f => f.Severity == Core.Models.Severity.Critical || f.Severity == Core.Models.Severity.High);
        }

        [Fact]
        public async Task Detects_GetTypeFromProgID()
        {
            var source = @"
                using System;
                
                class Program
                {
                    void Test()
                    {
                        Type excelType = Type.GetTypeFromProgID(""Excel.Application"");
                    }
                }";

            var analyzer = new ComInteropAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("GetTypeFromProgID"));
            Assert.Contains(findings, f => f.Severity == Core.Models.Severity.Critical);
        }

        [Fact]
        public async Task Detects_ReleaseComObject()
        {
            var source = @"
                using System.Runtime.InteropServices;
                
                class Program
                {
                    void Test(object comObject)
                    {
                        Marshal.ReleaseComObject(comObject);
                    }
                }";

            var analyzer = new ComInteropAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("ReleaseComObject"));
        }

        [Fact]
        public async Task Detects_ComTypes_Namespace()
        {
            var source = @"
                using System.Runtime.InteropServices.ComTypes;
                
                class Program
                {
                    void Test()
                    {
                        // Using COM types
                    }
                }";

            var analyzer = new ComInteropAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("COM"));
        }

        [Fact]
        public async Task No_Findings_For_Regular_Interface()
        {
            var source = @"
                interface IMyInterface
                {
                    void DoSomething();
                }";

            var analyzer = new ComInteropAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.Empty(findings);
        }
    }
}

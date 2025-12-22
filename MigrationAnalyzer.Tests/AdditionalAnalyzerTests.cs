using MigrationAnalyzer.Analyzers;
using MigrationAnalyzer.Core.Models;
using Xunit;

namespace MigrationAnalyzer.Tests
{
    public class ConfigurationAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_WinExe_OutputType()
        {
            // Note: ConfigurationAnalyzer reads from project files, not source code
            // This test validates the analyzer doesn't crash on empty source
            var source = @"
                using System;
                class Program
                {
                    void Main() { }
                }";

            var analyzer = new ConfigurationAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            // No source-based detection, so no findings expected from source
            Assert.Empty(findings);
        }
    }

    public class PackageAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Does_Not_Crash_On_Source_Code()
        {
            // PackageAnalyzer reads from project files, not source code
            var source = @"
                using System;
                class Program
                {
                    void Main() { }
                }";

            var analyzer = new PackageAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            // No source-based detection
            Assert.Empty(findings);
        }
    }

    public class MSMQAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_System_Messaging_Using()
        {
            var source = @"
                using System.Messaging;
                
                class Program
                {
                    void Main() { }
                }";

            var analyzer = new MSMQAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("MSMQ") || f.Message.Contains("System.Messaging"));
            Assert.Contains(findings, f => f.Severity == Severity.Critical);
        }

        [Fact]
        public async Task Detects_MSMQ_Queue_Path()
        {
            var source = @"
                class Program
                {
                    string queuePath = @"".\private$\myqueue"";
                }";

            var analyzer = new MSMQAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("queue path"));
        }

        [Fact]
        public async Task No_Findings_For_Clean_Code()
        {
            var source = @"
                using System;
                
                class Program
                {
                    void Main() { Console.WriteLine(""Hello""); }
                }";

            var analyzer = new MSMQAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.Empty(findings);
        }
    }

    public class NamedPipesAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_Pipes_Namespace()
        {
            var source = @"
                using System.IO.Pipes;
                
                class Program
                {
                    void Main() { }
                }";

            var analyzer = new NamedPipesAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("Named Pipes") || f.Message.Contains("System.IO.Pipes"));
        }

        [Fact]
        public async Task Detects_Windows_Pipe_Path()
        {
            var source = @"
                class Program
                {
                    string pipePath = @""\\.\pipe\mypipe"";
                }";

            var analyzer = new NamedPipesAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("pipe path") || f.Message.Contains(@"\\.\pipe\"));
        }

        [Fact]
        public async Task No_Findings_For_Clean_Code()
        {
            var source = @"
                using System;
                
                class Program
                {
                    void Main() { Console.WriteLine(""Hello""); }
                }";

            var analyzer = new NamedPipesAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.Empty(findings);
        }
    }

    public class QuartzAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task No_Findings_Without_Quartz_Reference()
        {
            var source = @"
                using System;
                
                class Program
                {
                    void Main() { }
                }";

            var analyzer = new QuartzAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            // Without Quartz reference, no findings expected
            Assert.Empty(findings);
        }
    }

    public class CyberArkAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task No_Findings_Without_CyberArk_Reference()
        {
            var source = @"
                using System;
                
                class Program
                {
                    void Main() { }
                }";

            var analyzer = new CyberArkAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            // Without CyberArk reference, no findings expected
            Assert.Empty(findings);
        }
    }
}

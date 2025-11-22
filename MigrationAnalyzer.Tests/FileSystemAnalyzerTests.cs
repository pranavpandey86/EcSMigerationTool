using MigrationAnalyzer.Analyzers;
using MigrationAnalyzer.Core.Models;
using Xunit;

namespace MigrationAnalyzer.Tests
{
    public class FileSystemAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_Hardcoded_Windows_Path()
        {
            var source = @"
                class Program {
                    string path = ""C:\\Temp\\myfile.txt"";
                }";

            var analyzer = new FileSystemAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("Hardcoded Windows path"));
            Assert.Equal(Severity.High, findings[0].Severity);
        }

        [Fact]
        public async Task Detects_UNC_Path()
        {
            var source = @"
                class Program {
                    string path = ""\\\\server\\share\\folder"";
                }";

            var analyzer = new FileSystemAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("UNC path"));
        }

        [Fact]
        public async Task Detects_Backslash_Separators()
        {
            var source = @"
                class Program {
                    string path = ""data\\files\\document.txt"";
                }";

            var analyzer = new FileSystemAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("path separator"));
        }

        [Fact]
        public async Task No_Findings_For_Relative_Path_With_Forward_Slashes()
        {
            var source = @"
                class Program {
                    string path = ""data/files/document.txt"";
                }";

            var analyzer = new FileSystemAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.Empty(findings);
        }

        [Fact]
        public async Task No_Findings_For_Path_Combine()
        {
            var source = @"
                using System.IO;
                
                class Program {
                    string GetPath() {
                        return Path.Combine(""data"", ""files"", ""document.txt"");
                    }
                }";

            var analyzer = new FileSystemAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.Empty(findings);
        }
    }
}

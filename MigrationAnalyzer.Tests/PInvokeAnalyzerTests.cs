using MigrationAnalyzer.Analyzers;
using MigrationAnalyzer.Core.Models;
using Xunit;

namespace MigrationAnalyzer.Tests
{
    public class PInvokeAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_Kernel32_PInvoke()
        {
            var source = @"
                using System.Runtime.InteropServices;
                
                class Program {
                    [DllImport(""kernel32.dll"")]
                    static extern bool Beep(uint frequency, uint duration);
                }";

            var analyzer = new PInvokeAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("kernel32.dll"));
            Assert.Equal(Severity.Critical, findings[0].Severity);
        }

        [Fact]
        public async Task Detects_User32_PInvoke()
        {
            var source = @"
                using System.Runtime.InteropServices;
                
                class Program {
                    [DllImport(""user32.dll"")]
                    static extern int MessageBox(int hWnd, string text, string caption, uint type);
                }";

            var analyzer = new PInvokeAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("user32.dll"));
            Assert.Equal(Severity.Critical, findings[0].Severity);
        }

        [Fact]
        public async Task Detects_Advapi32_PInvoke()
        {
            var source = @"
                using System.Runtime.InteropServices;
                
                class Program {
                    [DllImport(""advapi32.dll"", SetLastError = true)]
                    static extern bool LogonUser(string lpszUsername, string lpszDomain, string lpszPassword,
                        int dwLogonType, int dwLogonProvider, out IntPtr phToken);
                }";

            var analyzer = new PInvokeAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("advapi32.dll"));
        }

        [Fact]
        public async Task No_Findings_For_No_PInvoke()
        {
            var source = @"
                class Program {
                    void Main() {
                        Console.WriteLine(""Hello World"");
                    }
                }";

            var analyzer = new PInvokeAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.Empty(findings);
        }
    }
}

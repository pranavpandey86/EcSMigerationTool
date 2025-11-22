using MigrationAnalyzer.Analyzers;
using MigrationAnalyzer.Core.Models;
using Xunit;

namespace MigrationAnalyzer.Tests
{
    public class AuthenticationAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_WindowsIdentity_Usage()
        {
            var source = @"
                using System.Security.Principal;
                
                class Program {
                    void Main() {
                        WindowsIdentity identity = WindowsIdentity.GetCurrent();
                    }
                }";

            var analyzer = new AuthenticationAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("WindowsIdentity"));
            Assert.Equal(Severity.High, findings[0].Severity);
        }

        [Fact]
        public async Task Detects_IntegratedSecurity_In_ConnectionString()
        {
            var source = @"
                class Program {
                    string connString = ""Server=myServer;Database=myDB;Integrated Security=true;"";
                }";

            var analyzer = new AuthenticationAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("Integrated Security"));
        }

        [Fact]
        public async Task Detects_DirectoryServices_Usage()
        {
            var source = @"
                using System.DirectoryServices;
                
                class Program {
                    void QueryAD() {
                        var entry = new DirectoryEntry(""LDAP://DC=domain,DC=com"");
                    }
                }";

            var analyzer = new AuthenticationAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("DirectoryServices"));
        }

        [Fact]
        public async Task No_Findings_For_ClaimsIdentity()
        {
            var source = @"
                using System.Security.Claims;
                
                class Program {
                    void Main() {
                        var identity = new ClaimsIdentity();
                    }
                }";

            var analyzer = new AuthenticationAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.Empty(findings);
        }
    }
}

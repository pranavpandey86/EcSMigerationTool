using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MigrationAnalyzer.Analyzers;
using Xunit;

namespace MigrationAnalyzer.Tests
{
    public class IISCompatibilityAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_HttpContext_Current()
        {
            var source = @"
                using System.Web;
                
                class Program
                {
                    void Test()
                    {
                        var context = HttpContext.Current;
                    }
                }";

            var analyzer = new IISCompatibilityAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("HttpContext.Current"));
        }

        [Fact]
        public async Task Detects_Server_MapPath()
        {
            var source = @"
                using System.Web;
                
                class Program
                {
                    void Test()
                    {
                        var path = HttpContext.Current.Server.MapPath(""~/App_Data"");
                    }
                }";

            var analyzer = new IISCompatibilityAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("Server.MapPath") || f.Message.Contains("System.Web"));
        }

        [Fact]
        public async Task Detects_System_Web_Namespace()
        {
            var source = @"
                using System.Web;
                using System.Web.UI;
                
                class Program
                {
                    void Test()
                    {
                        // Using System.Web
                    }
                }";

            var analyzer = new IISCompatibilityAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("System.Web"));
        }

        [Fact]
        public async Task Detects_HttpApplication_Inheritance()
        {
            var source = @"
                using System.Web;
                
                public class Global : HttpApplication
                {
                    protected void Application_Start()
                    {
                        // Startup logic
                    }
                }";

            var analyzer = new IISCompatibilityAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("HttpApplication") || f.Message.Contains("System.Web"));
        }

        [Fact]
        public async Task No_Findings_For_AspNetCore_HttpContext()
        {
            var source = @"
                using Microsoft.AspNetCore.Http;
                
                class Program
                {
                    void Test(HttpContext context)
                    {
                        var path = context.Request.Path;
                    }
                }";

            var analyzer = new IISCompatibilityAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.Empty(findings);
        }
    }
}

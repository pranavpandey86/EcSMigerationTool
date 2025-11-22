using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MigrationAnalyzer.Analyzers;
using Xunit;

namespace MigrationAnalyzer.Tests
{
    public class PlatformDetectionAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_Environment_OSVersion()
        {
            var source = @"
                using System;
                
                class Program
                {
                    void Test()
                    {
                        var platform = Environment.OSVersion.Platform;
                    }
                }";

            var analyzer = new PlatformDetectionAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("Environment.OSVersion"));
        }

        [Fact]
        public async Task Detects_RuntimeInformation_IsOSPlatform()
        {
            var source = @"
                using System.Runtime.InteropServices;
                
                class Program
                {
                    void Test()
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            // Windows code
                        }
                    }
                }";

            var analyzer = new PlatformDetectionAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("RuntimeInformation") || f.Message.Contains("OSPlatform.Windows"));
        }

        [Fact]
        public async Task Detects_OperatingSystem_IsWindows()
        {
            var source = @"
                using System;
                
                class Program
                {
                    void Test()
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            // Windows code
                        }
                    }
                }";

            var analyzer = new PlatformDetectionAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("OperatingSystem.IsWindows"));
        }

        [Fact]
        public async Task Detects_PlatformID_Win32NT()
        {
            var source = @"
                using System;
                
                class Program
                {
                    void Test()
                    {
                        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                        {
                            // Windows code
                        }
                    }
                }";

            var analyzer = new PlatformDetectionAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("PlatformID") || f.Message.Contains("Win32NT") || f.Message.Contains("Environment.OSVersion"));
        }

        [Fact]
        public async Task Detects_Linux_Platform_Check()
        {
            var source = @"
                using System;
                
                class Program
                {
                    void Test()
                    {
                        if (OperatingSystem.IsLinux())
                        {
                            // Linux code
                        }
                    }
                }";

            var analyzer = new PlatformDetectionAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("Linux") && f.Severity == Core.Models.Severity.Info);
        }
    }
}

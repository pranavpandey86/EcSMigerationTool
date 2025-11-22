using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MigrationAnalyzer.Analyzers;
using Xunit;

namespace MigrationAnalyzer.Tests
{
    public class CryptographyAnalyzerTests : AnalyzerTestBase
    {
        [Fact]
        public async Task Detects_ProtectedData_Protect()
        {
            var source = @"
                using System.Security.Cryptography;
                
                class Program
                {
                    void Test(byte[] data)
                    {
                        byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                    }
                }";

            var analyzer = new CryptographyAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("ProtectedData.Protect"));
            Assert.Contains(findings, f => f.Severity == Core.Models.Severity.Critical);
        }

        [Fact]
        public async Task Detects_ProtectedData_Unprotect()
        {
            var source = @"
                using System.Security.Cryptography;
                
                class Program
                {
                    void Test(byte[] data)
                    {
                        byte[] decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine);
                    }
                }";

            var analyzer = new CryptographyAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("ProtectedData.Unprotect"));
            Assert.Contains(findings, f => f.Severity == Core.Models.Severity.Critical);
        }

        [Fact]
        public async Task Detects_X509Store_LocalMachine()
        {
            var source = @"
                using System.Security.Cryptography.X509Certificates;
                
                class Program
                {
                    void Test()
                    {
                        var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                        store.Open(OpenFlags.ReadOnly);
                    }
                }";

            var analyzer = new CryptographyAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("X509Store") || f.Message.Contains("Certificate"));
        }

        [Fact]
        public async Task Detects_DataProtectionScope()
        {
            var source = @"
                using System.Security.Cryptography;
                
                class Program
                {
                    DataProtectionScope GetScope()
                    {
                        return DataProtectionScope.CurrentUser;
                    }
                }";

            var analyzer = new CryptographyAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Message.Contains("DataProtectionScope") || f.Message.Contains("DPAPI"));
        }

        [Fact]
        public async Task No_Findings_For_AES_Encryption()
        {
            var source = @"
                using System.Security.Cryptography;
                
                class Program
                {
                    void Test()
                    {
                        using (var aes = Aes.Create())
                        {
                            aes.GenerateKey();
                        }
                    }
                }";

            var analyzer = new CryptographyAnalyzer();
            var findings = await RunAnalyzerAsync(analyzer, source);

            Assert.Empty(findings);
        }
    }
}

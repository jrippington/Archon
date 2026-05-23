using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Integrations.ExternalServices;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.Integrations.Tests.ExternalServices
{
    /// <summary>
    /// Verifies that the WP010 storage, SMTP/email, and payment-provider extractor emits safe external-service graph facts.
    /// </summary>
    public sealed class ExternalServiceIntegrationExtractorTests
    {
        /// <summary>
        /// Confirms Azure Blob Storage, Azure File Storage, and generic storage abstractions produce target, operation, configuration, and unknown facts.
        /// </summary>
        [Fact]
        public void Extract_WhenStorageEvidenceExists_ShouldEmitStorageFactsAndHints()
        {
            // The fixture uses source-only storage clients and never connects to Azure Storage or an external storage provider.
            ExternalServiceIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> externalServices = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.ExternalService)
                .ToArray();

            Assert.Empty(result.Errors);
            Assert.Contains(externalServices, node => node.DisplayName == "archive-account/images" && ContainsMetadata(node, "\"provider\":\"AzureBlobStorage\""));
            Assert.Contains(result.Snapshot.Edges, edge => ContainsMetadata(edge, "\"operationHint\":\"Write\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesConfig && edge.TargetNodeStableKey.Value == "config://Storage:Blob:ConnectionString");
            Assert.Contains(externalServices, node => node.DisplayName == "archive-account/files/reports" && ContainsMetadata(node, "\"provider\":\"AzureFileStorage\""));
            Assert.Contains(result.Snapshot.Evidence, evidence => evidence.ContainingSymbol == "StorageWorkflow" && evidence.EndLine >= 108);
            Assert.Contains(externalServices, node => node.DisplayName == "backup-bucket/invoices/2025.json" && ContainsMetadata(node, "\"provider\":\"StorageAbstraction\""));
            Assert.Contains(externalServices, node => node.UnknownState.HasUnknownData && ContainsMetadata(node, "\"integrationCategory\":\"Storage\""));
            Assert.Contains(result.Warnings, warning => warning.Contains("runtime-computed", StringComparison.OrdinalIgnoreCase) || warning.Contains("unresolved storage", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirms SMTP client and email sender abstraction evidence emits email facts without leaking credentials or recipients.
        /// </summary>
        [Fact]
        public void Extract_WhenEmailEvidenceExists_ShouldEmitEmailFactsAndRedactSecrets()
        {
            // SMTP evidence includes host, credential, and mail-message construction, which must be represented without exposing secret values.
            ExternalServiceIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> externalServices = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.ExternalService)
                .ToArray();

            Assert.Contains(externalServices, node => node.DisplayName == "smtp.contoso.test" && ContainsMetadata(node, "\"provider\":\"SMTP\""));
            Assert.Contains(result.Snapshot.Edges, edge => ContainsMetadata(edge, "\"operationName\":\"SendMailAsync\""));
            Assert.Contains(externalServices, node => node.DisplayName == "TransactionalEmailSender" && ContainsMetadata(node, "\"provider\":\"EmailAbstraction\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesConfig && edge.TargetNodeStableKey.Value == "config://Email:Smtp:Host");
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview));
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Warnings, ContainsSensitiveText);
        }

        /// <summary>
        /// Confirms payment SDK and HTTP wrapper evidence emits provider facts while aggressively redacting payment data.
        /// </summary>
        [Fact]
        public void Extract_WhenPaymentEvidenceExists_ShouldEmitPaymentFactsAndAggressivelyRedact()
        {
            // Payment evidence deliberately includes API keys, tokens, card-like data, and customer identifiers so redaction can be asserted across output surfaces.
            ExternalServiceIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> externalServices = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.ExternalService)
                .ToArray();

            Assert.Contains(externalServices, node => node.DisplayName == "Stripe" && ContainsMetadata(node, "\"provider\":\"Stripe\"") && ContainsMetadata(node, "\"operationHint\":\"Charge\""));
            Assert.Contains(externalServices, node => node.DisplayName == "Payments:Gateway:Endpoint" && ContainsMetadata(node, "\"provider\":\"PaymentHttpWrapper\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesConfig && edge.TargetNodeStableKey.Value == "config://Payments:Stripe:ApiKey");
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview));
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Warnings, ContainsSensitiveText);
        }

        /// <summary>
        /// Confirms duplicate observations collapse to deterministic keys and dynamic values remain explicit unknowns.
        /// </summary>
        [Fact]
        public void Extract_WhenDuplicateAndDynamicEvidenceExists_ShouldDeduplicateAndWarn()
        {
            // The fixture repeats storage calls and uses runtime-computed targets, so the graph should remain deduplicated and warning-backed.
            ExternalServiceIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<string> nodeKeys = result.Snapshot.Nodes.Select(node => node.StableKey.Value).ToArray();
            IReadOnlyList<string> edgeKeys = result.Snapshot.Edges.Select(edge => edge.StableKey.Value).ToArray();

            Assert.Equal(nodeKeys.Count, nodeKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(edgeKeys.Count, edgeKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.ExternalService && node.UnknownState.HasUnknownData);
            Assert.Contains(result.Warnings, warning => warning.Contains("runtime-computed", StringComparison.OrdinalIgnoreCase) || warning.Contains("unresolved", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Determines whether a node metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="node">The architecture node whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the node metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureNode node, string expectedFragment)
        {
            // Canonical JSON comparisons keep assertions stable regardless of metadata dictionary construction order.
            return node.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an edge metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="edge">The architecture edge whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the edge metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureEdge edge, string expectedFragment)
        {
            // Edge metadata assertions verify call-specific hints after target node deduplication.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether output text contains sensitive fixture data that must never leave the extractor.
        /// </summary>
        /// <param name="value">The output value to inspect.</param>
        /// <returns><see langword="true" /> when sensitive fixture text appears; otherwise, <see langword="false" />.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // The check covers storage secrets, SMTP passwords, payment API keys, tokens, card numbers, and customer identifiers used in the fixture.
            return value?.Contains("DefaultEndpointsProtocol", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("AccountKey", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("smtp-secret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("sk_test_secret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("tok_visa_secret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("4242424242424242", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("cus_secret", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Builds the shared repository/Roslyn fixture and invokes the production external-service extractor.
        /// </summary>
        /// <returns>The external-service extraction result for the fixture repository.</returns>
        private static ExternalServiceIntegrationExtractionResult ExtractFixture()
        {
            // The fixture uses local source and configuration artifacts only, ensuring tests never contact storage, SMTP, or payment services.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-external-service-integration-fixture", Guid.NewGuid().ToString("N"));
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.ExternalServices");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(projectDirectory, "appsettings.json"), "{ \"Storage\": { \"Blob\": { \"ConnectionString\": \"DefaultEndpointsProtocol=https;AccountKey=storage-secret\" } }, \"Email\": { \"Smtp\": { \"Host\": \"smtp.contoso.test\", \"Password\": \"smtp-secret\" } }, \"Payments\": { \"Stripe\": { \"ApiKey\": \"sk_test_secret\" }, \"Gateway\": { \"Endpoint\": \"https://payments.example.test\" } } }");
            string sourcePath = Path.Combine(projectDirectory, "ExternalServices.cs");
            string source = """
                namespace Azure.Storage.Blobs
                {
                    using System.Threading.Tasks;

                    public sealed class BlobServiceClient
                    {
                        public BlobServiceClient(string connectionString) { }
                        public BlobContainerClient GetBlobContainerClient(string blobContainerName) => new(blobContainerName);
                    }

                    public sealed class BlobContainerClient
                    {
                        public BlobContainerClient(string name) { }
                        public BlobClient GetBlobClient(string blobName) => new(blobName);
                    }

                    public sealed class BlobClient
                    {
                        public BlobClient(string name) { }
                        public Task UploadAsync(string path) => Task.CompletedTask;
                        public Task DownloadToAsync(string path) => Task.CompletedTask;
                        public Task DeleteIfExistsAsync() => Task.CompletedTask;
                    }
                }

                namespace Azure.Storage.Files.Shares
                {
                    using System.Threading.Tasks;

                    public sealed class ShareClient
                    {
                        public ShareClient(string connectionString, string shareName) { }
                        public ShareDirectoryClient GetDirectoryClient(string directoryName) => new(directoryName);
                    }

                    public sealed class ShareDirectoryClient
                    {
                        public ShareDirectoryClient(string name) { }
                        public ShareFileClient GetFileClient(string fileName) => new(fileName);
                    }

                    public sealed class ShareFileClient
                    {
                        public ShareFileClient(string name) { }
                        public Task DownloadAsync() => Task.CompletedTask;
                    }
                }

                namespace System.Net.Mail
                {
                    using System.Net;
                    using System.Threading.Tasks;

                    public sealed class MailMessage
                    {
                        public MailMessage(string from, string to, string subject, string body) { }
                    }

                    public sealed class SmtpClient
                    {
                        public SmtpClient(string host, int port) { }
                        public ICredentialsByHost? Credentials { get; set; }
                        public Task SendMailAsync(MailMessage message) => Task.CompletedTask;
                    }
                }

                namespace Stripe
                {
                    using System.Threading.Tasks;

                    public sealed class ChargeCreateOptions
                    {
                        public long Amount { get; set; }
                        public string? Currency { get; set; }
                        public string? Source { get; set; }
                        public string? Customer { get; set; }
                    }

                    public sealed class ChargeService
                    {
                        public ChargeService(string apiKey) { }
                        public Task CreateAsync(ChargeCreateOptions options) => Task.CompletedTask;
                    }
                }

                namespace Sample.ExternalServices
                {
                    using Azure.Storage.Blobs;
                    using Azure.Storage.Files.Shares;
                    using Stripe;
                    using System.Net;
                    using System.Net.Mail;
                    using System.Threading.Tasks;

                    public sealed class StorageWorkflow
                    {
                        public async Task RunAsync(string dynamicContainer)
                        {
                            var blobService = new BlobServiceClient("DefaultEndpointsProtocol=https;AccountKey=storage-secret");
                            var container = blobService.GetBlobContainerClient("images");
                            var blob = container.GetBlobClient("avatars/user-1.png");
                            await blob.UploadAsync("local-file.txt");
                            await blob.UploadAsync("local-file.txt");
                            await blob.DeleteIfExistsAsync();
                            var dynamic = blobService.GetBlobContainerClient(dynamicContainer);
                            await dynamic.GetBlobClient("runtime-name").DownloadToAsync("runtime-file.txt");
                            var share = new ShareClient("DefaultEndpointsProtocol=https;AccountKey=storage-secret", "files");
                            await share.GetDirectoryClient("reports").GetFileClient("q1.pdf").DownloadAsync();
                        }
                    }

                    public interface IObjectStore
                    {
                        Task WriteAsync(string bucket, string path, object payload);
                    }

                    public sealed class GenericStorageWorkflow
                    {
                        public Task SaveAsync(IObjectStore store) => store.WriteAsync("backup-bucket", "invoices/2025.json", new object());
                    }

                    public interface IEmailSender
                    {
                        Task SendAsync(string template, string recipient, object model);
                    }

                    public sealed class EmailWorkflow
                    {
                        public async Task SendAsync(IEmailSender emailSender)
                        {
                            using var client = new SmtpClient("smtp.contoso.test", 587);
                            client.Credentials = new NetworkCredential("smtp-user", "smtp-secret");
                            var message = new MailMessage("noreply@contoso.test", "customer@example.test", "Invoice", "token tok_visa_secret");
                            await client.SendMailAsync(message);
                            await emailSender.SendAsync("ReceiptTemplate", "customer@example.test", new { CustomerId = "cus_secret" });
                        }
                    }

                    public interface IPaymentGateway
                    {
                        Task ChargeAsync(string endpointKey, string token, decimal amount);
                    }

                    public sealed class PaymentWorkflow
                    {
                        public async Task ChargeAsync(IPaymentGateway gateway)
                        {
                            var service = new ChargeService("sk_test_secret");
                            await service.CreateAsync(new ChargeCreateOptions { Amount = 4200, Currency = "usd", Source = "tok_visa_secret", Customer = "cus_secret" });
                            await gateway.ChargeAsync("Payments:Gateway:Endpoint", "4242424242424242", 42m);
                        }
                    }
                }
                """;
            File.WriteAllText(sourcePath, source);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.ExternalServices",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Task).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest semanticRequest = new(repositoryRoot, "src/Sample.ExternalServices/Sample.ExternalServices.csproj", sourcePath, syntaxTree, semanticModel);
            ExternalServiceIntegrationExtractor extractor = new();

            return extractor.Extract(new ExternalServiceIntegrationExtractionRequest(StableKeyGenerator.ForRepository("Sample.ExternalServices"), repositoryRoot, [semanticRequest]));
        }
    }
}

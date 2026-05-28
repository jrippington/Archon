using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.AspNet.Runtime;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.Tests.AspNet
{
    /// <summary>
    /// Verifies the non-HTTP runtime consumer extractor contributes graph-ready facts for scheduled jobs, message consumers, and service-style host loops.
    /// </summary>
    public sealed class NonHttpRuntimeConsumerExtractorTests
    {
        /// <summary>
        /// Verifies Hangfire-style recurring job registration emits scheduled-job method facts with deterministic schedule metadata.
        /// </summary>
        [Fact]
        public void Extract_WhenHangfireRecurringJobExists_ShouldContributeScheduledJobFact()
        {
            // The fixture uses local Hangfire-like declarations so the extractor can reason from source shape without package restore or target execution.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-non-http-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Worker"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Worker", "Jobs.cs");
                File.WriteAllText(documentPath, CreateHangfireScheduledJobSource());
                SemanticExtractionRequest semanticRequest = CreateCSharpSemanticRequest(repositoryRoot, "src/Customer.Worker/Customer.Worker.csproj", documentPath);
                NonHttpRuntimeConsumerExtractor extractor = new();

                NonHttpRuntimeConsumerExtractionResult result = extractor.Extract(new NonHttpRuntimeConsumerExtractionRequest(new StableKey("snapshot://non-http-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode scheduledJobNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.Metadata.ToCanonicalJson().Contains("\"runtimeKind\":\"ScheduledJob\"", StringComparison.Ordinal));
                Assert.Equal("SyncCustomers", scheduledJobNode.DisplayName);
                string metadata = scheduledJobNode.Metadata.ToCanonicalJson();
                Assert.Contains("\"schedulerTechnology\":\"Hangfire\"", metadata, StringComparison.Ordinal);
                Assert.Contains("\"scheduleExpression\":\"0 0 * * *\"", metadata, StringComparison.Ordinal);
                Assert.Contains("\"detectionMode\":\"HangfireRecurringJob\"", metadata, StringComparison.Ordinal);
                Assert.False(scheduledJobNode.UnknownState.HasUnknownData);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SymbolName == "AddOrUpdate" && evidence.FilePath.Value == "src/Customer.Worker/Jobs.cs");
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies scheduler registrations with computed schedules keep the schedule expression unresolved without losing the scheduled method fact.
        /// </summary>
        [Fact]
        public void Extract_WhenScheduledJobUsesComputedSchedule_ShouldPreserveJobFactWithoutScheduleExpression()
        {
            // Computed schedules are safe to represent as scheduled-job facts, but the scheduleExpression metadata must not invent a cron value.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-non-http-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Worker"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Worker", "ComputedScheduleJob.cs");
                File.WriteAllText(documentPath, CreateComputedScheduleJobSource());
                SemanticExtractionRequest semanticRequest = CreateCSharpSemanticRequest(repositoryRoot, "src/Customer.Worker/Customer.Worker.csproj", documentPath);
                NonHttpRuntimeConsumerExtractor extractor = new();

                NonHttpRuntimeConsumerExtractionResult result = extractor.Extract(new NonHttpRuntimeConsumerExtractionRequest(new StableKey("snapshot://non-http-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode scheduledJobNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "SyncCustomers");
                string metadata = scheduledJobNode.Metadata.ToCanonicalJson();
                Assert.Contains("\"runtimeKind\":\"ScheduledJob\"", metadata, StringComparison.Ordinal);
                Assert.DoesNotContain("scheduleExpression", metadata, StringComparison.Ordinal);
                Assert.False(scheduledJobNode.UnknownState.HasUnknownData);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SymbolName == "AddOrUpdate" && evidence.FilePath.Value == "src/Customer.Worker/ComputedScheduleJob.cs");
                Assert.Empty(result.Snapshot.Warnings);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies Azure Service Bus style message processing emits queue and handler facts with a direct HANDLES relationship.
        /// </summary>
        [Fact]
        public void Extract_WhenAzureServiceBusProcessorExists_ShouldContributeQueueHandlerFact()
        {
            // The fixture mirrors common queue processor setup and verifies the queue name remains evidence-backed rather than inferred from naming alone.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-non-http-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Worker"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Worker", "QueueConsumer.cs");
                File.WriteAllText(documentPath, CreateQueueConsumerSource());
                SemanticExtractionRequest semanticRequest = CreateCSharpSemanticRequest(repositoryRoot, "src/Customer.Worker/Customer.Worker.csproj", documentPath);
                NonHttpRuntimeConsumerExtractor extractor = new();

                NonHttpRuntimeConsumerExtractionResult result = extractor.Extract(new NonHttpRuntimeConsumerExtractionRequest(new StableKey("snapshot://non-http-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode queueNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Queue);
                Assert.Equal("orders", queueNode.DisplayName);
                Assert.Contains("\"transportKind\":\"AzureServiceBus\"", queueNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                ArchitectureNode handlerNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "HandleOrderAsync");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Handles && edge.SourceNodeStableKey == handlerNode.StableKey && edge.TargetNodeStableKey == queueNode.StableKey);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SymbolName == "CreateProcessor" && evidence.FilePath.Value == "src/Customer.Worker/QueueConsumer.cs");
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies topic or subscription consumer registrations emit topic facts with subscription metadata.
        /// </summary>
        [Fact]
        public void Extract_WhenTopicSubscriptionConsumerExists_ShouldContributeTopicFactWithSubscriptionMetadata()
        {
            // Topic consumers use the same message-target path as queues but must preserve topic and subscription names separately.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-non-http-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Worker"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Worker", "TopicConsumer.cs");
                File.WriteAllText(documentPath, CreateTopicSubscriptionConsumerSource());
                SemanticExtractionRequest semanticRequest = CreateCSharpSemanticRequest(repositoryRoot, "src/Customer.Worker/Customer.Worker.csproj", documentPath);
                NonHttpRuntimeConsumerExtractor extractor = new();

                NonHttpRuntimeConsumerExtractionResult result = extractor.Extract(new NonHttpRuntimeConsumerExtractionRequest(new StableKey("snapshot://non-http-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode topicNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Topic);
                Assert.Equal("orders-topic", topicNode.DisplayName);
                string metadata = topicNode.Metadata.ToCanonicalJson();
                Assert.Contains("\"runtimeKind\":\"TopicConsumer\"", metadata, StringComparison.Ordinal);
                Assert.Contains("\"topicName\":\"orders-topic\"", metadata, StringComparison.Ordinal);
                Assert.Contains("\"subscriptionName\":\"billing-subscription\"", metadata, StringComparison.Ordinal);
                Assert.Contains("\"transportKind\":\"AzureServiceBus\"", metadata, StringComparison.Ordinal);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SymbolName == "CreateProcessor" && evidence.FilePath.Value == "src/Customer.Worker/TopicConsumer.cs");
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies computed queue names are preserved as explicit unknown queue facts instead of invented literal names.
        /// </summary>
        [Fact]
        public void Extract_WhenQueueNameIsComputed_ShouldContributeUnknownQueueFact()
        {
            // Dynamic names are a required explicit-unknown scenario because Work Item 6 must preserve evidence without guessing queue identity.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-non-http-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Worker"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Worker", "ComputedQueueConsumer.cs");
                File.WriteAllText(documentPath, CreateComputedQueueConsumerSource());
                SemanticExtractionRequest semanticRequest = CreateCSharpSemanticRequest(repositoryRoot, "src/Customer.Worker/Customer.Worker.csproj", documentPath);
                NonHttpRuntimeConsumerExtractor extractor = new();

                NonHttpRuntimeConsumerExtractionResult result = extractor.Extract(new NonHttpRuntimeConsumerExtractionRequest(new StableKey("snapshot://non-http-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode queueNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Queue);
                Assert.True(queueNode.UnknownState.HasUnknownData);
                Assert.Equal("Queue or topic name was not a compile-time string literal.", queueNode.UnknownState.UnknownReason);
                Assert.Contains("\"detectionMode\":\"AzureServiceBusProcessor\"", queueNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("Queue or topic name was not a compile-time string literal", StringComparison.Ordinal));
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies service-style host setup and long-running loop patterns emit conservative host-loop project and method facts.
        /// </summary>
        [Fact]
        public void Extract_WhenWindowsServiceAndCustomLoopExist_ShouldContributeHostLoopFacts()
        {
            // Windows-service and loop detection use conservative metadata because they describe runtime style rather than a precise external resource.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-non-http-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Worker"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Worker", "ServiceHost.cs");
                File.WriteAllText(documentPath, CreateWindowsServiceAndLoopSource());
                SemanticExtractionRequest semanticRequest = CreateCSharpSemanticRequest(repositoryRoot, "src/Customer.Worker/Customer.Worker.csproj", documentPath);
                NonHttpRuntimeConsumerExtractor extractor = new();

                NonHttpRuntimeConsumerExtractionResult result = extractor.Extract(new NonHttpRuntimeConsumerExtractionRequest(new StableKey("snapshot://non-http-test"), [semanticRequest]), CancellationToken.None);

                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Project && node.Metadata.ToCanonicalJson().Contains("\"runtimeKind\":\"WindowsServiceHost\"", StringComparison.Ordinal));
                ArchitectureNode loopNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.Metadata.ToCanonicalJson().Contains("\"runtimeKind\":\"CustomHostLoop\"", StringComparison.Ordinal));
                Assert.Equal("RunForeverAsync", loopNode.DisplayName);
                Assert.Equal(Confidence.Medium, loopNode.Confidence);
                Assert.Contains("\"detectionMode\":\"CustomHostLoopHeuristic\"", loopNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Creates a semantic extraction request for one C# source document.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root that scopes repository-relative evidence paths.</param>
        /// <param name="projectContext">The repository-relative project path used to scope project and runtime stable keys.</param>
        /// <param name="documentPath">The absolute source document path to parse.</param>
        /// <returns>A semantic extraction request with a C# syntax tree and semantic model.</returns>
        private static SemanticExtractionRequest CreateCSharpSemanticRequest(string repositoryRoot, string projectContext, string documentPath)
        {
            // The lightweight compilation is sufficient because Work Item 6 patterns rely on source shape and opportunistic symbol binding only.
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(documentPath), path: documentPath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(projectContext),
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Task).Assembly.Location), MetadataReference.CreateFromFile(typeof(CancellationToken).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return new SemanticExtractionRequest(repositoryRoot, projectContext, documentPath, syntaxTree, compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true));
        }

        /// <summary>
        /// Creates fixture source containing Hangfire-style scheduled job registration.
        /// </summary>
        /// <returns>The C# source text for a scheduled job fixture.</returns>
        private static string CreateHangfireScheduledJobSource()
        {
            // Local declarations mirror the shape the extractor sees in real repositories without requiring Hangfire package references.
            return string.Join(
                Environment.NewLine,
                "namespace Hangfire",
                "{",
                "    public static class RecurringJob",
                "    {",
                "        public static void AddOrUpdate<T>(string recurringJobId, System.Linq.Expressions.Expression<System.Action<T>> methodCall, string cronExpression) { }",
                "    }",
                "}",
                "namespace Customer.Worker;",
                "public sealed class CustomerSyncJob",
                "{",
                "    public void Configure()",
                "    {",
                "        Hangfire.RecurringJob.AddOrUpdate<CustomerSyncJob>(\"customer-sync\", job => job.SyncCustomers(), \"0 0 * * *\");",
                "    }",
                "    public void SyncCustomers()",
                "    {",
                "    }",
                "}");
        }

        /// <summary>
        /// Creates fixture source containing Hangfire-style scheduled job registration with a computed schedule expression.
        /// </summary>
        /// <returns>The C# source text for a computed schedule fixture.</returns>
        private static string CreateComputedScheduleJobSource()
        {
            // The schedule comes from a variable, so the extractor should not persist an unsupported concrete scheduleExpression metadata value.
            return string.Join(
                Environment.NewLine,
                "namespace Hangfire",
                "{",
                "    public static class RecurringJob",
                "    {",
                "        public static void AddOrUpdate<T>(string recurringJobId, System.Linq.Expressions.Expression<System.Action<T>> methodCall, string cronExpression) { }",
                "    }",
                "}",
                "namespace Customer.Worker;",
                "public sealed class CustomerSyncJob",
                "{",
                "    public void Configure(string schedule)",
                "    {",
                "        Hangfire.RecurringJob.AddOrUpdate<CustomerSyncJob>(\"customer-sync\", job => job.SyncCustomers(), schedule);",
                "    }",
                "    public void SyncCustomers()",
                "    {",
                "    }",
                "}");
        }

        /// <summary>
        /// Creates fixture source containing Azure Service Bus queue processor setup with a literal queue name.
        /// </summary>
        /// <returns>The C# source text for a queue consumer fixture.</returns>
        private static string CreateQueueConsumerSource()
        {
            // The fixture exposes both the queue source call and handler method assignment so the extractor can connect handler to queue.
            return string.Join(
                Environment.NewLine,
                "namespace Customer.Worker;",
                "public sealed class QueueConsumer",
                "{",
                "    public void Configure(ServiceBusClient client)",
                "    {",
                "        ServiceBusProcessor processor = client.CreateProcessor(\"orders\");",
                "        processor.ProcessMessageAsync += HandleOrderAsync;",
                "    }",
                "    public System.Threading.Tasks.Task HandleOrderAsync(ProcessMessageEventArgs args)",
                "    {",
                "        return System.Threading.Tasks.Task.CompletedTask;",
                "    }",
                "}",
                "public sealed class ServiceBusClient",
                "{",
                "    public ServiceBusProcessor CreateProcessor(string queueName) => new();",
                "}",
                "public sealed class ServiceBusProcessor",
                "{",
                "    public event System.Func<ProcessMessageEventArgs, System.Threading.Tasks.Task>? ProcessMessageAsync;",
                "}",
                "public sealed class ProcessMessageEventArgs",
                "{",
                "}");
        }

        /// <summary>
        /// Creates fixture source containing Azure Service Bus topic processor setup with literal topic and subscription names.
        /// </summary>
        /// <returns>The C# source text for a topic consumer fixture.</returns>
        private static string CreateTopicSubscriptionConsumerSource()
        {
            // The two-argument CreateProcessor call models Azure Service Bus topic/subscription processing without connecting to a broker.
            return string.Join(
                Environment.NewLine,
                "namespace Customer.Worker;",
                "public sealed class TopicConsumer",
                "{",
                "    public void Configure(ServiceBusClient client)",
                "    {",
                "        ServiceBusProcessor processor = client.CreateProcessor(\"orders-topic\", \"billing-subscription\");",
                "    }",
                "}",
                "public sealed class ServiceBusClient",
                "{",
                "    public ServiceBusProcessor CreateProcessor(string topicName, string subscriptionName) => new();",
                "}",
                "public sealed class ServiceBusProcessor",
                "{",
                "}");
        }

        /// <summary>
        /// Creates fixture source containing Azure Service Bus queue processor setup with a computed queue name.
        /// </summary>
        /// <returns>The C# source text for a computed queue consumer fixture.</returns>
        private static string CreateComputedQueueConsumerSource()
        {
            // The computed queue name should force UnknownState rather than a guessed literal queue identity.
            return string.Join(
                Environment.NewLine,
                "namespace Customer.Worker;",
                "public sealed class QueueConsumer",
                "{",
                "    public void Configure(ServiceBusClient client, string queueName)",
                "    {",
                "        ServiceBusProcessor processor = client.CreateProcessor(queueName);",
                "    }",
                "}",
                "public sealed class ServiceBusClient",
                "{",
                "    public ServiceBusProcessor CreateProcessor(string queueName) => new();",
                "}",
                "public sealed class ServiceBusProcessor",
                "{",
                "}");
        }

        /// <summary>
        /// Creates fixture source containing Windows-service setup and a long-running loop.
        /// </summary>
        /// <returns>The C# source text for service-host and custom-loop fixture.</returns>
        private static string CreateWindowsServiceAndLoopSource()
        {
            // The service-mode call and while loop represent non-HTTP runtime behavior with conservative confidence.
            return string.Join(
                Environment.NewLine,
                "namespace Customer.Worker;",
                "public sealed class ServiceHost",
                "{",
                "    public void Configure(IHostBuilder builder)",
                "    {",
                "        builder.UseWindowsService();",
                "    }",
                "    public async System.Threading.Tasks.Task RunForeverAsync(System.Threading.CancellationToken stoppingToken)",
                "    {",
                "        while (!stoppingToken.IsCancellationRequested)",
                "        {",
                "            await System.Threading.Tasks.Task.Delay(1000, stoppingToken);",
                "        }",
                "    }",
                "}",
                "public interface IHostBuilder",
                "{",
                "    IHostBuilder UseWindowsService();",
                "}");
        }
    }
}

using Archon.Application.Graph.Persistence;
using Archon.Infrastructure.Neo4j.Configuration;
using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.Infrastructure.Neo4j.Driver;
using Archon.Infrastructure.Neo4j.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Neo4j.Driver;
using System.Reflection;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.DependencyInjection
{
    /// <summary>
    /// Verifies dependency-injection registration for the Neo4j infrastructure adapter.
    /// </summary>
    public sealed class Neo4jServiceCollectionExtensionsTests
    {
        /// <summary>
        /// Confirms the registration binds in-memory configuration and exposes the expected lifecycle services.
        /// </summary>
        [Fact]
        public void AddArchonNeo4jRegistersOptionsDriverSessionProviderAndHealthCheck()
        {
            // The test uses in-memory configuration to prove hosts can consume Aspire-provided or test-provided values without
            // launching the Aspire AppHost.
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateConfiguration());

            using ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);

            Neo4jOptions options = serviceProvider.GetRequiredService<IOptions<Neo4jOptions>>().Value;
            IEnumerable<HealthCheckRegistration> healthRegistrations = serviceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

            Assert.Equal("bolt://localhost:7687", options.Uri);
            Assert.Equal("neo4j", options.Database);
            Assert.NotNull(serviceProvider.GetRequiredService<INeo4jDriverFactory>());
            Assert.NotNull(serviceProvider.GetRequiredService<INeo4jSessionProvider>());
            Assert.NotNull(serviceProvider.GetRequiredService<IArchitectureGraphRecreator>());
            Assert.NotNull(serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>());
            Assert.Contains(healthRegistrations, registration => registration.Name == Neo4jHealthCheck.Name);
        }

        /// <summary>
        /// Confirms the dependency-injection-owned driver is disposed when the service provider is disposed.
        /// </summary>
        [Fact]
        public void ServiceProviderDisposesSingletonDriver()
        {
            // The test replaces only the factory seam so it can observe disposal without opening a network connection to Neo4j.
            IDriver driver = DisposableDriverProxy.Create(out DisposableDriverProxy proxy);
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateConfiguration());
            services.AddSingleton<INeo4jDriverFactory>(new FakeDriverFactory(driver));

            ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);
            IDriver resolvedDriver = serviceProvider.GetRequiredService<IDriver>();

            serviceProvider.Dispose();

            Assert.Same(driver, resolvedDriver);
            Assert.True(proxy.Disposed);
        }

        /// <summary>
        /// Creates test configuration in the same section shape expected by production registration.
        /// </summary>
        /// <returns>An in-memory configuration root containing valid Neo4j settings.</returns>
        private static IConfiguration CreateConfiguration()
        {
            // In-memory configuration keeps the registration test deterministic and avoids any dependency on user secrets or
            // environment variables that might exist on a developer machine.
            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
            {
                [$"{Neo4jOptions.SectionName}:Uri"] = "bolt://localhost:7687",
                [$"{Neo4jOptions.SectionName}:Database"] = "neo4j",
                [$"{Neo4jOptions.SectionName}:Username"] = "neo4j",
                [$"{Neo4jOptions.SectionName}:Password"] = "local-development-password",
                [$"{Neo4jOptions.SectionName}:ConnectionTimeout"] = "00:00:05",
                [$"{Neo4jOptions.SectionName}:MaxTransactionRetryTime"] = "00:00:05",
                [$"{Neo4jOptions.SectionName}:EncryptionMode"] = nameof(Neo4jEncryptionMode.Unencrypted)
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
        }

        /// <summary>
        /// Test factory that returns a predetermined driver instance for disposal verification.
        /// </summary>
        private sealed class FakeDriverFactory : INeo4jDriverFactory
        {
            private readonly IDriver _driver;

            /// <summary>
            /// Initializes a new instance of the <see cref="FakeDriverFactory"/> class.
            /// </summary>
            /// <param name="driver">The fake driver that should be returned from <see cref="CreateDriver"/>.</param>
            public FakeDriverFactory(IDriver driver)
            {
                // The fake factory stores the test-owned driver so DI can own and dispose it through the production registration.
                _driver = driver;
            }

            /// <summary>
            /// Returns the predetermined fake driver for singleton registration.
            /// </summary>
            /// <returns>The fake driver instance supplied by the test.</returns>
            public IDriver CreateDriver()
            {
                // Returning the same fake instance lets the test assert that service-provider disposal reached the driver object.
                return _driver;
            }
        }

        /// <summary>
        /// Dynamic proxy for the Neo4j driver interface that tracks disposal without implementing every driver member by hand.
        /// </summary>
        private class DisposableDriverProxy : DispatchProxy
        {
            /// <summary>
            /// Gets a value indicating whether the dependency-injection container disposed or closed this fake driver.
            /// </summary>
            public bool Disposed { get; private set; }

            /// <summary>
            /// Creates a proxied <see cref="IDriver"/> and exposes the backing proxy state to the caller.
            /// </summary>
            /// <param name="proxy">The backing proxy instance that records disposal calls.</param>
            /// <returns>A dynamic <see cref="IDriver"/> implementation suitable for dependency-injection disposal tests.</returns>
            public static IDriver Create(out DisposableDriverProxy proxy)
            {
                // DispatchProxy generates an object implementing IDriver while routing method calls back to this proxy type.
                IDriver driver = DispatchProxy.Create<IDriver, DisposableDriverProxy>();
                proxy = (DisposableDriverProxy)(object)driver;
                return driver;
            }

            /// <summary>
            /// Handles calls made through the dynamic driver proxy.
            /// </summary>
            /// <param name="targetMethod">The interface method invoked on the generated proxy.</param>
            /// <param name="args">The arguments supplied to the proxied method.</param>
            /// <returns>A method-appropriate result for disposal calls or throws for unsupported behavior.</returns>
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                // The disposal test should only exercise service-provider cleanup. Any other driver use is intentionally rejected.
                if (targetMethod?.Name is nameof(IDisposable.Dispose) or "CloseAsync")
                {
                    Disposed = true;
                    return targetMethod.ReturnType == typeof(Task) ? Task.CompletedTask : null;
                }

                if (targetMethod?.Name == nameof(IAsyncDisposable.DisposeAsync))
                {
                    Disposed = true;
                    return ValueTask.CompletedTask;
                }

                throw new NotSupportedException("The disposable driver proxy supports disposal calls only.");
            }
        }
    }
}

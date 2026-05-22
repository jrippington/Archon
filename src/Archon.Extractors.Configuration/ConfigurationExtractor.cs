using Archon.Application.Extraction.Accumulation;

namespace Archon.Extractors.Configuration
{
    /// <summary>
    /// Coordinates all configuration extraction slices that contribute configuration graph facts for one repository context.
    /// </summary>
    public sealed class ConfigurationExtractor
    {
        /// <summary>
        /// Stores the modern appsettings and options extractor used by this composition layer.
        /// </summary>
        private readonly ModernConfigurationExtractor _modernExtractor;

        /// <summary>
        /// Stores the legacy XML and ConfigurationManager extractor used by this composition layer.
        /// </summary>
        private readonly LegacyConfigurationExtractor _legacyExtractor;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationExtractor"/> class using the default configuration extraction slices.
        /// </summary>
        public ConfigurationExtractor()
            : this(new ModernConfigurationExtractor(), new LegacyConfigurationExtractor())
        {
            // The default constructor is the normal production path and wires the current modern and legacy slices together.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationExtractor"/> class with explicit slice dependencies.
        /// </summary>
        /// <param name="modernExtractor">The extractor responsible for appsettings and Microsoft.Extensions.Configuration/options facts.</param>
        /// <param name="legacyExtractor">The extractor responsible for legacy .config and System.Configuration.ConfigurationManager facts.</param>
        public ConfigurationExtractor(ModernConfigurationExtractor modernExtractor, LegacyConfigurationExtractor legacyExtractor)
        {
            // Explicit dependency injection keeps the orchestration boundary testable without hiding ownership of individual slices.
            _modernExtractor = modernExtractor ?? throw new ArgumentNullException(nameof(modernExtractor));
            _legacyExtractor = legacyExtractor ?? throw new ArgumentNullException(nameof(legacyExtractor));
        }

        /// <summary>
        /// Runs the modern and legacy configuration extraction slices and merges their graph contributions into one deterministic snapshot result.
        /// </summary>
        /// <param name="request">The repository and semantic-document request that scopes configuration extraction.</param>
        /// <param name="cancellationToken">A token that signals when extraction should stop before or between slice execution.</param>
        /// <returns>A configuration extraction result containing the merged snapshot contributions from all configured slices.</returns>
        public ModernConfigurationExtractionResult Extract(ModernConfigurationExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // The composition layer delegates source-family-specific work to named slices, then uses the shared accumulator merge policy for deterministic de-duplication.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();

            cancellationToken.ThrowIfCancellationRequested();
            ModernConfigurationExtractionResult modernResult = _modernExtractor.Extract(request, cancellationToken);
            accumulator.Merge(modernResult.Snapshot);

            cancellationToken.ThrowIfCancellationRequested();
            ModernConfigurationExtractionResult legacyResult = _legacyExtractor.Extract(request, cancellationToken);
            accumulator.Merge(legacyResult.Snapshot);

            return new ModernConfigurationExtractionResult(accumulator.ToSnapshot());
        }
    }
}

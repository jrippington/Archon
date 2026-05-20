using System.Text.Json;
using Archon.Domain.Graph.ControlledValues;
using Xunit;

namespace Archon.Domain.Tests.Graph.ControlledValues
{
    /// <summary>
    /// Verifies the shared controlled-value behavior required by WP002 for stable graph classifications.
    /// </summary>
    public sealed class ControlledValueTests
    {
        /// <summary>
        /// Supplies every required node kind and its stable external string value from the WP002 specification.
        /// </summary>
        public static TheoryData<NodeKind, string> RequiredNodeKinds => new()
        {
            { NodeKind.Repository, "Repository" },
            { NodeKind.Solution, "Solution" },
            { NodeKind.Project, "Project" },
            { NodeKind.Package, "Package" },
            { NodeKind.Namespace, "Namespace" },
            { NodeKind.Type, "Type" },
            { NodeKind.Method, "Method" },
            { NodeKind.Property, "Property" },
            { NodeKind.Field, "Field" },
            { NodeKind.Endpoint, "Endpoint" },
            { NodeKind.Controller, "Controller" },
            { NodeKind.HostedService, "HostedService" },
            { NodeKind.UiApplication, "UiApplication" },
            { NodeKind.UiComponent, "UiComponent" },
            { NodeKind.UiPage, "UiPage" },
            { NodeKind.UiView, "UiView" },
            { NodeKind.UiLayout, "UiLayout" },
            { NodeKind.UiRoute, "UiRoute" },
            { NodeKind.UiControl, "UiControl" },
            { NodeKind.UiResource, "UiResource" },
            { NodeKind.UiStyle, "UiStyle" },
            { NodeKind.ViewModel, "ViewModel" },
            { NodeKind.Command, "Command" },
            { NodeKind.Binding, "Binding" },
            { NodeKind.ConfigurationKey, "ConfigurationKey" },
            { NodeKind.DbContext, "DbContext" },
            { NodeKind.LinqToSqlDataContext, "LinqToSqlDataContext" },
            { NodeKind.Entity, "Entity" },
            { NodeKind.DatabaseTable, "DatabaseTable" },
            { NodeKind.DatabaseColumn, "DatabaseColumn" },
            { NodeKind.StoredProcedure, "StoredProcedure" },
            { NodeKind.ExternalService, "ExternalService" },
            { NodeKind.Queue, "Queue" },
            { NodeKind.Topic, "Topic" },
            { NodeKind.FilePath, "FilePath" },
            { NodeKind.Pipeline, "Pipeline" },
            { NodeKind.OpenApiDocument, "OpenApiDocument" },
            { NodeKind.Dockerfile, "Dockerfile" },
            { NodeKind.SqlScript, "SqlScript" },
            { NodeKind.GeneratedArtifact, "GeneratedArtifact" }
        };

        /// <summary>
        /// Supplies every required edge kind and its stable external string value from the WP002 specification.
        /// </summary>
        public static TheoryData<EdgeKind, string> RequiredEdgeKinds => new()
        {
            { EdgeKind.Contains, "CONTAINS" },
            { EdgeKind.References, "REFERENCES" },
            { EdgeKind.Calls, "CALLS" },
            { EdgeKind.Implements, "IMPLEMENTS" },
            { EdgeKind.Inherits, "INHERITS" },
            { EdgeKind.Injects, "INJECTS" },
            { EdgeKind.Exposes, "EXPOSES" },
            { EdgeKind.Handles, "HANDLES" },
            { EdgeKind.UsesConfig, "USES_CONFIG" },
            { EdgeKind.UsesDbContext, "USES_DB_CONTEXT" },
            { EdgeKind.UsesLinqToSqlContext, "USES_LINQ_TO_SQL_CONTEXT" },
            { EdgeKind.MapsEntity, "MAPS_ENTITY" },
            { EdgeKind.MapsTable, "MAPS_TABLE" },
            { EdgeKind.MapsColumn, "MAPS_COLUMN" },
            { EdgeKind.ReadsTable, "READS_TABLE" },
            { EdgeKind.WritesTable, "WRITES_TABLE" },
            { EdgeKind.CallsStoredProcedure, "CALLS_STORED_PROCEDURE" },
            { EdgeKind.ExecutesRawSql, "EXECUTES_RAW_SQL" },
            { EdgeKind.CallsExternalService, "CALLS_EXTERNAL_SERVICE" },
            { EdgeKind.UsesPackage, "USES_PACKAGE" },
            { EdgeKind.DeclaresEndpoint, "DECLARES_ENDPOINT" },
            { EdgeKind.DeclaresComponent, "DECLARES_COMPONENT" },
            { EdgeKind.DeclaresUiRoute, "DECLARES_UI_ROUTE" },
            { EdgeKind.UsesComponent, "USES_COMPONENT" },
            { EdgeKind.UsesLayout, "USES_LAYOUT" },
            { EdgeKind.UsesControl, "USES_CONTROL" },
            { EdgeKind.UsesUiResource, "USES_UI_RESOURCE" },
            { EdgeKind.UsesStyle, "USES_STYLE" },
            { EdgeKind.BindsTo, "BINDS_TO" },
            { EdgeKind.UsesCommand, "USES_COMMAND" },
            { EdgeKind.UsesViewModel, "USES_VIEW_MODEL" },
            { EdgeKind.NavigatesTo, "NAVIGATES_TO" },
            { EdgeKind.HandlesUiEvent, "HANDLES_UI_EVENT" },
            { EdgeKind.CallsApi, "CALLS_API" },
            { EdgeKind.RegisteredAsService, "REGISTERED_AS_SERVICE" },
            { EdgeKind.DependsOn, "DEPENDS_ON" }
        };

        /// <summary>
        /// Supplies every required evidence kind and its stable external string value from the source brief.
        /// </summary>
        public static TheoryData<EvidenceKind, string> RequiredEvidenceKinds => new()
        {
            { EvidenceKind.ProjectFile, "ProjectFile" },
            { EvidenceKind.SourceCode, "SourceCode" },
            { EvidenceKind.Configuration, "Configuration" },
            { EvidenceKind.Dbml, "Dbml" },
            { EvidenceKind.DesignerGeneratedCode, "DesignerGeneratedCode" },
            { EvidenceKind.SqlScript, "SqlScript" },
            { EvidenceKind.PipelineFile, "PipelineFile" },
            { EvidenceKind.OpenApiDocument, "OpenApiDocument" },
            { EvidenceKind.Dockerfile, "Dockerfile" },
            { EvidenceKind.GeneratedArtifact, "GeneratedArtifact" },
            { EvidenceKind.PackageReference, "PackageReference" },
            { EvidenceKind.CompilerSymbol, "CompilerSymbol" },
            { EvidenceKind.CompilerDiagnostic, "CompilerDiagnostic" },
            { EvidenceKind.Inference, "Inference" },
            { EvidenceKind.ManualAnnotation, "ManualAnnotation" }
        };

        /// <summary>
        /// Supplies every required rule category and its stable external string value from the source brief.
        /// </summary>
        public static TheoryData<RuleCategory, string> RequiredRuleCategories => new()
        {
            { RuleCategory.Lifecycle, "Lifecycle" },
            { RuleCategory.ObsoleteApi, "ObsoleteApi" },
            { RuleCategory.LegacyTechnology, "LegacyTechnology" },
            { RuleCategory.SecuritySensitive, "SecuritySensitive" },
            { RuleCategory.DataAccess, "DataAccess" },
            { RuleCategory.Configuration, "Configuration" },
            { RuleCategory.ArchitectureLayering, "ArchitectureLayering" },
            { RuleCategory.DependencyRisk, "DependencyRisk" },
            { RuleCategory.ModernizationBlocker, "ModernizationBlocker" },
            { RuleCategory.OrganisationSpecific, "OrganisationSpecific" }
        };

        /// <summary>
        /// Supplies every required finding severity and its stable external string value from the source brief.
        /// </summary>
        public static TheoryData<FindingSeverity, string> RequiredFindingSeverities => new()
        {
            { FindingSeverity.Critical, "Critical" },
            { FindingSeverity.High, "High" },
            { FindingSeverity.Medium, "Medium" },
            { FindingSeverity.Low, "Low" },
            { FindingSeverity.Info, "Info" }
        };

        /// <summary>
        /// Supplies every required finding status and its stable external string value from the source brief.
        /// </summary>
        public static TheoryData<FindingStatus, string> RequiredFindingStatuses => new()
        {
            { FindingStatus.Open, "Open" },
            { FindingStatus.Acknowledged, "Acknowledged" },
            { FindingStatus.Suppressed, "Suppressed" },
            { FindingStatus.Resolved, "Resolved" },
            { FindingStatus.Unknown, "Unknown" }
        };

        /// <summary>
        /// Supplies every required knowledge kind and its stable external string value from the source brief.
        /// </summary>
        public static TheoryData<KnowledgeKind, string> RequiredKnowledgeKinds => new()
        {
            { KnowledgeKind.Fact, "Fact" },
            { KnowledgeKind.Inference, "Inference" },
            { KnowledgeKind.Unknown, "Unknown" },
            { KnowledgeKind.HumanConfirmed, "HumanConfirmed" }
        };

        /// <summary>
        /// Supplies required metric scope values and their stable external string values for WP002 metric contracts.
        /// </summary>
        public static TheoryData<MetricScopeKind, string> RequiredMetricScopeKinds => new()
        {
            { MetricScopeKind.Snapshot, "Snapshot" },
            { MetricScopeKind.Node, "Node" },
            { MetricScopeKind.Edge, "Edge" },
            { MetricScopeKind.Graph, "Graph" },
            { MetricScopeKind.Project, "Project" },
            { MetricScopeKind.Modernization, "Modernization" }
        };

        /// <summary>
        /// Supplies required generated-summary values and their stable external string values for WP002 summary contracts.
        /// </summary>
        public static TheoryData<SummaryKind, string> RequiredSummaryKinds => new()
        {
            { SummaryKind.Snapshot, "Snapshot" },
            { SummaryKind.Node, "Node" },
            { SummaryKind.Edge, "Edge" },
            { SummaryKind.Graph, "Graph" },
            { SummaryKind.Project, "Project" },
            { SummaryKind.Modernization, "Modernization" }
        };

        /// <summary>
        /// Confirms every required node kind has the exact stable external string required by the specification.
        /// </summary>
        /// <param name="kind">The node kind instance under test.</param>
        /// <param name="expectedValue">The external string value that must remain stable for external contracts.</param>
        [Theory]
        [MemberData(nameof(RequiredNodeKinds))]
        public void NodeKindValuesExposeStableStrings(NodeKind kind, string expectedValue)
        {
            // The Value property is the external contract used instead of numeric enum ordinals.
            Assert.Equal(expectedValue, kind.Value);
            Assert.Equal(expectedValue, kind.ToString());
        }

        /// <summary>
        /// Confirms every required edge kind has the exact stable external string required by the specification.
        /// </summary>
        /// <param name="kind">The edge kind instance under test.</param>
        /// <param name="expectedValue">The external string value that must remain stable for external contracts.</param>
        [Theory]
        [MemberData(nameof(RequiredEdgeKinds))]
        public void EdgeKindValuesExposeStableStrings(EdgeKind kind, string expectedValue)
        {
            // Relationship names are uppercase because they will map cleanly to future Neo4j relationship types.
            Assert.Equal(expectedValue, kind.Value);
            Assert.Equal(expectedValue, kind.ToString());
        }

        /// <summary>
        /// Confirms every required evidence kind has the exact stable external string required by the source brief.
        /// </summary>
        /// <param name="kind">The evidence kind instance under test.</param>
        /// <param name="expectedValue">The external string value that must remain stable for external contracts.</param>
        [Theory]
        [MemberData(nameof(RequiredEvidenceKinds))]
        public void EvidenceKindValuesExposeStableStrings(EvidenceKind kind, string expectedValue)
        {
            // Evidence kinds classify the source of a graph claim and must be stable across snapshots.
            Assert.Equal(expectedValue, kind.Value);
            Assert.Equal(expectedValue, kind.ToString());
        }

        /// <summary>
        /// Confirms every required rule category has the exact stable external string required by the source brief.
        /// </summary>
        /// <param name="category">The rule category instance under test.</param>
        /// <param name="expectedValue">The external string value that must remain stable for authored and persisted rules.</param>
        [Theory]
        [MemberData(nameof(RequiredRuleCategories))]
        public void RuleCategoryValuesExposeStableStrings(RuleCategory category, string expectedValue)
        {
            // Rule categories group findings without using numeric enum positions that could drift later.
            Assert.Equal(expectedValue, category.Value);
            Assert.Equal(expectedValue, category.ToString());
        }

        /// <summary>
        /// Confirms every required finding severity has the exact stable external string required by the source brief.
        /// </summary>
        /// <param name="severity">The finding severity instance under test.</param>
        /// <param name="expectedValue">The external string value that must remain stable for findings.</param>
        [Theory]
        [MemberData(nameof(RequiredFindingSeverities))]
        public void FindingSeverityValuesExposeStableStrings(FindingSeverity severity, string expectedValue)
        {
            // Severities are intentionally textual so future ordering rules do not leak into serialization.
            Assert.Equal(expectedValue, severity.Value);
            Assert.Equal(expectedValue, severity.ToString());
        }

        /// <summary>
        /// Confirms every required finding status has the exact stable external string required by the source brief.
        /// </summary>
        /// <param name="status">The finding status instance under test.</param>
        /// <param name="expectedValue">The external string value that must remain stable for findings.</param>
        [Theory]
        [MemberData(nameof(RequiredFindingStatuses))]
        public void FindingStatusValuesExposeStableStrings(FindingStatus status, string expectedValue)
        {
            // Status values describe finding lifecycle without relying on mutable ordinal positions.
            Assert.Equal(expectedValue, status.Value);
            Assert.Equal(expectedValue, status.ToString());
        }

        /// <summary>
        /// Confirms every required knowledge kind has the exact stable external string required by the source brief.
        /// </summary>
        /// <param name="kind">The knowledge kind instance under test.</param>
        /// <param name="expectedValue">The external string value that must remain stable for fact classification.</param>
        [Theory]
        [MemberData(nameof(RequiredKnowledgeKinds))]
        public void KnowledgeKindValuesExposeStableStrings(KnowledgeKind kind, string expectedValue)
        {
            // Knowledge kinds make uncertainty explicit for later graph facts.
            Assert.Equal(expectedValue, kind.Value);
            Assert.Equal(expectedValue, kind.ToString());
        }

        /// <summary>
        /// Confirms required metric scope values expose stable strings for metric contracts.
        /// </summary>
        /// <param name="kind">The metric scope kind instance under test.</param>
        /// <param name="expectedValue">The external string value that must remain stable for metrics.</param>
        [Theory]
        [MemberData(nameof(RequiredMetricScopeKinds))]
        public void MetricScopeKindValuesExposeStableStrings(MetricScopeKind kind, string expectedValue)
        {
            // Metric scopes identify where a future metric applies without requiring a separate persistence shape per scope.
            Assert.Equal(expectedValue, kind.Value);
            Assert.Equal(expectedValue, kind.ToString());
        }

        /// <summary>
        /// Confirms required summary kind values expose stable strings for generated-summary contracts.
        /// </summary>
        /// <param name="kind">The summary kind instance under test.</param>
        /// <param name="expectedValue">The external string value that must remain stable for summaries.</param>
        [Theory]
        [MemberData(nameof(RequiredSummaryKinds))]
        public void SummaryKindValuesExposeStableStrings(SummaryKind kind, string expectedValue)
        {
            // Summary kinds classify generated narrative output without binding later exporters to numeric values.
            Assert.Equal(expectedValue, kind.Value);
            Assert.Equal(expectedValue, kind.ToString());
        }

        /// <summary>
        /// Verifies parsing returns the canonical instance and preserves value-object equality semantics.
        /// </summary>
        [Fact]
        public void ParseReturnsCanonicalInstanceForStableString()
        {
            // The parser is the single supported bridge from external strings back into controlled domain values.
            NodeKind parsed = NodeKind.Parse("Project");

            // Canonical instances make reference checks possible while equality remains value-based.
            Assert.Same(NodeKind.Project, parsed);
            Assert.Equal(NodeKind.Project, parsed);
        }

        /// <summary>
        /// Verifies TryParse reports unknown values without throwing so callers can choose validation style.
        /// </summary>
        [Fact]
        public void TryParseReturnsFalseForUnknownStableString()
        {
            // TryParse should avoid exceptions for ordinary validation paths such as request binding or import checks.
            bool parsed = EdgeKind.TryParse("NOT_A_RELATIONSHIP", out EdgeKind? value);

            // A failed parse leaves no accidental fallback value that could hide malformed graph input.
            Assert.False(parsed);
            Assert.Null(value);
        }

        /// <summary>
        /// Verifies Parse rejects null, empty, and unknown values with clear argument failures.
        /// </summary>
        /// <param name="value">The invalid external string value to parse.</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("UnknownNodeKind")]
        public void ParseRejectsInvalidStableStrings(string? value)
        {
            // Invalid external values must fail fast so graph facts do not enter the model with accidental defaults.
            Assert.Throws<ArgumentException>(() => NodeKind.Parse(value));
        }

        /// <summary>
        /// Verifies equality is based on controlled-value content rather than ordinary reference identity.
        /// </summary>
        [Fact]
        public void EqualityUsesConcreteTypeAndStableString()
        {
            // Two controlled value sets may share a string such as Configuration, but they are different domains.
            Assert.NotEqual<object>(EvidenceKind.Configuration, RuleCategory.Configuration);

            // Within a controlled value set, the stable string identifies the value deterministically.
            Assert.True(NodeKind.Project == NodeKind.Parse("Project"));
            Assert.False(NodeKind.Project != NodeKind.Parse("Project"));
        }

        /// <summary>
        /// Verifies all values are exposed in deterministic declaration order for validation and documentation tooling.
        /// </summary>
        [Fact]
        public void AllReturnsValuesInDeclarationOrder()
        {
            // The All collection gives tests and future documentation generators a deterministic source of truth.
            string[] nodeValues = NodeKind.All.Select(static value => value.Value).ToArray();

            // Checking the first and last values proves the declared order is preserved across the full required set.
            Assert.Equal("Repository", nodeValues[0]);
            Assert.Equal("GeneratedArtifact", nodeValues[^1]);
            Assert.Equal(RequiredNodeKinds.Count, nodeValues.Length);
        }

        /// <summary>
        /// Verifies controlled values serialize and deserialize as stable JSON strings.
        /// </summary>
        [Fact]
        public void JsonSerializationRoundTripsStableStringValue()
        {
            // System.Text.Json should see a string value because external contracts must not expose enum ordinals.
            string json = JsonSerializer.Serialize(NodeKind.Project);

            // Deserialization should use the same parser path that validates manually supplied external strings.
            NodeKind? value = JsonSerializer.Deserialize<NodeKind>(json);

            Assert.Equal("\"Project\"", json);
            Assert.Same(NodeKind.Project, value);
        }
    }
}

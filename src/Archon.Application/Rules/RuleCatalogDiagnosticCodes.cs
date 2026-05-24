namespace Archon.Application.Rules
{
    /// <summary>
    /// Defines stable diagnostic codes emitted by the WP012 rule catalog loader and validator.
    /// </summary>
    public static class RuleCatalogDiagnosticCodes
    {
        /// <summary>Indicates that the runtime rules folder was not present.</summary>
        public const string RuleFolderMissing = "RULE_FOLDER_MISSING";

        /// <summary>Indicates that the runtime rules folder could not be enumerated or read.</summary>
        public const string RuleFolderUnreadable = "RULE_FOLDER_UNREADABLE";

        /// <summary>Indicates that a rule JSON file could not be parsed.</summary>
        public const string JsonParseFailed = "JSON_PARSE_FAILED";

        /// <summary>Indicates that a required rule field is missing or blank.</summary>
        public const string RequiredFieldMissing = "REQUIRED_FIELD_MISSING";

        /// <summary>Indicates that a category value is not part of the supported rule category vocabulary.</summary>
        public const string InvalidCategory = "INVALID_CATEGORY";

        /// <summary>Indicates that a severity value is not part of the supported finding severity vocabulary.</summary>
        public const string InvalidSeverity = "INVALID_SEVERITY";

        /// <summary>Indicates that a rule default status value is not part of the supported rule status vocabulary.</summary>
        public const string InvalidStatus = "INVALID_STATUS";

        /// <summary>Indicates that a rule version is missing or not a supported semantic version.</summary>
        public const string InvalidVersion = "INVALID_VERSION";

        /// <summary>Indicates that a rule detection block is missing.</summary>
        public const string DetectionMissing = "DETECTION_MISSING";

        /// <summary>Indicates that a detection group has no condition or nested-group operands.</summary>
        public const string EmptyDetectionGroup = "EMPTY_DETECTION_GROUP";

        /// <summary>Indicates that a detection match value is not all, any, or none.</summary>
        public const string InvalidMatch = "INVALID_MATCH";

        /// <summary>Indicates that a detection node kind is not part of the supported graph node vocabulary.</summary>
        public const string InvalidNodeKind = "INVALID_NODE_KIND";

        /// <summary>Indicates that a condition kind is not part of the supported rule DSL vocabulary.</summary>
        public const string UnsupportedConditionKind = "UNSUPPORTED_CONDITION_KIND";

        /// <summary>Indicates that a condition operator is not part of the supported rule DSL vocabulary.</summary>
        public const string UnsupportedOperator = "UNSUPPORTED_OPERATOR";

        /// <summary>Indicates that a condition operator cannot be used with the condition payload shape.</summary>
        public const string OperatorIncompatibleWithCondition = "OPERATOR_INCOMPATIBLE_WITH_CONDITION";

        /// <summary>Indicates that a rule code and version combination appears more than once in the loaded catalog.</summary>
        public const string DuplicateRuleIdentity = "DUPLICATE_RULE_IDENTITY";
    }
}

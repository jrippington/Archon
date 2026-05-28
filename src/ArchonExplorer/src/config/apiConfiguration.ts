/**
 * Describes the safe frontend view of Archon API base URL configuration.
 *
 * The value is intentionally reduced to configuration presence and an optional URL so the
 * Work Item 1 UI can explain setup state without performing network calls or exposing
 * sensitive diagnostics.
 */
export interface ApiConfiguration {
  /**
   * Indicates whether the Vite environment supplied a non-empty Archon API base URL.
   */
  readonly isConfigured: boolean;

  /**
   * Contains the configured Archon API base URL when present; otherwise remains undefined.
   */
  readonly baseUrl?: string;
}

/**
 * Names the Vite environment variable used to supply the Archon API base URL.
 *
 * Vite only exposes client-side variables prefixed with VITE_, so this key can be supplied
 * by local development tooling without leaking unrelated process environment values.
 */
const apiBaseUrlEnvironmentKey = 'VITE_ARCHON_API_BASE_URL';

/**
 * Reads Archon API base URL configuration from the Vite client environment.
 *
 * @returns A safe configuration object that distinguishes absent configuration from a
 * configured base URL without validating connectivity or calling the API.
 */
export function getApiConfiguration(): ApiConfiguration {
  // import.meta.env is the Vite-supported configuration surface available to browser code;
  // trimming prevents whitespace-only values from appearing as configured endpoints.
  const configuredBaseUrl = import.meta.env[apiBaseUrlEnvironmentKey]?.trim();

  if (configuredBaseUrl === undefined || configuredBaseUrl.length === 0) {
    return {
      isConfigured: false,
    };
  }

  return {
    isConfigured: true,
    baseUrl: configuredBaseUrl,
  };
}

# ArchonExplorer Frontend Foundation

ArchonExplorer is the browser-facing user interface foundation for Archon. In the current implementation, it exists as a standalone Vite, React, and TypeScript application in `src/ArchonExplorer`, renders a visible desktop-style workbench foundation shell, and is composed by the Aspire AppHost as a local Vite resource. A **frontend foundation** is the smallest runnable browser application structure that later product work can extend without replacing the package manager, bootstrap path, provider model, component-system seam, configuration seam, shell frame, or local distributed-application hosting path. This page describes the current frontend, shell, and Aspire-hosted development foundation only; functional API client behavior remains intentionally out of scope.

The foundation is intentionally separate from the ASP.NET Core API and MCP hosts. ArchonExplorer is not an ASP.NET Core-hosted SPA template and is not a Next.js application. It is an npm-managed Vite application that can be restored, typechecked, and built from its own project directory. The AppHost composes that application for local development, but the source of browser behavior remains the frontend folder. That separation keeps browser runtime concerns in `src/ArchonExplorer` while the .NET hosts continue to own API, MCP, persistence, and local composition behavior.

For repository architecture context, read [solution architecture](solution-architecture.md). For manual and automated validation commands, read [validation and test workflows](validation-and-test-workflows.md). For AppHost composition rules, read [runtime foundation](runtime-foundation.md). Terms such as AppHost, frontend foundation, workbench shell, activity rail, command/search affordance, status bar, shell placeholder, shadcn/ui, Vite, and TanStack Query are defined in the [glossary](glossary.md).

Reader path: [Home](home.md) -> [Solution architecture](solution-architecture.md) -> ArchonExplorer frontend foundation -> [Validation and test workflows](validation-and-test-workflows.md).

## Current application shape

The current frontend project lives under `src/ArchonExplorer` and uses npm as its package manager. The package manifest exposes three scripts:

```powershell
cd .\src\ArchonExplorer
npm install
npm run typecheck
npm run build
```

`npm install` restores the frontend dependency graph and maintains `package-lock.json`, which is the deterministic npm lockfile for this application. `npm run typecheck` runs the TypeScript build in type-checking mode without emitting browser assets. `npm run build` runs the same TypeScript project references and then asks Vite to produce a production build in the local `dist` output folder. The generated `dist` folder is a build artifact, not contributor-authored source.

The source layout keeps bootstrap, runtime providers, configuration, and styling concerns easy to find:

```text
src/ArchonExplorer/
  index.html
  package.json
  package-lock.json
  tsconfig.json
  tsconfig.app.json
  tsconfig.node.json
  vite.config.ts
  src/
	main.tsx
	App.tsx
	index.css
	config/
	providers/
```

`index.html` supplies the Vite HTML entry point and the `root` element that React mounts into. `src/main.tsx` is the browser bootstrap path. It locates the required root element, enables React `StrictMode`, and wraps the application in the centralized provider tree. `src/App.tsx` reads safe API configuration state and composes the visible workbench shell without calling an API. The shell implementation lives under `src/components/workbench`, while reusable component primitives live under `src/components/ui` and shared helpers live under `src/lib`.

## Workbench shell model

The current **workbench shell** is the stable desktop-style frame for ArchonExplorer. A shell is not the same thing as a finished product feature. It is the persistent structure around future features: the top application frame, activity rail, command/search affordance, main workspace, status bar, theme affordance, and safe setup indicators. Establishing those regions early lets later work packages add extraction, snapshot, search, graph, finding, evidence, and settings behavior without replacing the reader's mental model or rewriting the bootstrap path.

The **activity rail** is the left-side navigation placeholder. It names the planned workbench areas now visible to contributors: Dashboard, Extraction Center, Snapshots, Search, Projects, Findings, Diagnostics, and Settings. Only the Dashboard foundation placeholder is active. The other rail items are disabled and marked as later capability areas. That disabled state is intentional; it prevents the foundation shell from implying that extraction workflows, snapshot administration, project catalogues, finding review, or diagnostics have already been implemented.

The **command/search affordance** sits in the top frame and reserves the future location for broad architecture search and command-palette behavior. It is visibly unavailable and does not submit searches, run commands, query ArchonApi, inspect the graph, or display results. This is important because a search-shaped control can easily imply product capability. In the current shell, the copy, disabled button state, and `Unavailable` label all communicate that the affordance is only a reserved seam.

The **main workspace start state** is the large central area. It identifies the product as `ArchonExplorer`, explains that the workbench frame is ready, and states that operational and investigation features are pending. The start state deliberately names absent feature families: extraction runs, snapshots, graph visualisation, lenses, evidence inspection, findings triage, and real notifications. Contributors should preserve this kind of honest absence when adding future placeholders. A placeholder may orient the user, but it must not look like a feature that has been implemented.

The **status bar** is the bottom frame region reserved for cross-cutting context. It currently reports four safe states: active snapshot `current` is unavailable, API configuration is either present or not set, background work is not running, and nothing is selected. Those labels reserve future context without exposing raw environment variable names, connection strings, stack traces, Neo4j identifiers, raw Cypher, driver details, or implementation diagnostics.

The shell includes a simple light/dark **theme affordance**. Theme selection is implemented through a document-level `data-theme` attribute and CSS custom properties, also called CSS variables. The theme control is intentionally local and lightweight. It proves that the shell has a tokenized light/dark foundation, but it does not persist preferences, synchronize with operating-system theme settings, or introduce a settings store.

## Component-system foundation

ArchonExplorer now includes a shadcn/ui-compatible component-system seam. **shadcn/ui** is a copy-and-own component convention rather than a runtime component library in the same sense as a packaged design system. The repository records that convention in `components.json`, uses the `@` path alias for frontend imports, defines CSS custom properties compatible with shadcn token names, and provides minimal local primitives under `src/components/ui`.

The current primitives are intentionally small: `Button`, `Badge`, and `Card`. They are enough to prove ordinary shell composition, variant-style styling, focus behavior, and reusable panel structure. They should not be treated as a license to add unrelated UI frameworks. Future shell, form, table, dialog, command, tab, menu, badge, tooltip, popover, or notification patterns should extend this local primitive approach unless a later work package explicitly chooses another dependency for a well-documented reason.

The CSS remains hand-authored for this slice, while `tailwind.config.ts` and `components.json` preserve shadcn-compatible metadata. This hybrid is deliberate. It avoids expanding the foundation slice into a full styling migration while still making later generated or hand-authored shadcn-style primitives line up with the same aliases and tokens.

## Safe unavailable states and accessibility

Unavailable states in ArchonExplorer must be safe and specific. Safe means that the user can understand what is missing without seeing internal implementation details. The current shell reports only whether API base URL configuration exists; it does not print the configured URL, raw `VITE_` key name, exception text, driver type names, Neo4j internal identifiers, stack traces, or other sensitive diagnostics. When later work packages add real API calls, error handling should continue that pattern by converting dependency failures into controlled, user-facing messages.

The shell also avoids conveying state by color alone. Disabled rail items include text and `Later` labels. The command/search placeholder includes prose and an `Unavailable` label. The status bar states each status in words. Interactive controls use native buttons, accessible labels, disabled attributes where appropriate, semantic landmarks, and visible focus outlines. These basics matter because later workbench areas will become keyboard-heavy investigation workflows; the foundation should already model accessible interaction rather than postpone it.

## Runtime provider seam

A **runtime provider seam** is the React boundary where application-wide services are attached once and then made available to every component below it. ArchonExplorer currently exposes this seam through `src/providers/ApplicationProviders.tsx`. The provider creates one TanStack Query `QueryClient` for the browser page lifetime and supplies it through `QueryClientProvider`.

TanStack Query is present before real API calls exist because future server-state features should not each invent their own cache, retry, and polling conventions. In the current skeleton, no query is executed. The provider only proves that the application tree is ready for future controlled API client work, search results, snapshot lists, polling, and other server-backed state. The initial query defaults disable automatic retries and refetch-on-window-focus behavior so later features must make deliberate choices about user-visible loading and retry behavior.

## Aspire-hosted local development

The Aspire AppHost in `src/Archon` now declares ArchonExplorer as a **Vite resource**. A Vite resource is an Aspire-managed JavaScript application that runs the frontend development server as part of the local distributed application model. This means contributors can continue to run the frontend directly with `npm run dev`, but they can also launch the AppHost when they need to inspect the API, MCP host, Neo4j container, and browser shell together.

The AppHost uses the frontend project directory rather than copying UI code into the .NET host. It starts ArchonExplorer through the frontend package script named `dev`, waits for `ArchonApi` for local startup ordering, and passes the API HTTP endpoint to the frontend through the Vite-compatible `VITE_ARCHON_API_BASE_URL` environment value. The environment value is a development-time configuration seam, not a secret. It lets the shell report that API base URL configuration exists without hardcoding a localhost port into React source.

This composition preserves the ownership boundary. The AppHost may describe that ArchonExplorer exists, where it runs from, which API endpoint it should know about in local development, and which resource it should wait for. It must not contain React components, API client code, workbench state, routing behavior, graph visualization behavior, or user-interface feature logic. When future work packages add functional API calls, those calls should live behind frontend or application-layer seams rather than inside `src/Archon/Program.cs`.

Manual Aspire verification should be performed from the repository root when a contributor needs the full local distributed application:

```powershell
dotnet run --project .\src\Archon\Archon.csproj
```

After the Aspire dashboard opens, confirm that `neo4j`, `ArchonApi`, `ArchonMcp`, and `ArchonExplorer` appear as resources. Open the ArchonExplorer resource URL from the dashboard and confirm that the workbench shell renders with the same safe placeholder states described on this page. Stop the AppHost after verification. This is a manual smoke check because the AppHost is a long-running orchestration process and may require local container support for Neo4j.

## API base URL configuration seam

ArchonExplorer reads a development-time API base URL through the Vite environment key `VITE_ARCHON_API_BASE_URL`. A **Vite environment key** is a configuration value that Vite exposes to browser code only when the name is prefixed with `VITE_`. That prefix matters because browser code must not receive arbitrary process environment values, secrets, connection strings, credentials, or infrastructure diagnostics.

The current configuration adapter lives in `src/config/apiConfiguration.ts`. It trims the configured value and returns a safe object that distinguishes two states: configured and not configured. When the AppHost runs ArchonExplorer, it supplies this value from the `ArchonApi` HTTP endpoint. When the frontend is run directly through npm, a contributor may supply the same `VITE_ARCHON_API_BASE_URL` key manually or leave it unset. In both cases, the adapter does not validate connectivity, does not query `ArchonApi`, and does not display the raw environment-variable name in the UI. That limited behavior is intentional. The foundation proves that configuration can be represented safely while leaving typed API clients, route contracts, polling, and error handling to later work items.

When the value is absent, ArchonExplorer should still start. An unconfigured API base URL is an operational setup state, not a frontend crash. When the value is present, the UI can report that configuration exists, but this is not proof that the API is reachable or healthy. Contributors should preserve that distinction when adding later API calls.

## Current exclusions

The current frontend foundation does not yet implement functional API clients, extraction workflows, snapshot administration, search results, investigation tabs, graph views, lenses, evidence inspection, a real notification center, authentication, authorization, persisted theme preferences, or settings behavior. Those omissions are part of the staged implementation plan rather than accidental missing features.

This means contributors should avoid adding functional API calls, navigation behavior, server-state queries, graph rendering, notification behavior, or settings persistence to the shell as incidental cleanup. New behavior should follow the relevant work-package step so that documentation, validation, AppHost wiring, and component-system decisions stay aligned.

## Page-structure note

This topic page is the correct home for frontend setup, runtime-provider guidance, shell behavior, and the browser side of Aspire-hosted development because the subject crosses package management, React bootstrap, browser configuration, contributor validation, and safe UI boundaries. The .NET AppHost composition details also appear in [runtime foundation](runtime-foundation.md), but frontend behavior and Vite setup stay here. The details do not belong in `wiki/home.md`, which remains a landing page.

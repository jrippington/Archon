# Copilot Instructions for Radzen Blazor

This document is intended for GitHub Copilot or any AI coding assistant working in a Blazor codebase that uses **Radzen.Blazor**.

Use these instructions as project-level guidance whenever generating, editing, or reviewing Razor components, pages, layouts, services, and form/data UI that rely on Radzen.

---

## 1. Primary goals

When writing Radzen-based Blazor UI, always optimize for:

- Correct use of **Radzen components first**, before falling back to raw HTML or custom components.
- Clear, maintainable Razor markup with minimal duplication.
- Strong binding patterns with predictable state flow.
- Accessible, keyboard-friendly UI.
- Responsive layouts using Radzen layout primitives.
- Proper validation and form integration.
- Good performance for large data sets.
- Consistent visual design through Radzen theme and component variants.

---

## 2. Baseline assumptions

Assume the project uses:

- `Radzen.Blazor` NuGet package
- `@using Radzen`
- `@using Radzen.Blazor`
- `builder.Services.AddRadzenComponents();`
- `<RadzenTheme Theme="material" />` in `App.razor`
- `Radzen.Blazor.js` loaded from `_content/Radzen.Blazor/Radzen.Blazor.js`
- Interactive render mode enabled where needed
- `<RadzenComponents />` included in the main layout when dialogs, notifications, context menus, or tooltips are used

If any of these are missing in generated code, add or mention them.

---

## 3. General coding rules for Copilot

### 3.1 Prefer Radzen-native solutions
When implementing UI, first look for an appropriate Radzen component before inventing custom markup.

Examples:
- Use `RadzenDataGrid<TItem>` instead of a hand-built table for interactive tabular data.
- Use `RadzenTemplateForm<TItem>` plus validators instead of a plain `<EditForm>` unless there is a strong reason not to.
- Use `RadzenRow` / `RadzenColumn` / `RadzenStack` instead of ad hoc layout divs when a Radzen layout is appropriate.
- Use `RadzenDialog`, `RadzenNotification`, and `RadzenTooltip` patterns instead of custom overlays.

### 3.2 Generate complete examples
When generating code:
- Include `@page` when building a page.
- Include `@using` directives when needed.
- Include `@inject` services when needed.
- Include the full `@code` block.
- Include models, view models, and sample DTOs when necessary to make the example compile.
- Do not omit handlers, validation setup, or sample data if they are necessary for a working example.

### 3.3 Keep logic out of markup when possible
Prefer:
- concise bindings in `.razor`
- non-trivial transformations in helper methods
- reusable dialog/form/grid behavior in partial classes or services

Avoid deeply nested inline lambdas and large blocks of conditional rendering when a helper method or child component would be cleaner.

### 3.4 Respect Blazor binding conventions
Prefer:
- `@bind-Value` for simple two-way binding
- explicit `Value` + `Change` only when custom behavior is required
- strongly typed generics on components like `RadzenDataGrid<TItem>`, `RadzenDropDown<TValue>`, `RadzenNumeric<TValue>`

### 3.5 Favor explicitness
Always prefer readable, explicit parameters over clever shorthand when generating code for shared UI codebases.

---

## 4. Patterns to follow

## 4.1 Forms
Use this default form pattern unless told otherwise:

- `RadzenTemplateForm<TItem>` as the form container
- `RadzenFormField` for labelled inputs where appropriate
- input components bound with `@bind-Value`
- validators placed next to the inputs they validate
- submit and invalid submit handlers
- one clear primary action and optional cancel action

Preferred controls in forms:
- `RadzenTextBox`
- `RadzenTextArea`
- `RadzenPassword`
- `RadzenNumeric<TValue>`
- `RadzenDatePicker<TValue>`
- `RadzenDropDown<TValue>`
- `RadzenAutoComplete`
- `RadzenCheckBox<TValue>`
- `RadzenSwitch`
- `RadzenRadioButtonList<TValue>`
- `RadzenSelectBar<TValue>`
- `RadzenMask`
- `RadzenTimeSpanPicker<TValue>`
- `RadzenRating`
- `RadzenSlider<TValue>`
- `RadzenFileInput<TValue>`
- `RadzenUpload`

Validation defaults:
- Use `RadzenRequiredValidator` for mandatory fields.
- Use `RadzenEmailValidator` for email fields.
- Use `RadzenLengthValidator` for string length rules.
- Use `RadzenRegexValidator` for pattern rules.
- Use `RadzenNumericRangeValidator` for numeric ranges.

### 4.2 Data grids and tabular data
For interactive tables prefer `RadzenDataGrid<TItem>`.

Default expectations:
- Define columns explicitly.
- Use paging for non-trivial collections.
- Enable sorting/filtering only where useful.
- Use templates for custom display.
- Use `LoadData` for server-side paging/filtering/sorting on large data sets.
- Avoid loading huge collections into memory when server-side operations are possible.
- Use row/command templates for actions such as edit/delete/details.
- Prefer inline edit only when the interaction is simple and low risk.

Use:
- `RadzenDataGrid<TItem>`
- `RadzenDataGridColumn<TItem>`
- `RadzenDataFilter<TItem>` when advanced user-driven filtering is needed
- `RadzenPager` when standalone paging UI is required

### 4.3 Layout
For page layout use Radzen layout primitives consistently:
- `RadzenLayout`
- `RadzenHeader`
- `RadzenBody`
- `RadzenFooter`
- `RadzenSidebar`
- `RadzenSidebarToggle`
- `RadzenRow`
- `RadzenColumn`
- `RadzenStack`
- `RadzenSplitter`
- `RadzenSplitterPane`
- `RadzenTabs`
- `RadzenTabsItem`
- `RadzenCard`
- `RadzenCardGroup`
- `RadzenPanel`
- `RadzenFieldset`

Preferred rules:
- Use `RadzenStack` for vertical/horizontal alignment and spacing.
- Use `RadzenRow` + `RadzenColumn` for responsive grid layouts.
- Use `RadzenCard` for dashboard tiles and grouped content.
- Use `RadzenSplitter` only where end-user resizing adds real value.

### 4.4 Dialogs, notifications, menus, overlays
Prefer Radzen service-backed or component-backed UI:
- Dialogs through `DialogService` patterns and `RadzenDialog`
- Toast notifications through `NotificationService` and `RadzenNotification`
- Tooltips through `TooltipService` and `RadzenTooltip`
- Menus via `RadzenMenu`, `RadzenPanelMenu`, `RadzenProfileMenu`, `RadzenContextMenu`-style service patterns

If generating code that opens a dialog or notification, include the necessary injected service.

### 4.5 Navigation
Use:
- `RadzenMenu`
- `RadzenPanelMenu`
- `RadzenProfileMenu`
- `RadzenBreadCrumb`
- `RadzenLink`

Prefer semantic navigation structures and avoid abusing buttons for navigation.

### 4.6 Visual feedback
Use Radzen feedback primitives consistently:
- `RadzenAlert` for inline contextual messages
- `RadzenBadge` for status labels/counts
- `RadzenProgressBar` / `RadzenProgressBarCircular` for progress
- `RadzenSkeleton` for loading placeholders
- `RadzenButton` with `IsBusy` for async operations
- `RadzenChip` for compact tags/status elements

### 4.7 Charts and visualization
For data visualization prefer `RadzenChart` and series components.

Rules:
- Choose the simplest chart type that communicates the data.
- Include legends only when multiple series need distinction.
- Avoid overloaded charts with too many series.
- Label axes when meaning is not obvious.
- Use tooltips and data labels intentionally, not by default everywhere.
- Keep chart setup typed and explicit.

### 4.8 Rich text and markdown
For rich editing use `RadzenHtmlEditor`.
For markdown display use `RadzenMarkdown`.
If creating editor toolbars, use the Radzen HTML editor tool components rather than custom toolbar buttons where possible.

### 4.9 Scheduler, timeline, gantt
When building calendar/planning experiences:
- use `RadzenScheduler<TItem>` for appointments/calendars
- use `RadzenTimeline` for event chronology
- use `RadzenGantt<TItem>` for task scheduling/dependencies
Choose the component based on the domain problem rather than forcing one component into another role.

### 4.10 Upload and file input
- Use `RadzenUpload` for true upload workflows.
- Use `RadzenFileInput<TValue>` for simpler file selection/input scenarios.
- Always include file validation and user feedback.

---

## 5. UX and design rules

### 5.1 Consistency
Generated UI should keep consistent choices for:
- `Variant`
- `ButtonStyle`
- sizes
- spacing
- icons
- validation message placement

Do not mix random variants/styles on the same page without a reason.

### 5.2 Density
Prefer practical enterprise density:
- compact enough for real work
- not visually cramped
- avoid excessive nesting and oversized card stacks

### 5.3 Accessibility
Generated UI should:
- use labels and accessible names
- preserve keyboard navigation
- avoid click-only interactions
- not rely on color alone for meaning
- use validators and helper text in a clear way

### 5.4 Responsiveness
Assume pages must work on desktop first but remain responsive.
Use `RadzenRow`/`RadzenColumn`, `RadzenStack`, and layout breakpoints instead of custom CSS unless necessary.

---

## 6. Performance guidance

When Copilot generates Radzen-based UI:

- Prefer `LoadData` for large grids/lists.
- Avoid expensive per-row computations in Razor.
- Avoid recreating large collections on every render.
- Cache derived display values where sensible.
- Use virtualization-related approaches only when appropriate and supported by the chosen component/pattern.
- Keep dialogs/components small and focused.
- Avoid huge monolithic pages; split into child components.

For charts:
- avoid rendering too many series or points unless specifically required.

For forms:
- avoid unnecessary `StateHasChanged()` calls.

---

## 7. State management guidance

When generating page code:
- keep transient UI state local to the component
- move shared domain logic into services
- keep DTOs and UI view models separate when that improves clarity
- prefer async service calls with cancellation awareness for load operations
- guard against null state during initial render

Use common Blazor lifecycle methods appropriately:
- `OnInitializedAsync` for initial loading
- `OnParametersSetAsync` for parameter-driven reloads
- event callbacks for user interactions
- avoid doing heavy work directly in markup

---

## 8. Styling guidance

- Prefer built-in Radzen theming and variants first.
- Add custom CSS only when Radzen parameters and layout primitives are not enough.
- Keep CSS scoped to the page/component where possible.
- Avoid overriding Radzen internals unless absolutely necessary.
- Use icons from the Radzen/Material-style ecosystem consistently.

---

## 9. Testing and quality bar

Generated code should:
- compile
- avoid placeholder TODO logic unless explicitly requested
- use realistic example models
- handle empty/loading/error states
- include null safety where appropriate
- keep event handlers and service calls explicit

For data grids and forms, always consider:
- empty state
- loading state
- error state
- successful save/update feedback

---

## 10. Preferred generation templates

## 10.1 CRUD list page
Default structure:
- page header
- toolbar in `RadzenStack`
- filters
- `RadzenDataGrid<TItem>`
- action column
- empty/loading states
- optional dialog for create/edit
- notification on success/failure

## 10.2 Edit form page
Default structure:
- page title
- `RadzenTemplateForm<TItem>`
- grouped fields using `RadzenRow`/`RadzenColumn` or `RadzenStack`
- validators
- primary save button
- secondary cancel button
- loading/error/success handling

## 10.3 Dashboard page
Default structure:
- summary cards using `RadzenCard`
- charts using `RadzenChart`
- recent items grid/list
- responsive row/column layout
- avoid overcrowding

---

## 11. Things to avoid

Do not:
- replace obvious Radzen solutions with raw HTML controls
- generate massive inline style attributes
- put business logic directly in markup
- use weakly typed collections where a generic type is known
- rely on magic strings where a model property can be referenced clearly
- build custom modal, tooltip, or toast systems when Radzen already provides the pattern
- overuse nested cards/panels
- enable every grid feature by default without a reason
- generate inconsistent styling choices across the same page

---

## 12. If the user asks for a component choice

Use these defaults:

- **Data table** -> `RadzenDataGrid<TItem>`
- **Simple read-only table** -> `RadzenTable`
- **Form** -> `RadzenTemplateForm<TItem>`
- **Dropdown** -> `RadzenDropDown<TValue>`
- **Autocomplete** -> `RadzenAutoComplete`
- **Multi-select transfer** -> `RadzenPickList<TItem>`
- **Dialog** -> `RadzenDialog`
- **Notifications** -> `RadzenNotification`
- **Tabs** -> `RadzenTabs`
- **Accordion** -> `RadzenAccordion`
- **Sidebar navigation** -> `RadzenPanelMenu` or `RadzenMenu`
- **Wizard/steps** -> `RadzenSteps`
- **Charting** -> `RadzenChart`
- **Schedule/calendar** -> `RadzenScheduler<TItem>`
- **Timeline** -> `RadzenTimeline`
- **Task planning** -> `RadzenGantt<TItem>`
- **File upload** -> `RadzenUpload`
- **Formatted rich text editing** -> `RadzenHtmlEditor`
- **Markdown display** -> `RadzenMarkdown`

---

## 13. Complete Radzen component list

This is the current direct-use component inventory from the `Radzen.Blazor` API namespace. Base classes, interfaces, enums, and helper utility types are intentionally omitted from the main catalog below unless they are commonly used directly in markup.

## 13.1 Fundamentals and layout
- `RadzenAppearanceToggle`
- `RadzenBody`
- `RadzenCard`
- `RadzenCardGroup`
- `RadzenColumn`
- `RadzenFieldset`
- `RadzenFooter`
- `RadzenFormField`
- `RadzenGridLines`
- `RadzenGridRow`
- `RadzenHeader`
- `RadzenHeading`
- `RadzenLayout`
- `RadzenPanel`
- `RadzenRow`
- `RadzenSidebar`
- `RadzenSidebarToggle`
- `RadzenSplitter`
- `RadzenSplitterPane`
- `RadzenStack`
- `RadzenTabs`
- `RadzenTabsItem`

## 13.2 Buttons, actions, and feedback
- `RadzenAlert`
- `RadzenBadge`
- `RadzenButton`
- `RadzenChip`
- `RadzenFab`
- `RadzenFabMenu`
- `RadzenFabMenuItem`
- `RadzenNotification`
- `RadzenNotificationMessage`
- `RadzenProgressBar`
- `RadzenProgressBarCircular`
- `RadzenSkeleton`
- `RadzenSplitButton`
- `RadzenSplitButtonItem`
- `RadzenSteps`
- `RadzenStepsItem`
- `RadzenToggleButton`
- `RadzenTooltip`

## 13.3 Navigation and menus
- `RadzenBreadCrumb`
- `RadzenBreadCrumbItem`
- `RadzenLink`
- `RadzenMenu`
- `RadzenMenuItem`
- `RadzenMenuItemWrapper`
- `RadzenPanelMenu`
- `RadzenPanelMenuItem`
- `RadzenProfileMenu`
- `RadzenProfileMenuItem`

## 13.4 Text, media, display
- `RadzenGravatar`
- `RadzenHtml`
- `RadzenIcon`
- `RadzenImage`
- `RadzenLabel`
- `RadzenMarkdown`
- `RadzenText`
- `RadzenToc`
- `RadzenTocItem`

## 13.5 Forms and input
- `RadzenAutoComplete`
- `RadzenCheckBox<TValue>`
- `RadzenCheckBoxList<TValue>`
- `RadzenCheckBoxListItem<TValue>`
- `RadzenDatePicker<TValue>`
- `RadzenDropDown<TValue>`
- `RadzenDropDownDataGrid<TValue>`
- `RadzenDropDownDataGridColumn`
- `RadzenDropDownItem<TValue>`
- `RadzenEmailValidator`
- `RadzenFileInput<TValue>`
- `RadzenLengthValidator`
- `RadzenListBox<TValue>`
- `RadzenListBoxItem<TValue>`
- `RadzenLogin`
- `RadzenMask`
- `RadzenNumeric<TValue>`
- `RadzenNumericRangeValidator`
- `RadzenPassword`
- `RadzenPickList<TItem>`
- `RadzenRadioButtonList<TValue>`
- `RadzenRadioButtonListItem<TValue>`
- `RadzenRating`
- `RadzenRegexValidator`
- `RadzenRequiredValidator`
- `RadzenSecurityCode`
- `RadzenSelectBar<TValue>`
- `RadzenSelectBarItem`
- `RadzenSlider<TValue>`
- `RadzenSpeechToTextButton`
- `RadzenSwitch`
- `RadzenTemplateForm<TItem>`
- `RadzenTextArea`
- `RadzenTextBox`
- `RadzenTimeSpanPicker<TValue>`
- `RadzenUpload`
- `RadzenUploadHeader`

## 13.6 Data, grids, and collection UI
- `RadzenDataFilter<TItem>`
- `RadzenDataFilterProperty`
- `RadzenDataGrid<TItem>`
- `RadzenDataGridColumn<TItem>`
- `RadzenDataGridGroupRow<TItem>`
- `RadzenDataGridRow<TItem>`
- `RadzenDataList<TItem>`
- `RadzenDataListRow<TItem>`
- `RadzenPager`
- `RadzenPivotAggregate<TItem>`
- `RadzenPivotColumn<TItem>`
- `RadzenPivotDataGrid<TItem>`
- `RadzenPivotField<TItem>`
- `RadzenPivotRow<TItem>`
- `RadzenTable`
- `RadzenTableBody`
- `RadzenTableCell`
- `RadzenTableHeader`
- `RadzenTableHeaderCell`
- `RadzenTableHeaderRow`
- `RadzenTableRow`

## 13.7 Charts and visualization
- `RadzenAreaSeries<TItem>`
- `RadzenBarOptions`
- `RadzenBarSeries<TItem>`
- `RadzenBubbleSeries<TItem>`
- `RadzenCategoryAxis`
- `RadzenChart`
- `RadzenChartTooltip`
- `RadzenChartTooltipOptions`
- `RadzenDonutSeries<TItem>`
- `RadzenLegend`
- `RadzenLineSeries<TItem>`
- `RadzenMarkers`
- `RadzenPieSeries<TItem>`
- `RadzenScatterSeries<TItem>`
- `RadzenSeriesAnnotation<TItem>`
- `RadzenSeriesDataLabels`
- `RadzenSeriesMeanLine`
- `RadzenSeriesMedianLine`
- `RadzenSeriesModeLine`
- `RadzenSeriesTrendLine`
- `RadzenSeriesValueLine`
- `RadzenSparkline`
- `RadzenSpiderChart`
- `RadzenSpiderLegend`
- `RadzenSpiderSeries<TItem>`
- `RadzenStackedAreaSeries<TItem>`
- `RadzenStackedBarSeries<TItem>`
- `RadzenStackedColumnSeries<TItem>`
- `RadzenTicks`
- `RadzenValueAxis`

## 13.8 Gauges, QR, barcode, and diagrams
- `RadzenArcGauge`
- `RadzenArcGaugeScale`
- `RadzenArcGaugeScaleValue`
- `RadzenBarcode`
- `RadzenLinearGauge`
- `RadzenLinearGaugeScale`
- `RadzenLinearGaugeScalePointer`
- `RadzenLinearGaugeScaleRange`
- `RadzenQRCode`
- `RadzenRadialGauge`
- `RadzenRadialGaugeScale`
- `RadzenRadialGaugeScalePointer`
- `RadzenRadialGaugeScaleRange`
- `RadzenSankeyDiagram<TItem>`

## 13.9 Scheduling, timeline, and planning
- `RadzenDayView`
- `RadzenGantt<TItem>`
- `RadzenGanttColumn<TItem>`
- `RadzenGanttDayView<TItem>`
- `RadzenGanttMonthView<TItem>`
- `RadzenGanttWeekView<TItem>`
- `RadzenGanttYearView<TItem>`
- `RadzenMonthView`
- `RadzenMultiDayView`
- `RadzenScheduler<TItem>`
- `RadzenTimeline`
- `RadzenTimelineItem`
- `RadzenWeekView`
- `RadzenYearPlannerView`
- `RadzenYearTimelineView`
- `RadzenYearView`

## 13.10 Drag/drop and advanced interaction
- `RadzenAccordion`
- `RadzenAccordionItem`
- `RadzenCarousel`
- `RadzenCarouselItem`
- `RadzenDropZone<TItem>`
- `RadzenDropZoneContainer<TItem>`
- `RadzenDropZoneItem<TItem>`
- `RadzenMediaQuery`
- `RadzenTree`
- `RadzenTreeItem`
- `RadzenTreeLevel`

## 13.11 Rich text editor and editor tools
- `RadzenHtmlEditor`
- `RadzenHtmlEditorAlignCenter`
- `RadzenHtmlEditorAlignLeft`
- `RadzenHtmlEditorAlignRight`
- `RadzenHtmlEditorBackground`
- `RadzenHtmlEditorBackgroundItem`
- `RadzenHtmlEditorBold`
- `RadzenHtmlEditorColor`
- `RadzenHtmlEditorColorItem`
- `RadzenHtmlEditorCustomTool`
- `RadzenHtmlEditorFontName`
- `RadzenHtmlEditorFontNameItem`
- `RadzenHtmlEditorFontSize`
- `RadzenHtmlEditorFormatBlock`
- `RadzenHtmlEditorImage`
- `RadzenHtmlEditorIndent`
- `RadzenHtmlEditorItalic`
- `RadzenHtmlEditorJustify`
- `RadzenHtmlEditorLink`
- `RadzenHtmlEditorOrderedList`
- `RadzenHtmlEditorOutdent`
- `RadzenHtmlEditorRedo`
- `RadzenHtmlEditorRemoveFormat`
- `RadzenHtmlEditorSeparator`
- `RadzenHtmlEditorSource`
- `RadzenHtmlEditorStrikeThrough`
- `RadzenHtmlEditorSubscript`
- `RadzenHtmlEditorSuperscript`
- `RadzenHtmlEditorUnderline`
- `RadzenHtmlEditorUndo`
- `RadzenHtmlEditorUnlink`
- `RadzenHtmlEditorUnorderedList`

## 13.12 Communication, maps, and embedded content
- `RadzenAIChat`
- `RadzenChat`
- `RadzenDialog`
- `RadzenGoogleMap`
- `RadzenGoogleMapMarker`
- `RadzenSSRSViewer`
- `RadzenSSRSViewerParameter`

## 13.13 Theme/bootstrapper components
- `RadzenTheme`

---

## 14. Supplemental frequently seen direct-use support types

These are not usually top-level page building blocks, but Copilot may need them in markup or chart configuration:

- `RadzenAxisTitle`
- `RadzenChartTooltipOptions`
- `RadzenBarOptions`
- `RadzenMarkers`
- `RadzenSeriesDataLabels`
- `RadzenTicks`

---

## 15. Example instruction block for Copilot

Use the following as a compact reusable instruction block inside your repository:

```md
Build Blazor UI using Radzen components first. Prefer RadzenTemplateForm for forms, RadzenDataGrid for interactive tabular data, RadzenRow/RadzenColumn/RadzenStack for layout, DialogService and NotificationService patterns for overlays/feedback, and typed generic components throughout. Keep markup clean, responsive, accessible, and complete. Include full working examples with handlers, models, and validation. Use LoadData for large datasets, avoid custom HTML when a Radzen component exists, and keep business logic out of Razor markup.
```

---

## 16. Final instruction for Copilot

When asked to build or refactor a Blazor UI in this project:

1. choose the most appropriate Radzen component set
2. generate complete compiling examples
3. keep the UI consistent with Radzen patterns
4. preserve accessibility and responsiveness
5. prefer maintainable enterprise-style markup over clever shortcuts

# OmenXHub — Agent Context

## Goal
- Convert all pages/views to Windows 11 Settings-style layout using WPF-UI native controls and implicit styling.

## Constraints & Preferences
- .NET 4.8.1 WPF + WinForms, WPF-UI library (v3 alpha)
- Build: `dotnet build -c Release /p:Platform=x64` → **0 errors**
- App requires Administrator; launched via `Start-Process -Verb RunAs`
- All settings pages use `ui:CardControl` per item with `(*, Auto)` two-column grid (label left, control right, right-aligned vertical line)
- Items that trigger dialogs → ">" arrow button (`ChevronRight24`), not ComboBox
- Linked settings (OmenKey + sub-options, AccentColor + picker) → same card with indented sub-layout
- Automation list / quick action empty state → no card wrapper, right-aligned text + button
- All ComboBoxes use default WPF-UI implicit style
- Expand/collapse toggle buttons → standard WPF `ToggleButton` with `ui:SymbolIcon ChevronDown24`
- Text styles → inline `FontSize`/`FontWeight`/`Foreground`

## Progress
### Done
- **DashboardPage**: metric layout with 8 per-row Grids + color-coded ProgressBars; built-in+custom presets in single `PresetCombo` with rename/save only when custom index; system status & preset cards side-by-side
- **PerfPage**: 3 ComboBoxes (GfxMode/DbVersion/DState); EcoQos/CoreKeep → `ui:ToggleSwitch`; 8 expand Collapse buttons → `ToggleButton`+`SymbolIcon`; all `OmenSlider`/`OmenTextBox`/`OmenCheckBox`/`OmenListBox` removed
- **FanPage**: 3 ComboBoxes (FanMode/Sensitivity/Curve); AutoFanProtectToggle → `ui:ToggleSwitch`
- **LightingPage**: 8 ComboBoxes (Device/Protocol/AnimSpeed/Zone1-4/Animation); Brightness/Speed + Zone Colors side-by-side cards
- **SettingsPage → Windows 11 Settings-style**: 17+ single-item cards, `(*, Auto)` grid — left icon+title+subtitle, right control. OmenKey and AccentColor composite cards with indented sub-options. Custom Logo → ">" + reset buttons.
- **AutomationPage → Windows 11 Settings-style**: Master toggle card (启用自动化 + ToggleSwitch). Pipeline list area (no card wrapper, right-aligned empty + "新建" button). Quick actions (left heading+subtitle, right-aligned empty + "新建" button).
- **OtherPage**: 6 toggles → `ui:ToggleSwitch`
- **SysInfoPage**: 5 ComboBoxes (MonCpu/Gpu/Fan/Refresh/TempDisp); unused `_loading` removed
- **All Pages**: `OmenScrollViewer`/`OmenHeading`/`OmenSubHeading`/`OmenCaption` style refs removed
- **HelpWindow.xaml**: all `Omen*` style/gui refs replaced with inline properties
- **PipelineEditorWindow → 4-module refactoring**: (1) Card-based selector dialogs with proper padding/fonts; (2) Trigger/step cards with `ui:ToggleSwitch`, up/down/expand/delete buttons; (3) Expandable step body (`ChevronDown24`/`ChevronUp24`) with inline Value/Delay editing; (4) `Margin="20"` uniform padding, Grid layout with title/name/lists/buttons rows
- **Mouse wheel scrolling**: handler registered on visual root with `handledEventsToo: true`, walks UP from page to find `DynamicScrollViewer`, `HasOpenComboBox()` skips when dropdown open
- **Sidebar scrollbar hidden**: `HidePaneScrollBar` finds `ScrollViewer` under NavigationView → `VerticalScrollBarVisibility="Hidden"`
- **Build**: 0 errors (all pre-existing warnings: CLS compliance, Mono.Posix ref, version conflicts)

### Done (current session)
- **Tray quick actions fix**: `IsQuickAction` no longer hides triggerless pipelines; `GetQuickActions()` null-checks `Pipelines`; `RebuildMenu()` rebuilds both `TrayHelper._contextMenu` + `TrayService.wpfContextMenu` via `_trayHelperRef`; `RegisterTrayHelper()` called from `MainWindow`; `RebuildMenu()` called from `AutomationService.Load()`
- **Quick action editor mode**: `PipelineEditorWindow._isQuickAction` hides "Add Trigger" button and delete button on QuickAction trigger card; auto-creates QuickAction trigger on open
- **Quick action XAML fix**: `WrapPanel` moved outside header `Grid` to stop overlap
- **OmenKey preset sync**: `DashboardPage` subscribes to `ConfigService.OnPresetCycled` → saves prev preset, loads new values, applies hardware (`ApplyPresetHardware`), shows OSD (`ShowPresetOsd`), updates dropdown under `_loading=true` guard, refreshes buttons + dashboard
- **Build**: 0 errors

### Remaining Issues
- **WiFi/Bluetooth toggles**: exist only as automation step types (`SetWiFi`/`SetBluetooth`), not page-level toggles — WMI method may fail silently on modern Windows

## Key Decisions
- `ui:ToggleSwitch` for all on/off flags
- WPF-UI v3 alpha has no `ui:ToggleButton` — expand/collapse stays as WPF `ToggleButton` with `SymbolIcon` child
- `DynamicScrollViewer` (WPF-UI content scroll host) found by walking UP from page, not down
- All pages use uniform card pattern: `ui:CardControl` > `Grid(*, Auto)` > left StackPanel + right control
- PipelineEditorWindow dynamically-generated items (triggers/steps) use code-behind card styling matching XAML cards

## Critical Context
- All explicit `Omen*` style/GUI references removed from Pages and Views; only self-referencing styles remain in `Themes/`
- `SymbolIcon.Symbol` is `SymbolRegular` enum (not string) in code-behind — use `Wpf.Ui.Controls.SymbolRegular.ChevronDown24`
- `FindResource("AccentOmenBrush")` in DashboardPage.xaml.cs is a brush lookup — kept as-is
- `HidePaneScrollBar` runs on `DispatcherPriority.Background` after `Loaded`
- Pipeline step Value/Delay editing auto-saves via `TextChanged` (no save button per card)

## Relevant Files
- `Pages/SettingsPage.xaml` — Windows 11 Settings-style layout (17 cards)
- `Pages/AutomationPage.xaml` — master toggle + pipeline list + quick actions
- `Views/PipelineEditorWindow.xaml` + `.cs` — 4-module refactoring with card UI
- `Views/MainWindow.xaml.cs` — `AttachWheelHandler`/`FindScrollHost`/`HasOpenComboBox`/`HidePaneScrollBar`

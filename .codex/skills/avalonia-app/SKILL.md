---
name: avalonia-app
description: Build, migrate, review, test, and design the Draft Agent desktop application with Avalonia, C#, XAML, MVVM, cross-platform styling, and Avalonia MCP documentation/devtools. Use when working on Avalonia project files (.sln, .csproj, .axaml, .cs, view models, services, tests), replacing Tauri/Web UI, implementing desktop UI, troubleshooting Avalonia layout/binding/windowing, or validating Win/Mac consistency.
---

# Avalonia App

## Project Defaults

- Treat Avalonia as the primary desktop stack for Draft Agent; do not add new Tauri features.
- Prefer `net9.0`, Avalonia 12 template output, CommunityToolkit.Mvvm, compiled bindings, and MVVM.
- Keep domain logic in testable C# services under `DraftAgent.Core`; keep Avalonia UI thin.
- Use XAML resources and styles for repeatable UI, not inline one-off styling everywhere.
- Keep visual design close to Apple Store style: light gray app background, white surfaces, restrained shadow, clear typography, product-like cards, and predictable controls.

## Workflow

1. Inspect the solution and project files before editing.
2. Before modifying workbench drag/drop or drag preview animation, read the `README.md` section `工作台拖拽硬规则` and treat it as a hard implementation contract.
3. Query Avalonia official docs through the Avalonia Build MCP when API details, controls, styling, compiled bindings, or platform behavior might have changed.
4. Use Avalonia DevTools MCP only when configured and licensed; otherwise verify with build/tests and screenshots/manual launch.
5. Implement features with MVVM:
   - Models and parsing in `DraftAgent.Core`.
   - View models in `DraftAgent.App/ViewModels`.
   - Views in `DraftAgent.App/Views`.
   - Styles/resources in `DraftAgent.App/Styles`.
5. Validate with `dotnet build` and `dotnet test`. For UI layout changes, also run the app or capture screenshots when tooling is available.

## UI Rules

- Use a real workbench as the first screen: script input, shot breakdown, inspector/binding panel, and timeline.
- Prefer `Grid`, `DockPanel`, `SplitView`, `TabControl`, `ItemsControl`, and `DataGrid`-style table layouts over custom drawing.
- Keep fixed-format areas stable with row/column definitions and minimum sizes.
- Avoid nesting decorative cards inside cards. Use cards for repeated shots, modals, and inspector groups.
- Do not rely on native platform widgets for key visuals when cross-platform consistency matters; style Avalonia controls explicitly.
- Keep text from clipping by using wrapping, trimming, and sensible min widths.

## Testing

- Core parser and export behavior must have unit tests.
- View models should expose deterministic observable state and commands that can be tested without launching the app.
- Before declaring complete, run:

```powershell
dotnet build .\DraftAgent.sln
dotnet test .\DraftAgent.sln
```

## References

- Read `references/mcp.md` when connecting Avalonia MCP servers or explaining tool availability.
- Read `references/project-structure.md` before adding projects, folders, or large features.

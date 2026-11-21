# Draft Agent Avalonia Structure

Use this target structure:

```text
DraftAgent.sln
src/
  DraftAgent.App/          Avalonia UI, views, view models, styles
  DraftAgent.Core/         script analysis, draft model, exports
tests/
  DraftAgent.Core.Tests/   parser, timeline, export tests
README.md                  L0 project entry, commands, workflow summary
docs/                      L1/L2 architecture, hard rules, review evidence
```

The previous Web/Tauri prototype may remain as historical reference during migration, but it is not the active application runtime.
Draft Agent uses progressive documentation: keep `README.md` as the L0 entry and store deeper architecture, engineering rules, and review evidence under `docs/`.

Prefer these boundaries:

- `DraftAgent.Core`: no Avalonia references.
- `DraftAgent.App`: can reference `DraftAgent.Core`.
- Tests reference core and view models only when practical.

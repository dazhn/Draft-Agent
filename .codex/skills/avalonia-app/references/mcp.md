# Avalonia MCP

## Build MCP

Avalonia Build MCP provides official documentation access for agents. Prefer it over web search for Avalonia API, XAML, styling, layout, and binding questions.

Project-level MCP configuration lives in `.vscode/mcp.json`.

Expected server:

```json
{
  "servers": {
    "avalonia": {
      "url": "https://api.avaloniaui.net/mcp"
    }
  }
}
```

If a Codex session does not expose the Avalonia MCP tools, use the local config as setup guidance and fall back to official Avalonia docs.

## DevTools MCP

Avalonia DevTools MCP can inspect a running app, visual tree, properties, screenshots, and styling diagnostics. It requires Avalonia Plus/Accelerate access. Do not assume it is available unless a session exposes the tools or the user confirms licensing/configuration.

When available, use it for:

- Visual tree inspection.
- Layout/binding troubleshooting.
- Screenshot validation.
- Cross-platform UI consistency checks.

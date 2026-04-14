# Testing WPF Apps with Claude Code

This guide explains how to use WpfMcpInspector with [Claude Code](https://claude.ai/claude-code) to test your WPF application.

## Overview

The testing pattern is:

1. Build and launch your WPF app with the inspector enabled
2. Claude Code uses `curl` to discover UI elements, perform actions, and verify results
3. Screenshots provide visual verification

This creates a closed loop where Claude Code can interact with your running WPF app entirely through HTTP endpoints.

## Step 1: Build and Launch

Build your app in Debug mode (or whichever configuration has the inspector enabled):

```bash
dotnet build -c Debug
dotnet run --project MyApp/MyApp.csproj
```

The inspector server starts automatically and prints the port to the debug output.

## Step 2: Verify with /health

Confirm the server is running:

```bash
curl http://localhost:9222/health
```

Expected response:
```json
{
  "status": "ok",
  "app": "MyApp",
  "uptimeSeconds": 3.2,
  "componentCount": 45
}
```

If this fails, the app may not be running, or the port may be different (try 9223, 9224).

## Step 3: Discover UI with /tree

Get all interactable elements:

```bash
curl http://localhost:9222/tree
```

This returns buttons, text boxes, combo boxes, and other interactive elements with their names, types, and positions.

To see everything including labels and static text:
```bash
curl "http://localhost:9222/tree?interactable=false"
```

To filter to specific types:
```bash
curl "http://localhost:9222/tree?type=Button,TextBox"
```

## Step 4: Interact with /action

Perform UI actions by posting JSON:

**Type into a text box:**
```bash
curl -X POST http://localhost:9222/action \
  -H "Content-Type: application/json" \
  -d '{"action":"type","target":"InputBox","text":"Hello from Claude!"}'
```

**Click a button:**
```bash
curl -X POST http://localhost:9222/action \
  -H "Content-Type: application/json" \
  -d '{"action":"click","target":"SubmitButton"}'
```

**Select a combo box item:**
```bash
curl -X POST http://localhost:9222/action \
  -H "Content-Type: application/json" \
  -d '{"action":"select_combo","target":"ColorCombo","value":"Blue"}'
```

**Check a checkbox:**
```bash
curl -X POST http://localhost:9222/action \
  -H "Content-Type: application/json" \
  -d '{"action":"check","target":"AgreeCheckbox"}'
```

**Navigate a menu:**
```bash
curl -X POST http://localhost:9222/action \
  -H "Content-Type: application/json" \
  -d '{"action":"menu","target":"_","path":"File > Preferences"}'
```

Each action response includes the updated component state, so you can verify the effect immediately.

## Step 5: Assert State with /component

Read detailed state of any named element:

```bash
curl http://localhost:9222/component/StatusText
```

Response:
```json
{
  "name": "StatusText",
  "type": "TextBlock",
  "visible": true,
  "enabled": true,
  "foreground": "#808080",
  "fontSize": 12,
  "extra": null
}
```

Use this to verify that actions had the expected effect (e.g., after clicking Submit, check that StatusText shows the expected message).

## Step 6: Capture Visual State with /screenshot

Take a screenshot of the entire window:

```bash
curl http://localhost:9222/screenshot
```

Or capture a specific element:

```bash
curl "http://localhost:9222/screenshot?component=ItemList"
```

The response contains a base64 PNG data URI. Claude Code can read the image directly to verify visual layout.

## Step 7: Example Testing Scenario

Here is a complete test scenario using the SimpleWpfApp sample:

```bash
# 1. Verify the app is running
curl http://localhost:9222/health

# 2. Discover available UI elements
curl http://localhost:9222/tree

# 3. Type text into the input box
curl -X POST http://localhost:9222/action \
  -d '{"action":"type","target":"InputBox","text":"Test message"}'

# 4. Check the checkbox
curl -X POST http://localhost:9222/action \
  -d '{"action":"check","target":"AgreeCheckbox"}'

# 5. Select "Blue" in the color combo
curl -X POST http://localhost:9222/action \
  -d '{"action":"select_combo","target":"ColorCombo","value":"Blue"}'

# 6. Select the third item in the list
curl -X POST http://localhost:9222/action \
  -d '{"action":"select_listitem","target":"ItemList","index":2}'

# 7. Click Submit
curl -X POST http://localhost:9222/action \
  -d '{"action":"click","target":"SubmitButton"}'

# 8. Verify the status text changed
curl http://localhost:9222/component/StatusText
# Expected: text contains "Submitted: Test message"

# 9. Take a screenshot to verify visual state
curl http://localhost:9222/screenshot

# 10. Test menu navigation
curl -X POST http://localhost:9222/action \
  -d '{"action":"menu","target":"_","path":"File > New"}'

# 11. Verify the input was cleared
curl http://localhost:9222/component/InputBox
```

## Tips for Claude Code Users

- **Element names are stable** -- use `x:Name` strings rather than integer IDs, which can change between app restarts.
- **Action responses include state** -- you don't always need a separate `/component` call after an action; the response already contains the updated state.
- **100ms settle delay** -- the server waits 100ms after each action for WPF event propagation. For longer operations (file dialogs, network calls), add additional delays.
- **Interactable filter** -- the default `/tree` only returns interactive elements. Use `?interactable=false` when you need to read labels or status text.
- **Menu paths** -- strip the WPF access key underscore from menu headers. Use `"File > New"` not `"_File > _New"`.

## Real-World Example

[YTubeFetch](https://github.com/user/ytubefetcher) uses WpfMcpInspector to enable Claude Code to test its video download workflow end-to-end: pasting URLs, selecting download types, monitoring progress, and verifying output files.

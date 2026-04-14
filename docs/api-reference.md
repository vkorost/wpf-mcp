# API Reference

WpfMcpInspector exposes a REST API on `http://localhost:9222/` (falls back to ports 9223, 9224 if unavailable). All responses are JSON with `Content-Type: application/json; charset=utf-8`.

---

## GET /health

Returns server status and basic metrics.

**Request:**
```
GET /health
```

**Response (200):**
```json
{
  "status": "ok",
  "app": "SimpleWpfApp",
  "uptimeSeconds": 42.5,
  "componentCount": 127
}
```

| Field            | Type   | Description                                         |
|------------------|--------|-----------------------------------------------------|
| `status`         | string | Always `"ok"` if the server is running               |
| `app`            | string | Value of `McpServer.AppName`                         |
| `uptimeSeconds`  | number | Seconds since the server started                     |
| `componentCount` | int    | Total elements in the WPF visual tree                |

---

## GET /tree

Returns a flat list of UI elements from the visual tree.

**Request:**
```
GET /tree
GET /tree?interactable=false
GET /tree?type=Button,TextBox
GET /tree?interactable=false&type=TextBlock,Button
```

**Query Parameters:**

| Parameter      | Type    | Default | Description                                              |
|----------------|---------|---------|----------------------------------------------------------|
| `interactable` | boolean | `true`  | When `true`, only returns interactive elements (buttons, text boxes, etc.). Set to `false` to include all elements |
| `type`         | string  | (none)  | Comma-separated list of type names to filter by (e.g. `Button,TextBox`) |

**Response (200):**
```json
{
  "components": [
    {
      "id": 1,
      "type": "Button",
      "name": "SubmitButton",
      "parent": null,
      "bounds": { "x": 100, "y": 50, "width": 80, "height": 28 },
      "visible": true,
      "enabled": true,
      "focused": false,
      "text": "Submit",
      "childCount": 1
    },
    {
      "id": 2,
      "type": "TextBox",
      "name": "InputBox",
      "parent": null,
      "bounds": { "x": 10, "y": 50, "width": 350, "height": 28 },
      "visible": true,
      "enabled": true,
      "focused": true,
      "text": "Hello world",
      "childCount": 3
    }
  ],
  "truncated": false,
  "totalComponents": 2
}
```

| Field             | Type    | Description                                          |
|-------------------|---------|------------------------------------------------------|
| `components`      | array   | Flat list of `ComponentNode` objects                  |
| `truncated`       | boolean | `true` if the result was truncated (max 200 nodes)    |
| `totalComponents` | int     | Total matching nodes before truncation                |

**ComponentNode fields:**

| Field        | Type    | Description                                          |
|--------------|---------|------------------------------------------------------|
| `id`         | int     | Stable integer ID (assigned per element, survives across requests) |
| `type`       | string  | WPF type name (e.g. `Button`, `TextBox`, `ComboBox`) |
| `name`       | string  | `x:Name` from XAML (empty string if unnamed)          |
| `parent`     | int?    | ID of the parent node (null for top-level)            |
| `bounds`     | object  | Position and size relative to the window              |
| `visible`    | boolean | `IsVisible` state                                     |
| `enabled`    | boolean | `IsEnabled` state                                     |
| `focused`    | boolean | `IsFocused` state                                     |
| `text`       | string? | Extracted text content (truncated to 200 chars)       |
| `childCount` | int     | Number of visual children                             |

**Interactable types** (included when `interactable=true`):
Button, TextBox, ComboBox, ListBox, ListView, TreeView, CheckBox, RadioButton, Slider, TabControl, Menu, MenuItem, ToggleButton, ProgressBar, RichTextBox, PasswordBox

---

## GET /component/{nameOrId}

Returns detailed state for a single element, identified by `x:Name` or integer ID.

**Request:**
```
GET /component/SubmitButton
GET /component/3
```

**Response (200):**
```json
{
  "name": "ColorCombo",
  "type": "ComboBox",
  "bounds": { "x": 120, "y": 90, "width": 150, "height": 28 },
  "visible": true,
  "enabled": true,
  "focused": false,
  "foreground": "#000000",
  "background": "#FFFFFF",
  "tooltip": null,
  "fontFamily": "Segoe UI",
  "fontSize": 12,
  "extra": {
    "items": ["Red", "Green", "Blue"],
    "selectedItem": "Green",
    "selectedIndex": 1,
    "isEditable": false,
    "isDropDownOpen": false
  }
}
```

**Response (404):**
```json
{
  "error": "Component not found: NonExistentName"
}
```

The `extra` dictionary contains type-specific properties:

| Element Type   | Extra Fields                                                            |
|----------------|-------------------------------------------------------------------------|
| TextBox        | `text`, `isReadOnly`, `caretIndex`, `selectionStart`, `selectionLength` |
| ComboBox       | `items`, `selectedItem`, `selectedIndex`, `isEditable`, `isDropDownOpen`|
| CheckBox       | `isChecked`                                                             |
| RadioButton    | `isChecked`                                                             |
| Slider         | `value`, `minimum`, `maximum`, `tickFrequency`                          |
| TabControl     | `tabHeaders`, `selectedIndex`                                           |
| ProgressBar    | `value`, `minimum`, `maximum`, `isIndeterminate`                        |
| ListBox        | `selectedIndex`, `itemCount`                                            |
| ListView       | `columns`, `selectedIndex`, `itemCount`, `items`                        |
| DataGrid       | `columns`, `selectedIndex`, `itemCount`, `items`                        |
| TreeView       | `nodes`, `selectedPath`                                                 |
| Menu           | `items` (recursive menu structure)                                      |
| MenuItem       | `header`, `isChecked`, `isCheckable`, `items`                           |

---

## POST /action

Executes a UI action on a target element. Returns the updated component state after a 100ms settle delay.

**Request:**
```
POST /action
Content-Type: application/json
```

**Common fields:**

| Field    | Type    | Required | Description                                   |
|----------|---------|----------|-----------------------------------------------|
| `action` | string  | Yes      | Action name (see below)                       |
| `target` | string  | Yes      | Element name or integer ID                    |
| `text`   | string  | No       | Text for `type` action                        |
| `value`  | string  | No       | Value for `select_combo` (by display text)    |
| `index`  | int     | No       | Index for `select_combo`, `select_tab`, `select_listitem` |
| `path`   | string  | No       | Menu path for `menu` action                   |

### Action: click

Click a button or invoke an element via its automation peer.

```json
{
  "action": "click",
  "target": "SubmitButton"
}
```

### Action: type

Set text in a TextBox.

```json
{
  "action": "type",
  "target": "InputBox",
  "text": "Hello, World!"
}
```

### Action: clear

Clear a TextBox.

```json
{
  "action": "clear",
  "target": "InputBox"
}
```

### Action: select_combo

Select a ComboBox item by index or display value.

```json
{
  "action": "select_combo",
  "target": "ColorCombo",
  "value": "Blue"
}
```

```json
{
  "action": "select_combo",
  "target": "ColorCombo",
  "index": 2
}
```

### Action: select_tab

Select a tab by index.

```json
{
  "action": "select_tab",
  "target": "SettingsTabControl",
  "index": 1
}
```

### Action: check

Check a CheckBox or ToggleButton.

```json
{
  "action": "check",
  "target": "AgreeCheckbox"
}
```

### Action: uncheck

Uncheck a CheckBox or ToggleButton.

```json
{
  "action": "uncheck",
  "target": "AgreeCheckbox"
}
```

### Action: select_listitem

Select an item in a ListBox or ListView by index.

```json
{
  "action": "select_listitem",
  "target": "ItemList",
  "index": 2
}
```

### Action: menu

Navigate and click a menu item by path. Use `>` to separate levels. Access key underscores (`_File`) are stripped automatically.

```json
{
  "action": "menu",
  "target": "ignored",
  "path": "File > Preferences"
}
```

### Action: focus

Set keyboard focus to an element.

```json
{
  "action": "focus",
  "target": "InputBox"
}
```

**Success Response (200):**
```json
{
  "success": true,
  "action": "click",
  "target": "SubmitButton",
  "componentState": {
    "type": "Button",
    "visible": true,
    "enabled": true,
    "text": "Submit"
  }
}
```

**Error Response (400):**
```json
{
  "success": false,
  "action": "click",
  "target": "NonExistent",
  "error": "Target not found: NonExistent"
}
```

---

## GET /screenshot

Captures the window (or a specific component) as a PNG image encoded in base64.

**Request:**
```
GET /screenshot
GET /screenshot?component=InputBox
```

**Query Parameters:**

| Parameter   | Type   | Default    | Description                                    |
|-------------|--------|------------|------------------------------------------------|
| `component` | string | (none)     | Name of a specific element to capture. Omit for full window |

**Response (200):**
```json
{
  "image": "data:image/png;base64,iVBORw0KGgo...",
  "width": 600,
  "height": 400
}
```

| Field   | Type   | Description                                  |
|---------|--------|----------------------------------------------|
| `image` | string | Data URI with base64-encoded PNG              |
| `width` | int    | Width in device-independent pixels            |
| `height`| int    | Height in device-independent pixels           |

To save the image from the command line:
```bash
curl -s http://localhost:9222/screenshot | python -c "import sys,json,base64; d=json.load(sys.stdin); open('shot.png','wb').write(base64.b64decode(d['image'].split(',')[1]))"
```

---

## GET /state

Returns custom application state. Only available when an `AppStateProvider` delegate is registered.

**Request:**
```
GET /state
```

**Response (200):** The JSON object returned by the registered `AppStateProvider` delegate.

**Response (404) when no provider is registered:**
```json
{
  "error": "No state provider registered"
}
```

---

## Error Responses

All error responses follow this format:

```json
{
  "error": "Error message",
  "detail": "Optional additional detail"
}
```

| Status | Meaning                                        |
|--------|------------------------------------------------|
| 400    | Bad request (missing parameters, invalid JSON) |
| 404    | Element or endpoint not found                  |
| 500    | Internal server error                          |
| 503    | Application is shutting down                   |

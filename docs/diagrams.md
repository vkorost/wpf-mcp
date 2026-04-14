# WPF MCP Inspector Diagrams

## 1. How the MCP Server Works (Runtime Flow)

This diagram shows the runtime interaction between an external client (Claude Code or curl), the embedded HTTP server, the WPF Dispatcher, and the live WPF application.

```mermaid
sequenceDiagram
    participant Client as External Client<br/>(Claude Code / curl)
    participant HTTP as McpServer<br/>(HttpListener Thread)
    participant Disp as WPF Dispatcher<br/>(UI Thread Marshal)
    participant UI as WPF Application<br/>(Visual Tree)
    participant Render as RenderTargetBitmap<br/>(Screen Capture)

    Note over Client,Render: === Startup ===
    UI->>UI: Application_Startup() creates MainWindow
    UI->>HTTP: new McpServer(window).Start()
    HTTP->>HTTP: HttpListener binds localhost:9222-9224

    Note over Client,Render: === GET /tree — Visual Tree ===
    Client->>HTTP: GET /tree?interactable=true
    HTTP->>Disp: Dispatcher.Invoke(BuildTree)
    Disp->>UI: VisualTreeHelper.GetChildrenCount()
    Disp->>UI: Recursive depth-first traversal
    Disp->>UI: Assign stable IDs via ConditionalWeakTable
    Disp-->>HTTP: List<ComponentNode> (flat array, max 200)
    HTTP->>HTTP: JsonSerializer.Serialize (camelCase)
    HTTP-->>Client: 200 OK — JSON TreeResponse

    Note over Client,Render: === GET /component/{name} — Element State ===
    Client->>HTTP: GET /component/InputBox
    HTTP->>Disp: Dispatcher.Invoke(FindByName + Inspect)
    Disp->>UI: TreeWalker.FindByName("InputBox")
    Disp->>UI: ComponentInspector.Inspect(element)
    Disp->>UI: Extract type-specific state<br/>(text, selectedItem, isChecked, items...)
    Disp-->>HTTP: ComponentDetail
    HTTP-->>Client: 200 OK — JSON detail object

    Note over Client,Render: === POST /action — Click Button ===
    Client->>HTTP: POST /action<br/>{"action":"click", "target":"SubmitButton"}
    HTTP->>HTTP: Deserialize ActionRequest
    HTTP->>Disp: Dispatcher.Invoke(ActionExecutor.Execute)
    Disp->>UI: TreeWalker.FindByName("SubmitButton")

    alt ButtonBase (has Click event)
        Disp->>UI: RaiseEvent(ButtonBase.ClickEvent)
    else Other (AutomationPeer)
        Disp->>UI: IInvokeProvider.Invoke()
    end

    Disp->>Disp: Thread.Sleep(100ms) — settle delay
    Disp->>UI: ComponentInspector.Inspect(element)
    Disp-->>HTTP: ActionResponse(success, componentState)
    HTTP-->>Client: 200 OK — JSON with post-action state

    Note over Client,Render: === POST /action — Type Text ===
    Client->>HTTP: POST /action<br/>{"action":"type",<br/>"target":"InputBox", "text":"Hello"}
    HTTP->>Disp: Dispatcher.Invoke(Execute)
    Disp->>UI: TreeWalker.FindByName("InputBox")
    Disp->>UI: TextBox.Text = "Hello"
    Disp->>UI: TextBox.CaretIndex = text.Length
    Disp->>UI: RaiseEvent(TextChangedEvent)
    Disp->>Disp: 100ms settle
    Disp-->>HTTP: ActionResponse with updated text state
    HTTP-->>Client: 200 OK

    Note over Client,Render: === POST /action — Select ComboBox ===
    Client->>HTTP: POST /action<br/>{"action":"select_combo",<br/>"target":"ColorCombo", "value":"Blue"}
    HTTP->>Disp: Dispatcher.Invoke(Execute)
    Disp->>UI: TreeWalker.FindByName("ColorCombo")
    Disp->>UI: ComboBox.SelectedItem = "Blue"<br/>(case-insensitive match)
    Disp->>UI: SelectionChanged event fires
    Disp->>Disp: 100ms settle
    Disp-->>HTTP: ActionResponse with updated combo state
    HTTP-->>Client: 200 OK

    Note over Client,Render: === POST /action — Navigate Menu ===
    Client->>HTTP: POST /action<br/>{"action":"menu",<br/>"target":"_", "path":"File > New"}
    HTTP->>Disp: Dispatcher.Invoke(Execute)
    Disp->>UI: Find Menu in visual tree
    Disp->>UI: Match "File" header (strip _ access keys)
    Disp->>UI: Match "New" in sub-items
    Disp->>UI: RaiseEvent(MenuItem.ClickEvent)
    Disp->>Disp: 100ms settle
    Disp-->>HTTP: ActionResponse
    HTTP-->>Client: 200 OK

    Note over Client,Render: === GET /screenshot — Window Capture ===
    Client->>HTTP: GET /screenshot
    HTTP->>Disp: Dispatcher.Invoke(ScreenshotCapture.Capture)
    Disp->>UI: Measure window ActualWidth/Height
    Disp->>Render: new RenderTargetBitmap(w, h, 96, 96)
    Render->>UI: Render(window)
    Render-->>Disp: Bitmap pixels
    Disp->>Disp: PngBitmapEncoder → byte[]
    Disp->>Disp: Base64 → data:image/png;base64,...
    Disp-->>HTTP: ScreenshotResponse(dataUri, w, h)
    HTTP-->>Client: 200 OK — base64-encoded PNG

    Note over Client,Render: === GET /health — Server Status ===
    Client->>HTTP: GET /health
    HTTP->>Disp: Dispatcher.Invoke(TreeWalker.BuildTree)
    Disp->>UI: Count all visual tree elements
    Disp-->>HTTP: component count
    HTTP->>HTTP: Calculate uptime
    HTTP-->>Client: 200 OK — HealthResponse
```

## 2. How the Server is Designed (Architecture & Class Structure)

This diagram shows the internal class structure, responsibilities, and data flow within the WpfMcpInspector library and its integration with a host application.

```mermaid
graph TB
    subgraph External["External Clients"]
        CC["Claude Code<br/><i>MCP tool calls</i><br/>AI agent loop"]
        CLI["curl / HTTP Client<br/>Direct HTTP calls"]
    end

    subgraph WpfProcess["WPF Application Process (Single Process)"]

        subgraph HostApp["Host Application (e.g. SimpleWpfApp)"]
            App["App.xaml.cs<br/><i>Application_Startup()</i><br/>Entry point"]
            MW["MainWindow<br/>XAML layout + code-behind"]

            subgraph Controls["UI Controls"]
                TB["TextBox<br/><i>InputBox</i>"]
                BTN["Button<br/><i>SubmitButton</i>"]
                CB["ComboBox<br/><i>ColorCombo</i>"]
                CHK["CheckBox<br/><i>AgreeCheckbox</i>"]
                LB["ListBox<br/><i>ItemList</i>"]
                MN["Menu<br/>File, Help"]
                ST["TextBlock<br/><i>StatusText</i>"]
            end

            ASP["AppStateProvider<br/><i>optional delegate</i><br/>Custom state endpoint"]
        end

        subgraph MCPLib["WpfMcpInspector — Embedded MCP Server Library"]
            MCP["McpServer<br/><i>Start() / Dispose()</i><br/>HttpListener lifecycle<br/>Ports: 9222-9224"]

            subgraph Handlers["Endpoint Handlers"]
                TW["TreeWalker<br/>Visual tree traversal<br/>ConditionalWeakTable IDs<br/>Interactable filtering<br/>Flat array (max 200)"]
                CI["ComponentInspector<br/>Per-type state extraction<br/>TextBox: text, caret, selection<br/>ComboBox: items, selected<br/>DataGrid: columns, rows<br/>TreeView: nodes, selectedPath"]
                AE["ActionExecutor<br/>Action execution engine<br/>click, type, clear,<br/>select_combo, select_tab,<br/>check/uncheck, menu, focus<br/>100ms settle delay"]
                SC["ScreenshotCapture<br/>RenderTargetBitmap<br/>Window or component<br/>PNG → Base64 data URI"]
            end

            subgraph Models["Data Models"]
                CN["ComponentNode<br/>id, type, name, parent,<br/>bounds, visible, text"]
                CD["ComponentDetail<br/>+ foreground, background,<br/>tooltip, font, extra{}"]
                AR["ActionRequest<br/>action, target, text,<br/>value, index, path"]
                ARs["ActionResponse<br/>success, action, target,<br/>componentState, error"]
                SR["ScreenshotResponse<br/>image (data URI),<br/>width, height"]
                HR["HealthResponse<br/>status, app, uptime,<br/>componentCount"]
            end
        end
    end

    %% External connections
    CC -->|"HTTP :9222"| MCP
    CLI -->|"HTTP :9222"| MCP

    %% App startup
    App -->|"new McpServer(window)"| MCP
    App -->|"server.Start()"| MCP
    App --> MW
    MW --> Controls

    %% Optional state provider
    ASP -.->|"server.AppStateProvider"| MCP

    %% Endpoint routing
    MCP -->|"/tree"| TW
    MCP -->|"/component/{id}"| CI
    MCP -->|"/action"| AE
    MCP -->|"/screenshot"| SC
    MCP -->|"/health"| TW
    MCP -->|"/state"| ASP

    %% Dispatcher bridge
    MCP -.->|"Dispatcher.Invoke()"| TW
    MCP -.->|"Dispatcher.Invoke()"| CI
    MCP -.->|"Dispatcher.Invoke()"| AE
    MCP -.->|"Dispatcher.Invoke()"| SC

    %% Handler → Component access
    TW -->|"VisualTreeHelper"| Controls
    CI -->|"Inspect(element)"| Controls
    AE -->|"FindByName/Id()"| Controls
    SC -->|"Render(element)"| Controls

    %% Models usage
    TW -->|"produces"| CN
    TW -->|"produces"| HR
    CI -->|"produces"| CD
    AE -->|"consumes"| AR
    AE -->|"produces"| ARs
    SC -->|"produces"| SR

    %% Styling
    classDef external fill:#4a6fa5,stroke:#2d4a7a,color:#fff
    classDef server fill:#2d6a4f,stroke:#1b4332,color:#fff
    classDef handler fill:#40916c,stroke:#2d6a4f,color:#fff
    classDef model fill:#52796f,stroke:#354f52,color:#fff
    classDef control fill:#6d597a,stroke:#4a3a5c,color:#fff
    classDef app fill:#e07a5f,stroke:#b85842,color:#fff
    classDef provider fill:#d4a373,stroke:#b08050,color:#000

    class CC,CLI external
    class MCP server
    class TW,CI,AE,SC handler
    class CN,CD,AR,ARs,SR,HR model
    class TB,BTN,CB,CHK,LB,MN,ST control
    class App,MW app
    class ASP provider
```

## 3. Dispatcher Bridge Pattern (Thread Safety Detail)

This diagram zooms in on the critical Dispatcher bridge pattern that ensures all WPF visual tree access is thread-safe. WPF controls can only be accessed from the thread that created them (the UI thread).

```mermaid
sequenceDiagram
    participant HT as HttpListener Thread<br/>(handles incoming request)
    participant MCP as McpServer<br/>.HandleRequest()
    participant Disp as Application.Current<br/>.Dispatcher.Invoke()
    participant UI as UI Thread<br/>(WPF Dispatcher)
    participant VT as Visual Tree<br/>(FrameworkElement)

    Note over HT,VT: HTTP threads NEVER touch WPF elements directly

    HT->>MCP: GetContext() returns HttpListenerContext
    MCP->>MCP: Parse URL path, method, query, body

    MCP->>Disp: Dispatcher.Invoke(() => { ... })
    Note over MCP,Disp: HTTP thread BLOCKS here

    Disp->>UI: Marshal delegate to UI thread queue
    Note over UI: UI thread picks up work

    UI->>UI: Execute delegate
    UI->>VT: Access element properties<br/>(Name, Text, IsEnabled, Bounds...)
    VT-->>UI: Property values
    UI->>UI: Build result object

    UI-->>Disp: Delegate returns result
    Disp-->>MCP: Returns (HTTP thread unblocks)

    MCP->>MCP: JsonSerializer.Serialize(result)
    MCP-->>HT: Write HTTP response (200 OK)

    Note over HT,VT: For actions (click, type, select):

    HT->>MCP: POST /action
    MCP->>Disp: Dispatcher.Invoke(ActionExecutor.Execute)
    Disp->>UI: Marshal to UI thread
    UI->>VT: Execute action<br/>(RaiseEvent, set Text, set SelectedItem)
    UI->>UI: Thread.Sleep(100ms)<br/>Allow events to propagate
    UI->>VT: ComponentInspector.Inspect(element)<br/>Read updated state
    UI-->>Disp: ActionResponse
    Disp-->>MCP: Return
    MCP-->>HT: 200 OK + post-action state

    Note over HT,VT: Exception handling for app shutdown:

    HT->>MCP: Any request during shutdown
    MCP->>Disp: Dispatcher.Invoke(...)
    Note over Disp: App.Current is null or<br/>Dispatcher shutting down
    Disp-->>MCP: TaskCanceledException
    MCP->>MCP: Catch exception, return gracefully
    MCP-->>HT: 500 or connection closed
```

## 4. Data Flow: AI Agent Testing a Form via MCP

This diagram shows the complete data flow when Claude Code tests a WPF form through the MCP server — inspecting controls, filling fields, submitting, and verifying the result.

```mermaid
sequenceDiagram
    participant Agent as Claude Code<br/>(AI Agent)
    participant MCP as MCP Server<br/>(localhost:9222)
    participant Input as TextBox<br/>(InputBox)
    participant Combo as ComboBox<br/>(ColorCombo)
    participant Check as CheckBox<br/>(AgreeCheckbox)
    participant Btn as Button<br/>(SubmitButton)
    participant Status as TextBlock<br/>(StatusText)
    participant List as ListBox<br/>(ItemList)

    Note over Agent,List: Step 1: Discover UI structure
    Agent->>MCP: GET /tree?interactable=true
    MCP->>MCP: Dispatcher.Invoke(TreeWalker.BuildTree)
    MCP-->>Agent: components: [<br/>  {id:1, type:"TextBox", name:"InputBox"},<br/>  {id:2, type:"Button", name:"SubmitButton"},<br/>  {id:3, type:"ComboBox", name:"ColorCombo"},<br/>  {id:4, type:"CheckBox", name:"AgreeCheckbox"},<br/>  ...]

    Note over Agent,List: Step 2: Inspect initial state
    Agent->>MCP: GET /component/InputBox
    MCP->>Input: ComponentInspector.Inspect()
    Input-->>MCP: text:"", isReadOnly:false, enabled:true
    MCP-->>Agent: ComponentDetail with empty text

    Agent->>MCP: GET /component/ColorCombo
    MCP->>Combo: ComponentInspector.Inspect()
    Combo-->>MCP: items:["Red","Green","Blue"],<br/>selectedIndex:0, selectedItem:"Red"
    MCP-->>Agent: ComponentDetail with items list

    Note over Agent,List: Step 3: Fill the form
    Agent->>MCP: POST /action<br/>{"action":"type",<br/>"target":"InputBox", "text":"Test Item"}
    MCP->>Input: TextBox.Text = "Test Item"
    MCP->>Input: CaretIndex = 9
    MCP->>Input: RaiseEvent(TextChanged)
    MCP->>MCP: 100ms settle
    MCP->>Input: Inspect() → text:"Test Item"
    MCP-->>Agent: success:true, text:"Test Item"

    Agent->>MCP: POST /action<br/>{"action":"select_combo",<br/>"target":"ColorCombo", "value":"Blue"}
    MCP->>Combo: Find "Blue" (case-insensitive)
    MCP->>Combo: SelectedItem = "Blue"
    Combo->>Combo: SelectionChanged fires
    MCP->>MCP: 100ms settle
    MCP-->>Agent: success:true, selectedItem:"Blue"

    Agent->>MCP: POST /action<br/>{"action":"check", "target":"AgreeCheckbox"}
    MCP->>Check: IsChecked = true
    Check->>Check: Checked event fires
    MCP->>MCP: 100ms settle
    MCP-->>Agent: success:true, isChecked:true

    Note over Agent,List: Step 4: Take screenshot to verify form state
    Agent->>MCP: GET /screenshot
    MCP->>MCP: Dispatcher.Invoke(Capture)
    MCP->>MCP: RenderTargetBitmap → PNG → Base64
    MCP-->>Agent: data:image/png;base64,iVBOR...

    Note over Agent,List: Step 5: Submit the form
    Agent->>MCP: POST /action<br/>{"action":"click", "target":"SubmitButton"}
    MCP->>Btn: RaiseEvent(ButtonBase.ClickEvent)
    Btn->>Btn: Click handler executes
    Btn->>List: ItemList.Items.Add("Test Item")
    Btn->>Status: StatusText.Text = "Added: Test Item"
    Btn->>Input: InputBox.Text = "" (clear)
    MCP->>MCP: 100ms settle
    MCP-->>Agent: success:true

    Note over Agent,List: Step 6: Verify the result
    Agent->>MCP: GET /component/StatusText
    MCP->>Status: Inspect()
    Status-->>MCP: text:"Added: Test Item"
    MCP-->>Agent: text matches expected value ✓

    Agent->>MCP: GET /component/ItemList
    MCP->>List: Inspect()
    List-->>MCP: itemCount:1, items:["Test Item"]
    MCP-->>Agent: item appears in list ✓

    Agent->>MCP: GET /component/InputBox
    MCP->>Input: Inspect()
    Input-->>MCP: text:""
    MCP-->>Agent: input was cleared after submit ✓

    Note over Agent,List: Step 7: Navigate menu
    Agent->>MCP: POST /action<br/>{"action":"menu",<br/>"target":"_", "path":"File > New"}
    MCP->>MCP: Find Menu → match "File" → match "New"
    MCP->>MCP: RaiseEvent(MenuItem.ClickEvent)
    MCP->>MCP: 100ms settle
    MCP-->>Agent: success:true

    Agent->>MCP: GET /component/ItemList
    MCP->>List: Inspect()
    List-->>MCP: itemCount:0, items:[]
    MCP-->>Agent: list was cleared by "New" ✓
```

## 5. Stable ID Management (ConditionalWeakTable)

This diagram explains how TreeWalker maintains stable element IDs across multiple `/tree` requests without leaking memory.

```mermaid
graph TB
    subgraph Request1["GET /tree (first call)"]
        R1["TreeWalker.BuildTree()"]
        R1 --> A1["Visit TextBox 'InputBox'"]
        R1 --> A2["Visit Button 'SubmitButton'"]
        R1 --> A3["Visit ComboBox 'ColorCombo'"]
        A1 -->|"Not in WeakTable"| ID1["Assign ID = 1"]
        A2 -->|"Not in WeakTable"| ID2["Assign ID = 2"]
        A3 -->|"Not in WeakTable"| ID3["Assign ID = 3"]
    end

    subgraph WeakTable["ConditionalWeakTable<br/>(survives across requests)"]
        E1["TextBox → StableId(1)"]
        E2["Button → StableId(2)"]
        E3["ComboBox → StableId(3)"]
    end

    subgraph Request2["GET /tree (second call)"]
        R2["TreeWalker.BuildTree()"]
        R2 --> B1["Visit TextBox 'InputBox'"]
        R2 --> B2["Visit Button 'SubmitButton'"]
        R2 --> B3["Visit ComboBox 'ColorCombo'"]
        B1 -->|"Found in WeakTable"| SID1["Reuse ID = 1 ✓"]
        B2 -->|"Found in WeakTable"| SID2["Reuse ID = 2 ✓"]
        B3 -->|"Found in WeakTable"| SID3["Reuse ID = 3 ✓"]
    end

    subgraph GC["After element removed from UI + GC"]
        GC1["ComboBox garbage collected"]
        GC1 -->|"WeakTable auto-removes"| Gone["Entry (3) removed"]
        GC1 --> Note1["ID 3 is never reused<br/>(monotonic counter)"]
    end

    ID1 --> E1
    ID2 --> E2
    ID3 --> E3
    E1 --> B1
    E2 --> B2
    E3 --> B3

    classDef request fill:#4a6fa5,stroke:#2d4a7a,color:#fff
    classDef table fill:#2d6a4f,stroke:#1b4332,color:#fff
    classDef gc fill:#b56576,stroke:#8a3a4f,color:#fff
    classDef id fill:#40916c,stroke:#2d6a4f,color:#fff

    class R1,R2,A1,A2,A3,B1,B2,B3 request
    class E1,E2,E3,WeakTable table
    class GC1,Gone,Note1 gc
    class ID1,ID2,ID3,SID1,SID2,SID3 id
```

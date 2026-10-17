# Modernization backlog

Work items from the codebase assessment of October 2026. Each item can be picked up on its own.
Read the item's **Context**, do the **Steps**, and stop when the **Done when** list holds.

## Ground rules for every item

- Build: `dotnet build OfficeAssistant.slnx`. Test: `dotnet test tests\AcsFormat.Tests`
  (45 tests passed at the time of writing). Run the app: `winapp run src\OfficeAssistant`.
- The pixel-comparison tests (`BalloonRendererTests`, the character rendering in
  `AcsCharacterTests`) require **zero differing pixels**. No item may change rendered
  output unless the item says so.
- Match the existing style: file-scoped namespaces, XML doc comments on public members,
  `LibraryImport` rather than `DllImport`, nullable enabled.
- `src/AcsFormat` is platform-neutral (`net10.0`). Never add Windows dependencies to it.

## Suggested order

1. Pin package versions (small, lowers the risk of everything after it)
2. CsWin32
3. Markdig
4. Trimmed publish check
5. Default pixel size from DPI
6. Split `AssistantController`
7. Balloon text input (largest, needs a design decision first)

Items 2 and 3 touch different files and can run in parallel. Item 6 touches
`AssistantController.cs`, so don't run it alongside an item that edits that file.

---

## 1. Pin package versions

**Context:** [OfficeAssistant.csproj](../src/OfficeAssistant/OfficeAssistant.csproj)
references four packages as `Version="*"`, so any restore can pull a new major version and
break the build. The versions resolved at the time of writing were:

| Package | Resolved |
|---|---|
| `Microsoft.WindowsAppSDK` | 2.5.1 |
| `Microsoft.Windows.SDK.BuildTools` | 10.0.28000.2705 |
| `Microsoft.Windows.SDK.BuildTools.WinApp` | 0.7.1 |
| `CommunityToolkit.Mvvm` | 8.4.2 |

**Steps:**
1. Check `src/OfficeAssistant/obj/project.assets.json` for the currently resolved versions.
2. Replace each `*` with an exact version (the resolved one, or a newer stable one if you
   also verify it).
3. Optional: move all package versions into a `Directory.Packages.props` with central
   package management, so every project shares them.

**Done when:** no `Version="*"` is left, the solution builds from a clean clone, the tests
pass and the app starts.

---

## 2. Replace hand-written P/Invoke with CsWin32

**Context:** [Native.cs](../src/OfficeAssistant.Core/Interop/Native.cs) (389 lines) declares
about 50 imports, 66 constants and 8 structs by hand. `Microsoft.Windows.CsWin32` is
Microsoft's source generator. It produces the same declarations at build time from the
official Win32 metadata. Nothing extra ships with the app: the generated code is compiled
into `OfficeAssistant.Core.dll`.

Call sites that use `Native.`:
- [LayeredWindow.cs](../src/OfficeAssistant.Core/Windows/LayeredWindow.cs)
- [CharacterWindow.cs](../src/OfficeAssistant.Core/Windows/CharacterWindow.cs)
- [BalloonWindow.cs](../src/OfficeAssistant.Core/Windows/BalloonWindow.cs)
- [GdiCanvas.cs](../src/OfficeAssistant.Core/Rendering/GdiCanvas.cs)
- [BalloonRenderer.cs](../src/OfficeAssistant.Core/Rendering/BalloonRenderer.cs)
- [Clipboard.cs](../src/OfficeAssistant.Core/Interop/Clipboard.cs)
- [SoundPlayer.cs](../src/OfficeAssistant.Core/Animation/SoundPlayer.cs)

**Steps:**
1. Add `Microsoft.Windows.CsWin32` to `OfficeAssistant.Core.csproj` with an exact version
   and `PrivateAssets="all"`. CsWin32 is still versioned 0.x, so check its README for the
   current setup.
2. Create `src/OfficeAssistant.Core/NativeMethods.txt` and list every function used today
   (read them from `Native.cs`). Add a `NativeMethods.json` if needed, choosing options that
   keep the generated code trim-safe (see the CsWin32 README for marshaling settings).
3. Migrate the call sites one file at a time, building after each one:
   - `nint` handles become `HWND`, `HDC`, `HBITMAP`, `HGDIOBJ` and so on.
   - Constants become enum members (for example `WINDOW_EX_STYLE.WS_EX_LAYERED`) or
     `PInvoke.*` constants.
   - Where it simplifies the `try/finally` cleanup in `LayeredWindow.Present` and
     `GdiCanvas`, use the generated SafeHandle overloads. Keep explicit handles where the
     handle must outlive a scope (the fonts in `BalloonRenderer`, the canvas DIB).
   - Keep the wndproc delegate rooted, as `LayeredWindow.WndProcDelegate` does today. The
     generated `WNDPROC` delegate type must stay alive for the process lifetime.
4. Delete whatever is left of `Native.cs`. Keep small helpers such as `LowWord` and
   `MakeIntResource` in a tiny `NativeHelpers` class if they are still needed.

**Watch out for:**
- `TRACKMOUSEEVENT.CbSize` is computed by hand today (`16 + nint.Size`). Use
  `sizeof(TRACKMOUSEEVENT)` or `Marshal.SizeOf` instead.
- `CreateDIBSection` returns a pixel pointer that `GdiCanvas.Pixels` wraps. Keep it
  `unsafe`, because `AllowUnsafeBlocks` is already on.

**Done when:** `Native.cs` is gone (or reduced to helpers), the build has no new warnings,
all tests pass with zero pixel differences, and in the running app you have checked:
- dragging, clicking, double-clicking and right-clicking the character
- typing, selecting, copying and pasting in the balloon
- sounds
- the main window's gallery preview

---

## 3. Parse Claude's Markdown with Markdig

**Context:** `AnswerFormatter.ToPlainText` in
[AnswerFormatter.cs](../src/OfficeAssistant.Core/Chat/AnswerFormatter.cs) strips Markdown
with regexes, line by line, before the balloon draws the answer as plain text. It can trip
on things a parser handles:
- nested or numbered lists
- `*` or `_` inside inline code
- escaped characters (`\*`)
- links containing brackets
- lists inside block quotes
- inline HTML
- setext headings (underlined with `===`)

Markdig is a mature, CommonMark-compliant parser (BSD-2 license).

**Behaviour to keep** (covered by
[ChatTests.cs](../tests/AcsFormat.Tests/ChatTests.cs)):
- Headings, emphasis, quotes and inline-code markers are removed, and the text is kept.
- Bullets become `• ` with their indentation kept.
- `[text](url)` becomes `text (url)`, or just `text` when the text equals the URL. Images
  keep their alt text.
- Fenced code keeps its lines but drops the fences.
- Horizontal rules and table separator rows are removed. Table rows become their cells
  joined by two spaces.
- Runs of blank lines collapse to one blank line, and the result is trimmed.

`Markdown.ToPlainText` alone does **not** produce the bullets or the link form. Write a
small walk over the `MarkdownDocument` syntax tree (or a custom `TextRendererBase`).

**Steps:**
1. Add `Markdig` (exact version) to `OfficeAssistant.Core.csproj`.
2. Replace the body of `ToPlainText` with a parse (`Markdown.Parse` with a pipeline that
   enables pipe tables) and a tree walk that emits the format above. Delete the
   `[GeneratedRegex]` members that are no longer used. `Paginate` stays as it is.
3. Add tests for the cases listed under **Context** that the regexes got wrong. Ordered
   lists should keep their numbers (`1.`, `2.`).

**Done when:** the existing `ChatTests` pass (update an expectation only where the old
output was clearly wrong, and say so in the commit message), the new edge-case tests pass,
and a trimmed Release publish (item 4) still works with Markdig in it.

---

## 4. Verify the trimmed Release publish

**Context:** the app project sets `PublishTrimmed=True` and `PublishReadyToRun=True` for
non-Debug builds. Settings serialization is already trim-safe: it uses a source-generated
`SettingsJsonContext`. The `Anthropic` and `ModelContextProtocol` SDKs and WinUI itself
might use reflection, and nobody has tested this yet.

**Steps:**
1. Publish Release for x64 (see the `winui:winui-packaging` skill, or
   `dotnet publish src\OfficeAssistant -c Release -r win-x64`).
2. Collect the trim warnings (`IL2xxx`/`IL3xxx`).
3. Run the published app and test:
   - asking a question
   - a tool call through an MCP server (for example `@modelcontextprotocol/server-filesystem`)
   - saving settings
   - switching assistants
4. Fix the failures you find with trimmer root descriptors or `DynamicDependency`, or turn
   trimming off for the affected assemblies. If trimming can't be made reliable, set
   `PublishTrimmed` to false and write down why.

**Done when:** the Release package runs every flow in step 3, and any trim warnings left
are either fixed or listed with a reason in the project file.

---

## 5. Default the pixel size from the monitor DPI

**Context:** `AssistantController.Scale`
([AssistantController.cs](../src/OfficeAssistant.Core/AssistantController.cs)) is an
integer from 1 to 4, set from Options. It defaults to 1, so on a 200% display the character
looks tiny until the user changes it. The process is per-monitor DPI aware (V2) through
[app.manifest](../src/OfficeAssistant/app.manifest).

**Steps:**
1. When no pixel size has been saved, use `max(1, round(dpi / 96))`. Get the DPI with
   `GetDpiForWindow` on the character window, or `GetDpiForMonitor` for the target point.
2. Keep integer scaling, which keeps the art sharp. An explicit user choice always wins.
3. Optional: when the character is dragged to a monitor with a different DPI and the user
   has not picked a size, update the scale. Watch for `WM_DPICHANGED`, or compare the
   monitor DPI in `CharacterWindow.Moved`.

**Done when:** on a 100% display nothing changes, on a 200% display a fresh install shows
the character at 2x, and a saved choice is kept.

---

## 6. Split `AssistantController`

**Context:** [AssistantController.cs](../src/OfficeAssistant.Core/AssistantController.cs)
(712 lines) holds:
- character loading and the windows
- animation, idling and gestures
- the context menu
- balloon content and paging
- the Claude/MCP conversation: client creation, the system prompt, tool approval and
  cancellation

**Steps:**
1. Keep `AssistantController` as the public facade the app uses (`App`,
   `MainPageViewModel`). Its public API should not change.
2. Extract a `CharacterPresenter` (or similar) for load, show/hide, play, idle and present.
   Extract a `ConversationController` for the client, the chat, the request, approval,
   pages and the system prompt. Keep the balloon wiring in the facade or a small
   `BalloonCoordinator`.
3. Move the code without changing behaviour. Where it's cheap, add tests for the extracted
   pieces (for example paging and the system prompt).

**Done when:** no class is above roughly 300 lines, the public API is unchanged, all tests
pass, and a manual run of ask, follow-up, cancel, tool approval, new topic, hide/show and
switching assistants behaves as before.

---

## 7. Real text input in the balloon (IME and accessibility)

**Context:** [BalloonWindow.cs](../src/OfficeAssistant.Core/Windows/BalloonWindow.cs) and
[TextEditing.cs](../src/OfficeAssistant.Core/Windows/TextEditing.cs) implement the Answer
Wizard text box by hand: caret, selection, word selection, clipboard and keyboard. It has:
- no IME composition (Chinese, Japanese and Korean input don't work)
- no UI Automation (screen readers can't read or edit it)
- no emoji panel (Win+.)
- no spell check

A child control can't simply be dropped in, because `UpdateLayeredWindow` windows don't
paint child windows.

**Options (decide with the user before implementing):**
- **A. Hybrid (recommended).** Keep the GDI-drawn balloon. While the box is focused, place
  a small borderless owned window exactly over the box rectangle (`TextBoxGeometry` in
  `BalloonRenderer`) and host a XAML `TextBox` in it. Use `DesktopWindowXamlSource`, or
  `XamlIsland` with a `DesktopChildSiteBridge`. Style it to match the Office XP box: Tahoma
  13 px, a white background, the `#ECE9D8` border. When it loses focus, copy the text back
  and let GDI paint it again. You get IME, UIA and spell check, and only the box itself
  stops being pixel-exact while it's being edited.
- **B. Implement the platform interfaces in the custom box.** Add IMM32 composition
  (`WM_IME_*`, `ImmSetCompositionWindow`) and a UIA provider (`WM_GETOBJECT` with
  `IRawElementProviderSimple` and a text pattern). The output stays pixel-exact, but this
  adds code instead of removing it.
- **C. Make the whole balloon XAML.** This is the most code removed and the most fidelity
  lost. It breaks `BalloonRendererTests`.

**Done when** (option A): Japanese IME input works, Narrator reads and edits the box, the
emoji panel inserts characters, Enter and Escape still ask and dismiss, the pixel tests for
the unfocused balloon still pass, and `TextEditing.cs` plus the keyboard and selection code
in `BalloonWindow` are deleted.

---

## Small items

- **AcsTool PNG export:** replace [PngWriter.cs](../tools/AcsTool/PngWriter.cs) with
  `System.Drawing.Common` (the tests already use it). This only affects the developer tool.
- **Shared sound channel:** `PlaySound` has one channel per process. The gallery preview
  ([MainPage.xaml.cs](../src/OfficeAssistant/MainPage.xaml.cs)) and the floating assistant
  both use [SoundPlayer.cs](../src/OfficeAssistant.Core/Animation/SoundPlayer.cs), so each
  cuts the other off. If that matters, switch to NAudio or XAudio2 for mixing.
- **Per-frame allocations:** `LayeredWindow.Present` creates a DC and a DIB section on every
  frame, and `MainPage.RedrawPreview` allocates a new `byte[]` per frame. Reuse them when
  the size hasn't changed.
- **README accuracy:** the README says only `CLIPPIT.ACS` is included, but `Assistants/`
  tracks the full `.ACS` file for every assistant. Update the "Assistant files" section.

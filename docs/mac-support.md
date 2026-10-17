# Design: macOS support

Status: **proposal, not started.** It describes how to split the Windows-only code out of
`OfficeAssistant.Core` so the assistant can run on macOS, which seams to introduce, how the
Mac side is built, and in which order to do it. Phases 1–3 are pure refactoring on Windows
and are worth merging on their own even if the Mac app never ships.

## Where the Windows coupling is today

`src/AcsFormat` is already platform-neutral (`net10.0`). Everything else is
`net10.0-windows`. The Windows-only code is concentrated in a few files:

| Area | Files | Windows API |
|---|---|---|
| Floating windows (character, balloon) | [LayeredWindow.cs](../src/OfficeAssistant.Core/Windows/LayeredWindow.cs), [CharacterWindow.cs](../src/OfficeAssistant.Core/Windows/CharacterWindow.cs), [BalloonWindow.cs](../src/OfficeAssistant.Core/Windows/BalloonWindow.cs) | `CreateWindowEx`, `UpdateLayeredWindow`, wndproc messages, capture, `TrackPopupMenuEx`, cursors, `SetTimer` (caret) |
| Balloon text | [BalloonRenderer.cs](../src/OfficeAssistant.Core/Rendering/BalloonRenderer.cs), [GdiCanvas.cs](../src/OfficeAssistant.Core/Rendering/GdiCanvas.cs) | GDI: Tahoma 8 pt `NONANTIALIASED_QUALITY`, `TextOut`, `DrawText`, `GetTextExtentPoint32`, DIB sections |
| Sound | [SoundPlayer.cs](../src/OfficeAssistant.Core/Animation/SoundPlayer.cs) | `waveOut*` |
| Clipboard, keyboard state | [Clipboard.cs](../src/OfficeAssistant.Core/Interop/Clipboard.cs), [NativeHelpers.cs](../src/OfficeAssistant.Core/Interop/NativeHelpers.cs) | `OpenClipboard`…, `GetKeyState`, `VIRTUAL_KEY` |
| Monitors and DPI | `LayeredWindow.GetWorkArea` / `GetDpiAt`, used by [CharacterPresenter.cs](../src/OfficeAssistant.Core/CharacterPresenter.cs) | `MonitorFromPoint`, `GetMonitorInfo`, `GetDpiForMonitor` |
| API key storage | [AppSettings.cs](../src/OfficeAssistant.Core/Chat/AppSettings.cs) | DPAPI (`ProtectedData`) |
| App shell | `src/OfficeAssistant` (WinUI 3, MSIX) | WinUI, `Windows.Storage` (`LocalSettings`, `FileOpenPicker`), `InfoBarSeverity` in the view model |
| Tests and tool | [TestFiles.cs](../tests/AcsFormat.Tests/TestFiles.cs), [AcsTool](../tools/AcsTool/Program.cs) | `System.Drawing.Common` (Windows-only since .NET 7) |

Already portable: `AnimationPlayer`, `AcsFrameRenderer`, `UiTimer` (uses
`SynchronizationContext`, which AppKit's main thread also provides), `TextEditing`,
`BalloonState`, the balloon layout maths, all of `Chat/` except the DPAPI call, and
`AssistantController` apart from its use of the concrete window types.

The real problem is not the P/Invoke itself but that **behaviour lives inside the
wndprocs**: drag detection, click vs. double-click, balloon hit-testing, text-box editing,
key bindings and caret blinking are all written against `WM_*` messages. That logic has to
move into platform-neutral classes, leaving the platform layer as a thin "surface".

## Target structure

```
src/AcsFormat                       net10.0            unchanged
src/OfficeAssistant.Core            net10.0            all behaviour; platform interfaces
src/OfficeAssistant.Presentation    net10.0            MainPageViewModel and friends (CommunityToolkit.Mvvm)
src/OfficeAssistant.Platform.Windows net10.0-windows   CsWin32, layered windows, GDI text, waveOut, DPAPI
src/OfficeAssistant                 net10.0-windows10… WinUI shell (unchanged UX)
src/OfficeAssistant.Mac             net10.0-macos      AppKit shell + Mac platform layer
tests/AcsFormat.Tests               net10.0            portable tests (run on both OSes)
tests/OfficeAssistant.Windows.Tests net10.0-windows    GDI-vs-bitmap-font equality, Win32 smoke tests
```

The Mac platform layer can live inside `OfficeAssistant.Mac` to begin with; split it out
only if a second Mac consumer appears. Add solution filters (`Windows.slnf`, `Mac.slnf`),
because the `macos` workload only builds on a Mac and WinUI only builds on Windows.

## The seams

All coordinates in Core stay as today: top-left origin, integer "device pixels". Each
platform defines what a device pixel is (Windows: physical pixel; Mac: point, see "DPI and
Retina").

```csharp
// Core/Platform/IPlatform.cs (one instance, created by the shell and passed in)
public interface IPlatform
{
    IFloatingSurface CreateSurface(SurfaceKind kind);   // Character or Balloon
    IAudioOutput CreateAudioOutput();                   // one per SoundPlayer, so players mix
    IClipboard Clipboard { get; }
    ISecretStore Secrets { get; }
    IDisplays Displays { get; }                         // WorkAreaAt(x, y), DefaultScaleAt(x, y)
    ITextRenderer CreateTextRenderer(string face, int pixelHeight);
    int CaretBlinkMilliseconds { get; }
}

public interface IFloatingSurface : IDisposable
{
    void Present(ReadOnlySpan<uint> pixels, int width, int height, int x, int y, int scale);
    void Show(bool activate);
    void Hide();
    void MoveTo(int x, int y);
    void SetCursor(CursorKind cursor);
    void CapturePointer(); void ReleasePointer();
    int ShowMenu(int x, int y, IReadOnlyList<MenuItem> items);   // character only
    (int X, int Y) CursorPosition { get; }

    event EventHandler<PointerEvent> PointerPressed, PointerMoved, PointerReleased;
    event EventHandler<PointerEvent> SecondaryClick, DoubleClick;
    event EventHandler PointerLeft, CaptureLost;
    event EventHandler<KeyEvent> KeyDown;          // neutral Key enum + Modifiers
    event EventHandler<string> TextInput;          // committed text (WM_CHAR / insertText:)
    event EventHandler<bool> ActivationChanged;
}

public interface ITextRenderer : IDisposable
{
    int Measure(string text, bool bold);
    void Draw(PixelBuffer target, string text, int x, int y, uint color, bool bold);
}
```

`KeyEvent` carries a neutral `Key` (Left, Right, Home, End, Delete, Back, Escape, Enter,
Tab, A, C, X, V…) and `Modifiers` (Shift, Command, Word). The platform maps its own
conventions into these: on Windows Ctrl → Command and Ctrl+Arrow → Word; on Mac ⌘ →
Command, ⌥+Arrow → Word, ⌘+Left/Right → Home/End. Core's key handling then has one code
path.

With those seams:

- `CharacterWindow` becomes `CharacterInteraction` in Core: it owns an `IFloatingSurface`
  and implements the drag threshold, click vs. drag, double-click and the menu request.
- `BalloonWindow` becomes `BalloonInteraction` in Core: hit-testing, hover cursor, text-box
  focus, selection by mouse, `OnKeyDown`/`OnChar`, clipboard, and caret blink through a
  `UiTimer` instead of `SetTimer`.
- `LayeredWindow` becomes `Win32Surface` in Platform.Windows. Its wndproc just translates
  messages into the events above (it keeps `MA_NOACTIVATE`, capture and `TrackMouseEvent`).
- `BalloonRenderer` renders into a plain `PixelBuffer` (`uint[]` + width/height) and calls
  `ITextRenderer`. `GdiCanvas` is only used inside the Windows text renderer.
- `SoundPlayer` keeps the clip parsing (`WaveFile`) and the "one output per player" rule,
  and delegates playback to `IAudioOutput.Play(WaveFormat, ReadOnlyMemory<byte>)`.
- `AppSettings` takes an `ISecretStore` for the API key. The settings file path stays
  `LocalApplicationData/OfficeAssistant/settings.json` (on macOS .NET maps that to
  `~/Library/Application Support`). `PreviewMuted` moves from `Windows.Storage` into
  `AppSettings` so the view model has no WinRT dependency.

## Pixel-exact text without GDI

The balloon pixel tests need zero differing pixels, and GDI's bi-level hinted Tahoma
cannot be reproduced by CoreText. The plan is a **pre-rendered bitmap font**, the same way
the balloon chrome already ships as [BalloonSprites.g.cs](../src/OfficeAssistant.Core/Rendering/BalloonSprites.g.cs):

1. Add `AcsTool bake-font` (Windows only): renders each glyph of Tahoma −13 regular and
   bold with GDI, non-antialiased, and records its 1-bit mask, offset and advance width.
   Cover Latin-1, Latin Extended-A/B, Greek, Cyrillic, general punctuation and the
   characters used by the localized strings in `Assistants/*.acs`.
2. Commit the result as `Rendering/BalloonFont.g.cs`, plus a `BitmapFontRenderer :
   ITextRenderer`. `GetTextExtentPoint32` does not kern and the balloon draws with
   `TextOut`, so the sum of advances reproduces GDI layout exactly.
3. `DrawText(DT_CENTER)` and the `&` mnemonic underline in `PaintButton` get re-implemented
   in the renderer on top of `Measure`/`Draw` (centre maths and a 1 px underline).
4. A Windows-only test renders every baked glyph and a set of strings with both renderers
   and requires identical pixels. With that in place the existing `BalloonRendererTests`
   run on macOS with the bitmap font and still require zero differences.
5. Fallback for characters outside the baked set (CJK, emoji, rare symbols in Claude's
   answers): the platform renderer. Windows keeps GDI (with font linking); the Mac uses
   CoreText with antialiasing off, at 11 px. Those glyphs won't match Windows exactly, which
   is acceptable because Windows doesn't render them through Tahoma either.

Windows could switch to the bitmap font too, but keeping GDI there for now avoids any risk
to today's output.

## The Mac side

Use the .NET `macos` workload (`net10.0-macos`, the Microsoft.macOS AppKit bindings). It
gives subclassable `NSPanel`/`NSView`, which raw `objc_msgSend` interop makes painful, and
it's the supported path for signing, notarization and trimming.

### Floating surfaces (`MacSurface : IFloatingSurface`)

- **Window:** an `NSPanel` subclass, `styleMask = Borderless | NonactivatingPanel`,
  `isOpaque = false`, `backgroundColor = clear`, `hasShadow = false`,
  `level = .floating`, `collectionBehavior = canJoinAllSpaces | fullScreenAuxiliary |
  ignoresCycle`, `hidesOnDeactivate = false`. Override `canBecomeKeyWindow` to return
  `true` only for the balloon (so it can take typing without activating the app) and
  `false` for the character.
- **Pixels:** a layer-backed view whose `CALayer.contents` is a `CGImage` built from the
  frame (premultiplied BGRA, `kCGImageAlphaPremultipliedFirst | ByteOrder32Little`), with
  `magnificationFilter = kCAFilterNearest` and `contentsScale = 1`. The integer pixel size
  becomes the layer size, so no CPU `ScaleNearest` is needed. Wrap the frame update in a
  `CATransaction` with actions disabled so nothing animates between frames.
- **Click-through:** AppKit passes clicks through fully transparent pixels of a
  non-opaque borderless window. Verify this in the spike; the fallback is overriding
  `hitTest:` against the current frame's alpha mask.
- **Input:** `mouseDown/Dragged/Up` (with `clickCount` for double-click),
  `rightMouseDown` and Ctrl-click for the menu, `NSTrackingArea` for hover and leave,
  `resetCursorRects`/`NSCursor` (`arrow`, `pointingHand`, `IBeam`). Implement
  `NSTextInputClient` on the balloon view and route `keyDown:` through
  `interpretKeyEvents:`, so dead keys, ⌥-combinations and IMEs produce `TextInput`. Map
  `NSStandardKeyBindingResponding` selectors (`moveWordLeft:`, `deleteBackward:`,
  `selectAll:`…) to `KeyEvent`s.
- **Menu:** `NSMenu.PopUpMenu(positioningItem: null, at:, in:)`, run synchronously, the
  same contract as `TrackPopupMenuEx` returning the chosen id.

### DPI and Retina

On the Mac a device pixel is a **point**. `IDisplays.DefaultScaleAt` returns 1, and the
user's "pixel size" option multiplies from there. At pixel size 1 on a Retina screen each
art pixel becomes 2×2 physical pixels, which is crisp with the nearest filter and gives
the familiar on-screen size. `IDisplays.WorkAreaAt` uses `NSScreen.visibleFrame` (it
excludes the menu bar and Dock) and flips Y: `top = primaryScreen.frame.maxY -
frame.maxY`. Positions are rounded to whole points (whole pixels on Retina too).

### Other services

| Service | Mac implementation |
|---|---|
| `IAudioOutput` | `AVAudioPlayer(data:)` built from a RIFF/WAV header + the clip bytes; one instance per player, so they mix. `Stop()` → `stop()`. |
| `IClipboard` | `NSPasteboard.General`, `NSPasteboard.NSPasteboardTypeString`. |
| `ISecretStore` | Keychain generic password (`SecKeyChain`), service `OfficeAssistant`, account `anthropic-api-key`. Migrate nothing: Windows DPAPI blobs can't be read on a Mac anyway. |
| Caret blink | 530 ms (macOS has no public per-user setting worth reading). |
| File picker (custom `.acs`) | `NSOpenPanel` with `allowedContentTypes` for `.acs`. |

### MCP servers launched from a GUI app

Apps started from Finder get `PATH=/usr/bin:/bin:/usr/sbin:/sbin`, so `npx`/`uvx` installed
with Homebrew, nvm or uv are not found. Before starting stdio servers, resolve the user's
login-shell environment once (`$SHELL -l -c 'printf %s "$PATH"'`, with a short timeout) and
pass it in `StdioClientTransportOptions.EnvironmentVariables`. Do this in Core behind an
`IProcessEnvironment` so Windows keeps its current behaviour.

### App shell

The WinUI main window (gallery, options, Claude card, instructions and tools, animations)
needs a Mac equivalent.

**Decided: AppKit, built in code.** A menu-bar `NSStatusItem` (Show/Hide, Choose
Assistant, Options, Quit) for day-to-day use, plus a settings window with an `NSTabView`
mirroring the five cards, bound to the shared `MainPageViewModel` through a small
`INotifyPropertyChanged` → control helper. It looks native, needs no extra runtime, and
the process already owns AppKit for the panels. Windows keeps its WinUI shell.

Rejected alternatives, for the record:
- **Uno Platform**, reusing most of `MainPage.xaml`: less UI duplication, but a large
  dependency, and hosting AppKit panels next to Uno's Skia windows would need its own spike.
- **Avalonia** on both platforms: one UI codebase, but it rewrites the working Windows
  shell, and non-activating panels still need native code.

The gallery preview uses the same `MacSurface` pixel path inside a view (an `NSView` with
the nearest-filtered layer), not a separate renderer.

Set `LSUIElement` to `false` (the app has a real window and a Dock icon) and
`NSHighResolutionCapable` to `true`. Ship the `.acs`/`.acg` files in
`Contents/Resources/Assistants`; resolve them through a small `IAssetLocator` instead of
`AppContext.BaseDirectory`.

## Tests and tools

- Replace `System.Drawing` in [TestFiles.cs](../tests/AcsFormat.Tests/TestFiles.cs) and
  `AcsTool` with a portable PNG reader/writer (`StbImageSharp` / `StbImageWriteSharp`,
  public domain, no native code). The reference screenshots stay as they are.
- `AnimationPlayerTests` already install their own `SynchronizationContext`; they move
  over unchanged.
- Add fakes for `IFloatingSurface` and `IClipboard`, so `CharacterInteraction` and
  `BalloonInteraction` (drag threshold, double-click, key bindings, paste, caret blink) get
  unit tests that run on both OSes. `TextBoxEditingTests` should move onto
  `BalloonInteraction` this way.
- CI: a GitHub Actions matrix with `windows-latest` (full solution, all tests) and
  `macos-latest` (`Mac.slnf`, portable tests, `dotnet build` of the Mac app).

## Phased plan

0. **Spike (1–2 days, throwaway branch, on a Mac).** A `net10.0-macos` app with two
   `NSPanel`s showing Clippit's `RestPose` (from `AcsFrameRenderer`) and a static balloon
   bitmap. Answer:
   1. Transparent over other apps, and clicks on transparent pixels reach the window
      underneath?
   2. Clicking the character does not activate the app or steal focus from the frontmost
      app; the balloon can become key and receive `keyDown:` without activating it?
   3. Nearest-filtered layer at pixel size 1 and 2 on Retina and non-Retina screens looks
      identical to the art (screenshot and compare)?
   4. Panels stay visible across Spaces and over full-screen apps?
   Report before continuing.
1. **Portable PNG in tests and AcsTool; split tests into portable and Windows-only.** No
   behaviour change.
2. **Introduce the interfaces and move behaviour out of the wndprocs** (`IFloatingSurface`,
   `CharacterInteraction`, `BalloonInteraction`, `PixelBuffer`, `ITextRenderer` with a GDI
   implementation, `IAudioOutput`, `IClipboard`, `ISecretStore`, `IDisplays`). Windows
   implementations wrap today's code. All pixel tests still pass with zero differences;
   manually check drag, click, double-click, right-click menu, typing/selection/copy/paste
   in the balloon, sounds and the gallery preview. Can be split into one PR per seam.
3. **Split the projects**: move Win32 code to `OfficeAssistant.Platform.Windows`, retarget
   Core to `net10.0`, move the view model to `OfficeAssistant.Presentation` (replace
   `InfoBarSeverity` with a neutral enum mapped in the WinUI view, move `PreviewMuted` into
   `AppSettings`, put the file picker behind an interface). Core must build with no
   `Windows.*` usings.
4. **Bitmap font**: `AcsTool bake-font`, `BalloonFont.g.cs`, `BitmapFontRenderer`, and the
   GDI-equality test on Windows. `BalloonRendererTests` pass on macOS.
5. **Mac platform layer**: `MacSurface`, audio, clipboard, Keychain, displays, CoreText
   fallback text, login-shell `PATH` for MCP.
6. **Mac shell, first cut**: status-bar item + floating assistant + balloon chat. This is
   the first usable Mac build.
7. **Mac settings window** (AppKit `NSTabView`) with gallery preview, options, Claude
   key/model, instructions and MCP servers, animations.
8. **Packaging**: universal `osx-arm64` + `osx-x64` app, Developer ID signing, hardened
   runtime, notarization, a DMG, and README instructions for building on macOS.

Phases 1–4 are Windows-side refactoring. Phase 0 can run in parallel with them.

## Risks and open questions

- **Non-activating focus on macOS.** Typing into a panel of an inactive app works with
  `NonactivatingPanel` + `canBecomeKeyWindow`, but IME candidate windows and the
  accessibility of the custom text box need checking. If it fights back, a hidden
  `NSTextField` owning first responder (mirroring the balloon) is the fallback.
- **Glyph coverage.** Any localized `.acs` strings outside the baked set fall back to
  CoreText and won't be pixel-identical. The bake step should report which characters the
  shipped assistants use so the set is chosen from data.
- **Tahoma licensing.** The baked bitmaps are derived from Tahoma. macOS ships Tahoma too,
  but check whether committing rendered glyph bitmaps is acceptable; the alternative is
  baking at build time on the Mac from the system's Tahoma with FreeType's TrueType
  interpreter in mono mode, which is close to (but not guaranteed identical to) GDI.
- **Behaviour drift during phase 2.** Moving wndproc logic is where regressions hide (the
  capture and double-click rules in particular). The new interaction tests should be
  written against today's behaviour *before* the move.
- **Effort.** Rough guess: phases 1–4 about 1.5–2 weeks, phases 5–6 about 1.5 weeks,
  phase 7 about 1.5 weeks, phase 8 a few days plus Apple
  developer account setup.

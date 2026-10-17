# Design: rendering the assistant with Windows App SDK Composition

Status: **proposal, not started.** It describes how the floating character could be drawn
with `Microsoft.UI.Composition` instead of `UpdateLayeredWindow`, what that changes in the
code and assets, and what needs a prototype first. An agent picking this up should start
with the spike in the "Phased plan" section and report its result before building more.

## Why (and why not)

The assessment concluded that this does **not** make the code smaller. Today's character
pipeline is about 420 lines, and it gets per-pixel alpha, click-through on transparent
pixels and no-activate behaviour for free from a layered window. Do this only for something
the layered window can't do:

- GPU composition, with no per-frame CPU compositing or DIB allocation
- non-integer or animated scaling, effects (shadow, fade), and smooth show/hide transitions
- one shared renderer for the floating character and the gallery preview in
  [MainPage.xaml.cs](../src/OfficeAssistant/MainPage.xaml.cs), which today copies pixels
  into a `WriteableBitmap` byte by byte on every frame

The balloon is **out of scope**. It is already drawn with XAML in its own window
([BalloonWindow.xaml](../src/OfficeAssistant/Balloon/BalloonWindow.xaml)).

## Current pipeline (what gets replaced)

```
AcsCharacter (palettized images, frame graph)
  -> AnimationPlayer.Step()             timer per frame, branching, sounds
  -> AcsFrameRenderer.Render(frame)     CPU: paints the frame's images back to front into uint[]
  -> AssistantController.Present()
  -> LayeredWindow.Present()            new DC + DIB, nearest-neighbour scale, UpdateLayeredWindow
```

Files involved:
- [AnimationPlayer.cs](../src/OfficeAssistant.Core/Animation/AnimationPlayer.cs)
- [AcsFrameRenderer.cs](../src/AcsFormat/AcsFrameRenderer.cs)
- [AssistantController.cs](../src/OfficeAssistant.Core/AssistantController.cs) (`Present`,
  `Scale`, `FrameBounds`)
- [LayeredWindow.cs](../src/OfficeAssistant.Core/Windows/LayeredWindow.cs)
- [CharacterWindow.cs](../src/OfficeAssistant.Core/Windows/CharacterWindow.cs)

## Target pipeline

```
AcsCharacter
  -> CharacterAtlas (once per character)   every AcsImage decoded to premultiplied BGRA,
                                           packed into one atlas surface
  -> AnimationPlayer.Step()                unchanged timing/branching/sounds
  -> IFramePresenter.Show(AcsFrame)        no pixel work: updates a small visual tree
       CompositionFramePresenter           floating character (own HWND + ContentIsland)
       XamlFramePresenter                  gallery preview (ElementCompositionPreview)
```

### Visual tree per character

- **Root `ContainerVisual`.** Its `Scale` is the pixel size (an integer by default) and its
  `Size` is `Character.Width × Character.Height`.
- **A pool of `SpriteVisual`s,** one per image slot in a frame. Size the pool to the
  largest `frame.Images.Count` in the character.
  - Each sprite gets the `Offset` from `AcsFrameImage.X/Y` and the `Size` of that image.
  - Each sprite has its own `CompositionSurfaceBrush` on the shared atlas surface, with:
    - `Stretch = None`
    - an `Offset` (or `TransformMatrix`) set to minus the image's atlas position, so only
      its sub-rectangle shows. Set `Clip`/`Size` on the sprite so neighbouring atlas cells
      don't bleed in.
    - `BitmapInterpolationMode = NearestNeighbor`, so scaling never filters the art.
- **Z-order:** the first image in a frame is the top-most (see `AcsFrameRenderer`), so add
  the sprites to `Children` in reverse order, or reorder them on each frame.
- **Showing a frame:** for each slot, set the brush's atlas offset, the sprite offset and
  size, and `IsVisible`. Hide the unused slots. This is a handful of property sets, with
  no allocation and no pixel copying.

Batch the property sets for a frame (for example with `Compositor.GetCommitBatch`, or
simply set them all before returning to the message loop) so a frame never shows half
updated.

### Building the atlas surface

The images are 8-bit palettized with a transparent index (`AcsCharacter.TransparentIndex`,
`Palette`). Convert each one once to premultiplied BGRA, treating the transparent index as
0x00000000, and pack them into one or a few atlas textures. A simple shelf packer is
enough: about 900–1,000 images per character, each at most 124×93.

Options for getting the pixels onto a surface, roughly simplest first:

1. **`LoadedImageSurface`** (`Microsoft.UI.Xaml.Media`) from an in-memory PNG of the atlas.
   It handles device loss itself, needs no DirectX code, and works with the XAML thread's
   compositor. It means encoding a PNG once per character load (use
   `Windows.Graphics.Imaging.BitmapEncoder`), or shipping a pre-built atlas (see "Asset
   pre-processing").
2. **Win2D** (`Microsoft.Graphics.Win2D`): `CanvasComposition.CreateCompositionGraphicsDevice`,
   then `CanvasBitmap.CreateFromBytes` drawn into a `CompositionDrawingSurface`. It takes
   raw pixels and needs no encoding, but you must handle `RenderingDeviceReplaced` and
   redraw the atlas when it fires.
3. **Raw interop** (`ICompositorInterop` and `ICompositionDrawingSurfaceInterop` with a
   D3D11 device). Avoid this: it's the most code, for no benefit at this size.

### Hosting the floating character

The character must be a borderless, top-most, non-activating popup that is transparent
outside the character's pixels. There are two ways to attach a compositor visual tree to
your own HWND.

**Option B1: Windows App SDK `ContentIsland` (preferred, but it needs the spike).**
1. Create the popup with `WS_POPUP` and
   `WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP`
   (not `WS_EX_LAYERED`).
2. Create the island with `ContentIsland.Create(rootVisual)`, using the UI thread's
   compositor (`App.Window.Compositor`, or `new Compositor()` on the UI thread).
3. Connect it with `DesktopChildSiteBridge.Create(compositor, windowId)` and
   `bridge.Connect(island)`. Keep the bridge's child HWND sized to the popup.
   `DesktopAttachedSiteBridge.CreateFromWindowId` (Windows App SDK 1.7+) attaches to the
   popup itself, without a child window, and may be a better fit. Try it first.
4. **Unknown:** whether the bridge composes with a transparent background over the desktop
   when it is hosted in a top-level `WS_EX_NOREDIRECTIONBITMAP` popup. Settle this in the
   spike.

**Option B2: system `Windows.UI.Composition` (fallback, known to work).**
1. Call `Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().EnsureSystemDispatcherQueue()`
   on the UI thread, then create a `Windows.UI.Composition.Compositor`.
2. Get `ICompositorDesktopInterop` from it and call `CreateDesktopWindowTarget(hwnd,
   isTopmost: true)` for the same `WS_EX_NOREDIRECTIONBITMAP` popup. Declare the COM
   interface with `[GeneratedComInterface]`, taking the IID from
   `windows.ui.composition.interop.h`.
3. This is the documented way to get per-pixel transparency for a Win32 window. The
   drawbacks: it's a second compositor, separate from the WinUI one, so `LoadedImageSurface`
   and Win2D-for-WinUI surfaces can't be shared with it, and the atlas must be built for
   that compositor (Win2D's UWP flavour, or raw interop).
4. `OfficeAssistant.Core` currently targets `net10.0-windows`, which has no WinRT
   projection. Either target `net10.0-windows10.0.26100.0` (or the minimum you need), or
   put the composition presenter in the app project.

### Input and click-through (the main cost)

A layered window ignores clicks on fully transparent pixels by itself. A
`WS_EX_NOREDIRECTIONBITMAP` window does not: it hit-tests its whole rectangle. And
`HTTRANSPARENT` from `WM_NCHITTEST` only forwards the click to windows on the **same
thread**, so it does not let clicks through to other apps. Options:

1. **A window region per frame (recommended).** Build an `HRGN` from each frame's opaque
   pixels, scaled by the pixel size, and apply it with `SetWindowRgn` when the frame
   changes. Cache the regions per frame and pixel size. The `.acs` format stores region
   data for every image after the pixel data. `AcsImage.Decode` skips it today, but
   parsing it would avoid building the regions from pixels. Without the region data, build
   one `RECT` per run of opaque pixels on each row and call `ExtCreateRegion`. Note that a
   window region also clips the rendering, which is fine because the pixels outside it are
   transparent anyway.
2. **Toggle `WS_EX_TRANSPARENT`** from a cursor-position check against the current frame's
   alpha mask. This is more fragile: it polls and races with fast mouse movement.

Mouse messages (`WM_LBUTTONDOWN`, drag, double-click, right-click) can stay as they are in
`CharacterWindow` **if** the input reaches the popup's wndproc. With `DesktopChildSiteBridge`
the child HWND receives the input instead. Either use `DesktopAttachedSiteBridge`, make the
child transparent to input, or move the handling to the island's `InputPointerSource`
events. Check this in the spike.

### What happens to existing code

| Code | Fate |
|---|---|
| `AcsFrameRenderer` | Kept. It's still the reference for pixel tests, and the source of the hit-test regions if the `.acs` region data isn't parsed. |
| `AnimationPlayer` | Takes an `IFramePresenter` (or `Action<AcsFrame>`) instead of `Action<uint[]>`. Timing, branching, exit paths and sounds are unchanged. `CurrentFrame` (a pixel buffer) becomes the current `AcsFrame`. |
| `LayeredWindow.Present`, `ScaleNearest` | Removed; the character was their only user. |
| `CharacterWindow` | Keeps its mouse and menu handling and loses the layered style. It gains region updates. |
| `AssistantController.Present/Scale` | `Scale` sets `root.Scale`, the window size and the region cache key. "Keep the feet in place" stays. |
| `MainPage.RedrawPreview` | Replaced by a `XamlFramePresenter` on the preview element (`ElementCompositionPreview.SetElementChildVisual`) with the same atlas. The `WriteableBitmap` loop goes away. |
| `UiTimer` | Unchanged. It could become a `DispatcherQueueTimer`, since the composition work needs the WinUI dispatcher anyway. |

### Asset pre-processing (optional)

Composition makes a pre-built atlas worthwhile, which a layered window never did. The
`tools/AcsTool` command could gain `atlas <file.acs> <out-dir>`, writing:
- `<KEY>.atlas.png`: the premultiplied atlas
- `<KEY>.atlas.json`: each image's atlas rectangle, plus optionally the per-frame regions

The app would load the pair when it's present and build it from the `.acs` at runtime when
it isn't. Users can still add `.acs` files at runtime, so the parser and the runtime atlas
builder must both stay. Don't pre-bake animations into GIF, APNG or WebP: those formats are
linear and can't express Agent branching, exit branches or per-frame sounds.

### DPI and scaling

- Integer `root.Scale` with nearest-neighbour sampling gives output identical to today's
  `ScaleNearest`.
- Non-integer scales (for example 1.5 at 150% DPI) now become possible. They show uneven
  pixel widths with nearest-neighbour sampling, so keep integers as the default and offer
  fractional sizes only if the user asks for them.
- Snap the popup position and size to physical pixels, so the art never lands on half
  pixels.

## Phased plan

0. **Spike (1–2 days, throwaway branch).** One popup showing Clippit's `RestPose` frame
   through option B1. Answer these:
   1. Is the background transparent over the desktop? (Screenshot over a coloured window.)
   2. Do clicks on transparent pixels reach the window underneath, once `SetWindowRgn` is
      applied?
   3. Does `WM_LBUTTONDOWN`/drag reach the popup's wndproc with the chosen bridge?
   4. Does `NearestNeighbor` at scale 2 match `LayeredWindow.ScaleNearest` exactly?
      (Capture with `Windows.Graphics.Capture` or `PrintWindow` and compare.)

   If question 1 or 3 fails with B1, repeat with option B2. Report the findings before
   continuing.
1. **Introduce the `IFramePresenter` seam** in `AnimationPlayer`, with a
   `LayeredFramePresenter` that wraps today's path. Behaviour stays identical and all tests
   pass. This step is worth merging on its own.
2. **`CharacterAtlas`** in `OfficeAssistant.Core`: build the BGRA images, pack them and
   expose each image's rectangle. Unit-test that drawing a frame from the atlas rectangles
   equals `AcsFrameRenderer` output, pixel for pixel, for every frame of every assistant in
   `Assistants/`.
3. **Per-frame region builder** (from the `.acs` region data or from pixels), cached. Test
   that the region covers exactly the opaque pixels.
4. **`CompositionFramePresenter`** and the new popup. Make it selectable with a setting or
   a compile-time switch, so the layered path stays available as a fallback.
5. **`XamlFramePresenter`** for the gallery preview. Delete the `WriteableBitmap` loop.
6. **Remove the layered character path** once the composition path has run day to day
   without regressions: the show/hide animations, drag, the menu, switching assistants,
   scaling, multiple monitors at different DPIs, Remote Desktop, and sleep/resume (device
   loss).

## Risks and open questions

- **Transparency and input with `ContentIsland` bridges on a top-level popup.** Unverified;
  that's what the spike is for.
- **Device loss** (driver update, Remote Desktop, sleep): the atlas surface must be rebuilt.
  `LoadedImageSurface` does this itself. Win2D and raw interop need a handler.
- **Remote Desktop and VMs:** composition works there, but test it, because the layered
  path is known to work.
- **Two compositors (option B2):** objects can't be shared between the system compositor
  and the Windows App SDK one. Keep the floating character and the preview on separate
  atlas instances.
- **Effort:** roughly 1–2 weeks including the spike, for about the same amount of code as
  today, plus capabilities the layered window doesn't have. If those capabilities aren't
  wanted, don't do this.

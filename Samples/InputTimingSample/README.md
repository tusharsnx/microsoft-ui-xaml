# WinUI 3 Input & TextBox Timing Verification Sample

This sample app measures precise, nanosecond-to-millisecond level timings across the entire lifecycle of input events in WinUI 3:
1. **Window Receipt ($T_1$)**: When the OS / CoreMessaging delivers the input event (`WM_POINTERDOWN`, `WM_POINTERUPDATE`, `WM_MOUSEMOVE`, `WM_LBUTTONDOWN`, `WM_KEYDOWN`, `WM_CHAR`) to the window message loop.
2. **TextBox Processing Start ($T_2$)**: When XAML completes routing and hit-testing, and delivers the event to `TextBox::OnPointerPressed`, `TextBox::OnPointerMoved`, or `TextBox::OnKeyDown`.
3. **TextBox Processing Finish ($T_3$)**: When RichEdit (WinUIEdit) completes hover state, caret movement, selection range calculation, and marks the view dirty.
4. **Composition Render Pass ($T_4$)**: When XAML ticks the next render pass via `CompositionTarget.Rendering` and calls `TxDrawD2D` to redraw glyphs.

---

## Building and Running

### Method 1: Using `dotnet build`
```powershell
# Restore using local repo package cache
dotnet restore Samples/InputTimingSample/InputTimingSample.csproj -s D:\dev\microsoft-ui-xaml\packages

# Build the project
dotnet build Samples/InputTimingSample/InputTimingSample.csproj -p:Platform=x64
```

### Method 2: Using the Repository Build Script
From an initialized developer prompt (`init.cmd`):
```cmd
init.cmd
scripts\buildSample InputTimingSample 3.0.0-dev -unpackaged -selfcontained
```

### Method 3: Visual Studio
Open `InputTimingSample.sln` in Visual Studio 2022 / 2026, set the platform to `x64`, and press **F5**.

---

## Verifying in Windows Performance Analyzer (WPA) with ETW

The app emits custom ETW events via `Microsoft-WinUI-InputTimingSample` (`EventSource`) alongside WinUI's native provider `Microsoft-Windows-XAML` (`{531A35AB-63CE-4BCF-AA98-F88C7A89E455}`).

To capture a trace while using the sample app:
```powershell
# Start recording
wpr -start perf\profiles\XAML.wprp!XAML_CPU.Light.Memory -filemode

# (Interact with the TextBox in the app)

# Stop recording
wpr -stop input_timing_trace.etl
```

Open `input_timing_trace.etl` in WPA to cross-reference:
* `Microsoft-Windows-XAML: TracePointerPressedBegin / End`
* `Microsoft-WinUI-InputTimingSample: TextBoxProcessingStarted / Finished`
* `Microsoft-Windows-XAML: CoreServices_Frame`

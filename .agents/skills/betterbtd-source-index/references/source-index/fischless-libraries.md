# Fischless Platform Libraries

Windows game capture, global hot-key, and input simulation libraries.

[Back to BetterBTD Source Index](../source-index.md)

## Related Indexes

- [BetterBTD Application and Assets](./betterbtd-application.md)
- [BetterBTD Core and Helpers](./betterbtd-core.md)

## Directory Summary

| Directory | Files |
| --- | ---: |
| `Fischless.GameCapture` | 5 |
| `Fischless.GameCapture/BitBlt` | 4 |
| `Fischless.GameCapture/DwmSharedSurface` | 1 |
| `Fischless.GameCapture/Graphics` | 1 |
| `Fischless.GameCapture/Graphics/Helpers` | 5 |
| `Fischless.HotkeyCapture` | 6 |
| `Fischless.WindowsInput` | 15 |

## File Inventory

Paths are relative to the repository root. Open the linked file and verify current behavior before editing.

### `Fischless.GameCapture`

| File | Description |
| --- | --- |
| [CaptureModeExtensions.cs](../../../../../Fischless.GameCapture/CaptureModeExtensions.cs) | Application source code; primary symbols: CaptureModeExtensions |
| [CaptureModes.cs](../../../../../Fischless.GameCapture/CaptureModes.cs) | Application source code; primary symbols: CaptureModes |
| [Fischless.GameCapture.csproj](../../../../../Fischless.GameCapture/Fischless.GameCapture.csproj) | MSBuild project definition |
| [GameCaptureFactory.cs](../../../../../Fischless.GameCapture/GameCaptureFactory.cs) | Application source code; primary symbols: GameCaptureFactory |
| [IGameCapture.cs](../../../../../Fischless.GameCapture/IGameCapture.cs) | Application source code; primary symbols: IGameCapture, struct, IGameCaptureFrameMetadataProvider |

### `Fischless.GameCapture/BitBlt`

| File | Description |
| --- | --- |
| [BitBltCapture.cs](../../../../../Fischless.GameCapture/BitBlt/BitBltCapture.cs) | Application source code; primary symbols: BitBltCapture |
| [BitBltMat.cs](../../../../../Fischless.GameCapture/BitBlt/BitBltMat.cs) | Application source code; primary symbols: BitBltMat |
| [BitBltRegistryHelper.cs](../../../../../Fischless.GameCapture/BitBlt/BitBltRegistryHelper.cs) | Application source code; primary symbols: BitBltRegistryHelper |
| [BitBltSession.cs](../../../../../Fischless.GameCapture/BitBlt/BitBltSession.cs) | Application source code; primary symbols: BitBltSession |

### `Fischless.GameCapture/DwmSharedSurface`

| File | Description |
| --- | --- |
| [SharedSurfaceCapture.cs](../../../../../Fischless.GameCapture/DwmSharedSurface/SharedSurfaceCapture.cs) | Application source code; primary symbols: SharedSurfaceCapture |

### `Fischless.GameCapture/Graphics`

| File | Description |
| --- | --- |
| [GraphicsCapture.cs](../../../../../Fischless.GameCapture/Graphics/GraphicsCapture.cs) | Application source code; primary symbols: GraphicsCapture |

### `Fischless.GameCapture/Graphics/Helpers`

| File | Description |
| --- | --- |
| [CaptureHelper.cs](../../../../../Fischless.GameCapture/Graphics/Helpers/CaptureHelper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: CaptureHelper, IInitializeWithWindow, IGraphicsCaptureItemInterop |
| [Direct3D11Helper.cs](../../../../../Fischless.GameCapture/Graphics/Helpers/Direct3D11Helper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: Direct3D11Helper, IDirect3DDxgiInterfaceAccess, for |
| [HdrToSdrShader.cs](../../../../../Fischless.GameCapture/Graphics/Helpers/HdrToSdrShader.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: HdrToSdrShader |
| [Texture2DExtensions.cs](../../../../../Fischless.GameCapture/Graphics/Helpers/Texture2DExtensions.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: Texture2DExtensions |
| [WinRT.cs](../../../../../Fischless.GameCapture/Graphics/Helpers/WinRT.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: IGraphicsCaptureItemInterop, IActivationFactoryVftbl, Platform, WinrtModule |

### `Fischless.HotkeyCapture`

| File | Description |
| --- | --- |
| [Fischless.HotkeyCapture.csproj](../../../../../Fischless.HotkeyCapture/Fischless.HotkeyCapture.csproj) | MSBuild project definition |
| [Hotkey.cs](../../../../../Fischless.HotkeyCapture/Hotkey.cs) | Application source code; primary symbols: Hotkey |
| [HotkeyHolder.cs](../../../../../Fischless.HotkeyCapture/HotkeyHolder.cs) | Application source code; primary symbols: HotkeyHolder |
| [HotkeyHook.cs](../../../../../Fischless.HotkeyCapture/HotkeyHook.cs) | Application source code; primary symbols: HotkeyHook, Window |
| [KeyPressedEventArgs.cs](../../../../../Fischless.HotkeyCapture/KeyPressedEventArgs.cs) | Application source code; primary symbols: KeyPressedEventArgs |
| [SystemErrorCodes.cs](../../../../../Fischless.HotkeyCapture/SystemErrorCodes.cs) | Application source code; primary symbols: SystemErrorCodes |

### `Fischless.WindowsInput`

| File | Description |
| --- | --- |
| [Fischless.WindowsInput.csproj](../../../../../Fischless.WindowsInput/Fischless.WindowsInput.csproj) | MSBuild project definition |
| [IInputDeviceStateAdaptor.cs](../../../../../Fischless.WindowsInput/IInputDeviceStateAdaptor.cs) | Application source code; primary symbols: IInputDeviceStateAdaptor |
| [IInputMessageDispatcher.cs](../../../../../Fischless.WindowsInput/IInputMessageDispatcher.cs) | Application source code; primary symbols: IInputMessageDispatcher |
| [IInputSimulator.cs](../../../../../Fischless.WindowsInput/IInputSimulator.cs) | Application source code; primary symbols: IInputSimulator |
| [IKeyboardSimulator.cs](../../../../../Fischless.WindowsInput/IKeyboardSimulator.cs) | Application source code; primary symbols: IKeyboardSimulator |
| [IMouseSimulator.cs](../../../../../Fischless.WindowsInput/IMouseSimulator.cs) | Application source code; primary symbols: IMouseSimulator |
| [InputBuilder.cs](../../../../../Fischless.WindowsInput/InputBuilder.cs) | Application source code; primary symbols: InputBuilder, VK2 |
| [InputSimulator.cs](../../../../../Fischless.WindowsInput/InputSimulator.cs) | Application source code; primary symbols: InputSimulator |
| [KeyboardSimulator.cs](../../../../../Fischless.WindowsInput/KeyboardSimulator.cs) | Application source code; primary symbols: KeyboardSimulator |
| [LICENSE.txt](../../../../../Fischless.WindowsInput/LICENSE.txt) | Application source code |
| [MouseButton.cs](../../../../../Fischless.WindowsInput/MouseButton.cs) | Application source code; primary symbols: MouseButton |
| [MouseSimulator.cs](../../../../../Fischless.WindowsInput/MouseSimulator.cs) | Application source code; primary symbols: MouseSimulator |
| [README.md](../../../../../Fischless.WindowsInput/README.md) | Application source code |
| [WindowsInputDeviceStateAdaptor.cs](../../../../../Fischless.WindowsInput/WindowsInputDeviceStateAdaptor.cs) | Application source code; primary symbols: WindowsInputDeviceStateAdaptor |
| [WindowsInputMessageDispatcher.cs](../../../../../Fischless.WindowsInput/WindowsInputMessageDispatcher.cs) | Application source code; primary symbols: WindowsInputMessageDispatcher |

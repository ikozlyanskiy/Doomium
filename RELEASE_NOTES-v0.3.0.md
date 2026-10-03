# Doomium 0.3.0

DOOM now has three ways to live inside an Altium Designer PCB document. Choose **Tools → Convert → Doomium Original** for the familiar window renderer, **Doomium Primitives** for PCB fills, or **Doomium Regions** for colored PCB regions.

The native modes use temporary mechanical-layer colors and PCB objects, follow the selected frame, and restore the board's previous layer settings when stopped. Middle-click captures or releases the mouse; while captured, game keys and mouse buttons are intercepted before Altium sees them.

The renderer you choose makes a real tradeoff. Original has the clearest image and highest frame rate. Primitives limits the number of fills by reducing detail when necessary. Regions keeps more detail but may run at single-digit FPS in complex scenes. Use a spare `.PcbDoc` for native playback, and avoid saving it while the game is running. See [renderers](https://github.com/ikozlyanskiy/Doomium/blob/v0.3.0/docs/renderers.md) for the details.

Requires Altium Designer 25 on Windows x64 and a DOOM-compatible IWAD such as Freedoom 2. WAD files are not included.

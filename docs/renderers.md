# Renderers

Doomium has three commands under **Tools → Convert**. All three use the same DOOM engine, frame selection, and controls.

| Command | Image | What changes on the board |
| :-- | :-- | :-- |
| **Doomium Original** | 320×200 window over the PCB editor | Nothing beyond the frame you drew |
| **Doomium Primitives** | PCB fill objects on colored mechanical layers | Temporary fill pool and layer settings |
| **Doomium Regions** | PCB regions on colored mechanical layers | Temporary regions and layer settings |

Original is the clearest and fastest choice. It follows the frame when you move it, but the image itself is not part of the board.

Primitives samples each frame and merges neighboring pixels of the same color into rectangles. It reuses up to 640 PCB fills, reducing resolution when a detailed frame would exceed that limit. It can use up to twelve mechanical-layer colors.

Regions builds geometric contours for each color and places them in PCB regions. It can use up to sixteen mechanical-layer colors. The higher resolution costs more CPU time; on the test machine, detailed scenes often ran in single-digit FPS. This is a visual experiment, not a promise of 20–30 FPS on every board.

Both native modes use free mechanical layer slots where possible. Doomium assigns temporary names and colors, hides a selected solid-fill frame so it cannot cover the image, and restores the previous state when the game stops normally. If Altium exits unexpectedly during playback, close that `.PcbDoc` without saving and inspect it before reusing it. Playback can also affect the Undo history. The native modes intercept game keys and mouse buttons while the mouse is captured so they do not activate PCB commands.

The frame can be four selected tracks or a selected rectangular fill. Altium's grouped **Place → Rectangle** needs **Tools → Convert → Explode Rectangle to Free Primitives** first. Native modes are best tried on a spare `.PcbDoc` in 2D view. Their FPS and render workload are logged to `%LOCALAPPDATA%\Doomium\doomium.log`.

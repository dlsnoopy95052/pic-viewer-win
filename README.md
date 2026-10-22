# Picture Viewer for Windows

Windows 11 port of the macOS [Picture Viewer](../pic-viewer): a small native picture viewer with light annotation and editing. Written in C# / WPF on .NET 10.

## Install

Download `PictureViewer-Setup-<version>.exe` (built below) and run it. It installs for the current user only (no administrator rights), needs no separate .NET install, and can start in the tray at sign-in. Pictures also appear in Explorer's **Open with** list. Uninstalling removes everything, including the Open at Login entry.

## Features

- Opens JPEG, PNG, GIF (animated GIFs play), TIFF, BMP, ICO, WebP, HEIC, AVIF, JXR, PSD, SVG, TGA, JPEG XL, EXR, HDR, PCX, XCF, PDF (first page) and camera RAW (DNG, CR2, CR3, NEF, NRW, ARW, ORF, RW2, RAF, PEF, SRW). WebP, HEIC and AVIF come from Windows when its codec is installed, otherwise from Magick.NET
- Browse every picture in the same folder; drag and drop; fit / actual size / zoom / pan
- Info panel with file and EXIF details, thumbnail strip, slideshow, rename, Recycle Bin, copy, show in Explorer, full screen
- Annotate, edit, save, capture, print: see the sections below
- Printing has a live preview, printer / paper / orientation choice, and four scaling modes (shrink to fit, fit to page, actual size, fill page) with optional centering

## Build

Requires the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`).

```powershell
dotnet run --project src/PictureViewer          # run from source
.uild-installer.ps1                           # artifacts\PictureViewer-Setup-<version>.exe
```

The installer script publishes a self-contained win-x64 build and packs it with Inno Setup (`winget install JRSoftware.InnoSetup --scope user`).

## Where macOS parts went

| Feature | macOS | Windows |
|---|---|---|
| Window, toolbar, zoom / pan | AppKit | WPF |
| Image decoding and metadata | ImageIO | WIC (built in), Magick.NET for the rest |
| PDF | PDFKit | `Windows.Data.Pdf` |
| Global capture hot key | Carbon `RegisterEventHotKey` | Win32 `RegisterHotKey` |
| Area capture | `screencapture` | Frozen-screen selection overlay (GDI `BitBlt`) |
| Menu bar presence | `NSStatusItem` | System tray `NotifyIcon` |
| Open at Login | `SMAppService` | `HKCU\...\Run` registry value |
| Edit (crop, flip, brightness, contrast, saturation) | CoreImage | `WriteableBitmap` pixel maths |
| Annotations | AppKit drawing | WPF layer in Picture-pixel space inside the zoom / pan transform |
| Clipboard | `NSPasteboard` | `System.Windows.Clipboard` (bitmap + PNG) |
| Printing with preview | `NSPrintPanel` | Own preview window + `PrintDialog` |
| Trash | Finder trash | Recycle Bin (`Microsoft.VisualBasic.FileIO`) |

Terminology is shared with the macOS app; see [GLOSSARY.md](GLOSSARY.md).

## Tray and Capture

Picture Viewer lives in the system tray. Closing the window only hides it; use **Quit** in the tray menu to exit.

- Capture a screen area with the global hot key, **Ctrl+Alt+4** by default (works while another app is in front; no permission prompt). Change it with **Set Capture Hot Key…** in the tray menu: press the combination you want. It needs Ctrl, Alt, Shift or Win with the key (Print Screen works alone), and is saved in `%APPDATA%\PictureViewer\settings.json` Drag the area, Esc or right-click cancels. Every screen is frozen first, so you get exactly what you saw
- The capture is copied to the clipboard (toggle in the tray menu). If a picture is already open it stays put and you can paste the capture onto it with Ctrl+V; otherwise the capture opens to annotate and edit
- Tray menu: Capture Screen Area, Show Picture Viewer, Open…, Set Capture Hot Key…, Copy Captures to Clipboard, **Open at Login**, About, Quit
- Open at Login starts straight into the tray (no window). Starting the app by hand shows the window
- Only one copy runs; opening a picture while it's running hands the file to the running one
- If the hot key belongs to another program, you get a notice and can still capture from the tray menu; picking a taken combination in the dialog keeps the old one

## Edit

Editing changes the picture's pixels, so it flattens any annotations and the view rotation first.

- Crop (drag the area, click Crop again to apply; Esc cancels), flip horizontally / vertically
- Brightness / contrast / saturation with live preview; Cancel by closing the popup
- Undo for every step, including edits that change the pixels
- Save overwrites after a confirmation (JPEG, PNG, TIFF); anything else, and pasted pictures, go through Save a Copy. JPEG is saved at quality 92 on a white background, and re-saving drops EXIF data

## Keyboard shortcuts

| Action | Keys |
|---|---|
| Open | Ctrl+O |
| Previous / next picture | ← / → (or PgUp / PgDn) |
| First / last picture | Home / End |
| Fit to window / actual size | 0 / 1 |
| Zoom in / out | + / - or mouse wheel |
| Slideshow (3 s, loops) | Space; Esc stops |
| Info panel | Ctrl+I |
| Thumbnail strip | Ctrl+T |
| Copy picture | Ctrl+C |
| Rename | F2 |
| Move to Recycle Bin | Delete |
| Show in Explorer | Ctrl+R |
| Full screen | F11; Esc leaves |
| Paste picture (as a layer) | Ctrl+V |
| Undo | Ctrl+Z |
| Rotate view left / right | Ctrl+[ / Ctrl+] |
| Delete the selected annotation (otherwise the file) | Delete |
| Crop | Ctrl+K |
| Save / Save a copy | Ctrl+S / Ctrl+Shift+S |
| Turn off tool / deselect | Esc |

## Annotate

Annotations stay editable until the picture is flattened (by an edit below, or when saving).

- Tools: line, arrow, rectangle, circle, freehand pen, text, and **Mosaic** to pixelate sensitive areas
- Click to select, drag to move, drag end points to resize, change color / width of the selection, Delete to remove
- Drag the round knob to rotate rectangles, circles, text and pasted pictures (Shift = 15° steps)
- Double-click text to edit (multi-line); drag the right-edge handle to wrap text (double-click it to unwrap); pick font, bold, italic and background color
- Paste (Ctrl+V) a picture from the clipboard as a layer you can move, resize and rotate; with nothing open it opens as a new picture
- Rotating the view only changes how the picture is displayed; copy includes annotations and the rotation
- Undo, and a prompt before unsaved annotations are discarded

## Milestones

1. ✅ Open a Picture, browse the folder, fit / zoom / pan
2. ✅ Info panel, thumbnail strip, slideshow, rename, Recycle Bin, copy, full screen
3. ✅ Annotations, including paste (a Pasted layer) and View rotation
4. ✅ Flatten and Edit
5. ✅ Capture with global hot key, tray icon, Open at Login
6. ✅ Printing, PDF, animated GIF, installer (Inno Setup)

## Known limits

- PDF shows the first page only (as on macOS)
- Camera RAW decoding comes from Magick.NET's LibRaw and hasn't been tried against real RAW files yet
- Saving re-encodes, so EXIF data is dropped; JPEG is written at quality 92
- Printing was verified up to the document sent to the printer; the Windows print dialog and physical printers haven't been exercised in automated tests

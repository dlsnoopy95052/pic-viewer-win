# Picture Viewer for Windows

Windows 11 port of the macOS [Picture Viewer](../pic-viewer): a small native picture viewer with light annotation and editing. Written in C# / WPF on .NET 10.

## Build and run

Requires the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`).

```powershell
dotnet run --project src/PictureViewer
dotnet publish src/PictureViewer -c Release -r win-x64 --self-contained false
```

## Port plan

Features carried over from the macOS app, with the Windows technology used for each:

| Feature | macOS | Windows |
|---|---|---|
| Window, toolbar, zoom / pan | AppKit | WPF |
| Image decoding and metadata | ImageIO | WIC (built in), Magick.NET for RAW / PSD / AVIF / HEIC |
| PDF | PDFKit | Windows.Data.Pdf |
| Global capture hot key | Carbon `RegisterEventHotKey` | Win32 `RegisterHotKey` |
| Area capture | `screencapture` | Selection overlay + `Graphics.CopyFromScreen` |
| Menu bar presence | `NSStatusItem` | System tray `NotifyIcon` |
| Open at Login | `SMAppService` | `HKCU\...\Run` registry value |
| Edit (crop, flip, brightness, contrast, saturation) | CoreImage | Pixel shaders / WriteableBitmap |
| Annotations (line, arrow, rectangle, circle, pen, text, Mosaic, Pasted layer) | AppKit drawing | WPF `Adorner` / `DrawingVisual` layer |
| Clipboard | `NSPasteboard` | `System.Windows.Clipboard` |
| Printing with preview | `NSPrintPanel` | `PrintDialog` + `DocumentViewer` |
| Trash | Finder trash | Recycle Bin (`Microsoft.VisualBasic.FileIO`) |

Terminology is shared with the macOS app; see [GLOSSARY.md](GLOSSARY.md).

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

## Milestones

1. ✅ Open a Picture, browse the folder, fit / zoom / pan
2. ✅ Info panel, thumbnail strip, slideshow, rename, Recycle Bin, copy, full screen
3. Annotations, including paste (a Pasted layer) and View rotation
4. Flatten and Edit
5. Capture with global hot key, tray icon, Open at Login
6. Printing, extra formats, installer (MSIX or Inno Setup)

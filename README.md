# clipora

Lite snippet/clipboard widget for Windows (WinUI 3).

- Saves text snippets in `ApplicationData.Current.LocalSettings` (`SavedSnippets`, delimiter `|||`)
- Tray icon (open/exit)
- Copy-to-clipboard + delete per snippet
- First-activate window placement (top-right)

## Install (MSIX sideload)
```powershell
Add-AppxPackage .\Clipora_*.msix
# or
Add-AppxPackage .\Clipora_*.msixbundle

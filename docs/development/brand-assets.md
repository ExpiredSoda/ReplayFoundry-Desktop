# Brand assets

`src/ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png` is the canonical desktop mark. `favicon.svg` is the canonical vector web mark. Keep those sources unchanged across features rather than creating local logo variants.

From the repository root, regenerate the Windows icon derivative with:

```powershell
.\eng\New-ReplayFoundryBrandAssets.ps1
```

The command writes `src/ReplayFoundry.Desktop/Assets/Icons/Application/ReplayFoundry.ico` with 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixel 32-bit PNG frames. The WPF application, secondary windows, shortcuts, and Inno Setup all use this one generated icon.

The website consumes the canonical SVG from its own project. When it needs an Apple touch icon, generate that derivative explicitly instead of committing another hand-edited mark:

```powershell
.\eng\New-ReplayFoundryBrandAssets.ps1 `
  -AppleTouchOutput <website-root>\public\apple-touch-icon.png
```

Installer artwork is generated separately by `eng/New-ReplayFoundryInstallerBranding.ps1`; see the [Windows distribution guide](../distribution/windows.md).

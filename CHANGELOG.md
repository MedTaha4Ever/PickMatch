# Changelog

All notable changes to PickMatch. Newest first.
Each release on GitHub uses its section from this file as release notes.

## [3.1.1] - 2026-11-27

### Fixed
- Clicking one of the **Close matches** photos did nothing. It now chooses that photo, with the mouse or the keyboard.

### Added
- Microsoft Store package (MSIX, x64 and ARM64). It passes the Windows App Certification Kit.
- Privacy policy (`PRIVACY.md`): PickMatch works offline and collects nothing.

## [3.1.0] - 2026-11-27

### Added
- Installers:
  - **Windows:** setup program in English and French. No admin rights needed. Adds Start menu and desktop shortcuts and an uninstaller.
  - **macOS:** `.dmg` disk image.
  - **Linux:** `.deb` package that adds PickMatch to the app menu and installs Tesseract.
- Each installer is tested automatically: install, start the app, uninstall.

### Changed
- Linux: no longer needs the system ICU library, so it runs on any recent distribution.

## [3.0.0] - 2026-11-26

### Added
- **macOS and Linux** versions (Uno Platform), with the same screens as the Windows app.
- Reading names written on pictures on macOS/Linux with Tesseract, when installed.
- New icon.

### Changed
- Renamed from TahaCull to **PickMatch**.
- Moved to .NET 10.
- The settings cards and filter bar are now standard WinUI controls. They look the same.

## [2.0.0] - 2026-11-26

### Added
- Windows 11 app (WinUI 3, Windows App SDK 2.7) with Mica, a light/dark theme and your accent colour.
- RAW files from all major cameras (Canon, Nikon, Sony, Fujifilm, Panasonic, Olympus, Pentax, DNG and more), read from the preview inside each file.
- RAW + JPG copies of the same shot are grouped by name and capture time.
- Large compare view: ← → to move, Space to confirm, Esc to close.
- Copy options: RAW, JPG, edit sidecars (.xmp…), and renaming client pictures. Nothing is overwritten.
- MIT license and a donation link.

### Fixed
- The published app closed at start because some interface files were missing from the package.

## [1.0.0] - 2026-11-26

### Added
- First version (TahaCull): matches client-picked pictures to CR3 files by file name, name written on the picture
  (Windows OCR) or image similarity, then copies the confirmed CR3 files.

[3.1.1]: https://github.com/MedTaha4Ever/PickMatch/releases/tag/v3.1.1
[3.1.0]: https://github.com/MedTaha4Ever/PickMatch/releases/tag/v3.1.0
[3.0.0]: https://github.com/MedTaha4Ever/PickMatch/releases/tag/v3.0.0
[2.0.0]: https://github.com/MedTaha4Ever/PickMatch/releases/tag/v2.0.0

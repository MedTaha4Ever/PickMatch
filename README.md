# PickMatch

[![Donate with Wise](https://img.shields.io/badge/Donate-Wise-9FE870?logo=wise&logoColor=163300)](https://wise.com/pay/me/medb5)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![build](https://github.com/MedTaha4Ever/PickMatch/actions/workflows/build.yml/badge.svg)](https://github.com/MedTaha4Ever/PickMatch/actions/workflows/build.yml)

For after the client has chosen their photos. The client sends back the pictures they picked as WhatsApp copies,
screenshots or low-res JPGs. PickMatch finds the original RAW/JPG of each one, you check the pairs side by side,
and it copies the originals to a separate folder.

Runs on **Windows**, **macOS** and **Linux**.

## Download

Get the latest version from [Releases](https://github.com/MedTaha4Ever/PickMatch/releases/latest):

| System | File | How to start |
|---|---|---|
| Windows 10/11 | `PickMatch-windows-x64.zip` (ARM: `-arm64`) | Unzip, run `PickMatch.exe` |
| macOS (Apple Silicon) | `PickMatch-macos-arm64.zip` (Intel: `-x64`) | Unzip, then **right-click PickMatch → Open** the first time (see below) |
| Linux | `PickMatch-linux-x64.tar.gz` (ARM: `-arm64`) | `tar xzf PickMatch-linux-x64.tar.gz && ./PickMatch/PickMatch` |

**macOS:** PickMatch is not signed with a paid Apple developer certificate, so macOS asks for confirmation the
first time. Right-click the app and choose **Open**. On recent macOS versions, open it once, then go to
**System Settings → Privacy & Security** and click **Open Anyway**. If macOS says the app is "damaged", run
`xattr -dr com.apple.quarantine PickMatch.app` in Terminal.

## Using it

1. **Photos**: the shoot folder. All subfolders are searched (e.g. `Wedding/cr3`, `Wedding/Compressed`).
2. **Client picks**: the folder with the pictures the client sent.
3. **Find originals**. For each client picture, PickMatch tries:
   - the **file name** (e.g. `DSC_1234 (1).jpg`)
   - a **file name written on the picture** (gallery or screenshot labels), read with text recognition
   - **image similarity**: crops, screenshots and heavy compression are fine
4. Check the pairs. A green tick means the original was found by name. Orange means it was found by image
   similarity, so check burst frames. Red means no match, or two picks using the same photo.
   - Click one of the **close matches** to switch, or type a file name (`3314` is enough).
   - **Compare large** (or double-click) opens a full-window comparison: ← → to move, Space to confirm, Esc to close.
   - The **To check** filter hides the pairs that are already sure.
5. **Copy photos** copies the confirmed originals to `Originals` inside the client folder (you can change this).
   **Options** sets what is copied: RAW files, JPG files, edit sidecars (`.xmp`, `.pp3`, `.dop`…), and whether to
   rename the client's pictures to the original names. Nothing is ever overwritten.

You can also start it with both folders: `PickMatch "D:\Shoot" "D:\Shoot\client"`.

### Reading names written on pictures

- **Windows:** built in.
- **macOS / Linux:** install [Tesseract](https://tesseract-ocr.github.io/) (`brew install tesseract`, or
  `sudo apt install tesseract-ocr`). Without it, matching uses file names and image similarity only, which works
  well too. Burst frames are then more often marked "to check".

## Files it reads

| | Formats |
|---|---|
| RAW | Canon CR3, CR2 · Nikon NEF, NRW · Sony ARW, SRF, SR2 · Fujifilm RAF · Panasonic/Leica RW2, RWL, RAW · Olympus/OM ORF · Pentax PEF · Samsung SRW · Adobe DNG (and GoPro GPR) · Hasselblad 3FR, FFF · Phase One IIQ · Kodak, Leaf, Mamiya, Epson and others |
| Images | JPG, PNG, BMP, WebP everywhere; also TIFF, HEIC/HEIF, AVIF and JPEG XL on Windows (with the Store extensions) |

RAW files are never developed. PickMatch reads the full-size JPEG preview the camera stores inside every RAW, which
is fast: about 1,000 photos load in under a minute from a hard disk. On Windows, RAW files without a readable
preview go through the free **Raw Image Extension** from the Microsoft Store.

RAW + JPG copies of the same shot are shown once. They are grouped by file name and capture time, so two cameras
that both produce `IMG_0001` stay separate.

## Build

Needs the .NET 10 SDK.

| Project | What |
|---|---|
| `src/PickMatch` | Windows app: WinUI 3, Windows App SDK 2.7 |
| `src/PickMatch.Desktop` | macOS / Linux app: [Uno Platform](https://platform.uno) 7, sharing the screens and code of the Windows app |
| `src/PickMatch.Core` | Reading photos, matching and copying; SkiaSharp on macOS/Linux, Windows Imaging on Windows |
| `tests/MatchCheck` | Command-line checks |

```
dotnet build src/PickMatch -c Release                                     # Windows app
dotnet run --project src/PickMatch.Desktop -c Release -f net10.0-desktop  # macOS / Linux app
dotnet run --project tests/MatchCheck -f net10.0 -- --selftest            # quick check on any system
dotnet run --project tests/MatchCheck -f net10.0 -- "D:\Shoot" "D:\client" expected.tsv
dotnet run --project tests/MatchCheck -f net10.0 -- --info D:\Shoot\IMG_0001.CR2
```

GitHub Actions runs the self-test on Windows, macOS and Linux, and builds all six downloads on every push.
A `v*` tag publishes them as a release.

## Support

PickMatch is free. If it saves you time, you can [buy me a coffee through Wise](https://wise.com/pay/me/medb5).

## License

[MIT](LICENSE): free to use, change, share and fork, including for commercial work.

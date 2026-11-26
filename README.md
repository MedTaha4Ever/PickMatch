# PickMatch

[![Donate with Wise](https://img.shields.io/badge/Donate-Wise-9FE870?logo=wise&logoColor=163300)](https://wise.com/pay/me/medb5)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A Windows 11 app for after the client has chosen their photos. The client sends back the pictures they picked:
WhatsApp copies, screenshots or low-res JPGs. PickMatch finds the original RAW/JPG for each one, you check the pairs
side by side, and it copies the originals to a separate folder.

## Using it

1. **Photos**: the shoot folder. All subfolders are searched (e.g. `Mariage\cr3`, `Outia\Compressed`).
2. **Client picks**: the folder with the pictures the client sent.
3. **Find originals**. For each client picture, PickMatch tries:
   - the **file name** (e.g. `DSC_1234 (1).jpg`)
   - a **file name written on the picture** (gallery or screenshot labels), read with the text recognition built into Windows
   - **image similarity**: crops, screenshots and heavy compression are fine
4. Check the pairs. A green tick means the original was found by name. Orange means it was found by image similarity,
   so check burst frames. Red means no match, or two picks using the same photo.
   - Click one of the **close matches** to switch, or type a file name (`3314` is enough).
   - **Compare large** (or double-click a pair) opens a full-window comparison. Use ← → to move through the pairs,
     Space to confirm and Esc to close.
   - The **To check** filter hides the pairs that are already sure.
5. **Copy photos** copies the confirmed originals to `Originals` inside the client folder (you can change this).
   **Options** sets what is copied: RAW files, JPG files, edit sidecars (`.xmp`, `.pp3`, `.dop`…), and whether
   to rename the client's pictures to the original names. Nothing is ever overwritten.

You can also start it with both folders: `PickMatch.exe "D:\Shoot" "D:\Shoot\client"`.

## Files it reads

| | Formats |
|---|---|
| RAW | Canon CR3, CR2, CRW · Nikon NEF, NRW · Sony ARW, SRF, SR2 · Fujifilm RAF · Panasonic/Leica RW2, RWL, RAW · Olympus/OM ORF · Pentax PEF · Samsung SRW · Adobe DNG (and GoPro GPR) · Hasselblad 3FR, FFF · Phase One IIQ · Kodak, Leaf, Mamiya, Epson, Sigma and others |
| Images | JPG, PNG, TIFF, HEIC/HEIF, AVIF, WebP, JPEG XL, BMP |

RAW files are never developed. PickMatch reads the full-size JPEG preview the camera stores inside every RAW, which is
fast: about 1,000 photos load in under a minute from a hard disk. For RAW files without a readable preview it uses
Windows' own codec, the free **Raw Image Extension** from the Microsoft Store. HEIC, AVIF, WebP and JPEG XL need their
Store extensions too.

RAW + JPG copies of the same shot are shown once. They are grouped by file name and capture time, so two cameras that
both produce `IMG_0001` stay separate.

## Build

Needs Windows 10/11 and the .NET 9 SDK. The project uses the Windows App SDK 2.7 (WinUI 3), unpackaged and self-contained.

```
dotnet build src/PickMatch -c Release
dotnet publish src/PickMatch -c Release -r win-x64 -o publish/PickMatch
```

`tests/MatchCheck` checks the matching from the command line:

```
dotnet run --project tests/MatchCheck -c Release -- "D:\Shoot" "D:\client" expected.tsv
dotnet run --project tests/MatchCheck -c Release -- --info D:\Shoot\IMG_0001.CR2
```

GitHub Actions builds x64 and ARM64 zips on every push. A `v*` tag also publishes them as a release.

## Support

PickMatch is free. If it saves you time, you can [buy me a coffee through Wise](https://wise.com/pay/me/medb5).

## License

[MIT](LICENSE): free to use, change, share and fork, including for commercial work.

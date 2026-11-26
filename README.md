# TahaCull

Windows app for wedding culling: the client sends back the pictures they chose (WhatsApp, screenshots,
low-res JPGs), TahaCull finds the matching **CR3** files and copies them to a separate folder after you check them.

## How it works

1. **CR3 folder**: the shoot folder. Subfolders are searched too (e.g. `Mariage\cr3`, `Outia\cr3`).
2. **Client pictures folder**: the pictures the client sent.
3. **Auto match**. For each client picture, TahaCull tries in this order:
   - the **file name** (e.g. `9A2A3314 (1).jpg`)
   - a **file name written on the picture** (gallery or screenshot labels), read with the OCR built into Windows
   - **image similarity**: the client picture is compared with the preview inside every CR3. Crops, screenshots and
     compression are fine.
4. Check each pair side by side:
   - green: found by name
   - orange: found by image similarity, check burst frames
   - red: no match, or the same CR3 as another pick

   Click one of the other close photos to change the match, or type a name (`3314` is enough).
5. **Copy confirmed CR3** copies the CR3 files (and `.xmp` sidecars) to the output folder, `CR3` inside the client
   folder by default. It can also rename the client pictures to the CR3 names.

Only the small JPEG preview inside each CR3 is read, so even 1,000 RAW files load in seconds. Original files are never
changed: only the client pictures are renamed, and only if that option is ticked.

## Build

Needs the .NET 8 SDK on Windows.

```
dotnet run --project src/TahaCull -c Release
```

Single exe (no install needed):

```
dotnet publish src/TahaCull -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Pushing to GitHub builds `TahaCull.exe` in Actions; pushing a `v*` tag also makes a release.

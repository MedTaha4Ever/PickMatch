# Microsoft Store listing: PickMatch

Copy these into Partner Center → PickMatch → Submission → **Store listings → English**.
Store ID **9PF0BD9NSW5Z** · https://apps.microsoft.com/detail/9PF0BD9NSW5Z

## Product name
PickMatch

## Short description
Your client sent back the photos they chose as WhatsApp copies or screenshots. PickMatch finds the original RAW or JPG of each one in seconds, lets you check the pairs side by side, and copies the originals to a separate folder.

## Description
Your client has chosen their photos, but they sent them back as WhatsApp copies, screenshots or small JPGs, with names like "IMG-20261122-WA0012.jpg". PickMatch finds the original file of each picture for you.

HOW IT WORKS
1. Choose your shoot folder (subfolders are included) and the folder with the client's pictures.
2. PickMatch finds the original of each picture:
   • by the file name, if it is still there
   • by a file name written on the picture (gallery screenshots), read with the text recognition built into Windows
   • by comparing the pictures themselves: crops, screenshots and heavy compression are fine
3. Check the pairs side by side. Green means found by name; orange means "please check" (useful for burst shots). Click a close match to switch, or open the large compare view.
4. Copy the confirmed originals (RAW, JPG and edit sidecars like .xmp) to a new folder, ready for retouching and printing.

FAST, EVEN WITH THOUSANDS OF RAW FILES
PickMatch reads the preview stored inside each RAW file, so a whole wedding loads in under a minute.

WORKS WITH YOUR CAMERA
Canon CR3/CR2, Nikon NEF, Sony ARW, Fujifilm RAF, Panasonic RW2, Olympus ORF, Pentax PEF, Adobe DNG, Hasselblad, Phase One and more, plus JPG, PNG, TIFF and HEIC. RAW+JPG copies of the same shot are shown once.

SAFE
Your files are never changed or overwritten, and nothing is uploaded: PickMatch works entirely on your computer.

Free and open source: https://github.com/MedTaha4Ever/PickMatch

## What's new in this version
First release in the Microsoft Store.

## Product features (one per line)
Finds the original RAW/JPG of each photo your client picked
Works with WhatsApp copies, screenshots, crops and small JPGs
Reads file names written on gallery screenshots
Side-by-side check with a large compare view and keyboard shortcuts
Supports RAW files from Canon, Nikon, Sony, Fujifilm, Panasonic, Olympus, Pentax, DNG and more
Groups RAW + JPG copies of the same shot
Copies RAW, JPG and .xmp edits; never overwrites your files
Fast: reads the preview inside RAW files instead of developing them
Works offline: nothing is uploaded

## Search terms (max 7)
photo culling
client selection
wedding photography
RAW files
photo matching
CR3
photographer tools

## Copyright and trademark info
© 2026 TahaProd

## Additional license terms
MIT License: https://github.com/MedTaha4Ever/PickMatch/blob/main/LICENSE

## Privacy policy URL
https://github.com/MedTaha4Ever/PickMatch/blob/main/PRIVACY.md

## Website
https://github.com/MedTaha4Ever/PickMatch

## Support contact info
https://github.com/MedTaha4Ever/PickMatch/issues

---

# Other submission pages

**Pricing and availability**
- Markets: all · Visibility: Public · Pricing: Free

**Properties**
- Category: **Photo & video**
- Privacy policy: yes, see the URL above (it doesn't collect personal information)
- Product declarations: none needed

**Age ratings**
- Answer the questionnaire: no violence, no user-generated content shared online, no purchases, no location. The
  result should be 3+ / Everyone.

**Packages**
- Upload both `PickMatch_x.y.z.0_x64.msixupload` and `PickMatch_x.y.z.0_arm64.msixupload` (from GitHub Actions →
  the **store** artifact, or from `src\PickMatch\AppPackages\`).

**Submission options → Notes for certification**
> PickMatch is a desktop photo tool (WinUI 3 / Windows App SDK). It uses the runFullTrust capability because it
> reads the photo folders the user chooses and copies the selected files to a folder they choose. No account or
> sign-in is needed. To test: choose any folder with photos as "Photos" and a folder with a few resized copies of
> them as "Client picks", then select "Find originals".

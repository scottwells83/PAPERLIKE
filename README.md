# PaperLike

PaperLike is a free, local-first EPUB and PDF reader for macOS, with a Windows build awaiting device testing. It aims to make reading feel calm and comfortable, with warm page colors and a few simple controls.

## What works

- Import EPUBs and PDFs into a library managed by the app. The selected original file stays in place.
- Browse an expanded chapter index in the left pane. PDFs use embedded outlines when present and printed chapter headings when detectable. PDF page entry remains available.
- Turn EPUBs by visible page, with book-wide page progress, and reopen at the saved page and scroll position.
- Save, revisit, and remove bookmarks within each book.
- Search extracted text within the current book and jump to a matching chapter or PDF page.
- Choose Paper, Ivory, or Night colors across the whole window, including PDF pages. Choose Georgia, Inter, Verdana, or the bundled Atkinson Hyperlegible Next font and adjust text size.
- Collapse the library, open its folder in Finder, or remove an imported copy. Removed copies move to `Removed Books` for recovery; originals remain in place.
- Save a Continue reading bookmark automatically when switching books or closing the app.
- Keep books and progress on this computer; no account, cloud service, storefront, or in-app purchase.

EPUB content is currently shown as extracted chapter text, so book images and rich inline styling are not shown. PDF pages retain their visual layout. Search uses the PDF's text layer; scanned image-only PDFs need OCR outside PaperLike to become searchable. Search results navigate to the relevant chapter or page and show a snippet; matches are not highlighted on the page. Syncing is not included.

## Install

- **macOS:** Download the disk image for your processor, open it, and run `Install PaperLike.command`. This installs `~/Applications/PaperLike.app`; the library lives at `~/Library/Application Support/PaperLike/Books`.
- **Windows:** The included workflow builds `PaperLike-Setup-x64.exe` for GitHub Releases. Until a release is published, the portable x64 preview can be downloaded as a ZIP; extract the whole folder and run `PaperLike.exe`.

The current Mac packages are ad-hoc signed and not notarized, so macOS may show a security warning. Broad distribution needs Apple Developer ID signing and notarization.

## Build

Requires the .NET 10 SDK. Run `dotnet run` from this directory. The `scripts/package-macos.sh` script creates the `.app` bundle and `.dmg` for `osx-arm64` or `osx-x64`. The Windows installer is compiled with Inno Setup on a GitHub Actions Windows runner.

## Data

The app keeps EPUB and PDF copies, bookmarks, and progress locally. It does not upload books. Removing the app does not automatically remove its library folder.

## License

An open-source license has not been chosen yet. Standard copyright applies until a license is added; ask the author before redistributing the code.

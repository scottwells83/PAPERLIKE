# PaperLike

PaperLike is a free, local-first EPUB reader for Windows and macOS. It aims to make reading feel calm and comfortable, with warm page colors and a few simple controls.

## What works

- Import EPUBs into a library managed by the app. The selected original file stays in place.
- Browse chapter headings and jump directly to a chapter.
- Reopen a book at its saved chapter and scroll position.
- Choose Paper, Ivory, or Night colors and adjust text size. PaperLike remembers these preferences.
- Keep books and progress on this computer; no account, cloud service, storefront, or in-app purchase.

This is an early prototype. It currently extracts chapter text, so book images and rich inline EPUB styling are not shown. Bookmarks, search, PDF, and syncing are not included.

## Install

- **macOS:** Download the disk image for Apple Silicon or Intel, open it, and run `Install PaperLike.command`. This installs `~/Applications/PaperLike.app`; the library lives at `~/Library/Application Support/PaperLike/Books`.
- **Windows:** The included workflow builds `PaperLike-Setup-x64.exe` for GitHub Releases. Until a release is published, the portable x64 preview can be downloaded as a ZIP; extract the whole folder and run `PaperLike.exe`.

The current Mac packages are ad-hoc signed and not notarized, so macOS may show a security warning. Broad distribution needs Apple Developer ID signing and notarization.

## Build

Requires the .NET 10 SDK. Run `dotnet run` from this directory. The `scripts/package-macos.sh` script creates the `.app` bundle and `.dmg` for `osx-arm64` or `osx-x64`. The Windows installer is compiled with Inno Setup on a GitHub Actions Windows runner.

## Data

The app keeps EPUB copies and progress locally. It does not upload books. Removing the app does not automatically remove its library folder.

## License

An open-source license has not been chosen yet. Standard copyright applies until a license is added; ask the author before redistributing the code.

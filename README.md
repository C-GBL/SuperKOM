# SuperKOM

A simple drag-and-drop tool for extracting and repacking KOG `.kom` archive files.

## Usage

**Extract** - Drag a `.kom` file onto the window. Contents are extracted to a folder with the same name next to the file.

**Repack** - Drag a folder onto the window. It is repacked into a `.kom` file with the same name, overwriting the original if it exists.

## Format Support

| Format | Extract | Repack |
|--------|---------|--------|
| V.0.3 (current, encrypted) | ✓ | ✓ |
| V.0.1 / V.0.2 (legacy) | ✓ | — |

## Details

- Files with `Algorithm=1` are Blowfish (ECB) decrypted then zlib decompressed on extraction, and the reverse on repack
- Files with `Algorithm=0` are zlib only
- Encryption key: `gpfrpdlxm`

## Requirements

- Windows
- [.NET Framework 4.7.2](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net472)

## Build

Open `KOM_DUMP_MARCH.csproj` in Visual Studio 2019+ and build, or:

```
msbuild KOM_DUMP_MARCH.csproj /p:Configuration=Release
```

# M8 LOSAT

M8 vehicle conversion for **Gunner, HEAT, PC!** Replaces stock M2 Bradley instances.

## Install

Download the complete ZIP from [Releases](https://github.com/AIM928/M8-LOSAT/releases/latest). Exit the game, back up old M8 files, and copy `M8LOSATM2.dll` and `M8LOSATAssets` directly into the game's `Bin/Mods` folder.

## Controls

- `E`: auto-lock targets
- `Backspace`: clear all locks
- Middle mouse: lock a single target
- Tube status: `R` ready, `E` empty, `X` damaged; `F` indicates sight failure.

## Source and build

`src/` contains all 97 C# files compiled into v0.1.69. The original installed DLL and release ZIP are unchanged. This branch includes the source; the existing v0.1.69 tag still points to its original release commit.

Use PowerShell 7 and .NET SDK 7, with the reference GHPC `0.1.0-alpha+20261014` installation and MelonLoader `0.7.3` (`net35`). Unzip the v0.1.69 package separately for model/texture/audio inputs. Proprietary game assemblies are read locally, never distributed here.

```powershell
./build.ps1 -GameRoot 'D:/Games/GHPC/Bin' -ReleaseRoot 'D:/BuildInputs/M8-LOSAT-v0.1.69' -OutputRoot 'D:/Builds/M8-custom' -CscPath 'C:/Program Files/dotnet/sdk/7.0.410/Roslyn/bincore/csc.dll'
```

Use a new output folder. The build does not install anything. Resource data is extracted from the official v0.1.69 mod DLL without executing it. Other game versions are unverified. Compiler/environment changes can affect output hashes.

Original v0.1.69 DLL SHA256: `FA743A7E116BC8D814AFE7FD1DFECD93631E4C617FB03B2B1BA5F93929B2547F`.

## License

Project-authored source code and build tooling are MIT licensed; see [LICENSE](LICENSE). This does not license third-party models, textures, recordings, game code or dependencies. No such rights are granted by this repository. Assets remain separate release inputs; the user-supplied launch recording's provenance and redistribution license have not been independently verified.

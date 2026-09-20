# 🪄Spell Breaker
<p align="center">
	<a href="https://i.postimg.cc/J4nmhJ5z/Spell-Breaker-Main.png"><img width="400" src="https://i.postimg.cc/J4nmhJ5z/Spell-Breaker-Main.png" alt="Spell-Breaker-Main"></a>
	<a href="https://i.postimg.cc/qMR07nxg/Spell-Breaker-Options.png"><img width="400" src="https://i.postimg.cc/qMR07nxg/Spell-Breaker-Options.png" alt="Spell-Breaker-Options"></a>
	<a href="https://i.postimg.cc/VL61NMWd/Spell-Breaker-Modify.png"><img width="400" src="https://i.postimg.cc/VL61NMWd/Spell-Breaker-Modify.png" alt="Spell-Breaker-Modify"></a>
	<a href="https://i.postimg.cc/KvzFYLD1/Spell-Breaker-Modified.png"><img width="400" src="https://i.postimg.cc/KvzFYLD1/Spell-Breaker-Modified.png" alt="Spell-Breaker-Modified"></a>
	<a href="https://i.postimg.cc/c4HZJYBC/Spell-Breaker-Restored.png"><img width="400" src="https://i.postimg.cc/c4HZJYBC/Spell-Breaker-Restored.png" alt="Spell-Breaker-Restored"></a>
</p>

<p align="center">
An educational programming exercise: a modern C# / WPF reimplementation inspired by
the legacy batch/HTA tool [brunolee-GIT/W3M0dP4tch32](https://github.com/brunolee-GIT/W3M0dP4tch32).
It demonstrates how procedural script automation and binary-modification concepts can be
ported into a compiled, object-oriented desktop application.
</p>

## ✨ Functionality

Spell Breaker is a desktop utility for inspecting and modifying the package files of
a locally installed, Electron-based application.

- **Automatic install detection** — resolves the target client's folder via saved
  settings → `%LOCALAPPDATA%` → the uninstall registry key → common store install
  paths → a folder-picker fallback.
- **Single-window WPF UI** — Launcher → Selector → progress pages with BACK
  navigation; light/dark/auto themes; embedded localization in 8 languages
  (auto-detected from the system language, overridable in Options).
	<br>
	Languages:
		&nbsp;<i title="Deutsch">🇩🇪</i>
		&nbsp;<i title="English">🇬🇧</i>
		&nbsp;<i title="Español">🇪🇸</i>
		&nbsp;<i title="Français">🇫🇷</i>
		&nbsp;<i title="Português">🇵🇹</i>
		&nbsp;<i title="Русский">🇷🇺</i>
		&nbsp;<i title="Türkçe">🇹🇷</i>
		&nbsp;<i title="中文（简体）">🇨🇳</i>
	<br><br>
- **Version list** — enumerates every installed application version (newest first),
  asynchronously scans each one, and displays its per-version modification status as
  colored tags with a Restore action.
- **Pluggable modification methods** — two bundled JavaScript logic variants plus
  an Adaptive regex-driven fallback that locates response-decode anchors tolerantly.
- **Optional extras** — resource cleanup, additional bundle modification, a custom
  display name, and configuration toggles for update checks and developer tools.
- **Modification status & restore** — per-version detection via `ModInspector` and
  `resources\sb_meta.json` metadata; Restore uses `.bak` backups when present or
  synthetically reverses the applied modifications without backups.
- **Native implementation** — ASAR unpack/repack and byte-level executable modification
  written in C# with no external tools; configurable outcome sounds (embedded MP3s
  or user-selected audio).
- **CLI flags** — `-csht` (create desktop shortcut).

## 🛠️ How to Build

To minimize security risks and ensure complete transparency of the execution logic,
**pre-compiled binaries (.exe) are not provided**. Compile the source yourself:

1. Clone this repository locally.
2. Install the **.NET 8 SDK** and open `SpellBreaker.slnx` in **Visual Studio 2022
   (17.12 or later)** — or use the `dotnet` CLI directly.
3. Build with the `Release` configuration:

   ```
   dotnet build SpellBreaker\SpellBreaker.csproj -c Release
   ```

A folder publish profile is included for optional standalone packaging.

Requirements: Windows 10/11. The app requests administrator rights at runtime
(`requireAdministrator` manifest) because it modifies files under protected
install locations.

## 🔬 Educational Purpose

The codebase in this repository functions as an academic and reverse-engineering
case study. It is designed to demonstrate:

- **Package Architecture Analysis:** Modifying, extracting, and rewriting local
  Electron-based application containers (ASAR manipulation) completely on-disk
  without active runtime memory manipulation.
- **Workflow Migration:** Porting legacy, procedural batch and script-based
  workflows into a compiled, object-oriented desktop application framework.
- **WPF UI Design Patterns:** Implementing robust page navigation, asynchronous
  task scanning with real-time UI reporting, and reactive globalization pipelines.

## 📜 Credits

- Inspired by [brunolee-GIT/W3M0dP4tch32](https://github.com/brunolee-GIT/W3M0dP4tch32)
  — the original batch/HTA implementation this project reimagines in C#.
- The bundled modification logic is based on the **Sak32009** technique.
- All C# code, UI, and localization in this repository are original work.

This project is licensed under the **MIT License** — see [LICENSE](LICENSE).

## ⚖️ Disclaimer

This software is provided "as is" without warranty of any kind. The authors and
contributors assume no liability for how this code is used, nor for any
modifications made to third-party software on your local machine. Use at your own
discretion in a safe, isolated testing environment.

This project is **not affiliated with or endorsed by** the target application or its
developers. Modifying third-party software may violate its terms of service — you
are responsible for ensuring your use complies with the target software's license
and applicable law.

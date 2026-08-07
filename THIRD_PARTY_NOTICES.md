# Third-Party Notices

This document summarizes the third-party software and content distributed with or accessed by SATLI (Steam Achievement Translation & Localization Integrator). Each project retains its own copyright and license terms.

## Desktop application

### Framework and libraries

The Windows desktop interface uses the following dependencies:

- Microsoft Windows App SDK — MIT License
- Microsoft Windows SDK Build Tools — MIT License
- CommunityToolkit.Mvvm — MIT License
- Markdig — BSD 2-Clause License; used to render GitHub Release Markdown

Test-only dependencies include the MIT-licensed xUnit.net runner and Microsoft.NET.Test.Sdk.

### Distribution packaging

The independently distributed Windows installer is built with Inno Setup and includes the official Simplified Chinese message file from the Inno Setup source repository. Inno Setup and its message files remain subject to the Inno Setup license and their respective copyright notices.

### Embedded Python runtime and tools

Windows release packages include the official CPython 3.13 embeddable runtime. Python is distributed under the Python Software Foundation License; the complete runtime `LICENSE.txt` is included beside its binaries in `_satl_runtime`.

The embedded Python command-line payload includes:

- Dulwich — dual-licensed under Apache-2.0 or GPL-2.0-or-later; used for local Git repository access
- urllib3 — MIT License; used by Dulwich

SATLI uses Dulwich's pure-Python implementation and does not require Git for Windows or additional native libraries.

## Translation data and game content

SATLI downloads community-maintained achievement schema files from [GaBoron/steam-achievement-translation-library](https://github.com/GaBoron/steam-achievement-translation-library). That repository has its own mixed-rights notice.

Original game content, achievement text, Steam schema content, names, and trademarks remain the property of their respective rights holders.

## Acknowledgements

Thanks to GitHub user [KneeArcher](https://github.com/KneeArcher) for sharing a prototype in translation-library Issue #94 and for helping define the desired workflow. The contributor explicitly licensed the source ZIP attached to that issue under the MIT License in [this comment](https://github.com/GaBoron/steam-achievement-translation-library/issues/94#issuecomment-4935293487). The initial SATLI implementation remains an independent clean-room implementation and does not copy the prototype's source.

The separate MIT-licensed [PanVena/SteamAchievementLocalizer](https://github.com/PanVena/SteamAchievementLocalizer) project edits and authors Steam achievement localizations. SATLI integrates translation discovery, import, editing, installation, restoration, and management in one Windows application.

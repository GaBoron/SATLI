# Third-Party Notices

This document summarizes the third-party software and content distributed with or accessed by SATLI. Each project retains its own copyright and license terms.

## Desktop application

### Framework and libraries

The Windows desktop interface uses the following dependencies:

- Microsoft Windows App SDK — MIT License
- Microsoft Windows SDK Build Tools — MIT License
- CommunityToolkit.Mvvm — MIT License
- Markdig — BSD 2-Clause License; used to render GitHub Release Markdown

### Distribution packaging

The independently distributed Windows installer is built with Inno Setup and includes the official Simplified Chinese message file from the Inno Setup source repository. Inno Setup and its message files remain subject to the Inno Setup license and their respective copyright notices.

### Command-line and core runtime

SATLI's command-line interface and shared business logic are implemented with
.NET and distributed as a self-contained C# executable. No Python runtime,
Dulwich, urllib3, or Git installation is included or required.

## Translation data and game content

SATLI downloads community-maintained achievement schema files from [GaBoron/steam-achievement-translation-library](https://github.com/GaBoron/steam-achievement-translation-library). That repository has its own mixed-rights notice.

Original game content, achievement text, Steam schema content, names, and trademarks remain the property of their respective rights holders.

## Acknowledgements

Thanks to GitHub user [KneeArcher](https://github.com/KneeArcher) for sharing a prototype in translation-library Issue #94 and for helping define the desired workflow. The contributor explicitly licensed the source ZIP attached to that issue under the MIT License in [this comment](https://github.com/GaBoron/steam-achievement-translation-library/issues/94#issuecomment-4935293487). The initial SATLI implementation remains an independent clean-room implementation and does not copy the prototype's source.

The separate MIT-licensed [PanVena/SteamAchievementLocalizer](https://github.com/PanVena/SteamAchievementLocalizer) project edits and authors Steam achievement localizations. SATLI integrates translation discovery, import, editing, installation, restoration, and management in one Windows application.

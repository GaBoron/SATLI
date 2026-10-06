# SATLI Achievement Display Bridge

Current bundled plugin version: `0.2.1`.

This Millennium plugin reads SATLI's static achievement bridge from
`<Steam>\millennium\config\satli-bridge-v1.json`. SATLI does not need to stay
running. Keeping the bridge inside Millennium's real install directory also
avoids Microsoft Store AppData virtualization.

## Choosing a plugin

This bundled plugin displays translations prepared by the SATLI desktop client.
[SATLI lite](https://github.com/GaBoron/SATLI-lite) is a separate Millennium plugin
that downloads community translations and manages variants, languages, and local
edits directly inside Steam, without requiring the desktop client or BIN writes.

Enable only one of these display plugins at a time. They keep separate settings
and translation data; switching plugins does not synchronize local edits or
restore BIN files installed by SATLI. See the [SATLI lite installation guide](https://github.com/GaBoron/SATLI-lite#安装)
for setup, or [SATLI usage](../../../docs/USAGE.md#与-satli-lite-的区别)
for switching between them.

The Steam main frontend first overrides structured achievement responses by App
ID and achievement API name, covering the library data path without relying on
display-string identity. This includes both Steam's cached `achievements`
groups and its `achievementmap`, which is used to restore library activity
cards, at both the native and already-loaded Steam library cache boundaries.
The same data-shape translator handles array and API-keyed maps, including
achievement notifications, and recognizes the field aliases used by Steam's
native and Web API models. A DOM observer then covers activity
surfaces, overlays, and frontend-rendered notifications that expose only rendered
text, with a bounded periodic reconciliation for renderer updates that bypass
observable DOM mutations. The frontend keeps watching for Steam's lazily-created
library cache instead of requiring it to exist at plugin startup, and the plugin
status counts both structured achievement replacements and rendered text. The
WebKit preload uses the same DOM engine for Store
and Community WebViews. Exact text and accessibility attributes are replaced;
ambiguous source strings are deliberately skipped.

The bridge includes source text from both the installed translation and SATLI's
verified pre-install backup. This lets a translated field replace an English
fallback as well as an older same-language value supplied by Steam.

## Coverage and verification

| Surface | Injection path | Intended coverage | Current evidence |
| --- | --- | --- | --- |
| Library game page and achievement sidebar | Structured SteamClient response and both app-details cache fields, then main-frontend fallback | Names, descriptions, `aria-label`, and `title` values | App/API-keyed response and cache transforms packaged; live Steam acceptance pending |
| Steam activity feed rendered in the client | Cached `achievementmap`, Millennium main frontend, or WebKit preload | Names and descriptions in existing and dynamically added cards | App/API-keyed cache transform, periodic reconciliation, and both entry points packaged; live Steam acceptance pending |
| Achievement popup/toast | Steam notification-record hook, then structured game-session notification override | Names and descriptions selected by App ID and achievement API name before React renders the toast | Current Steam notification record matched and translated with a real bridge sample; live unlock acceptance pending |
| In-game overlay | Millennium main frontend or WebKit preload | Achievement text rendered in Chromium documents | Entry points packaged; overlay target attachment requires live acceptance |
| Store and Community achievement pages | Millennium WebKit preload | Names and descriptions in embedded web pages | Document-start-safe entry point packaged; the current host has logged intermittent dynamic-module load failures, so live coverage remains unconfirmed |

“Intended coverage” means the surface is handled by an injection path and the
same exact-text engine; it is not a claim of runtime completion. Before release,
test every row online with a game whose server schema differs from SATLI's local
translation, then repeat after a Steam restart and after changing pages.

This is an experimental compatibility layer. It does not intercept Steam
network traffic, change achievement state, or patch Steam binaries. Steam UI
updates may require selector or lifecycle adjustments.

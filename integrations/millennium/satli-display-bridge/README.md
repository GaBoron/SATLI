# SATLI Achievement Display Bridge

This Millennium plugin reads SATLI's static achievement bridge from
`<Steam>\millennium\config\satli-bridge-v1.json`. SATLI does not need to stay
running. Keeping the bridge inside Millennium's real install directory also
avoids Microsoft Store AppData virtualization.

The Steam main frontend first overrides structured achievement responses by App
ID and achievement API name, covering the library data path without relying on
display-string identity. A DOM observer then covers activity surfaces, overlays,
and frontend-rendered notifications that expose only rendered text. The WebKit
preload uses the same DOM engine for Store and Community WebViews. Exact text and
accessibility attributes are replaced; ambiguous source strings are deliberately
skipped.

The bridge includes source text from both the installed translation and SATLI's
verified pre-install backup. This lets a translated field replace an English
fallback as well as an older same-language value supplied by Steam.

## Coverage and verification

| Surface | Injection path | Intended coverage | Current evidence |
| --- | --- | --- | --- |
| Library game page and achievement sidebar | Structured SteamClient response override, then main-frontend fallback | Names, descriptions, `aria-label`, and `title` values | App/API-keyed response transform and fallback packaged; live Steam acceptance pending |
| Steam activity feed rendered in the client | Millennium main frontend or WebKit preload | Names and descriptions in existing and dynamically added cards | Both entry points packaged; live Steam acceptance pending |
| Achievement popup/toast | Millennium main frontend | Text already present when the popup is created and later DOM mutations | Mutation observer packaged; separate popup-window behavior requires live acceptance |
| In-game overlay | Millennium main frontend or WebKit preload | Achievement text rendered in Chromium documents | Entry points packaged; overlay target attachment requires live acceptance |
| Store and Community achievement pages | Millennium WebKit preload | Names and descriptions in embedded web pages | WebKit entry point packaged; live Steam acceptance pending |

“Intended coverage” means the surface is handled by an injection path and the
same exact-text engine; it is not a claim of runtime completion. Before release,
test every row online with a game whose server schema differs from SATLI's local
translation, then repeat after a Steam restart and after changing pages.

This is an experimental compatibility layer. It does not intercept Steam
network traffic, change achievement state, or patch Steam binaries. Steam UI
updates may require selector or lifecycle adjustments.

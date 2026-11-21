# Frontend

The PWA hosts these Blazor components. `wwwroot/css/tavern.css` owns the responsive
table, lobby, and dialog theme; `sk.css` only maps the original card artwork.
The hand fits its width with a ResizeObserver; approximately its lower half is
cropped at the table edge to keep ranks and suits large. Short and small phone
screens use an overlapping row of trick cards; other layouts use an overlapping
four-seat arrangement. Trick layers follow play order, including after reconnect
and in the last-trick report: the latest card is always on top.

Bootstrap **5.3.9** is vendored locally (CSS, bundled JavaScript, and source maps)
from `https://cdn.jsdelivr.net/npm/bootstrap@5.3.9/dist/`. Update all four files
together. The PWA references this copy and caches its assets, SVG cards, and fonts.
No Node build pipeline or runtime CDN is required.

## Card playback

`GameHubClient.OnStateChanged` supplies a detached server snapshot and a resync flag.
`TableComponent` serializes receipt and measures the old hand before rendering a
new one. `TrickPlayback` deduplicates cards within a round and creates ordered play
and collection steps; collection winners come from completed trick ownership,
never the later active player. `TrickComponent` awaits JavaScript animations and
gates the result report until playback completes.

`skTable` owns pointer capture, animation-frame drag updates, temporary flight
elements, and cancellation. Local plays retain their release geometry until the
server accepts or rejects the move. Opponent hands show one miniature back per
remaining card, including the top seat. Their flights always start at the fixed
center of that hand, never an individual card position or sorting-dependent index.
Reconnects, round changes, viewport resizing, and component disposal discard
obsolete animation work. Reduced-motion mode keeps the same state progression
without flights or collection pauses.

Touch and pen input select from stable fan lanes: slide sideways to lift and
highlight a card, pull it upward until “Loslassen zum Spielen” appears, then
release to play. Returning down before release disarms the gesture. A simple tap
only previews; the 48px “Spielen” button is an alternative to swiping upward.
Illegal cards can be inspected but cannot be submitted. Pointer cancellation,
resizing and resync clear selection; mouse click/drag and keyboard play remain.

Round numbers are projected from the session ledger, including all-pass deals.
They are independent of `Turn`, which counts completed tricks.

## Validation

Run `dotnet test src/sk.tests/sk.tests.csproj` from the repository root, then build
and publish `src/sk.pwa/sk.pwa.csproj`. Start the API and PWA as described in the
root README for browser checks. `FrontendStateTests` covers round projection,
completed-player identities, duplicate and stale snapshots, reset/reconnect, and
collection ordering across trick boundaries.

Check 320×568, 390×844, 568×320, 844×390, 768×1024, 1024×768, 1920×1080,
and 3440×1440. Verify eight accessible hand cards, touch scrubbing, upward-release
play, cancellation, mouse drag and keyboard play, last-trick
review, all result dialogs, reduced motion, reconnect, and successive deals.

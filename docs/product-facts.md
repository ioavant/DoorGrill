# Door Grill — product facts

Plain-text source of truth for **all** product copy: the website, the Autodesk
App Store listing, and anything else. No HTML, no store-specific formatting —
each consumer styles this its own way.

- The App Store submission is `appstore-listing.md`, filled in from this file.
  When a fact changes, change it **here first**, then re-derive that one.
- Technical facts here are read from the code, not written from memory:
  `version.json`, `branding.props`, `App.cs`, `GrillSettings.cs`,
  `GrillPlacer.cs`, `Installer/Product.wxs`.

---

## Identity

- **Name:** Door Grill
- **Publisher:** Vixeldorf
- **Category:** Revit add-in — MEP / HVAC modelling productivity
- **Current version:** 1.0.0
- **Platform:** Autodesk Revit 2022, 2023, 2024, 2025, 2026 (Windows)
- **Interface language:** English
- **Website:** https://www.vixeldorf.com
- **Support:** yoav@vixeldorf.com, answered within 5 business days
- **Source:** https://github.com/ioavant/DoorGrill

## One-liner

Overflow grilles above doors, taken from the linked architectural model — and
kept in sync with it.

## Short description (one sentence)

Door Grill places overflow grilles above doors from a linked architectural
model, and keeps them in sync when the architect moves a door or changes a wall.

## What it does

Pick a door in the linked architectural model, and the grille appears above it
in your own model: at the height you set, on the wall face, facing away from the
side the door opens to. The architect's file is never touched, and no grille is
positioned by hand.

The real value is what happens on the next revision. Every grille remembers
which linked door it belongs to. Run Update All after the link is reissued, and
each grille follows its own door — doors that moved get their grilles moved, new
doors get grilles, and doors that disappeared are reported with their grilles
selected for review. Nothing is deleted automatically.

## Feature bullets

- Place a grille above any door picked in a linked model; keep picking, one door
  after another, until you press Esc.
- Picking a door that already carries a grille re-syncs that grille instead of
  stacking a second one on top of it.
- Update All: pick one sample door and get grilles above every door of the same
  family and type on that level of the link.
- The same command re-syncs every grille already in the model with its own door,
  so it doubles as "bring my model back in line with the architect".
- Idempotent: running it twice in a row changes nothing.
- Your own grille family — any Air Terminal family loaded in the project.
- Mounting height set in the project's own units, measured as clear distance
  from the door head to the bottom of the grille.
- Orientation follows the door's opening direction automatically, including
  doors the architect has flipped since the last issue; one option reverses it.
- Grille depth follows the host wall's thickness when the family exposes a
  "Wall thickness" instance parameter.
- Grilles deleted on purpose can stay deleted, with one option.
- Every run ends with a report: created, repositioned, unchanged, skipped, and
  any door no longer present in the link.
- One undo step per run.

## How it works (the mechanic worth explaining once)

Three pieces of bookkeeping, all invisible and all inside the model:

1. Each placed grille is stamped with the unique id of its source door and of
   the link it came from.
2. The project keeps a list of every door that has ever been served. That is
   what tells "this door never had a grille" apart from "someone deleted this
   grille on purpose".
3. Position is always recomputed from the door, never stored: door centre in
   plan, door head plus the mounting height in elevation, offset to the wall
   face along the direction the grille faces.

Orientation and the offset to the wall face are derived from one vector, so
reversing the orientation mirrors the mounting side with it — a 180° rotation
about the wall's centre axis, not a grille left on the wrong face.

## Who it is for

HVAC and MEP engineers who model air transfer between rooms and receive doors as
a linked architectural model — where doors keep moving right up to issue day and
the grilles have to keep up.

## Requirements

- Autodesk Revit 2022–2026.
- An architectural model linked into the MEP project.
- An Air Terminal family for the grille, loaded into the project.

## Options reference

| Option | Default | Effect |
|---|---|---|
| Grille family | not set — asked for on first run | Which Air Terminal family is placed |
| Mounting height above door head | 100 mm | Clear distance from the door head to the bottom of the grille, entered in project units |
| Orientation | away from the opening direction | Reversed flips the grille about the wall centre axis, changing the mounting face with it |
| Do not place grilles where they were deleted manually | off | Off: Update All restores every missing grille. On: a door whose grille was deleted stays empty, and only Place Grille can put one back |

Options are stored in a settings file in the add-in's installation folder, so
they are set once for every Revit version and every project on the machine.

## Where data lives

- Grille-to-door links: on the grille, in Revit Extensible Storage.
- List of doors already served: on Project Information, same mechanism.
- User options: a settings file next to the add-in library.

No external database, no cloud service, and no network access for the add-in's
own work. Model data travels with the file, survives Save As and worksharing,
is shared with the whole team, stays out of schedules, and adds no shared
parameters to the project template. The only network request the add-in ever
makes is the optional check for a newer published version.

## What it never does

- Never writes to the linked architectural model.
- Never deletes an element. Grilles whose door left the link are reported and
  selected, not removed.
- Never installs an update by itself: a notice appears, the download is yours to
  start.

## Install / uninstall

Windows Installer package (MSI), not Autodesk App Manager packaging. The library
installs to `C:\ProgramData\Vixeldorf\DoorGrill\`, and one manifest per selected
Revit version to `C:\ProgramData\Autodesk\Revit\Addins\<year>\`. The installer
asks which Revit versions to register. Administrator rights are required.
Uninstall through Windows Settings > Apps; grilles already placed are ordinary
Revit air terminals and stay untouched.

## Known limitations

- "Wall thickness" is only matched when the family exposes it per instance. A
  type parameter is deliberately left alone, because writing it would change
  every other grille of that type.
- Doors hosted in something other than a plain wall (a curtain wall, for
  instance) fall back to the door's own geometry for the offset to the wall
  face, which is less exact.
- Copying a grille into another project loses its link to the source door; such
  grilles are ignored by Update All.
- Update All places new grilles only for doors of the sample door's family and
  type; other door types need their own run.
- Revit 2027 is not supported yet — it needs a separate .NET 10 build.

## Version history

### 1.0.0

First release.

- Place grilles above doors picked in a linked architectural model, positioned
  from the door head by a mounting height set in project units.
- Update All re-synchronises every grille with its own linked door after an
  architectural revision: moved doors, new doors, changed wall thickness, and
  doors flipped by the architect.
- Grilles oriented away from the door's opening side, with an option to reverse.
- Options for grille family, mounting height, orientation and how manually
  deleted grilles are treated, shared across Revit versions and projects.

# Door Grill — Autodesk App Store listing copy (v1.0.0)

Shared source doc for the store listing and the website. Facts pulled from
`version.json`, `App.cs`, `branding.props`, `Installer/Product.wxs` and the
plugin's project memory. Character counts checked against Autodesk's limits.

---

## App Name  (limit 50)

```
Door Grill
```

## App Short Description  (limit 200)

```
Places overflow grilles above doors from a linked architectural model, and keeps them in sync when the architect moves a door or changes a wall.
```

## App Description  (limit 4000)

```
Door Grill puts overflow (transfer) grilles above doors for you, using the architectural model you already link into your MEP project.

Pick a door in the linked model and the grille appears above it in YOUR model, at the right height, on the right face of the wall, facing away from the side the door opens to. You never touch the architect's file, and you never place a grille by hand.

The point of Door Grill is what happens on the next architectural revision. Every grille it places remembers which linked door it belongs to. Run Update All after the architect reissues the link and each grille follows its own door: doors that moved get their grilles moved, new doors get grilles, and doors that disappeared are reported and their grilles selected in the model so you can decide what to do. Nothing is ever deleted behind your back.

WHAT IT DOES

- Place Grille — pick doors in the linked model one after another; a grille goes above each one. Picking a door that already has a grille re-syncs that grille instead of stacking a second one on top.
- Update All — pick one sample door: grilles are placed above every door of the same family and type on that level of the link, and every grille already in the model is brought back onto its own door. Running it twice changes nothing, so it is safe to use as a routine "bring my model in line with the architect" step.
- Your own grille family — pick any Air Terminal family loaded in the project; Door Grill asks you to choose it the first time you run a command, then remembers it for every project and every Revit version on the machine.
- Height from the door head is yours to set, in your project's own units — the default is 100 mm of clear space between the door head and the bottom of the grille.
- Orientation follows the door's opening direction automatically, including doors the architect has flipped since the last issue. One option reverses it for families built the other way round.
- The grille's depth is matched to the thickness of the host wall, when the family exposes a "Wall thickness" parameter.
- Grilles you deleted on purpose can be left deleted: an option tells Update All to leave those doors empty instead of restoring them.
- Every run ends with a plain report: created, repositioned, unchanged, skipped, and any door that is no longer in the link.

WHO IT IS FOR

HVAC and MEP engineers who model air transfer between rooms and receive doors as a linked architectural model — where the doors keep moving right up to issue, and the grilles have to keep up.

REQUIREMENTS

- Autodesk Revit 2022, 2023, 2024, 2025 or 2026.
- An architectural model linked into the project, and your own Air Terminal family for the grille loaded into the project.
- Areas and Volumes enabled is not required; Door Grill places air terminals, not spaces.
```

## App Version

**Version Number**

```
1.0.0
```

**Version Description**

```
First public release.

- Place grilles above doors picked in a linked architectural model, positioned from the door head with a mounting height you set in project units.
- Update All re-synchronises every grille with its own linked door after an architectural revision: moved doors, new doors, changed wall thickness, and doors flipped by the architect.
- Grilles are oriented away from the door's opening side, with an option to reverse it.
- Options dialog for the grille family, mounting height, orientation and how manually deleted grilles are treated; the options are stored with the add-in, so they are shared by every Revit version and every project on the machine.
```

## General Usage Instructions

```
SETUP

1. Link the architectural model into your MEP project as usual.
2. Load your overflow grille family (an Air Terminal family) into the project.

PLACING GRILLES

3. Go to the Vixeldorf ribbon tab, Door Grill panel. It holds three buttons: Place Grille, Update All and Options.
4. Click Options and choose your grille family from the Air Terminal families loaded in the project. Set the mounting height above the door head (in your project's units; 100 mm by default), choose the grille orientation, and decide whether Update All may restore grilles you delete by hand. The options are saved once for every Revit version and every project — the first time you run a command without a family chosen, this dialog opens by itself.
5. Click Place Grille and pick a door in the linked model. A grille is placed above it. Keep picking doors; press Esc when you are done.
6. Or click Update All and pick one sample door. Grilles are placed above every door of the same family and type on that level of the link. A report tells you how many were created, repositioned and skipped.

AFTER AN ARCHITECTURAL REVISION

7. Reload the architectural link.
8. Click Update All and pick a sample door again. Grilles whose doors moved are repositioned, doors that gained a grille requirement get one, and grilles whose door no longer exists in the link are counted in the report and left selected in the model for you to review.
9. Repeat as often as you like — a run that finds nothing to change reports "Unchanged" and modifies nothing.

NOTES

- Picking a door that already carries a grille never creates a duplicate; it re-syncs the existing one.
- Every change happens in one undoable transaction, and nothing is ever deleted automatically.
```

## Installation / Uninstallation  (limit 1000)

```
INSTALLATION

Door Grill ships as a Windows Installer package (MSI), not via Autodesk's App Manager. Run DoorGrill_Setup_Vixeldorf.msi and accept the licence. The installer asks which Revit versions to register for — clear any you do not want.

The add-in library is installed to:
C:\ProgramData\Vixeldorf\DoorGrill\

A manifest is registered per selected Revit version in:
C:\ProgramData\Autodesk\Revit\Addins\<year>\Vixeldorf_DoorGrill_<yy>.addin

Administrator rights are required: the installer writes to ProgramData for all users. Your options are later saved in the same folder and need no elevation. Restart Revit after installing.

UNINSTALLATION

Windows Settings > Apps (or Control Panel > Programs and Features), select "Vixeldorf Door Grill", Uninstall. This removes the library and every Revit manifest it registered. Grilles already placed in your models are ordinary Revit air terminals and stay untouched.
```

## Support Information  (limit 1000)

```
<b>Email:</b> <a href="mailto:yoav@vixeldorf.com">yoav@vixeldorf.com</a><br><br>
We answer within 5 business days.<br><br>
To help us reproduce a problem quickly, please include:<br>
- your Revit version and build,<br>
- the Door Grill version (shown in the title bar of the Options dialog),<br>
- what you picked and what happened, plus the text of any message Door Grill showed,<br>
- a screenshot of the situation in the model, and the grille family you are using if the problem is about position or orientation.<br><br>
Feature requests are welcome at the same address.
```

## Additional Information  (limit 2000)

```
<b>Supported Revit versions:</b> 2022, 2023, 2024, 2025, 2026.<br>
<b>Category:</b> MEP / HVAC modelling productivity.<br>
<b>Language:</b> English.<br><br>

<b>How your data is stored</b><br>
Everything Door Grill remembers about your model lives inside the model itself — no external database, no cloud service, no network connection for the add-in's own work. The link between a grille and its door is stored on the grille using Revit's Extensible Storage, and the list of doors already served is stored on Project Information. That data travels with the model, survives Save As and worksharing, and is shared with the whole team automatically; it is invisible in schedules and adds no shared parameters to your project template.<br><br>
Your preferences — grille family, mounting height, orientation, deletion policy — are kept in a small settings file in the add-in's own installation folder, so they apply to every Revit version and every project on the machine and only have to be set once.<br><br>

<b>What Door Grill never does</b><br>
It never writes to the linked architectural model, and it never deletes an element. Grilles whose door has disappeared from the link are reported and selected for you to review, never removed. Every command runs in a single transaction, so one Undo reverses a whole run.<br><br>

<b>Publisher:</b> Vixeldorf — <a href="https://www.vixeldorf.com">www.vixeldorf.com</a>
```

## Known Issues  (limit 1000)

```
- The "Wall thickness" match only applies when the family exposes that parameter per instance. If it is a type parameter it is skipped, because writing it would change every other grille of that type.<br>
- Doors hosted in something other than a plain wall (for example a curtain wall) fall back to the door's own geometry for the offset to the wall face, which can be less exact.<br>
- Copying a grille into another project loses its link to the source door; such grilles are ignored by Update All.<br>
- Revit 2027 is not supported yet: it requires a separate .NET 10 build.
```

## Learn More Url

```
https://www.vixeldorf.com
```

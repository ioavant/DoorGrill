using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace DoorGrill
{
    // Mode 1: repeatedly pick a door in a linked model and place a grille above it. Keeps asking for the
    // next door until the user presses Esc. No success message; only real errors are reported.
    //
    // Tracking is always on: every grille is stamped with its source door, and the door is written into
    // the project registry. Picking a door that already carries a grille does NOT add a second one - the
    // existing grille is re-synced to the door's current position instead. This is also the only way to
    // bring back a grille the engineer deleted on purpose, since mode 2 will never re-create it.
    // Load via the Revit Add-in Manager.
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceGrillCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "No active document. Open a project first.";
                return Result.Failed;
            }
            Document doc = uidoc.Document;

            // Options plus the grille family, asking the user to choose one on first use.
            GrillSettings settings;
            FamilySymbol symbol;
            if (!OptionsCommand.TryPrepare(commandData, doc, out settings, out symbol))
                return Result.Cancelled;

            DoorFilter filter = new DoorFilter(doc);
            Dictionary<string, List<FamilyInstance>> byDoor = GrillLink.BuildIndex(doc);
            HashSet<string> served = GrillRegistry.Read(doc);

            // Loop: pick door → place or re-sync grille → repeat until Esc (OperationCanceledException).
            while (true)
            {
                Reference refer;
                try
                {
                    refer = uidoc.Selection.PickObject(
                        ObjectType.LinkedElement,
                        filter,
                        "Pick a door in the linked model (Esc to finish)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break; // user finished
                }

                RevitLinkInstance link = doc.GetElement(refer.ElementId) as RevitLinkInstance;
                FamilyInstance door = LinkHelper.GetLinkedElem(doc, refer) as FamilyInstance;
                if (link == null || door == null || !LinkHelper.IsDoor(door))
                    continue; // shouldn't happen (filter), just skip

                string key = GrillRegistry.MakeKey(link, door);
                List<FamilyInstance> existing;
                bool hasGrille = byDoor.TryGetValue(key, out existing) && existing.Count > 0;

                using (Transaction tx = new Transaction(doc, hasGrille
                           ? "Update grille above door"
                           : "Place grille above door"))
                {
                    tx.Start();
                    try
                    {
                        if (hasGrille)
                        {
                            // Already served: re-sync in place rather than stacking a second grille.
                            // Explicit pick → force, so even an unmoved door snaps its grille back.
                            foreach (FamilyInstance g in existing)
                                GrillPlacer.SyncGrille(doc, g, door, link, settings, force: true);
                        }
                        else
                        {
                            FamilyInstance placed = GrillPlacer.PlaceForDoor(doc, symbol, door, link, settings);
                            if (placed == null)
                            {
                                tx.RollBack();
                                TaskDialog.Show("Door Grill",
                                    "Could not place the grille: the door has no readable geometry, " +
                                    "or the project has no levels.");
                                return Result.Failed;
                            }
                            served.Add(key);
                            GrillRegistry.Write(doc, served);
                            byDoor[key] = new List<FamilyInstance> { placed };
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started)
                            tx.RollBack();
                        TaskDialog.Show("Door Grill", "Placement failed:\n" + ex.Message);
                        return Result.Failed;
                    }
                }
            }

            return Result.Succeeded;
        }
    }

    // Restricts picking to doors that live inside a link.
    internal class DoorFilter : ISelectionFilter
    {
        private readonly Document _hostDoc;
        public DoorFilter(Document hostDoc) { _hostDoc = hostDoc; }

        public bool AllowElement(Element elem) { return true; } // link instances pass through

        public bool AllowReference(Reference reference, XYZ position)
        {
            return LinkHelper.IsDoor(LinkHelper.GetLinkedElem(_hostDoc, reference));
        }
    }
}

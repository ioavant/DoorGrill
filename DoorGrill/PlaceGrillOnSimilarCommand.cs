using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace DoorGrill
{
    // Mode 2 = full update pass. Pick one sample door in a linked model, then:
    //   1. re-sync EVERY grille in the model with its source door (the architect may have moved doors or
    //      changed wall thickness) - this part is not limited to the picked scope, it is a global
    //      "bring the model back in line with AR" step;
    //   2. place grilles above the doors of the picked scope - same family AND type name, same level
    //      (story) in that link, optionally limited to the active view's crop region - but only where no
    //      grille exists yet AND the door was never served before;
    //   3. report doors that disappeared from the link. Nothing is ever deleted.
    //
    // Running it twice in a row is a no-op: doors that already carry a grille are only synced, and doors
    // whose grille the engineer deleted on purpose stay empty (they are in the registry). Such a door can
    // only get a grille back through mode 1.
    // Load via the Revit Add-in Manager.
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceGrillOnSimilarCommand : IExternalCommand
    {
        // ~0.75 m: story separation tolerance used only when a door has no usable Level parameter.
        private const double SameStoryTolFt = 2.5;
        private const double CropTolFt = 0.003;

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

            Reference refer;
            try
            {
                refer = uidoc.Selection.PickObject(
                    ObjectType.LinkedElement,
                    new DoorFilter(doc),
                    "Pick a sample door in the linked model");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            RevitLinkInstance link = doc.GetElement(refer.ElementId) as RevitLinkInstance;
            Document linkDoc = link != null ? link.GetLinkDocument() : null;
            FamilyInstance picked = LinkHelper.GetLinkedElem(doc, refer) as FamilyInstance;
            if (link == null || linkDoc == null || picked == null || !LinkHelper.IsDoor(picked)
                || picked.Symbol == null || picked.Symbol.Family == null)
            {
                message = "The picked element is not a door in a linked model.";
                return Result.Failed;
            }

            Transform t = link.GetTotalTransform();
            View view = doc.ActiveView;

            string famName = picked.Symbol.Family.Name;
            string typeName = picked.Symbol.Name;
            ElementId pickedLevel = DoorLevelId(picked);
            double pickedZ = DoorBaseZLink(picked);

            List<FamilyInstance> doors = new FilteredElementCollector(linkDoc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(d => SameNames(d, famName, typeName))
                .Where(d => SameStory(d, pickedLevel, pickedZ))
                .Where(d => InCropXY(view, t, d))
                .ToList();

            Dictionary<string, List<FamilyInstance>> byDoor = GrillLink.BuildIndex(doc);
            HashSet<string> served = GrillRegistry.Read(doc);

            if (doors.Count == 0 && byDoor.Count == 0)
            {
                TaskDialog.Show("Door Grill",
                    "No \"" + famName + " / " + typeName + "\" doors found on this level, " +
                    "and there are no grilles in the model to update.");
                return Result.Cancelled;
            }

            int created = 0, moved = 0, unchanged = 0, suppressed = 0;
            List<ElementId> lostDoor = new List<ElementId>();
            List<ElementId> lostLink = new List<ElementId>();
            List<ElementId> unloadedLink = new List<ElementId>();
            List<ElementId> failed = new List<ElementId>();

            // Link instances and their documents resolved once per UniqueId.
            Dictionary<string, RevitLinkInstance> linkCache = new Dictionary<string, RevitLinkInstance>();

            using (Transaction tx = new Transaction(doc, "Update grilles above doors"))
            {
                tx.Start();

                // ---- 1. Sync every existing grille with its source door ----
                foreach (List<FamilyInstance> bucket in byDoor.Values)
                {
                    foreach (FamilyInstance grille in bucket)
                    {
                        string doorUid, linkUid;
                        if (!GrillLink.TryRead(grille, out doorUid, out linkUid))
                        {
                            failed.Add(grille.Id);
                            continue;
                        }

                        RevitLinkInstance srcLink;
                        if (!linkCache.TryGetValue(linkUid, out srcLink))
                        {
                            srcLink = doc.GetElement(linkUid) as RevitLinkInstance;
                            linkCache[linkUid] = srcLink;
                        }
                        if (srcLink == null)
                        {
                            lostLink.Add(grille.Id);
                            continue;
                        }

                        Document srcDoc = srcLink.GetLinkDocument();
                        if (srcDoc == null)
                        {
                            unloadedLink.Add(grille.Id); // link unloaded in this session
                            continue;
                        }

                        FamilyInstance srcDoor = srcDoc.GetElement(doorUid) as FamilyInstance;
                        if (srcDoor == null || !LinkHelper.IsDoor(srcDoor))
                        {
                            lostDoor.Add(grille.Id);
                            continue;
                        }

                        try
                        {
                            if (GrillPlacer.SyncGrille(doc, grille, srcDoor, srcLink, settings, force: false)) moved++;
                            else unchanged++;
                        }
                        catch
                        {
                            failed.Add(grille.Id);
                        }
                    }
                }

                // ---- 2. Place grilles on scoped doors that have none and were never served ----
                foreach (FamilyInstance d in doors)
                {
                    string key = GrillRegistry.MakeKey(link, d);
                    List<FamilyInstance> existing;
                    if (byDoor.TryGetValue(key, out existing) && existing.Count > 0)
                        continue; // already handled by the sync pass

                    // This door has been served before and has no grille now, so the engineer deleted
                    // it. Whether that decision sticks is an option: off by default, mode 2 restores
                    // it like any other missing grille.
                    if (settings.KeepDeletedDoorsEmpty && served.Contains(key))
                    {
                        suppressed++;
                        continue;
                    }

                    try
                    {
                        FamilyInstance placed = GrillPlacer.PlaceForDoor(doc, symbol, d, link, settings);
                        if (placed != null)
                        {
                            created++;
                            served.Add(key);
                        }
                    }
                    catch
                    {
                        // A single unplaceable door must not abort the whole run.
                    }
                }

                GrillRegistry.Write(doc, served);
                tx.Commit();
            }

            // Show the problem grilles so the engineer can act on them; nothing was deleted.
            List<ElementId> problems = new List<ElementId>();
            problems.AddRange(lostDoor);
            problems.AddRange(lostLink);
            problems.AddRange(unloadedLink);
            problems.AddRange(failed);
            if (problems.Count > 0)
                uidoc.Selection.SetElementIds(problems);

            string report =
                "Type: " + famName + " / " + typeName + "\n" +
                "Doors in scope: " + doors.Count + "\n\n" +
                "Grilles created: " + created + "\n" +
                "Repositioned: " + moved + "\n" +
                "Unchanged: " + unchanged;
            if (suppressed > 0)
                report += "\nSkipped (grille deleted manually): " + suppressed;
            if (lostDoor.Count > 0)
                report += "\nDoor no longer in the link: " + lostDoor.Count + " (grilles kept)";
            if (lostLink.Count > 0)
                report += "\nLink removed from the project: " + lostLink.Count;
            if (unloadedLink.Count > 0)
                report += "\nLink not loaded: " + unloadedLink.Count + " (load the link and run again)";
            if (failed.Count > 0)
                report += "\nErrors: " + failed.Count;
            if (problems.Count > 0)
                report += "\n\nThe affected grilles have been selected in the model.";

            TaskDialog.Show("Door Grill", report);
            return Result.Succeeded;
        }

        private static bool SameNames(FamilyInstance d, string famName, string typeName)
        {
            return d.Symbol != null && d.Symbol.Family != null
                && string.Equals(d.Symbol.Family.Name, famName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.Symbol.Name, typeName, StringComparison.OrdinalIgnoreCase);
        }

        // Same story as the picked door: match Level id when available, else fall back to base-Z
        // proximity — all computed inside the link document, so it's independent of how the link is
        // positioned in the host (which is what broke the earlier view-range test).
        private static bool SameStory(FamilyInstance d, ElementId pickedLevel, double pickedZLink)
        {
            if (pickedLevel != null && !pickedLevel.Equals(ElementId.InvalidElementId))
            {
                ElementId dl = DoorLevelId(d);
                if (dl != null && !dl.Equals(ElementId.InvalidElementId))
                    return dl.Equals(pickedLevel);
            }
            return Math.Abs(DoorBaseZLink(d) - pickedZLink) < SameStoryTolFt;
        }

        private static ElementId DoorLevelId(FamilyInstance d)
        {
            ElementId lid = d.LevelId;
            if (lid != null && !lid.Equals(ElementId.InvalidElementId))
                return lid;

            foreach (BuiltInParameter bip in new[]
                     { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM })
            {
                Parameter p = d.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    ElementId pl = p.AsElementId();
                    if (pl != null && !pl.Equals(ElementId.InvalidElementId))
                        return pl;
                }
            }
            return ElementId.InvalidElementId;
        }

        private static XYZ DoorBaseLink(FamilyInstance d)
        {
            LocationPoint lp = d.Location as LocationPoint;
            if (lp != null)
                return lp.Point;
            BoundingBoxXYZ bb = d.get_BoundingBox(null);
            if (bb == null)
                return null;
            return new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, bb.Min.Z);
        }

        private static double DoorBaseZLink(FamilyInstance d)
        {
            XYZ p = DoorBaseLink(d);
            return p != null ? p.Z : 0.0;
        }

        // Limit to the active view's crop region (XY) when a crop is active; otherwise no XY limit.
        private static bool InCropXY(View v, Transform linkTransform, FamilyInstance d)
        {
            if (!v.CropBoxActive)
                return true;
            XYZ baseLink = DoorBaseLink(d);
            if (baseLink == null)
                return true;
            XYZ hostPt = linkTransform.OfPoint(baseLink);
            BoundingBoxXYZ cb = v.CropBox;
            XYZ q = cb.Transform.Inverse.OfPoint(hostPt);
            return q.X >= cb.Min.X - CropTolFt && q.X <= cb.Max.X + CropTolFt
                && q.Y >= cb.Min.Y - CropTolFt && q.Y <= cb.Max.Y + CropTolFt;
        }
    }
}

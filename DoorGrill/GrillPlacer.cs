using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace DoorGrill
{
    // Core placement logic, kept independent of the command/UI so both modes can reuse it.
    internal static class GrillPlacer
    {
        public const double MM_TO_FT = 1.0 / 304.8;
        public const string WallThicknessParamName = "Wall thickness";

        private const double PositionTolFt = 0.001;  // ~0.3 mm: below this a grille counts as in place
        private const double AngleTolRad = 1e-4;

        // Find the grille FamilySymbol by family name in the active document (any type of that family).
        public static FamilySymbol FindSymbol(Document doc, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Family != null &&
                                     string.Equals(s.Family.Name, familyName, StringComparison.OrdinalIgnoreCase));
        }

        // From a door in a link, compute where the grille goes, in host coordinates:
        //   pointHost  - door centre in XY, and the Z the BOTTOM of the grille has to sit at
        //                (door head + the mounting gap from the options);
        //   facingHost - the direction the grille has to look.
        public static bool TryComputeTarget(FamilyInstance door, Transform linkTransform,
                                            double gapFt, bool flip,
                                            out XYZ pointHost, out XYZ facingHost)
        {
            pointHost = null;
            facingHost = null;

            BoundingBoxXYZ bb = door.get_BoundingBox(null);
            if (bb == null)
                return false;

            double topZ = bb.Max.Z; // door head, in link-doc model coords

            double cx, cy;
            LocationPoint lp = door.Location as LocationPoint;
            if (lp != null)
            {
                cx = lp.Point.X;
                cy = lp.Point.Y;
            }
            else
            {
                cx = (bb.Min.X + bb.Max.X) / 2.0;
                cy = (bb.Min.Y + bb.Max.Y) / 2.0;
            }

            // Where the grille has to look, in link-doc coords. The grille must face AWAY from the side
            // the door opens to.
            //
            // door.FacingOrientation IS that opening direction: Revit keeps it in sync with the
            // instance's facing flip, so a door flipped by the architect reports the new direction on its
            // own. (HandFlipped only swaps the hinge side of the leaf - it does not change which side the
            // door swings to, and the grille sits centred above the opening, so it is irrelevant here.)
            //
            // Our family's geometry points BACKWARDS along its own facing vector, so aiming the instance
            // ALONG the door's facing is what makes it visually face away from the swing side. Hence no
            // negation here. `flip` inverts it, for families built the other way round.
            XYZ facingLink = door.FacingOrientation;
            facingLink = new XYZ(facingLink.X, facingLink.Y, 0.0);
            facingLink = facingLink.GetLength() < 1e-9 ? XYZ.BasisY : facingLink.Normalize();
            if (flip)
                facingLink = facingLink.Negate();

            // Shift from the wall centreline out to the wall face, by HALF THE HOST WALL THICKNESS (the
            // frame depth). We use the wall, not the door bounding box, because a door's bbox includes
            // the leaf swing and would push the grille far off. This lands the grille flush on the wall
            // face instead of inside the wall.
            //
            // The shift goes ALONG the grille's own facing, so that flipping the orientation mirrors the
            // mounting side with it: facing and offset together are a 180 deg rotation about the wall /
            // door centre axis, which is also the grille's centre axis. Tying the offset to a fixed
            // direction instead would leave a flipped grille on the wrong face of the wall.
            double halfD = GetHostWallHalfThicknessFt(door);
            if (halfD <= 1e-9)
                halfD = HalfExtentAlong(bb, facingLink); // fallback if host isn't a plain wall

            XYZ insertLink = new XYZ(cx, cy, topZ + gapFt) + facingLink * halfD;
            pointHost = linkTransform.OfPoint(insertLink);

            XYZ f = linkTransform.OfVector(facingLink);
            f = new XYZ(f.X, f.Y, 0.0);
            facingHost = f.GetLength() < 1e-9 ? XYZ.BasisY : f.Normalize();
            return true;
        }

        // Thickness (feet) of the door's host wall; 0 if the host is not a plain wall (e.g. curtain
        // wall, or door hosted differently).
        private static double GetHostWallThicknessFt(FamilyInstance door)
        {
            Wall wall = door.Host as Wall;
            if (wall == null)
                return 0.0;
            try { return wall.Width; }
            catch { return 0.0; }
        }

        private static double GetHostWallHalfThicknessFt(FamilyInstance door)
        {
            return GetHostWallThicknessFt(door) / 2.0;
        }

        // Match the grille's depth to the host wall via the family's "Wall thickness" instance
        // parameter. Silently does nothing when the family has no such parameter, it is read-only, or
        // it is not a length - per requirement, this step must never interrupt placement.
        // True when the value was actually changed.
        private static bool TrySetWallThickness(FamilyInstance grille, FamilyInstance door)
        {
            double widthFt = GetHostWallThicknessFt(door);
            if (widthFt <= 1e-9)
                return false;

            Parameter p = FindWritableLengthParam(grille, WallThicknessParamName);
            if (p == null)
                return false;

            try
            {
                if (Math.Abs(p.AsDouble() - widthFt) <= PositionTolFt)
                    return false;
                return p.Set(widthFt);
            }
            catch
            {
                return false;
            }
        }

        // Instance parameter by name (case-insensitive), writable and length-valued; null otherwise.
        // Type parameters are deliberately ignored: they are shared by every instance of the type, so
        // writing a single door's wall thickness there would corrupt all the other grilles.
        private static Parameter FindWritableLengthParam(Element e, string name)
        {
            foreach (Parameter p in e.Parameters)
            {
                if (p == null || p.Definition == null)
                    continue;
                if (!string.Equals(p.Definition.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (p.IsReadOnly || p.StorageType != StorageType.Double)
                    continue;
                return p;
            }
            return null;
        }

        // Half the extent of an axis-aligned box projected onto a direction (fallback door width).
        private static double HalfExtentAlong(BoundingBoxXYZ bb, XYZ dir)
        {
            double min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < 8; i++)
            {
                XYZ c = new XYZ(
                    (i & 1) == 0 ? bb.Min.X : bb.Max.X,
                    (i & 2) == 0 ? bb.Min.Y : bb.Max.Y,
                    (i & 4) == 0 ? bb.Min.Z : bb.Max.Z);
                double d = c.DotProduct(dir);
                if (d < min) min = d;
                if (d > max) max = d;
            }
            return (max - min) / 2.0;
        }

        // Level whose elevation is the highest at or below z; falls back to the lowest level.
        public static Level PickLevel(Document doc, double z)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            if (levels.Count == 0)
                return null;

            Level chosen = null;
            foreach (Level l in levels)
                if (l.Elevation <= z + 1e-6)
                    chosen = l;
            return chosen ?? levels[0];
        }

        // Compute the target for a door, place a grille there and stamp it with the source door.
        // Must run inside a transaction. Returns null if geometry couldn't be read or the model has
        // no levels.
        public static FamilyInstance PlaceForDoor(Document doc, FamilySymbol symbol, FamilyInstance door,
                                                  RevitLinkInstance link, GrillSettings settings)
        {
            XYZ point, facing;
            if (!TryComputeTarget(door, link.GetTotalTransform(),
                                  settings.MountingGapFt, settings.FlipOrientation,
                                  out point, out facing))
                return null;

            Level level = PickLevel(doc, point.Z);
            if (level == null)
                return null;

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            FamilyInstance grille = doc.Create.NewFamilyInstance(
                point, symbol, level, StructuralType.NonStructural);
            doc.Regenerate();

            GrillLink.Write(grille, link, door);
            // Depth first: it changes the geometry the bottom alignment below measures.
            TrySetWallThickness(grille, door);
            Orient(doc, grille, facing);
            AlignBottomTo(doc, grille, point);
            GrillAnchor.Write(grille, point); // baseline for the door-move guard in SyncGrille
            return grille;
        }

        // Bring an existing grille back onto its source door: the architect may have moved the door or
        // changed the wall, or the options may have changed. Must run inside a transaction. Returns
        // true when the grille was actually moved, rotated or resized; false when it was already
        // correct. Geometry failures return false.
        // When force is false (the Update All pass), a grille is left untouched unless its source door
        // actually moved in plan - so a manual nudge survives. When force is true (an explicit re-pick
        // in mode 1) the grille is always brought back onto its door. Pinned grilles are never touched.
        public static bool SyncGrille(Document doc, FamilyInstance grille, FamilyInstance door,
                                      RevitLinkInstance link, GrillSettings settings, bool force)
        {
            // Honour Revit's native Pin: a grille the engineer moved by hand and pinned is left exactly
            // where it is. (Moving/rotating a pinned element would also throw, which the update pass
            // would otherwise report as a failure.) Unpin it to let the update take over again.
            if (grille.Pinned)
                return false;

            XYZ point, facing;
            if (!TryComputeTarget(door, link.GetTotalTransform(),
                                  settings.MountingGapFt, settings.FlipOrientation,
                                  out point, out facing))
                return false;

            // Door-move guard: compare only the XY of the door's computed target with the stored anchor.
            // If the door has NOT moved in plan, leave the grille where it is (respecting a manual move).
            // Z is ignored on purpose - mounting height is an option, not a manual decision.
            if (!force)
            {
                double ax, ay;
                if (GrillAnchor.TryRead(grille, out ax, out ay))
                {
                    double dx = point.X - ax, dy = point.Y - ay;
                    if (dx * dx + dy * dy <= PositionTolFt * PositionTolFt)
                        return false; // door unchanged in plan → keep the manual placement
                }
            }

            bool changed = TrySetWallThickness(grille, door);
            changed |= Orient(doc, grille, facing);
            changed |= AlignBottomTo(doc, grille, point);
            GrillAnchor.Write(grille, point); // record the door's current target as the new anchor
            return changed;
        }

        // Move an instance so it sits at target: its location point over target in XY, and the BOTTOM
        // of its geometry at target's Z - that is what the mounting gap in the options measures to.
        // Falls back to the instance's own origin when it has no readable bounding box.
        // True if it moved.
        private static bool AlignBottomTo(Document doc, FamilyInstance instance, XYZ target)
        {
            LocationPoint loc = instance.Location as LocationPoint;
            if (loc == null)
                return false;

            XYZ origin = loc.Point;
            BoundingBoxXYZ bb = instance.get_BoundingBox(null);
            double bottomZ = bb != null ? bb.Min.Z : origin.Z;

            XYZ delta = new XYZ(target.X - origin.X, target.Y - origin.Y, target.Z - bottomZ);
            if (delta.GetLength() <= PositionTolFt)
                return false;

            ElementTransformUtils.MoveElement(doc, instance.Id, delta);
            doc.Regenerate();
            return true;
        }

        // Rotate an instance about the vertical axis through its own location point so its facing
        // matches facingHost. Rotating about its own origin keeps the origin - and therefore the
        // vertical extent - put, so the bottom alignment afterwards stays valid. True if it rotated.
        private static bool Orient(Document doc, FamilyInstance instance, XYZ facingHost)
        {
            LocationPoint loc = instance.Location as LocationPoint;
            if (loc == null)
                return false;

            XYZ cur = instance.FacingOrientation;
            cur = new XYZ(cur.X, cur.Y, 0.0);
            if (cur.GetLength() < 1e-9)
                return false;

            cur = cur.Normalize();
            double dot = cur.DotProduct(facingHost);
            if (dot > 1.0) dot = 1.0;
            if (dot < -1.0) dot = -1.0;
            XYZ cross = cur.CrossProduct(facingHost);
            double angle = Math.Atan2(cross.Z, dot);
            if (Math.Abs(angle) <= AngleTolRad)
                return false;

            XYZ pivot = loc.Point;
            Line axis = Line.CreateBound(pivot, pivot + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, instance.Id, axis, angle);
            doc.Regenerate();
            return true;
        }
    }
}

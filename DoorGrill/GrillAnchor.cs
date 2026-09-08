using System;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DoorGrill
{
    // Remembers the XY of the door target the grille was last positioned for (host coordinates, feet).
    // The Update pass uses it to tell a moved DOOR (re-sync the grille) from a grille the engineer
    // nudged by hand while the door stayed put (leave it alone). Z is deliberately not stored - the
    // mounting height is driven by options, so only the plan position decides whether the door moved.
    //
    // Kept in its OWN Extensible Storage schema, separate from GrillLink's door reference, so adding
    // this later did not disturb grilles already stamped by earlier builds.
    internal static class GrillAnchor
    {
        // FIXED schema GUID - never change it.
        private static readonly Guid SchemaGuid = new Guid("e76593e3-a7e5-4823-a9eb-acc6fa937d65");

        private const string SchemaName = "DoorGrillDoorAnchor";
        private const string FieldXY = "DoorTargetXY"; // "x;y" in invariant culture, host feet

        private static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
            {
                SchemaBuilder sb = new SchemaBuilder(SchemaGuid);
                sb.SetSchemaName(SchemaName);
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Public);
                sb.SetVendorId("VXLD");
                sb.AddSimpleField(FieldXY, typeof(string));
                schema = sb.Finish();
            }
            return schema.GetField(FieldXY) != null ? schema : null;
        }

        // Store the door target XY. Must run inside a transaction.
        public static void Write(Element grille, XYZ hostTarget)
        {
            if (grille == null || hostTarget == null)
                return;
            Schema schema = GetSchema();
            if (schema == null)
                return;

            string value = hostTarget.X.ToString("R", CultureInfo.InvariantCulture) + ";"
                         + hostTarget.Y.ToString("R", CultureInfo.InvariantCulture);

            Entity entity = new Entity(schema);
            entity.Set<string>(schema.GetField(FieldXY), value);
            grille.SetEntity(entity);
        }

        // Read the stored door target XY. False when this grille has no anchor yet (e.g. placed by an
        // earlier build) - callers then treat it as "sync now and record".
        public static bool TryRead(Element grille, out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            if (grille == null)
                return false;

            Schema schema = GetSchema();
            if (schema == null)
                return false;

            Entity entity;
            try { entity = grille.GetEntity(schema); }
            catch { return false; }
            if (entity == null || !entity.IsValid())
                return false;

            string value;
            try { value = entity.Get<string>(schema.GetField(FieldXY)); }
            catch { return false; }
            if (string.IsNullOrEmpty(value))
                return false;

            string[] parts = value.Split(';');
            return parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        }
    }
}

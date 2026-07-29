using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DoorGrill
{
    // Mode 3 storage: remembers which linked door a grille was placed for, so the Update command can
    // re-sync the grille after the architect moves the door.
    //
    // Extensible Storage on the grille instance (invisible to the user, no shared parameters and no
    // project-template changes needed). Data is lost if the grille is copy/pasted into another file
    // or created by hand - such grilles are simply ignored by the Update command.
    internal static class GrillLink
    {
        // FIXED schema GUID - never change it, that would orphan every already-stamped grille.
        private static readonly Guid SchemaGuid = new Guid("e65c23dd-20d7-49ab-963d-91c14db7ef24");

        private const string SchemaName = "DoorGrillSourceDoor";
        private const string FieldDoor = "DoorUniqueId";
        private const string FieldLink = "LinkUniqueId";

        // Get the schema, creating it on first use. Returns null if a schema with this GUID already
        // exists in the session but lacks our fields (stale build) - callers then just skip stamping.
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
                sb.AddSimpleField(FieldDoor, typeof(string));
                sb.AddSimpleField(FieldLink, typeof(string));
                schema = sb.Finish();
            }
            return schema.GetField(FieldDoor) != null && schema.GetField(FieldLink) != null ? schema : null;
        }

        // Stamp a freshly placed grille with the source door. Must run inside a transaction.
        public static void Write(Element grille, RevitLinkInstance link, Element door)
        {
            if (grille == null || link == null || door == null)
                return;

            Schema schema = GetSchema();
            if (schema == null)
                return;

            Entity entity = new Entity(schema);
            entity.Set<string>(schema.GetField(FieldDoor), door.UniqueId);
            entity.Set<string>(schema.GetField(FieldLink), link.UniqueId);
            grille.SetEntity(entity);
        }

        // Read the stored source door reference. False when this grille carries no stamp.
        public static bool TryRead(Element grille, out string doorUniqueId, out string linkUniqueId)
        {
            doorUniqueId = null;
            linkUniqueId = null;
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

            try
            {
                doorUniqueId = entity.Get<string>(schema.GetField(FieldDoor));
                linkUniqueId = entity.Get<string>(schema.GetField(FieldLink));
            }
            catch
            {
                return false;
            }

            return !string.IsNullOrEmpty(doorUniqueId) && !string.IsNullOrEmpty(linkUniqueId);
        }

        // All stamped grilles alive in the model, keyed by source door (see GrillRegistry.MakeKey).
        // A key can hold several grilles (mode 1 run twice, or a copied grille) - they all get synced.
        public static Dictionary<string, List<FamilyInstance>> BuildIndex(Document doc)
        {
            Dictionary<string, List<FamilyInstance>> index =
                new Dictionary<string, List<FamilyInstance>>(StringComparer.Ordinal);

            foreach (FamilyInstance fi in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_DuctTerminal)
                         .WhereElementIsNotElementType()
                         .OfClass(typeof(FamilyInstance)))
            {
                string doorUid, linkUid;
                if (!TryRead(fi, out doorUid, out linkUid))
                    continue;

                string key = GrillRegistry.MakeKey(linkUid, doorUid);
                List<FamilyInstance> bucket;
                if (!index.TryGetValue(key, out bucket))
                {
                    bucket = new List<FamilyInstance>();
                    index[key] = bucket;
                }
                bucket.Add(fi);
            }

            return index;
        }
    }
}

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DoorGrill
{
    // Project-level record of every linked door this add-in has ever placed a grille for.
    //
    // Why it exists: mode 2 must not re-create a grille the engineer deliberately deleted. Without this
    // record "no grille here" and "grille was removed on purpose" look identical. The removed state is
    // therefore DERIVED: door is in the registry, but no live grille carries its stamp => leave it
    // alone. Placing again through mode 1 makes a live stamped grille exist, which clears that state by
    // itself - nothing to reset.
    //
    // Stored as one string-array field in Extensible Storage on ProjectInformation.
    internal static class GrillRegistry
    {
        // FIXED schema GUID - never change it, that would forget every served door and mode 2 would
        // resurrect grilles the engineer had deleted.
        private static readonly Guid SchemaGuid = new Guid("3b557df7-4401-48e1-994e-660d44f7f5a7");

        private const string SchemaName = "DoorGrillServedDoors";
        private const string FieldKeys = "ServedDoorKeys";

        // Doors are identified per link, because the same door UniqueId could come from two links.
        public static string MakeKey(string linkUniqueId, string doorUniqueId)
        {
            return linkUniqueId + "|" + doorUniqueId;
        }

        public static string MakeKey(RevitLinkInstance link, Element door)
        {
            return MakeKey(link.UniqueId, door.UniqueId);
        }

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
                sb.AddArrayField(FieldKeys, typeof(string));
                schema = sb.Finish();
            }
            return schema.GetField(FieldKeys) != null ? schema : null;
        }

        // Every door key served so far; empty set when nothing was ever placed or storage is unreadable.
        public static HashSet<string> Read(Document doc)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);

            Element holder = doc.ProjectInformation;
            Schema schema = GetSchema();
            if (holder == null || schema == null)
                return keys;

            try
            {
                Entity entity = holder.GetEntity(schema);
                if (entity == null || !entity.IsValid())
                    return keys;
                IList<string> stored = entity.Get<IList<string>>(schema.GetField(FieldKeys));
                if (stored != null)
                    foreach (string k in stored)
                        if (!string.IsNullOrEmpty(k))
                            keys.Add(k);
            }
            catch
            {
                // Unreadable storage is treated as "nothing served yet" - worst case a grille the user
                // deleted comes back once and gets re-registered.
            }

            return keys;
        }

        // Merge keys into the registry. Must run inside a transaction. Writing an empty array is not
        // allowed by Extensible Storage, so an empty result is simply not written.
        public static void Write(Document doc, HashSet<string> keys)
        {
            Element holder = doc.ProjectInformation;
            Schema schema = GetSchema();
            if (holder == null || schema == null || keys == null || keys.Count == 0)
                return;

            try
            {
                Entity entity = new Entity(schema);
                entity.Set<IList<string>>(schema.GetField(FieldKeys), new List<string>(keys));
                holder.SetEntity(entity);
            }
            catch
            {
                // Never fail a placement because bookkeeping could not be saved.
            }
        }
    }
}

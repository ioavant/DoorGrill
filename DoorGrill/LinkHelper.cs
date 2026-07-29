using System;
using System.Reflection;
using Autodesk.Revit.DB;

namespace DoorGrill
{
    // Helpers for working with elements that live inside a Revit link, plus the ElementId
    // int/long compatibility shim needed to run one net48 DLL across Revit 2022-2027.
    internal static class LinkHelper
    {
        // Revit 2024+ changed ElementId to a 64-bit value: the ElementId(int) constructor was
        // removed in favour of ElementId(long). This DLL is compiled against the Revit 2023 API
        // (int) but also runs on 2024-2027 (long), so pick whichever constructor exists at run
        // time instead of calling one directly.
        private static readonly ConstructorInfo IdLongCtor = typeof(ElementId).GetConstructor(new[] { typeof(long) });
        private static readonly ConstructorInfo IdIntCtor = typeof(ElementId).GetConstructor(new[] { typeof(int) });

        public static ElementId MakeElementId(long value)
        {
            if (IdLongCtor != null)
                return (ElementId)IdLongCtor.Invoke(new object[] { value });
            if (IdIntCtor != null)
                return (ElementId)IdIntCtor.Invoke(new object[] { (int)value });
            throw new InvalidOperationException("No usable ElementId constructor was found in this Revit version.");
        }

        // Resolve the actual element living inside the linked document from a picked reference.
        // Returns null if the reference does not point into a link.
        public static Element GetLinkedElem(Document hostDoc, Reference refer)
        {
            RevitLinkInstance link = hostDoc.GetElement(refer.ElementId) as RevitLinkInstance;
            Document linkDoc = link?.GetLinkDocument();
            if (linkDoc == null || refer.LinkedElementId == null || refer.LinkedElementId.Equals(ElementId.InvalidElementId))
                return null;
            return linkDoc.GetElement(refer.LinkedElementId);
        }

        // True when the element belongs to the Doors category. Uses the ElementId(BuiltInCategory)
        // constructor (stable across versions) rather than IntegerValue (removed in Revit 2026).
        public static bool IsDoor(Element e)
        {
            return e != null
                && e.Category != null
                && e.Category.Id.Equals(new ElementId(BuiltInCategory.OST_Doors));
        }
    }
}

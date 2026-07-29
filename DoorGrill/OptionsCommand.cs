using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DoorGrill
{
    // Options for the two placement commands. They live in a file next to the DLL, so nothing in the
    // document changes and no transaction is needed.
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class OptionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "No active document. Open a project first.";
                return Result.Failed;
            }

            return Show(commandData, uidoc.Document, null) == null ? Result.Cancelled : Result.Succeeded;
        }

        /// <summary>
        /// Show the options dialog and save what the user accepted. Returns the saved options, or null
        /// if the dialog was cancelled. Also used by the placement commands when no grille family has
        /// been chosen yet, in which case <paramref name="notice"/> explains why it opened by itself.
        /// </summary>
        internal static GrillSettings Show(ExternalCommandData commandData, Document doc, string notice)
        {
            using (OptionsForm form = new OptionsForm(doc, GrillSettings.Load(), notice))
            {
                if (form.ShowDialog(new RevitWindow(commandData.Application.MainWindowHandle))
                    != DialogResult.OK)
                    return null;

                if (!form.Settings.Save())
                    TaskDialog.Show("Door Grill",
                        "The options could not be saved to disk, so they apply to this Revit session "
                        + "only.\n\nTried: " + GrillSettings.CurrentLocation);

                return form.Settings;
            }
        }

        /// <summary>
        /// Everything a placement run needs from the options: the settings themselves and the grille
        /// FamilySymbol. Asks the user to choose a family the first time, and explains it when the
        /// chosen family is not loaded in this project. False means the run should not go ahead.
        /// </summary>
        internal static bool TryPrepare(ExternalCommandData commandData, Document doc,
                                        out GrillSettings settings, out FamilySymbol symbol)
        {
            symbol = null;
            settings = GrillSettings.Load();

            if (!settings.HasFamily)
            {
                settings = Show(commandData, doc,
                    "No grille family has been chosen yet.\nPick the family to place, then continue.");
                if (settings == null) return false;
            }

            symbol = GrillPlacer.FindSymbol(doc, settings.FamilyName);
            if (symbol == null)
            {
                TaskDialog.Show("Door Grill",
                    "The family \"" + settings.FamilyName + "\" is not loaded in this project.\n\n"
                    + "Load it, or choose a different family in the options.");
                return false;
            }

            return true;
        }
    }
}

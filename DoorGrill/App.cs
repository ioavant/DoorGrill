using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DoorGrill
{
    // Ribbon entry point. Every brand-dependent string comes from the generated Brand class
    // (Properties\BrandInfo.cs) - see ..\..\_branding.
    public class App : IExternalApplication
    {
        private UIControlledApplication _uiApp;

        public Result OnStartup(UIControlledApplication application)
        {
            UpdateChecker.CheckInBackground();

            string tab = Brand.RibbonTab;
            string panel = Brand.RibbonPanel;

            // Created by whichever add-in of this brand loads first.
            try { application.CreateRibbonTab(tab); } catch { /* tab exists - OK */ }

            RibbonPanel ribbonPanel = GetOrCreatePanel(application, tab, panel);
            string asmPath = Assembly.GetExecutingAssembly().Location;
            ContextualHelp help = new ContextualHelp(ContextualHelpType.Url, Brand.HelpUrl);

            // Three related commands stacked in one column, the shape used by the other small
            // Vixeldorf add-ins (Link Section Box). Stacked items are rendered small, so they take
            // Image (16x16), not LargeImage.
            PushButtonData placeData = new PushButtonData(
                "DoorGrillPlace", "Place\nGrille",
                asmPath, "DoorGrill.PlaceGrillCommand")
            {
                ToolTip = "Place a grille above a door picked in a linked model.",
                LongDescription = "Pick doors one after another in the linked architectural model; a "
                                + "grille is placed above each of them and remembers which door it "
                                + "belongs to. Picking a door that already has a grille re-syncs that "
                                + "grille instead of adding a second one. Press Esc to finish.",
                Image = LoadImage("doorgrill_16.png"),
                LargeImage = LoadImage("doorgrill_32.png")
            };
            placeData.SetContextualHelp(help);

            PushButtonData updateData = new PushButtonData(
                "DoorGrillUpdateAll", "Update\nAll",
                asmPath, "DoorGrill.PlaceGrillOnSimilarCommand")
            {
                ToolTip = "Place grilles above all similar doors and re-sync every existing grille.",
                LongDescription = "Pick one sample door: grilles are placed above every door of the "
                                + "same family and type on the same level of that link, and every "
                                + "grille already in the model is brought back onto its own door - "
                                + "useful after the architect issues a new revision. Nothing is ever "
                                + "deleted; doors that vanished from the link are reported instead.",
                Image = LoadImage("doorgrill_update_16.png"),
                LargeImage = LoadImage("doorgrill_update_32.png")
            };
            updateData.SetContextualHelp(help);

            PushButtonData optionsData = new PushButtonData(
                "DoorGrillOptions", "Options",
                asmPath, "DoorGrill.OptionsCommand")
            {
                ToolTip = "Grille family, mounting height, orientation and how deleted grilles are treated.",
                LongDescription = "The options are stored next to the add-in, so they are shared by "
                                + "every Revit version and every project on this machine.",
                Image = LoadImage("settings_16.png"),
                LargeImage = LoadImage("settings_32.png")
            };
            optionsData.SetContextualHelp(help);

            ribbonPanel.AddStackedItems(placeData, updateData, optionsData);

            // ── About / Help button ───────────────────────────────────────────
            // Every add-in of this brand shares one "About" panel holding a single help button, so
            // only the first one loaded gets to add it. Two independent checks, because neither alone
            // covers every case:
            //   • an AppDomain data slot - all add-ins in a Revit process share one AppDomain, and this
            //     catches siblings built from the same skill regardless of load order (the Vixeldorf
            //     convention);
            //   • a scan of the live ribbon via AdWindows - RibbonPanel.GetItems() only sees items the
            //     CURRENT add-in added, so this is the only way to notice a button placed by an older
            //     add-in of the same brand (the TES convention).
            RibbonPanel aboutPanel = GetOrCreatePanel(application, tab, Brand.AboutPanel);
            bool alreadyAdded = AppDomain.CurrentDomain.GetData(Brand.AboutFlagKey) != null
                                || AboutButtonExistsOnRibbon(tab, Brand.AboutPanel);
            if (!alreadyAdded)
            {
                AppDomain.CurrentDomain.SetData(Brand.AboutFlagKey, true);
                PushButtonData webData = new PushButtonData(
                    Brand.AboutButtonId, Brand.AboutButtonText,
                    asmPath, "DoorGrill.OpenWebPageCommand")
                {
                    ToolTip = Brand.AboutButtonTooltip,
                    LargeImage = LoadImage("web_icon.png")
                };
                aboutPanel.AddItem(webData);
            }

            // The "About" panel should stay last on the tab even as other add-ins of the same brand add
            // panels afterwards. There is no supported API to reorder panels, and ApplicationInitialized
            // can fire before the ribbon exists (and never fires when the add-in loads after Revit has
            // started), so this happens on the first Idling tick instead.
            if (Brand.PinAboutPanelToEnd)
            {
                _uiApp = application;
                application.Idling += OnFirstIdling;
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private void OnFirstIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            _uiApp.Idling -= OnFirstIdling;   // one-shot: only the first idle
            MoveAboutPanelToEnd();
        }

        /// <summary>
        /// True if a button whose caption contains Brand.AboutMatchText already exists on the given
        /// ribbon tab / panel. Uses AdWindows (Autodesk.Windows), which sees ALL add-ins' items.
        /// </summary>
        private static bool AboutButtonExistsOnRibbon(string tabTitle, string panelTitle)
        {
            try
            {
                var ribbon = Autodesk.Windows.ComponentManager.Ribbon;
                if (ribbon == null) return false;

                foreach (Autodesk.Windows.RibbonTab ribbonTab in ribbon.Tabs)
                {
                    if (!string.Equals(ribbonTab.Title, tabTitle, StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (Autodesk.Windows.RibbonPanel ribbonPanel in ribbonTab.Panels)
                    {
                        if (ribbonPanel.Source == null ||
                            !string.Equals(ribbonPanel.Source.Title, panelTitle, StringComparison.OrdinalIgnoreCase))
                            continue;

                        foreach (Autodesk.Windows.RibbonItem item in ribbonPanel.Source.Items)
                            if (ItemTextMatchesAboutButton(item)) return true;
                    }
                }
            }
            catch { /* AdWindows unavailable - fall through and add the button */ }
            return false;
        }

        private static bool ItemTextMatchesAboutButton(Autodesk.Windows.RibbonItem item)
        {
            if (item == null) return false;
            string text = item.Text ?? string.Empty;
            if (text.IndexOf(Brand.AboutMatchText, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // SplitButton / StackedItems may contain children.
            Autodesk.Windows.RibbonRowPanel rowPanel = item as Autodesk.Windows.RibbonRowPanel;
            if (rowPanel != null)
                foreach (Autodesk.Windows.RibbonItem child in rowPanel.Items)
                    if (ItemTextMatchesAboutButton(child)) return true;

            return false;
        }

        /// <summary>
        /// Moves the shared "About" panel to the end of the brand's ribbon tab. Uses the unofficial but
        /// long-stable Autodesk.Windows ribbon model, wrapped so a cosmetic tweak can never break
        /// add-in loading.
        /// </summary>
        private static void MoveAboutPanelToEnd()
        {
            try
            {
                var ribbon = Autodesk.Windows.ComponentManager.Ribbon;
                if (ribbon == null) return;

                foreach (Autodesk.Windows.RibbonTab tab in ribbon.Tabs)
                {
                    if (tab.Title != Brand.RibbonTab && tab.Id != Brand.RibbonTab) continue;

                    Autodesk.Windows.RibbonPanel about = null;
                    foreach (Autodesk.Windows.RibbonPanel p in tab.Panels)
                        if (p.Source != null && p.Source.Title != null &&
                            p.Source.Title.Trim().Equals(Brand.AboutPanel, StringComparison.OrdinalIgnoreCase))
                        { about = p; break; }

                    if (about != null && tab.Panels[tab.Panels.Count - 1] != about)
                    {
                        tab.Panels.Remove(about);
                        tab.Panels.Add(about);
                    }
                    break;
                }
            }
            catch { /* never let ribbon cosmetics interfere with loading */ }
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication app, string tab, string name)
        {
            foreach (RibbonPanel p in app.GetRibbonPanels(tab))
                if (p.Name == name) return p;
            return app.CreateRibbonPanel(tab, name);
        }

        internal static BitmapImage LoadImage(string fileName)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                // Brand-neutral resource names, identical in both brand builds.
                System.IO.Stream stream = asm.GetManifestResourceStream("DoorGrill.Resources." + fileName);
                if (stream == null)
                    foreach (string n in asm.GetManifestResourceNames())
                        if (n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                        { stream = asm.GetManifestResourceStream(n); break; }

                if (stream == null) return null;

                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }

    // ── Help / About button ───────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenWebPageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Process.Start(new ProcessStartInfo(Brand.HelpUrl) { UseShellExecute = true });
            return Result.Succeeded;
        }
    }
}

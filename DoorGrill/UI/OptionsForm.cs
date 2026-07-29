using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.Revit.DB;
// Autodesk.Revit.DB has its own Form (a Revit modelled form), Color and Point, so those names are
// ambiguous once the Revit namespace is in scope. In this file they always mean the WinForms / GDI+ ones.
using Form = System.Windows.Forms.Form;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;

namespace DoorGrill
{
    // Options dialog. Built in code rather than a designer file so the whole layout stays reviewable
    // in one place. Lengths are shown and typed in the PROJECT's own length units, even though they
    // are stored in millimetres next to the DLL.
    internal class OptionsForm : Form
    {
        private readonly Units _units;
        private readonly ComboBox _familyBox = new ComboBox();
        private readonly TextBox _gapBox = new TextBox();
        private readonly RadioButton _orientAway = new RadioButton();
        private readonly RadioButton _orientFlipped = new RadioButton();
        private readonly CheckBox _keepDeleted = new CheckBox();

        /// <summary>The edited options; valid once ShowDialog returned OK.</summary>
        public GrillSettings Settings { get; private set; }

        /// <param name="notice">Optional line shown at the top, e.g. why the dialog opened by itself.</param>
        public OptionsForm(Document doc, GrillSettings settings, string notice)
        {
            _units = doc.GetUnits();
            Settings = settings;

            Text = "Door Grill Options  —  v" + UpdateChecker.CurrentVersion;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Padding = new Padding(12);

            int y = 12;

            Image logo = LoadLogo();
            if (logo != null)
            {
                Controls.Add(new PictureBox
                {
                    Image = logo,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Location = new Point(12, y),
                    Size = new Size(160, 30)
                });
                y += 40;
            }

            LinkLabel banner = BuildUpdateBanner();
            if (banner != null)
            {
                banner.Location = new Point(12, y);
                banner.Size = new Size(428, 20);
                Controls.Add(banner);
                y += 28;
            }

            if (!string.IsNullOrEmpty(notice))
            {
                Controls.Add(new Label
                {
                    Text = notice,
                    Location = new Point(12, y),
                    Size = new Size(428, 34),
                    ForeColor = Color.FromArgb(150, 60, 0),
                    Font = new Font(Font, FontStyle.Bold)
                });
                y += 40;
            }

            // ── Grille family ─────────────────────────────────────────────────
            Controls.Add(new Label
            {
                Text = "Grille family:",
                Location = new Point(12, y + 3),
                AutoSize = true
            });

            _familyBox.Location = new Point(140, y);
            _familyBox.Size = new Size(300, 22);
            _familyBox.DropDownStyle = ComboBoxStyle.DropDown; // typed names allowed too
            foreach (string name in LoadedTerminalFamilies(doc))
                _familyBox.Items.Add(name);
            _familyBox.Text = Settings.FamilyName;
            Controls.Add(_familyBox);
            y += 26;

            Controls.Add(new Label
            {
                Text = "Air Terminal families loaded in this project. A family that is not loaded yet "
                     + "can be typed in.",
                Location = new Point(14, y),
                Size = new Size(426, 32),
                ForeColor = SystemColors.GrayText
            });
            y += 40;

            // ── Mounting height ───────────────────────────────────────────────
            Controls.Add(new Label
            {
                Text = "Mounting height above door head:",
                Location = new Point(12, y + 3),
                AutoSize = true
            });

            _gapBox.Location = new Point(252, y);
            _gapBox.Size = new Size(110, 22);
            _gapBox.Text = FormatLength(Settings.MountingGapFt);
            Controls.Add(_gapBox);

            Controls.Add(new Label
            {
                Text = UnitSymbol(),
                Location = new Point(370, y + 3),
                AutoSize = true
            });
            y += 26;

            Controls.Add(new Label
            {
                Text = "Clear distance from the door head up to the bottom of the grille.",
                Location = new Point(14, y),
                Size = new Size(424, 18),
                ForeColor = SystemColors.GrayText
            });
            y += 30;

            // ── Orientation ───────────────────────────────────────────────────
            GroupBox orientGroup = new GroupBox
            {
                Text = "Grille orientation",
                Location = new Point(12, y),
                Size = new Size(428, 76)
            };

            _orientAway.Text = "Away from the door's opening direction (default)";
            _orientAway.Location = new Point(14, 22);
            _orientAway.AutoSize = true;
            _orientAway.Checked = !Settings.FlipOrientation;

            _orientFlipped.Text = "Reversed — flipped about the wall centre axis";
            _orientFlipped.Location = new Point(14, 46);
            _orientFlipped.AutoSize = true;
            _orientFlipped.Checked = Settings.FlipOrientation;

            orientGroup.Controls.Add(_orientAway);
            orientGroup.Controls.Add(_orientFlipped);
            Controls.Add(orientGroup);
            y += 86;

            // ── Deleted grilles ───────────────────────────────────────────────
            _keepDeleted.Text = "Do not place grilles where they were deleted manually";
            _keepDeleted.Location = new Point(14, y);
            _keepDeleted.AutoSize = true;
            _keepDeleted.Checked = Settings.KeepDeletedDoorsEmpty;
            Controls.Add(_keepDeleted);
            y += 22;

            Controls.Add(new Label
            {
                Text = "Off: Update All restores every missing grille. On: a door whose grille you\n"
                     + "deleted stays empty, and only Place Grille can put one back.",
                Location = new Point(32, y),
                Size = new Size(408, 34),
                ForeColor = SystemColors.GrayText
            });
            y += 44;

            Controls.Add(new Label
            {
                Text = "Saved for every Revit version in:\n" + GrillSettings.CurrentLocation,
                Location = new Point(14, y),
                Size = new Size(426, 32),
                ForeColor = SystemColors.GrayText,
                AutoEllipsis = true
            });
            y += 40;

            // ── Buttons ───────────────────────────────────────────────────────
            Button ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(276, y),
                Size = new Size(80, 26)
            };
            ok.Click += OnOk;

            Button cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(360, y),
                Size = new Size(80, 26)
            };

            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            ClientSize = new Size(452, y + 26 + 12);
        }

        private void OnOk(object sender, EventArgs e)
        {
            string family = (_familyBox.Text ?? string.Empty).Trim();
            if (family.Length == 0)
            {
                Warn("Choose the grille family to place. It is the family Door Grill puts above each "
                     + "door, and there is no sensible default for it.");
                _familyBox.Focus();
                return;
            }

            double feet;
            if (!TryParseLength(_gapBox.Text, out feet) || feet < 0.0)
            {
                Warn("Enter the mounting height as a length in this project's units, for example \""
                     + FormatLength(GrillSettings.DefaultGapMm * GrillPlacer.MM_TO_FT) + "\".");
                _gapBox.Focus();
                _gapBox.SelectAll();
                return;
            }

            Settings.FamilyName = family;
            Settings.SetMountingGapFt(feet);
            Settings.FlipOrientation = _orientFlipped.Checked;
            Settings.KeepDeletedDoorsEmpty = _keepDeleted.Checked;
        }

        private void Warn(string text)
        {
            MessageBox.Show(this, text, "Door Grill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None; // keep the dialog open
        }

        // Family names of every air terminal type loaded in the project.
        private static IEnumerable<string> LoadedTerminalFamilies(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_DuctTerminal)
                    .Cast<FamilySymbol>()
                    .Where(s => s.Family != null && !string.IsNullOrEmpty(s.Family.Name))
                    .Select(s => s.Family.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        // ── Project units ─────────────────────────────────────────────────────

        private string FormatLength(double feet)
        {
            try { return UnitFormatUtils.Format(_units, SpecTypeId.Length, feet, true); }
            catch { return (feet / GrillPlacer.MM_TO_FT).ToString("0.#"); }
        }

        private bool TryParseLength(string text, out double feet)
        {
            feet = 0.0;
            if (string.IsNullOrEmpty(text)) return false;
            try { return UnitFormatUtils.TryParse(_units, SpecTypeId.Length, text, out feet); }
            catch { return false; }
        }

        private string UnitSymbol()
        {
            try
            {
                return LabelUtils.GetLabelForUnit(_units.GetFormatOptions(SpecTypeId.Length).GetUnitTypeId());
            }
            catch { return ""; }
        }

        // ── Update banner ─────────────────────────────────────────────────────

        // Non-null only when the background check found a newer published version. Clicking it opens
        // the download page in the browser; nothing is ever installed automatically.
        private static LinkLabel BuildUpdateBanner()
        {
            UpdateInfo update = UpdateChecker.AvailableUpdate;
            if (update == null) return null;

            LinkLabel link = new LinkLabel
            {
                Text = "Version " + update.Version + " is available — click to download."
                       + (string.IsNullOrEmpty(update.ReleaseNotes) ? "" : "  (" + update.ReleaseNotes + ")"),
                BackColor = Color.FromArgb(255, 249, 219),
                Padding = new Padding(6, 3, 6, 3),
                AutoEllipsis = true
            };
            link.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(update.DownloadUrl) { UseShellExecute = true }); }
                catch { /* no browser / bad URL - nothing useful to say */ }
            };
            return link;
        }

        private static Image LoadLogo()
        {
            try
            {
                // Contributed per brand by _branding\Branding.targets under a brand-neutral name.
                Assembly asm = Assembly.GetExecutingAssembly();
                using (System.IO.Stream s = asm.GetManifestResourceStream("DoorGrill.Resources.logo.png"))
                    return s == null ? null : Image.FromStream(s);
            }
            catch { return null; }
        }
    }

    // Lets a WinForms dialog be owned by the Revit main window, so it cannot end up behind it.
    internal class RevitWindow : IWin32Window
    {
        public RevitWindow(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; private set; }
    }
}

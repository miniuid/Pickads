// UI-only checks. Never instantiate TrayApp or invoke blocking/startup actions.
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.IO;
using System.Collections.Generic;

static class ResizeLayoutChecks
{
    static int checks;

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static Control Find(Control root, string name)
    {
        return root.Controls.Find(name, true).Single();
    }

    static void Layout(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls) Layout(child);
    }

    static void SettleLayout(Control control)
    {
        // Native Windows can defer layout while handles and DPI are initialized.
        for (int pass = 0; pass < 3; pass++)
        {
            Layout(control);
            Application.DoEvents();
        }
    }

    static string Bounds(Control control)
    {
        return control.Name + "=" + control.Bounds;
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Console.WriteLine("Runtime: " + Environment.Version + "; 64-bit process: " + Environment.Is64BitProcess);
            Console.WriteLine("Screen: " + SystemInformation.VirtualScreen);
            var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var type = assembly.GetType("PickAds.MainForm", true);
            using (var form = (Form)Activator.CreateInstance(type, new object[] { null }))
            {
                form.Show();
                Application.DoEvents();
                SettleLayout(form);
                var original = form.ClientSize;
                var hero = Find(form, "StatusCard");
                var settings = Find(form, "SettingsCard");
                var sites = Find(form, "SitesCard");
                var list = Find(form, "BlockedSitesList");
                var actions = (TableLayoutPanel)Find(form, "ActionButtons");
                var footer = Find(form, "LastCheckLabel");
                Check(Find(form, "ApplyBlocks").Text == "Apply new blocks", "System changes need an explicit Apply control");
                var originalList = list.Size;
                var originalCardWidth = hero.Width;
                var originalFooterBottom = original.Height - footer.Bottom;
                var originalSettingsTop = settings.Top;
                var originalSitesTop = sites.Top;
                var rightMargins = settings.Controls.Cast<Control>()
                    .Where(c => c.GetType().Name == "ToggleSwitch")
                    .Select(c => settings.ClientSize.Width - c.Right).ToArray();

                Console.WriteLine("Initial client=" + original + "; window=" + form.Size + "; minimum=" + form.MinimumSize);

                Check(form.MaximizeBox, "Maximize must be enabled");
                Check(form.FormBorderStyle == FormBorderStyle.Sizable, "Window border must be resizable");
                Check(form.MinimumSize.Width >= form.Width && form.MinimumSize.Height >= form.Height,
                    "Minimum window size must protect the original layout");

                foreach (var size in new[] { new Size(900, original.Height), new Size(original.Width, 900),
                                             new Size(1280, 900), original })
                {
                    form.ClientSize = size;
                    Application.DoEvents();
                    SettleLayout(form);
                    Console.WriteLine("Client=" + form.ClientSize + "; " + Bounds(hero) + "; " + Bounds(sites) + "; " + Bounds(list) + "; " + Bounds(actions));
                    int dx = form.ClientSize.Width - original.Width;
                    int dy = form.ClientSize.Height - original.Height;
                    Check(hero.Width == originalCardWidth + dx, "Status card width must follow the window");
                    Check(settings.Top == originalSettingsTop && sites.Top == originalSitesTop,
                        "Top cards must keep their vertical positions");
                    Check(list.Width == originalList.Width + dx && list.Height == originalList.Height + dy,
                        "Domain list must grow in both directions");
                    Check(form.ClientSize.Height - footer.Bottom == originalFooterBottom, "Footer must stay at the bottom");
                    Check(list.Bottom <= actions.Top, "Domain list must not overlap action buttons");
                    Check(sites.Bottom <= footer.Top, "Sites card must not overlap footer");
                    int index = 0;
                    foreach (var toggle in settings.Controls.Cast<Control>().Where(c => c.GetType().Name == "ToggleSwitch"))
                        Check(settings.ClientSize.Width - toggle.Right == rightMargins[index++], "Switches must stay aligned right");
                    Check(actions.Controls.Count == 4, "All four actions must remain available");
                    var buttons = actions.Controls.Cast<Control>().OrderBy(c => c.Left).ToArray();
                    Check(buttons.Max(c => c.Width) - buttons.Min(c => c.Width) <= 2, "Actions must share available width");
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        Check(actions.ClientRectangle.Contains(buttons[i].Bounds), "Action must remain inside its row");
                        if (i > 0) Check(buttons[i - 1].Right <= buttons[i].Left, "Action buttons must not overlap");
                    }
                }

                // Dismiss test form directly; do not call the real app's tray-close path.
                type.GetField("AllowClose").SetValue(form, true);
                form.Close();
            }
            using (var lookups = (Form)Activator.CreateInstance(assembly.GetType("PickAds.LookupsForm", true),
                new object[] { new List<string> { "ads.example.com", "login.minecraft.net" } }))
            {
                var sites = (ListBox)Find(lookups, "LookupSites");
                Check(sites.Items.Count == 2, "Lookup view must retain all queried names");
                Check(sites.SelectedItems.Count == 0, "Lookup domains must not be preselected for blocking");
            }
            Console.WriteLine("PASS: " + checks + " resize layout assertions (wide, tall, large, restored).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PickAds
{
    // Reimplements the uploaded build's useful lookup view without changing hosts
    // or automatically selecting domains on the user's behalf.
    class LookupsForm : Form
    {
        public readonly List<string> ToBlock = new List<string>();
        public LookupsForm(List<string> domains)
        {
            Text = "Recent lookups — review before adding";
            ClientSize = new Size(540, 500);
            MinimumSize = SizeFromClientSize(ClientSize);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Font(9.5f, false);
            StartPosition = FormStartPosition.CenterParent;
            var heading = new Label { Text = "Computer-wide DNS names; these are not all ads. Select only domains you recognize. Added sites still need Apply new blocks.",
                Bounds = new Rectangle(16, 16, 508, 48), ForeColor = Theme.Text, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(heading);
            var list = new SiteList { Name = "LookupSites", Bounds = new Rectangle(16, 76, 508, 360),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            list.Items.AddRange(domains.Cast<object>().ToArray());
            Controls.Add(list);
            var copy = new Button { Text = "Copy all", Bounds = new Rectangle(16, 450, 110, 32), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            copy.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "This copies DNS names from all applications, which may reveal sites you visited. Copy them?", "PickAds privacy",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                try { if (domains.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, domains)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Clipboard unavailable"); }
            };
            var add = new Button { Text = "Add selected", Bounds = new Rectangle(394, 450, 130, 32), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            add.Click += (s, e) => { ToBlock.AddRange(list.SelectedItems.Cast<string>()); DialogResult = DialogResult.OK; Close(); };
            Controls.Add(copy);
            Controls.Add(add);
        }
    }
}

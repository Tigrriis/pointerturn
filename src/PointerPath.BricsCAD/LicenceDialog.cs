using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PointerPath.Core.Licensing;

namespace PointerPath.BricsCAD
{
    /// <summary>
    /// Sign in to PointerPath with an account, or, when signed in, see the licence on this
    /// computer and sign out.
    /// </summary>
    internal sealed class LicenceDialog : Form
    {
        private const int TextWidth = 340;

        private readonly LicenceManager _manager;
        private readonly TableLayoutPanel _body;
        private TextBox? _email;
        private TextBox? _password;
        private Label? _message;
        private Button? _primary;
        private Button? _secondary;

        public LicenceDialog(LicenceManager manager, LicenceStatus status)
        {
            _manager = manager;

            Text = "PointerPath - Licence";
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(7f, 15f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            // Everything sizes to its text, so the layout holds at any display scaling.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14);

            _body = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = new Padding(0) };
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(_body);

            Show(status);
        }

        /// <summary>Lays the dialog out for where this computer stands.</summary>
        private void Show(LicenceStatus status)
        {
            SuspendLayout();
            _body.SuspendLayout();
            while (_body.Controls.Count > 0)
            {
                Control old = _body.Controls[0];
                _body.Controls.RemoveAt(0);
                old.Dispose();
            }

            _email = null;
            _password = null;
            _message = null;
            AcceptButton = null;
            CancelButton = null;

            switch (status.State)
            {
                case LicenceState.Licensed:
                    SignedIn(status);
                    break;
                case LicenceState.NotConfigured:
                    NoLicenceNeeded();
                    break;
                default:
                    SignIn(status);
                    break;
            }

            _body.ResumeLayout();
            ResumeLayout();
        }

        private void SignIn(LicenceStatus status)
        {
            _body.Controls.Add(Heading("Sign in to PointerPath"));
            _body.Controls.Add(Note("Use the email address and password of your PointerPath account. You stay signed in on this computer."));

            _message = new Label { AutoSize = true, MaximumSize = new Size(TextWidth, 0), ForeColor = Color.FromArgb(160, 30, 20), Margin = new Padding(3, 0, 3, 8) };
            _body.Controls.Add(_message);
            Say(status.Message);

            _body.Controls.Add(Caption("Email"));
            _email = new TextBox { Width = TextWidth, Margin = new Padding(3, 2, 3, 8) };
            _body.Controls.Add(_email);
            _body.Controls.Add(Caption("Password"));
            _password = new TextBox { Width = TextWidth, UseSystemPasswordChar = true, Margin = new Padding(3, 2, 3, 8) };
            _body.Controls.Add(_password);
            if (status.Email.Length > 0)
            {
                _email.Text = status.Email;
            }

            _body.Controls.Add(Link("Create an account or buy a licence", _manager.Website));

            _primary = Button("Sign in");
            _primary.Click += OnSignIn;
            _secondary = Button("Cancel");
            _secondary.DialogResult = DialogResult.Cancel;
            _body.Controls.Add(Buttons(_primary, _secondary));
            AcceptButton = _primary;
            CancelButton = _secondary;
            ActiveControl = _email.Text.Length > 0 ? _password : _email;
        }

        private void SignedIn(LicenceStatus status)
        {
            _body.Controls.Add(Heading("PointerPath is licensed on this computer"));
            _body.Controls.Add(Note($"Signed in as {status.Email}."));
            string until = status.ExpiresUtc.HasValue ? status.ExpiresUtc.Value.ToLocalTime().ToString("d MMMM yyyy") : "";
            _body.Controls.Add(Note(
                "The licence is checked again whenever this computer is online." +
                (until.Length > 0 ? $" Without a connection, PointerPath keeps working until {until}." : "")));

            _message = new Label { AutoSize = true, MaximumSize = new Size(TextWidth, 0), ForeColor = Color.FromArgb(160, 30, 20), Margin = new Padding(3, 0, 3, 8), Visible = false };
            _body.Controls.Add(_message);
            _body.Controls.Add(Link("Your account", _manager.Website + "/account"));

            _primary = Button("Close");
            _primary.DialogResult = DialogResult.OK;
            _secondary = Button("Sign out");
            _secondary.Click += OnSignOut;
            _body.Controls.Add(Buttons(_secondary, _primary));
            AcceptButton = _primary;
            CancelButton = _primary;
        }

        private void NoLicenceNeeded()
        {
            _body.Controls.Add(Heading("No licence needed"));
            _body.Controls.Add(Note("This build of PointerPath does not check for a licence."));
            _primary = Button("Close");
            _primary.DialogResult = DialogResult.OK;
            _body.Controls.Add(Buttons(_primary));
            AcceptButton = _primary;
            CancelButton = _primary;
        }

        private async void OnSignIn(object? sender, EventArgs e)
        {
            if (_email == null || _password == null)
            {
                return;
            }

            string email = _email.Text;
            string password = _password.Text;
            Busy(true);
            Say("");
            LicenceStatus result;
            try
            {
                result = await Task.Run(() => _manager.SignInAsync(email, password, Licence.MachineName));
            }
            catch (Exception ex)
            {
                result = new LicenceStatus { State = LicenceState.SignedOut, Message = "Signing in did not work: " + ex.Message };
            }

            if (IsDisposed)
            {
                return;
            }

            Busy(false);
            if (result.MayRun)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            Say(result.Message);
            _password?.SelectAll();
            _password?.Focus();
        }

        private async void OnSignOut(object? sender, EventArgs e)
        {
            Busy(true);
            try
            {
                await Task.Run(() => _manager.SignOutAsync());
            }
            catch (Exception)
            {
                // Signed out here either way; the place can be freed on the account page.
            }

            if (IsDisposed)
            {
                return;
            }

            Busy(false);
            Show(_manager.Check());
        }

        private void Busy(bool busy)
        {
            UseWaitCursor = busy;
            foreach (Control? control in new Control?[] { _email, _password, _primary, _secondary })
            {
                if (control != null)
                {
                    control.Enabled = !busy;
                }
            }
        }

        private void Say(string message)
        {
            if (_message != null)
            {
                _message.Text = message;
                _message.Visible = message.Length > 0;
            }
        }

        // ---- the pieces ----

        private Label Heading(string text) => new Label { Text = text, AutoSize = true, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Margin = new Padding(3, 0, 3, 8) };

        private static Label Note(string text) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(TextWidth, 0), Margin = new Padding(3, 0, 3, 8) };

        private static Label Caption(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(3, 0, 3, 0) };

        private static Button Button(string text) => new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 27), Padding = new Padding(8, 0, 8, 0), Margin = new Padding(6, 0, 0, 0) };

        private static FlowLayoutPanel Buttons(params Button[] buttons)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
            // Right to left: the first button given ends up leftmost.
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                row.Controls.Add(buttons[i]);
            }

            return row;
        }

        private static LinkLabel Link(string text, string url)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(3, 0, 3, 4) };
            link.LinkClicked += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception)
                {
                    // No browser to open it in: the address is in the README.
                }
            };
            return link;
        }
    }
}

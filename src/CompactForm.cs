using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bf6CrashHelper
{
    internal static class UiLanguage
    {
        internal static string Text(string german, string english) { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? german : english; }
        internal static void Set(bool german)
        {
            var culture = CultureInfo.GetCultureInfo(german ? "de-DE" : "en-US");
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
    }

    // This surface exposes only the two-file workaround and its recovery.
    public sealed class MainForm : Form
    {
        const string Repository = "https://github.com/IamAngusU/bf6-crash-helper";
        readonly Color navy = Color.FromArgb(21, 32, 43), muted = Color.FromArgb(97, 113, 129);
        readonly ComboBox language = new ComboBox();
        readonly FlowLayoutPanel body = new FlowLayoutPanel();
        readonly TextBox gamePath = new TextBox();
        readonly Label subtitle = new Label(), introduction = new Label(), folderLabel = new Label(), scope = new Label();
        readonly Label statusTitle = new Label(), statusDetail = new Label(), actionNote = new Label(), footerNote = new Label();
        readonly Panel statusBox = new Panel(), folderRow = new Panel(), actions = new Panel();
        readonly Button browse = new Button(), mirakCheck = new Button(), mirakApply = new Button(), mirakUndo = new Button();
        readonly CheckBox mirakConsent = new CheckBox(), mirakUndoConsent = new CheckBox();
        readonly LinkLabel details = new LinkLabel(), source = new LinkLabel(), backups = new LinkLabel(), folder = new LinkLabel(), author = new LinkLabel();
        readonly ProgressBar progress = new ProgressBar();
        readonly MirakRecovery mirak;
        MirakPlan mirakPlan;
        bool german, busy;
        string notice, failure;
        int noticeKind;

        string L(string de, string en) { return german ? de : en; }
        public MainForm() : this(new MirakRecovery()) { }
        internal MainForm(MirakRecovery engine)
        {
            mirak = engine;
            UiLanguage.Set(false);
            Text = "BF6 Crash Helper";
            ClientSize = new Size(548, 650); MinimumSize = new Size(540, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f); ForeColor = navy;
            BackColor = Color.FromArgb(247, 249, 251); AutoScaleMode = AutoScaleMode.Dpi;
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(26, 20, 26, 14) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 73));
            Controls.Add(shell);
            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "BF6 Crash Helper", Font = new Font("Segoe UI", 21, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) });
            subtitle.AutoSize = true; subtitle.ForeColor = muted; subtitle.Location = new Point(1, 43); header.Controls.Add(subtitle);
            language.DropDownStyle = ComboBoxStyle.DropDownList; language.Items.AddRange(new object[] { "English", "Deutsch" });
            language.Width = 92; language.SelectedIndex = 0; language.AccessibleName = "Language / Sprache";
            language.Anchor = AnchorStyles.Top | AnchorStyles.Right; header.Controls.Add(language);
            header.Resize += delegate { language.Location = new Point(header.ClientSize.Width - language.Width, 6); };
            language.SelectedIndexChanged += delegate { ChangeLanguage(language.SelectedIndex == 1); };
            shell.Controls.Add(header, 0, 0);

            body.Dock = DockStyle.Fill; body.FlowDirection = FlowDirection.TopDown; body.WrapContents = false; body.AutoScroll = true;
            body.Margin = Padding.Empty; shell.Controls.Add(body, 0, 1);
            Block(introduction, 47); introduction.ForeColor = muted;
            // Folder selection is optional; keep it behind a link rather than
            // making every player navigate an empty installation-path field.
            folderRow.Height = 35;
            gamePath.BorderStyle = BorderStyle.FixedSingle; gamePath.Location = new Point(0, 4); gamePath.AccessibleName = "Steam game folder";
            gamePath.TextChanged += delegate { InvalidateReview(); }; folderRow.Controls.Add(gamePath);
            StyleButton(browse, false); browse.Width = 82; browse.Height = 30; browse.Click += delegate { ChooseFolder(); }; folderRow.Controls.Add(browse);
            StyleButton(mirakCheck, true); mirakCheck.Height = 39; mirakCheck.Margin = new Padding(0, 0, 0, 12);
            mirakCheck.Click += async delegate { await CheckCompatibility(); }; body.Controls.Add(mirakCheck);

            statusBox.BackColor = Color.FromArgb(238, 242, 245); statusBox.Height = 95; statusBox.Margin = new Padding(0, 0, 0, 12);
            statusTitle.Font = new Font(Font, FontStyle.Bold); statusTitle.Location = new Point(12, 10); statusTitle.Height = 23; statusBox.Controls.Add(statusTitle);
            statusDetail.ForeColor = muted; statusDetail.Location = new Point(12, 35); statusDetail.Height = 47; statusDetail.AutoEllipsis = true; statusBox.Controls.Add(statusDetail); body.Controls.Add(statusBox);
            Block(scope, 61); scope.ForeColor = muted;

            actions.Height = 125; actions.Margin = new Padding(0, 0, 0, 4); body.Controls.Add(actions);
            foreach (var consent in new [] { mirakConsent, mirakUndoConsent })
            {
                consent.Location = new Point(0, 0); consent.Height = 42; consent.Checked = false;
                consent.CheckedChanged += delegate { UpdateMirakButtons(); }; actions.Controls.Add(consent);
            }
            StyleButton(mirakApply, true); mirakApply.Location = new Point(0, 52); mirakApply.Height = 40;
            mirakApply.Click += async delegate { await ChangeFiles(false); }; actions.Controls.Add(mirakApply);
            StyleButton(mirakUndo, false); mirakUndo.Location = new Point(0, 52); mirakUndo.Height = 40;
            mirakUndo.Click += async delegate { await ChangeFiles(true); }; actions.Controls.Add(mirakUndo);
            actionNote.Location = new Point(0, 100); actionNote.Height = 23; actionNote.ForeColor = muted; actions.Controls.Add(actionNote);

            var linkRow = new FlowLayoutPanel { Height = 31, WrapContents = true, Margin = Padding.Empty };
            ConfigureLink(details, delegate { ShowDetails(); }); ConfigureLink(backups, delegate { OpenBackups(); });
            ConfigureLink(source, delegate { Open(Repository + "/tree/v0.4.0"); });
            ConfigureLink(folder, delegate { ChooseFolder(); });
            linkRow.Controls.Add(details); linkRow.Controls.Add(backups); linkRow.Controls.Add(source); linkRow.Controls.Add(folder); body.Controls.Add(linkRow);

            var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            progress.Location = new Point(0, 0); progress.Height = 3; progress.Style = ProgressBarStyle.Marquee; progress.Visible = false; footer.Controls.Add(progress);
            footerNote.Location = new Point(0, 10); footerNote.Height = 23; footerNote.ForeColor = muted; footer.Controls.Add(footerNote);
            ConfigureLink(author, delegate { Open(Repository); }); author.Text = "Powered by IamAngusU"; author.Location = new Point(0, 39); footer.Controls.Add(author);
            footer.Resize += delegate { footerNote.Width = progress.Width = footer.ClientSize.Width; };
            shell.Controls.Add(footer, 0, 2);
            body.ClientSizeChanged += delegate { SizeContent(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; noticeKind = 5; RenderLanguage(); } };
            RenderLanguage(); RefreshLocalHistory(); SizeContent();
            Shown += async delegate { if (mirakPlan != null && !String.IsNullOrEmpty(mirakPlan.TransactionId)) await CheckCompatibility(); };
        }

        void Block(Label label, int height) { label.Height = height; label.Margin = new Padding(0, 0, 0, 0); body.Controls.Add(label); }
        void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 1; button.FlatAppearance.BorderColor = navy;
            button.BackColor = primary ? navy : Color.White; button.ForeColor = primary ? Color.White : navy;
            button.Font = new Font(Font, FontStyle.Bold); button.UseVisualStyleBackColor = false; button.Cursor = Cursors.Hand;
        }
        void ConfigureLink(LinkLabel link, EventHandler action)
        {
            link.AutoSize = true; link.LinkColor = Color.FromArgb(68, 86, 101); link.ActiveLinkColor = navy;
            link.LinkBehavior = LinkBehavior.HoverUnderline; link.Margin = new Padding(0, 3, 19, 0); link.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e) { action(sender, e); };
        }
        void SizeContent()
        {
            int width = Math.Max(280, body.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 3);
            body.SuspendLayout();
            foreach (Control control in body.Controls) control.Width = width;
            browse.Location = new Point(width - browse.Width, 1); gamePath.Width = width - browse.Width - 10;
            statusTitle.Width = statusDetail.Width = width - 24;
            mirakConsent.Width = mirakUndoConsent.Width = mirakApply.Width = mirakUndo.Width = actionNote.Width = width;
            body.ResumeLayout();
        }
        void ChangeLanguage(bool useGerman)
        {
            if (busy) return;
            german = useGerman; UiLanguage.Set(german);
            // A language change does not carry a previous opt-in into a new explanation.
            mirakConsent.Checked = mirakUndoConsent.Checked = false;
            RenderLanguage(); SizeContent();
        }
        void RenderLanguage()
        {
            subtitle.Text = "REDSEC / Rapid Royale";
            introduction.Text = L("Ein experimenteller Versuch gegen Mirak-Tal-Ladeabstürze. Bisher auf einem PC erprobt; kein bestätigter allgemeiner Fix.", "An experimental workaround for Mirak Valley loading crashes. Tested on one PC; not a confirmed universal fix.");
            folderLabel.Text = L("Steam-Spielordner · leer lassen für automatische Suche", "Steam game folder · leave empty to detect automatically");
            gamePath.AccessibleName = L("Steam-Spielordner", "Steam game folder"); browse.Text = L("Wählen …", "Browse …");
            mirakCheck.Text = busy ? L("Bitte warten …", "Please wait …") : L("Kompatibilität prüfen", "Check compatibility");
            bool undoMode = RecoveryMode;
            scope.Text = undoMode
                ? L("Rückgängig prüft die Sicherungen und stellt die Originale wieder her. Neuere oder geänderte Dateien werden nicht überschrieben. BF6 muss geschlossen sein.", "Undo checks the backups and restores the originals. Newer or changed files are never overwritten. BF6 must be closed.")
                : L("Sichert genau zwei optionale Mirak-Indexdateien und nimmt sie aus dem Ladepfad. Die Basiskarte bleibt erhalten. BF6 muss geschlossen sein.", "Backs up exactly two optional Mirak index files, then removes them from the loading path. The base map is kept. BF6 must be closed.");
            mirakConsent.Text = L("Ich möchte diesen Versuch mit geprüfter Sicherung anwenden und die Windows-Administratorabfrage öffnen.", "I want to apply this experiment with verified backups and open the Windows administrator prompt.");
            mirakUndoConsent.Text = L("Ich möchte die gespeicherte Änderung rückgängig machen und die Windows-Administratorabfrage öffnen.", "I want to undo the saved change and open the Windows administrator prompt.");
            mirakApply.Text = L("Sichern und Workaround anwenden", "Back up and apply workaround");
            mirakUndo.Text = mirakPlan != null && (mirakPlan.Status == "OriginalsPresent" || mirakPlan.Status == "UpdatedFilesPresent") ? L("Wiederherstellung abschließen", "Finish recovery") : L("Workaround rückgängig machen", "Undo workaround");
            mirakConsent.Visible = mirakApply.Visible = !undoMode;
            mirakUndoConsent.Visible = mirakUndo.Visible = undoMode;
            actionNote.Text = L("Sicherungen bleiben für Rückgängig erhalten.", "Backups are kept so you can undo the change.");
            details.Text = L("Details", "Details"); backups.Text = L("Sicherungen", "Backups"); source.Text = L("Quellcode", "Review source"); folder.Text = L("Spielordner …", "Game folder …");
            footerNote.Text = L("v0.4.0 · Lokal. Kein automatischer Upload.", "v0.4.0 · Local. No automatic uploads.");
            if (failure != null) { statusTitle.Text = L("Vorgang gestoppt", "Operation stopped"); statusDetail.Text = L("Die Prüfung oder Änderung konnte nicht abgeschlossen werden. Die vollständige Meldung steht unter Details.", "The check or change could not be completed. Open Details for the full message."); }
            else if (noticeKind != 0)
            {
                statusTitle.Text = noticeKind == 1 ? L("Workaround angewendet", "Workaround applied") : noticeKind == 2 ? L("Originale wiederhergestellt", "Original files restored") : noticeKind == 3 ? L("Neuere Dateien behalten", "Newer files preserved") : noticeKind == 4 ? L("Wird geprüft", "Checking") : L("Vorgang läuft", "Operation in progress");
                statusDetail.Text = noticeKind == 1 ? L("Du kannst BF6 jetzt testen. Mehrere erfolgreiche Beitritte sind noch kein Dauerfix-Nachweis.", "You can test BF6 now. Several successful joins do not prove a permanent fix.") : noticeKind == 2 ? L("Die Originale sind wieder aktiv. Deine Sicherungen bleiben erhalten.", "The originals are active again. Your backup copies are retained.") : noticeKind == 3 ? L("Das Spiel wurde aktualisiert. Aktuelle Dateien und alte Sicherungen bleiben erhalten.", "The game was updated. Current files and old backups are retained.") : noticeKind == 4 ? L("Steam-Build und Dateihashes werden gelesen. Es wird noch nichts geändert.", "Reading the Steam build and file hashes. No change is being applied.") : L("Bitte warte, bis der Vorgang abgeschlossen ist.", "Please wait until the operation finishes.");
            }
            else if (mirakPlan == null)
            {
                statusTitle.Text = L("Noch nicht geprüft", "Not checked yet");
                statusDetail.Text = L("Nur Steam-Build 25851446 mit passenden Originaldateien. Prüfen verändert keine Spieldateien.", "Only Steam build 25851446 with matching originals is supported. Checking does not change game files.");
            }
            else
            {
                statusTitle.Text = mirakPlan.Status == "AppliedVerified" ? L("Workaround bereits angewendet", "Workaround already applied")
                    : mirakPlan.Status == "OriginalsPresent" ? L("Originale bereits wiederhergestellt", "Original files already restored")
                    : mirakPlan.Status == "UpdatedFilesPresent" ? L("Neuere Spieldateien erkannt", "Newer game files detected")
                    : mirakPlan.Status == "RecoveryBlocked" ? L("Wiederherstellung blockiert", "Recovery blocked")
                    : mirakPlan.Status == "UntrackedMissing" ? L("Dateien fehlen ohne bekannte Sicherung", "Files absent without known backups")
                    : mirakPlan.Status == "RecoveryPending" ? L("Wiederherstellung verfügbar", "Recovery available")
                    : undoMode ? L("Gespeicherte Änderung erkannt", "Saved change detected") : L("Für diesen Versuch geeignet", "Compatible with this experiment");
                statusDetail.Text = mirakPlan.Status == "AppliedVerified" ? L("Vorgang, Sicherungen und aktueller Dateizustand geprüft. Es wird nichts doppelt angewendet. Rückgängig ist verfügbar.", "Record, backups and current file state checked. Nothing will be applied twice. Undo is available.")
                    : mirakPlan.Status == "OriginalsPresent" ? L("Der Workaround ist nicht mehr aktiv. Wiederherstellung abschließen prüft die Originale und schließt den Vorgang.", "The workaround is no longer active. Finish recovery checks the originals and closes the record.")
                    : mirakPlan.Status == "UpdatedFilesPresent" ? L("Aktuelle Dateien bleiben erhalten. Wiederherstellung abschließen fügt keine alten Dateien ein.", "Current files will be kept. Finish recovery does not insert old files.")
                    : mirakPlan.Status == "UntrackedMissing" ? L("Ein früherer Workaround ist möglich, aber nicht belegt. Kein erneutes Anwenden. Vorhandene Backups behalten; Details lesen.", "An earlier workaround is possible, not established. Apply stays disabled. Keep existing backups; read Details.")
                    : mirakPlan.Status == "RecoveryBlocked" ? L("Aktueller Build, Dateien oder Sicherungen konnten nicht bestätigt werden. Alles bleibt erhalten. Details lesen.", "The current build, files or backups could not be verified. Everything is preserved. Read Details.")
                    : mirakPlan.Status == "RecoveryPending" ? L("Ein unterbrochener Vorgang wurde erkannt. Originale und benötigte Sicherungen sind geprüft; Rückgängig kann fortsetzen.", "An interrupted operation was detected. Originals and required backups are checked; Undo can resume recovery.")
                    : undoMode ? L("Der aktuelle Zustand wird geprüft. Vor einer bestätigten Prüfung bleibt Rückgängig gesperrt.", "Checking the current state. Undo stays disabled until the check confirms recovery is available.") : L("Steam-Build und Dateihashes stimmen überein. Lies den Umfang und bestätige erst dann das Anwenden.", "The Steam build and file hashes match. Read the scope, then choose whether to apply it.");
            }
            statusTitle.ForeColor = failure == null ? navy : Color.FromArgb(152, 63, 41);
            UpdateMirakButtons();
        }
        void UpdateMirakButtons()
        {
            mirakCheck.Enabled = language.Enabled = !busy;
            bool undoMode = RecoveryMode;
            gamePath.Enabled = browse.Enabled = folder.Enabled = !busy && !undoMode;
            mirakConsent.Enabled = !busy && mirakPlan != null && mirakPlan.CanApply;
            mirakUndoConsent.Enabled = !busy && undoMode && mirakPlan.CanUndo;
            mirakApply.Enabled = !busy && mirakConsent.Checked && mirakPlan != null && mirakPlan.CanApply;
            mirakUndo.Enabled = !busy && mirakUndoConsent.Checked && undoMode && mirakPlan.CanUndo;
            mirakApply.BackColor = mirakApply.Enabled ? navy : Color.FromArgb(225, 231, 236);
            mirakApply.ForeColor = mirakApply.Enabled ? Color.White : muted;
            mirakApply.FlatAppearance.BorderColor = mirakApply.Enabled ? navy : Color.FromArgb(209, 218, 225);
        }
        bool RecoveryMode { get { return mirakPlan != null && (mirakPlan.CanUndo || !String.IsNullOrEmpty(mirakPlan.TransactionId)); } }
        void SetBusy(bool value, string message)
        {
            busy = value; progress.Visible = value; if (message != null) notice = message;
            RenderLanguage();
        }
        void InvalidateReview()
        {
            if (busy || RecoveryMode) return;
            mirakPlan = null; failure = null; noticeKind = 0;
            mirakConsent.Checked = mirakUndoConsent.Checked = false; RenderLanguage();
        }
        void RefreshLocalHistory()
        {
            try { mirakPlan = mirak.Recorded(); }
            catch (Exception error) { mirakPlan = null; failure = error.Message; }
            RenderLanguage();
        }
        void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = L("Battlefield-6-Steam-Spielordner wählen", "Choose the Battlefield 6 Steam game folder") })
                if (dialog.ShowDialog(this) == DialogResult.OK) gamePath.Text = dialog.SelectedPath;
        }
        async Task CheckCompatibility()
        {
            if (busy) return;
            mirakConsent.Checked = mirakUndoConsent.Checked = false; mirakPlan = null; failure = null; noticeKind = 4;
            string selected = gamePath.Text; SetBusy(true, null);
            try { mirakPlan = await Task.Run(() => mirak.Review(selected)); }
            catch (Exception error) { failure = error.Message; }
            finally { noticeKind = 0; SetBusy(false, null); }
        }
        async Task ChangeFiles(bool undo)
        {
            if (busy || mirakPlan == null || (undo ? !mirakUndoConsent.Checked || !mirakPlan.CanUndo : !mirakConsent.Checked || !mirakPlan.CanApply)) return;
            failure = null; noticeKind = 5; SetBusy(true, null);
            try
            {
                var outcome = await MirakWorker.Execute(mirakPlan, undo);
                if (outcome.Success) noticeKind = outcome.State == "Applied" ? 1 : outcome.State == "UpdatedFilesPreserved" ? 3 : 2;
                else { noticeKind = 0; failure = outcome.Message; }
                notice = outcome.Message;
            }
            catch (Exception error) { noticeKind = 0; failure = error.Message; }
            mirakConsent.Checked = mirakUndoConsent.Checked = false; mirakPlan = null;
            try { var recorded = mirak.Recorded(); mirakPlan = recorded == null ? null : await Task.Run(() => mirak.Review(null)); }
            catch (Exception error) { failure = error.Message; }
            if (mirakPlan != null) noticeKind = 0;
            SetBusy(false, null);
        }
        void ShowDetails()
        {
            using (var dialog = new Form { Text = L("Details und Sicherung", "Details and backup"), Size = new Size(600, 400), StartPosition = FormStartPosition.CenterParent, Font = Font })
            {
                var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, BorderStyle = BorderStyle.None, Font = Font };
                text.Text = L("Experimenteller Zwei-Dateien-Workaround. Kein allgemeiner Fixnachweis.\r\n\r\n", "Experimental two-file workaround. Not a proven universal fix.\r\n\r\n") +
                    (mirakPlan == null ? "" : "Steam build: " + mirakPlan.BuildId + "\r\nState: " + mirakPlan.State + "\r\n" + mirakPlan.GamePath + "\r\n\r\n" + mirakPlan.Message + "\r\n\r\n") +
                    L("Sicherungen: ", "Backups: ") + (mirakPlan == null ? mirak.BackupRoot : mirakPlan.BackupDirectory) + "\r\n\r\n" +
                    @"Update\BF6_SAN2_DLC\Data\Win32\game\glaciermp\levels\mp_tungsten\mp_tungsten.toc + .digest" +
                    (failure == null ? "" : "\r\n\r\n" + failure) + (notice == null ? "" : "\r\n\r\n" + notice);
                dialog.Padding = new Padding(18); dialog.Controls.Add(text); dialog.ShowDialog(this);
            }
        }
        void OpenBackups() { if (Directory.Exists(mirak.BackupRoot)) Open(mirak.BackupRoot); else { failure = L("Noch keine Sicherung vorhanden. Kompatibilität prüfen verändert keine Dateien.", "No backup exists yet. Checking compatibility does not change files."); RenderLanguage(); } }
        void Open(string target) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception error) { failure = error.Message; RenderLanguage(); } }
    }
}

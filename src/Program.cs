using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("BF6 Crash Helper")]
[assembly: AssemblyDescription("BF6 / REDSEC diagnostics and an opt-in reversible Rapid Royale workaround")]
[assembly: AssemblyCompany("IamAngusU")]
[assembly: AssemblyProduct("BF6 Crash Helper")]
[assembly: AssemblyCopyright("Copyright © 2026 IamAngusU")]
[assembly: AssemblyVersion("0.2.0.0")]

namespace Bf6CrashHelper
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (MirakWorker.HandleWorker(args) || TraceRecorder.HandleWorker(args)) return;
            if (args.Length != 0) { MessageBox.Show("Start BF6 Crash Helper without command-line arguments."); return; }
            bool first;
            using (var mutex = new Mutex(true, "Local\\BF6CrashHelper", out first))
            {
                if (!first) { MessageBox.Show("BF6 Crash Helper is already open."); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

    public sealed class MainForm : Form
    {
        const string Repository = "https://github.com/IamAngusU/bf6-crash-helper";
        readonly bool german = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
        readonly Color ink = Color.FromArgb(22, 45, 63), blue = Color.FromArgb(32, 88, 145);
        readonly TabControl tabs = new TabControl();
        readonly TextBox gamePath = new TextBox(), results = new TextBox(), traceStatus = new TextBox();
        readonly Label stage = new Label(), recoveryStatus = new Label();
        readonly ProgressBar progress = new ProgressBar();
        readonly CheckBox scanConsent = new CheckBox(), traceConsent = new CheckBox(), cacheConsent = new CheckBox(), undoConsent = new CheckBox();
        readonly Button scan = new Button(), export = new Button(), preview = new Button(), reset = new Button(), undo = new Button(), traceStart = new Button(), traceStop = new Button();
        readonly NumericUpDown minutes = new NumericUpDown();
        readonly ComboBox mode = new ComboBox(), map = new ComboBox(), access = new ComboBox();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Recovery recovery = new Recovery();
        readonly MirakRecovery mirak = new MirakRecovery();
        readonly CheckBox mirakConsent = new CheckBox(), mirakUndoConsent = new CheckBox();
        readonly Button mirakCheck = new Button(), mirakApply = new Button(), mirakUndo = new Button();
        readonly TextBox mirakSummary = new TextBox();
        MirakPlan mirakPlan;
        ScanReport report;
        string captureDirectory;
        bool busy;

        string L(string de, string en) { return german ? de : en; }

        public MainForm()
        {
            Text = "BF6 Crash Helper";
            Size = new Size(1050, 820); MinimumSize = new Size(940, 730);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10); ForeColor = ink; BackColor = Color.FromArgb(239, 244, 248);
            AutoScaleMode = AutoScaleMode.Dpi;
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(26, 18, 26, 14) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 86)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            Controls.Add(shell);
            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "BF6 Crash Helper", Font = new Font("Segoe UI", 26, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) });
            header.Controls.Add(new Label { Text = L("Abstürze verstehen. Änderungen kontrollieren.", "Understand crashes. Stay in control of changes."), AutoSize = true, Location = new Point(2, 51) });
            shell.Controls.Add(header, 0, 0);
            tabs.Dock = DockStyle.Fill; tabs.Padding = new Point(20, 10); shell.Controls.Add(tabs, 0, 1);
            var rapidPage = Page("Rapid Royale"); var start = Page(L("Prüfen", "Check")); var resultPage = Page(L("Ergebnis", "Results"));
            var capture = Page(L("Aufzeichnen", "Record")); var recover = Page(L("Cache & Rückgängig", "Cache & undo"));
            BuildScan(start); BuildResults(resultPage); BuildCapture(capture); BuildRecovery(recover); BuildMirak(rapidPage);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
            stage.Text = L("Bereit. Du entscheidest, was gestartet wird.", "Ready. You choose what starts."); stage.Dock = DockStyle.Fill; stage.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(stage, 0, 0); progress.Dock = DockStyle.Fill; progress.Visible = false; footer.Controls.Add(progress, 1, 0); shell.Controls.Add(footer, 0, 2);
            var author = new LinkLabel { Text = "IamAngusU · github.com/IamAngusU/bf6-crash-helper", Dock = DockStyle.Fill, AutoSize = true, LinkColor = blue };
            author.LinkClicked += delegate { Open(Repository); }; shell.Controls.Add(author, 0, 3);
            timer.Interval = 1500; timer.Tick += delegate { RefreshCapture(); }; timer.Start();
            FormClosing += HandleClosing;
            RefreshRecovery(); RefreshMirak();
            captureDirectory = TraceRecorder.FindPendingRun();
            RefreshCapture();
        }

        TabPage Page(string name) { var p = new TabPage(name) { BackColor = Color.White, Padding = new Padding(22) }; tabs.TabPages.Add(p); return p; }
        FlowLayoutPanel Flow(TabPage page) { var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true }; page.Controls.Add(p); return p; }
        Label TextBlock(string text, int height) { return new Label { Text = text, Width = 860, Height = height, Margin = new Padding(0, 0, 0, 12) }; }
        void Heading(FlowLayoutPanel flow, string text) { var label = TextBlock(text, 40); label.Font = new Font("Segoe UI", 18, FontStyle.Bold); flow.Controls.Add(label); }
        void ConfigureButton(Button b, string text, EventHandler action)
        {
            b.Text = text; b.AutoSize = true; b.MinimumSize = new Size(180, 40); b.Padding = new Padding(10, 4, 10, 4); b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = blue; b.ForeColor = blue; b.Margin = new Padding(0, 5, 12, 10); b.Click += action;
        }
        void Consent(CheckBox box, string text) { box.Text = text; box.Width = 850; box.Height = 54; box.Checked = false; box.Margin = new Padding(0, 6, 0, 6); }

        void BuildScan(TabPage page)
        {
            var flow = Flow(page);
            Heading(flow, L("Zuerst die Ursache eingrenzen", "Start with the evidence"));
            flow.Controls.Add(TextBlock(L("Die Prüfung liest Steam-Installationsdaten, vorhandene BF6-Absturzdateien und ausgewählte Spieldateien. Sie verändert keine Spieleinstellungen. Nichts wird hochgeladen. Ein Kontozugriff oder der genaue Auslöser lässt sich dadurch nicht immer bestimmen.", "The check reads Steam installation metadata, existing BF6 crash files and selected game files. It does not change game settings. Nothing is uploaded. Account permissions and the exact trigger cannot always be determined."), 75));
            flow.Controls.Add(TextBlock(L("Optional: Spielordner wählen, falls die automatische Suche ihn nicht findet.", "Optional: select the game folder if automatic detection cannot find it."), 24));
            var row = new FlowLayoutPanel { Width = 860, Height = 50, WrapContents = false };
            gamePath.Width = 620; gamePath.Margin = new Padding(0, 8, 12, 0); row.Controls.Add(gamePath);
            var browse = new Button(); ConfigureButton(browse, L("Ordner wählen", "Choose folder"), delegate { using (var dialog = new FolderBrowserDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) gamePath.Text = dialog.SelectedPath; }); row.Controls.Add(browse); flow.Controls.Add(row);
            flow.Controls.Add(TextBlock(L("Kontext für deinen teilbaren Bericht (freiwillig; Angaben werden nicht am Konto geprüft):", "Context for your shareable report (optional; these are your statements, not account checks):"), 30));
            var context = new FlowLayoutPanel { Width = 860, Height = 53, WrapContents = false };
            ConfigureCombo(mode, new [] { L("Modus: unbekannt", "Mode: unknown"), "Battle Royale", "Rapid Royale Solo", "Rapid Royale Trios", L("Anderer Modus", "Other") }, 260);
            ConfigureCombo(map, new [] { L("Karte: unbekannt", "Map: unknown"), "Fort Lyndon", "Mirak Valley", "Vegas Strip", L("Andere Karte", "Other") }, 250);
            ConfigureCombo(access, new [] { L("Spielzugang: keine Angabe", "Access: not stated"), L("Nur kostenloses REDSEC", "Free REDSEC only"), L("Battlefield 6 gekauft", "Own Battlefield 6") }, 285);
            context.Controls.Add(mode); context.Controls.Add(map); context.Controls.Add(access); flow.Controls.Add(context);
            Consent(scanConsent, L("Ich möchte diese lokale Prüfung starten. Sie kann einige Minuten dauern; ihr Ergebnis bleibt zunächst nur in diesem Fenster.", "I want to run this local check. It may take a few minutes; results initially stay in this window.")); flow.Controls.Add(scanConsent);
            ConfigureButton(scan, L("Jetzt prüfen", "Run check"), ScanClicked); scan.Enabled = false; scanConsent.CheckedChanged += delegate { scan.Enabled = scanConsent.Checked && !busy; }; flow.Controls.Add(scan);
        }
        void ConfigureCombo(ComboBox combo, string[] values, int width) { combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.Width = width; combo.Items.AddRange(values); combo.SelectedIndex = 0; combo.Margin = new Padding(0, 3, 12, 0); }

        void BuildResults(TabPage page)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); page.Controls.Add(layout);
            results.Multiline = true; results.ReadOnly = true; results.ScrollBars = ScrollBars.Vertical; results.Dock = DockStyle.Fill; results.BackColor = Color.FromArgb(247, 250, 252); results.BorderStyle = BorderStyle.None;
            results.Text = L("Starte zuerst eine Prüfung. Beobachtete Fakten, mögliche Ursachen und offene Fragen werden getrennt erklärt.", "Run a check first. Observed facts, possible causes and unanswered questions are explained separately."); layout.Controls.Add(results, 0, 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            ConfigureButton(preview, L("Teilbaren Bericht ansehen", "Preview shareable report"), delegate { ShowReport(); }); preview.Enabled = false;
            ConfigureButton(export, L("Bericht speichern", "Save report"), delegate { SaveReport(); }); export.Enabled = false;
            var help = new Button(); ConfigureButton(help, L("EA-Anleitung zum Inhaltsstatus", "EA content-status guide"), delegate { Open("https://help.ea.com/en/articles/battlefield/battlefield-6/error-codes/"); });
            buttons.Controls.Add(preview); buttons.Controls.Add(export); buttons.Controls.Add(help); layout.Controls.Add(buttons, 0, 1);
        }

        void BuildCapture(TabPage page)
        {
            var flow = Flow(page); Heading(flow, L("Den nächsten Absturz aufzeichnen", "Record the next crash"));
            flow.Controls.Add(TextBlock(L("Schließe BF6 vor dem Start der Aufzeichnung. Danach kannst du das Spiel selbst starten. Die Windows-Dateispur speichert Dateipfade, Prozessnamen und Dateioperationen aller Programme. Sie erfasst keine Dateiinhalte und hängt sich nicht als Debugger an das Spiel.", "Close BF6 before starting the recording. Then start the game yourself. The Windows file trace records file paths, process names and file operations across applications. It does not collect file contents or attach a debugger to the game."), 85));
            flow.Controls.Add(TextBlock(L("Die Aufzeichnung braucht einmal Administratorrechte. Sie endet nach dem ersten Spielende, bei Stopp oder nach der gewählten Dauer. Die private ETL-Datei wird nicht in den teilbaren Bericht aufgenommen. Eine Dateispur zeigt keine internen Lizenzentscheidungen und garantiert keine Ursachenfindung.", "Recording needs administrator permission once. It ends after the first game exit, when stopped, or at the chosen duration. The private ETL file is excluded from the shareable report. A file trace cannot reveal internal license decisions or guarantee a diagnosis."), 85));
            var duration = new FlowLayoutPanel { Width = 850, Height = 40 };
            duration.Controls.Add(new Label { Text = L("Maximale Dauer in Minuten:", "Maximum duration in minutes:"), AutoSize = true, Margin = new Padding(0, 6, 12, 0) });
            minutes.Minimum = 1; minutes.Maximum = 30; minutes.Value = 10; minutes.Width = 65; duration.Controls.Add(minutes); flow.Controls.Add(duration);
            Consent(traceConsent, L("Ich möchte die erklärte private Dateispur starten und dafür die Windows-Abfrage für Administratorrechte öffnen.", "I want to start the private file trace described above and open the Windows administrator permission prompt.")); flow.Controls.Add(traceConsent);
            var buttons = new FlowLayoutPanel { Width = 850, Height = 65 };
            ConfigureButton(traceStart, L("Aufzeichnung starten", "Start recording"), delegate { StartCapture(); }); traceStart.Enabled = false;
            ConfigureButton(traceStop, L("Aufzeichnung beenden", "Stop recording"), delegate { StopCapture(); }); traceStop.Enabled = false;
            traceConsent.CheckedChanged += delegate { traceStart.Enabled = traceConsent.Checked && !busy && captureDirectory == null; };
            buttons.Controls.Add(traceStart); buttons.Controls.Add(traceStop); flow.Controls.Add(buttons);
            traceStatus.Multiline = true; traceStatus.ReadOnly = true; traceStatus.ScrollBars = ScrollBars.Vertical; traceStatus.Width = 850; traceStatus.Height = 125; traceStatus.Text = L("Keine Aufzeichnung gestartet.", "No recording started."); flow.Controls.Add(traceStatus);
            var folder = new Button(); ConfigureButton(folder, L("Lokale Aufnahmen öffnen", "Open local recordings"), delegate { string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BF6CrashHelper", "captures"); if (Directory.Exists(p)) Open(p); else MessageBox.Show(this, L("Es gibt noch keine Aufnahmen.", "There are no recordings yet.")); }); flow.Controls.Add(folder);
        }

        void BuildRecovery(TabPage page)
        {
            var flow = Flow(page); Heading(flow, L("Ein Versuch mit sicherer Rückkehr", "Try a change with a way back"));
            flow.Controls.Add(TextBlock(L("Ein leerer BF6-Cache kann bei manchen Abstürzen helfen. Für den untersuchten Rapid-Ladefehler ist das kein bestätigter Fix. Dieser Versuch verschiebt ausschließlich den lokalen BF6-Cache in eine Sicherung und legt einen leeren Cache an. Beim nächsten Spielstart wird er neu aufgebaut; der erste Start kann länger dauern.", "An empty BF6 cache can help some crashes. It is not a confirmed fix for the investigated Rapid loading failure. This experiment only moves the local BF6 cache to a backup and creates an empty cache. The game rebuilds it on its next start; that start may take longer."), 90));
            Consent(cacheConsent, L("Ich möchte den Cache-Versuch durchführen. BF6 muss geschlossen sein. Meine bestehende Sicherung bleibt erhalten; es werden keine Spieldateien gelöscht.", "I want to try the cache change. BF6 must be closed. The original cache is kept in a backup; no game files are deleted.")); flow.Controls.Add(cacheConsent);
            ConfigureButton(reset, L("Cache sichern und neu anlegen", "Back up and reset cache"), ResetClicked); reset.Enabled = false; cacheConsent.CheckedChanged += delegate { reset.Enabled = cacheConsent.Checked && !busy; }; flow.Controls.Add(reset);
            flow.Controls.Add(TextBlock(L("Rückgängig stellt den gesicherten Cache wieder her. Einen inzwischen neu aufgebauten Cache behalten wir in einem separaten Ordner. Bei einer veränderten Sicherung oder einem unerwarteten Pfad wird gestoppt.", "Undo restores the saved cache. A rebuilt cache is kept in a separate folder. A changed backup or an unexpected path stops the operation."), 60));
            Consent(undoConsent, L("Ich möchte die letzte Cache-Änderung rückgängig machen und den aktuellen Cache separat behalten.", "I want to undo the last cache change and keep the current cache separately.")); flow.Controls.Add(undoConsent);
            ConfigureButton(undo, L("Cache-Änderung rückgängig", "Undo cache change"), UndoClicked); undo.Enabled = false; undoConsent.CheckedChanged += delegate { undo.Enabled = undoConsent.Checked && !busy; }; flow.Controls.Add(undo);
            recoveryStatus.Width = 850; recoveryStatus.Height = 55; flow.Controls.Add(recoveryStatus);
            var validate = new Button(); ConfigureButton(validate, L("Steam-Dateiprüfung starten", "Start Steam file verification"), delegate {
                if (MessageBox.Show(this, L("Steam öffnet seine Dateiprüfung und kann Originaldateien erneut herunterladen. Dies betrifft die Steam-Installation und hat keine Rückgängig-Funktion im Helper. Fortfahren?", "Steam will open its file verification and may download original files again. This affects the Steam installation and has no undo in the helper. Continue?"), "Steam", MessageBoxButtons.OKCancel) == DialogResult.OK) Open("steam://validate/2807960");
            }); flow.Controls.Add(validate);
        }

        void BuildMirak(TabPage page)
        {
            var flow = Flow(page); Heading(flow, L("Rapid Royale: experimenteller Workaround", "Rapid Royale: experimental workaround"));
            flow.Controls.Add(TextBlock(L("Bei einem untersuchten Steam/REDSEC-PC wurden Rapid-Beitritte nach diesem Versuch stabiler. Das beweist keinen allgemeinen Fix. Der Versuch unterstützt ausschließlich den geprüften Steam-Build 25851446 und unveränderte Dateihashes. Er kann auch fehlschlagen oder das Laden verhindern.", "On one investigated Steam/REDSEC PC, Rapid joins became stable after this experiment. That does not establish a universal fix. Only Steam build 25851446 with the reviewed file hashes is supported. The workaround may fail or prevent loading."), 75));
            flow.Controls.Add(TextBlock(L("Die Kompatibilitätsprüfung liest den gewählten Steam-Spielordner (sonst automatische Suche), bf6.exe und Kartendateien. Anwenden sichert genau die optionale SAN2-Mirak-TOC und Digest und entfernt sie anschließend aus dem Ladepfad. Die Basiskarte bleibt erhalten. Rückgängig prüft die Sicherung; nach Updates bleiben neuere Dateien erhalten. Schließe BF6 vor Anwenden oder Rückgängig.", "Check reads the selected Steam game folder (or detects it), bf6.exe and map metadata. Apply backs up exactly the optional SAN2 Mirak TOC and digest, then removes them from the loading path. The base map remains. Undo validates backups and keeps newer files after updates. Close BF6 before Apply or Undo."), 105));
            ConfigureButton(mirakCheck, L("Kompatibilität prüfen / Status aktualisieren", "Check compatibility / refresh recovery"), MirakCheckClicked); flow.Controls.Add(mirakCheck);
            mirakSummary.Multiline = true; mirakSummary.ReadOnly = true; mirakSummary.ScrollBars = ScrollBars.Vertical; mirakSummary.Width = 850; mirakSummary.Height = 115; flow.Controls.Add(mirakSummary);
            Consent(mirakConsent, L("Ich möchte diesen experimentellen Zwei-Dateien-Versuch mit Sicherung anwenden und dafür die Windows-Administratorabfrage öffnen.", "I want to apply this experimental two-file workaround with backups and open the Windows administrator prompt.")); flow.Controls.Add(mirakConsent);
            ConfigureButton(mirakApply, L("Sichern und Workaround anwenden", "Back up and apply workaround"), delegate { MirakChange(false); }); mirakApply.Enabled = false; flow.Controls.Add(mirakApply);
            Consent(mirakUndoConsent, L("Ich möchte die gespeicherte Änderung rückgängig machen. Sicherungen und neuere/veränderte Dateien werden behalten. Dafür öffnet sich die Administratorabfrage.", "I want to undo the saved change. Backups and newer/changed files are preserved. This opens the administrator prompt.")); flow.Controls.Add(mirakUndoConsent);
            ConfigureButton(mirakUndo, L("Workaround rückgängig / Vorgang wiederherstellen", "Undo workaround / recover interruption"), delegate { MirakChange(true); }); mirakUndo.Enabled = false; flow.Controls.Add(mirakUndo);
            mirakConsent.CheckedChanged += delegate { UpdateMirakButtons(); }; mirakUndoConsent.CheckedChanged += delegate { UpdateMirakButtons(); };
            var links = new FlowLayoutPanel { Width = 850, Height = 60 };
            var source = new Button(); ConfigureButton(source, L("Quellcode und genaue Änderungen", "Source and exact changes"), delegate { Open(Repository + "/blob/v0.2.0/docs/rapid-workaround.md"); }); links.Controls.Add(source);
            var backups = new Button(); ConfigureButton(backups, L("Lokale Sicherungen öffnen", "Open local backups"), delegate { if (Directory.Exists(mirak.BackupRoot)) Open(mirak.BackupRoot); else MessageBox.Show(this, L("Noch keine Sicherung vorhanden.", "No backup exists yet.")); }); links.Controls.Add(backups); flow.Controls.Add(links);
        }
        async void MirakCheckClicked(object sender, EventArgs e)
        {
            if (busy) return; mirakConsent.Checked = mirakUndoConsent.Checked = false; mirakPlan = null;
            SetBusy(true, L("Build und Dateien prüfen …", "Checking build and files …"));
            string selected = gamePath.Text;
            try { mirakPlan = await Task.Run(() => mirak.Review(selected)); ShowMirakPlan(); stage.Text = L("Prüfung fertig. Noch nichts angewendet.", "Review complete. No change applied."); }
            catch (Exception error) { mirakSummary.Text = error.Message; ShowError(error); }
            finally { SetBusy(false, null); }
        }
        async void MirakChange(bool undoChange)
        {
            if (busy || mirakPlan == null || (undoChange ? !mirakUndoConsent.Checked || !mirakPlan.CanUndo : !mirakConsent.Checked || !mirakPlan.CanApply)) return;
            SetBusy(true, L("Sicherung und Spieldateien prüfen …", "Checking backup and game files …"));
            try
            {
                MirakOutcome outcome = await MirakWorker.Execute(mirakPlan, undoChange);
                string message = outcome.Success ? (outcome.State == "Applied" ? L("Workaround angewendet. Das ist noch kein bestätigter Fix. Du kannst BF6 zum Testen starten.", "Workaround applied. This is not a confirmed fix. You can start BF6 to test it.") : outcome.State == "UpdatedFilesPreserved" ? L("Das Spiel wurde aktualisiert. Neuere Dateien bleiben aktiv; alte Sicherungen wurden behalten.", "The game was updated. Newer files remain active; old backups were retained.") : L("Originale wiederhergestellt. Sicherungen bleiben erhalten.", "Original files restored. Backups retained.")) : outcome.Message;
                stage.Text = message; MessageBox.Show(this, message + (String.IsNullOrEmpty(outcome.BackupDirectory) ? "" : "\n\n" + outcome.BackupDirectory), "BF6 Crash Helper", MessageBoxButtons.OK, outcome.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception error) { ShowError(error); }
            finally { mirakConsent.Checked = mirakUndoConsent.Checked = false; mirakPlan = null; RefreshMirak(); SetBusy(false, null); }
        }
        void RefreshMirak()
        {
            try { var pending = mirak.Latest(); if (pending != null) { mirakPlan = mirak.Review(null); ShowMirakPlan(); } else { mirakSummary.Text = L("Keine offene Änderung. Für einen neuen Versuch zuerst Kompatibilität prüfen.", "No pending change. Check compatibility before a new experiment."); } }
            catch (Exception error) { mirakPlan = null; mirakSummary.Text = error.Message; }
            UpdateMirakButtons();
        }
        void ShowMirakPlan()
        {
            mirakSummary.Text = mirakPlan.Message + "\r\n\r\nSteam build: " + mirakPlan.BuildId + " · " + mirakPlan.State + "\r\n" + mirakPlan.GamePath + "\r\n" + L("Sicherungen: ", "Backups: ") + mirakPlan.BackupDirectory + "\r\n\r\nUpdate\\BF6_SAN2_DLC\\Data\\Win32\\game\\glaciermp\\levels\\mp_tungsten\\mp_tungsten.toc + .digest";
            UpdateMirakButtons();
        }
        void UpdateMirakButtons()
        {
            mirakCheck.Enabled = !busy;
            mirakApply.Enabled = !busy && mirakConsent.Checked && mirakPlan != null && mirakPlan.CanApply;
            mirakUndo.Enabled = !busy && mirakUndoConsent.Checked && mirakPlan != null && mirakPlan.CanUndo;
        }

        async void ScanClicked(object sender, EventArgs e)
        {
            if (!scanConsent.Checked || busy) return;
            SetBusy(true, L("Prüfung startet …", "Starting check …"));
            string folder = gamePath.Text.Trim();
            try
            {
                report = await Task.Run(() => Diagnostics.Scan(folder, message => { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => stage.Text = message)); }));
                var text = new System.Text.StringBuilder(); text.AppendLine(report.Summary); text.AppendLine();
                text.AppendLine("Steam build: " + report.BuildId); text.AppendLine(report.OwnershipStatus); text.AppendLine();
                foreach (var f in report.Findings) { text.AppendLine(f.Level + " · " + f.Title); text.AppendLine(f.Detail); text.AppendLine(); }
                foreach (var c in report.Crashes) {
                    text.AppendLine(c.Time + "  " + c.ExceptionCode + "  " + c.ModuleOffset); text.AppendLine(c.Signature);
                    if (!String.IsNullOrEmpty(c.CasFileId)) text.AppendLine(L("Datei-ID: ", "File ID: ") + c.CasFileId);
                    if (!String.IsNullOrEmpty(c.CasByteOffset) && !String.IsNullOrEmpty(c.CasByteLength)) text.AppendLine(L("Angeforderter Bereich: Offset ", "Requested range: offset ") + c.CasByteOffset + L(", Länge ", ", length ") + c.CasByteLength + " bytes");
                    if (!String.IsNullOrEmpty(c.CasRequestStatus)) text.AppendLine(c.CasRequestStatus);
                    text.AppendLine();
                }
                results.Text = text.ToString(); preview.Enabled = export.Enabled = true; tabs.SelectedIndex = 2;
                stage.Text = L("Prüfung abgeschlossen. Das Ergebnis ist lokal.", "Check complete. Results are local.");
            }
            catch (Exception error) { ShowError(error); }
            finally { scanConsent.Checked = false; SetBusy(false, null); }
        }

        async void ResetClicked(object sender, EventArgs e)
        {
            if (!cacheConsent.Checked || busy) return;
            SetBusy(true, L("Cache sichern …", "Backing up cache …"));
            try { var r = await Task.Run(() => recovery.Reset()); stage.Text = L("Cache-Versuch vorbereitet. Das ist noch kein bestätigter Fix.", "Cache experiment ready. This is not a confirmed fix."); MessageBox.Show(this, L("Original gesichert:\n", "Original saved:\n") + r.Backup + L("\n\nDu kannst BF6 jetzt starten. Mit Rückgängig stellst du den alten Cache wieder her.", "\n\nYou can start BF6 now. Undo restores the original cache.")); }
            catch (Exception error) { ShowError(error); }
            finally { cacheConsent.Checked = false; SetBusy(false, null); RefreshRecovery(); }
        }

        async void UndoClicked(object sender, EventArgs e)
        {
            if (!undoConsent.Checked || busy) return;
            SetBusy(true, L("Sicherung prüfen und wiederherstellen …", "Checking and restoring backup …"));
            try { var r = await Task.Run(() => recovery.Undo()); stage.Text = L("Cache-Änderung rückgängig gemacht.", "Cache change undone."); MessageBox.Show(this, L("Der ursprüngliche Cache ist wieder aktiv. Separat behalten:\n", "The original cache is active again. Kept separately:\n") + (r.KeptAfterUndo ?? L("Keine zusätzlichen Daten verschoben.", "No additional data moved."))); }
            catch (Exception error) { ShowError(error); }
            finally { undoConsent.Checked = false; SetBusy(false, null); RefreshRecovery(); }
        }

        void StartCapture()
        {
            if (!traceConsent.Checked || captureDirectory != null || busy) return;
            try { Recovery.EnsureGameClosed(); captureDirectory = TraceRecorder.Start((int)minutes.Value); traceConsent.Checked = false; traceStart.Enabled = false; traceStop.Enabled = true; stage.Text = L("Windows-Aufzeichnung angefordert. Status beachten.", "Windows recording requested. Check its status."); RefreshCapture(); }
            catch (System.ComponentModel.Win32Exception error) { if (error.NativeErrorCode == 1223) stage.Text = L("Administratorabfrage abgebrochen. Keine neue Aufzeichnung gestartet.", "Administrator prompt cancelled. No new recording started."); else ShowError(error); }
            catch (Exception error) { ShowError(error); }
        }

        void RefreshCapture()
        {
            if (captureDirectory == null) return;
            try { ApplyCaptureStatus(TraceRecorder.ReadStatus(captureDirectory)); }
            catch (Exception error) { traceStatus.Text = error.Message; }
        }

        void ApplyCaptureStatus(string status)
        {
            traceStatus.Text = status;
            if (status.StartsWith("Completed", StringComparison.Ordinal) || status.StartsWith("Failed", StringComparison.Ordinal)) { captureDirectory = null; traceStop.Enabled = false; traceStart.Enabled = traceConsent.Checked && !busy; }
            else if (status.StartsWith("CleanupPending", StringComparison.Ordinal)) { traceStart.Enabled = false; traceStop.Enabled = true; traceStop.Text = L("Bereinigung wiederholen", "Retry cleanup"); stage.Text = L("Windows hat die Bereinigung noch nicht bestätigt. Lokalen Status lesen.", "Windows has not confirmed cleanup. Read the local status."); }
            else { traceStart.Enabled = false; traceStop.Enabled = true; traceStop.Text = L("Aufzeichnung beenden", "Stop recording"); }
        }

        void StopCapture()
        {
            if (captureDirectory == null) return;
            try
            {
                string status = TraceRecorder.ReadStatus(captureDirectory);
                if (status.StartsWith("CleanupPending", StringComparison.Ordinal))
                {
                    Recovery.EnsureGameClosed();
                    if (MessageBox.Show(this, L("Windows hat das Ende dieser Aufnahme nicht bestätigt. Nur die eigene Aufnahme erneut mit Administratorrechten beenden? Andere Aufnahmen bleiben unberührt.", "Windows has not confirmed that this recording ended. Retry stopping only this recording with administrator permission? Other recordings are unaffected."), "BF6 Crash Helper", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                    TraceRecorder.RetryCleanup(captureDirectory);
                }
                else TraceRecorder.RequestStop(captureDirectory);
                stage.Text = L("Stopp angefordert; Status wird geprüft.", "Stop requested; checking status.");
            }
            catch (Exception error) { ShowError(error); }
        }

        object Shareable()
        {
            return new { tool = "BF6 Crash Helper", version = "0.2.0", author = "IamAngusU", repository = Repository,
                createdUtc = DateTime.UtcNow.ToString("o"), userReportedContext = new {
                    mode = new [] { "unknown", "Battle Royale", "Rapid Royale Solo", "Rapid Royale Trios", "other" }[mode.SelectedIndex],
                    map = new [] { "unknown", "Fort Lyndon", "Mirak Valley", "Vegas Strip", "other" }[map.SelectedIndex],
                    access = new [] { "not stated", "Free REDSEC only", "Own Battlefield 6" }[access.SelectedIndex] },
                diagnostics = report.ToShareable(), privacy = "No raw dumps, ETL, absolute paths or account identifiers are included. Review before posting." };
        }

        void ShowReport()
        {
            if (report == null) return;
            using (var dialog = new Form { Text = L("Vorschau: Teilbarer Bericht", "Preview: shareable report"), Size = new Size(850, 650), StartPosition = FormStartPosition.CenterParent })
            { var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true, Dock = DockStyle.Fill, Text = new JavaScriptSerializer().Serialize(Shareable()), Font = new Font("Segoe UI", 10) }; dialog.Controls.Add(text); dialog.ShowDialog(this); }
        }

        void SaveReport()
        {
            if (report == null) return;
            using (var dialog = new SaveFileDialog { FileName = "bf6-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", Filter = "JSON report|*.json" })
                if (dialog.ShowDialog(this) == DialogResult.OK) { try { File.WriteAllText(dialog.FileName, new JavaScriptSerializer().Serialize(Shareable()), new System.Text.UTF8Encoding(false)); stage.Text = L("Bericht gespeichert. Bitte vor dem Teilen ansehen.", "Report saved. Review it before sharing."); } catch (Exception error) { ShowError(error); } }
        }

        void RefreshRecovery() { try { var r = recovery.Latest(); recoveryStatus.Text = r == null ? L("Keine offene Cache-Änderung.", "No pending cache change.") : L("Letzte Änderung: ", "Last change: ") + r.Created + " · " + r.State; } catch (Exception error) { recoveryStatus.Text = error.Message; } }
        void SetBusy(bool value, string message) { busy = value; progress.Visible = value; progress.Style = ProgressBarStyle.Marquee; scan.Enabled = scanConsent.Checked && !value; reset.Enabled = cacheConsent.Checked && !value; undo.Enabled = undoConsent.Checked && !value; traceStart.Enabled = traceConsent.Checked && !value && captureDirectory == null; UpdateMirakButtons(); if (message != null) stage.Text = message; }
        void ShowError(Exception error) { stage.Text = L("Vorgang gestoppt. Details im Hinweis.", "Operation stopped. See the message for details."); MessageBox.Show(this, error.Message, "BF6 Crash Helper", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        void Open(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception error) { ShowError(error); } }
        void HandleClosing(object sender, FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; MessageBox.Show(this, L("Bitte warte, bis der laufende Vorgang abgeschlossen ist.", "Wait for the current operation to finish.")); return; }
            if (captureDirectory != null)
            {
                string status = TraceRecorder.ReadStatus(captureDirectory);
                if (status.StartsWith("CleanupPending", StringComparison.Ordinal))
                {
                    if (MessageBox.Show(this, L("Die Bereinigung ist nicht bestätigt. Schließe BF6 und verwende zuerst Bereinigung wiederholen. Das Schließen dieses Fensters bestätigt keine beendete Windows-Aufnahme. Trotzdem schließen?", "Cleanup is not confirmed. Close BF6 and use Retry cleanup first. Closing this window does not confirm that Windows recording ended. Close anyway?"), "BF6 Crash Helper", MessageBoxButtons.OKCancel) != DialogResult.OK) e.Cancel = true;
                    return;
                }
                var answer = MessageBox.Show(this, L("Eine Aufnahme ist noch aktiv. Jetzt Stopp anfordern? Bei Nein versucht sie an ihrer gewählten Grenze automatisch zu enden. Prüfe anschließend den lokalen Status.", "A recording is still active. Request stop now? If you choose No, it will attempt to end automatically at its selected limit. Check its local status afterwards."), "BF6 Crash Helper", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) e.Cancel = true;
                if (answer == DialogResult.Yes) { try { TraceRecorder.RequestStop(captureDirectory); } catch (Exception error) { e.Cancel = true; ShowError(error); } }
            }
        }
    }
}

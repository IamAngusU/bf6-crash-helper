using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Bf6CrashHelper
{
    internal static class Tests
    {
        static int count;
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--preview") { RenderPreview(args[1]); return 0; }
                Run("cache reset and undo preserve both generations", ResetUndo);
                Run("repeated experiments undo in order", RepeatUndo);
                Run("modified backup stops undo without touching current data", ChangedBackup);
                Run("tampered journal path is rejected", TamperedPath);
                Run("interrupted backup recovers", InterruptedBackup);
                Run("interrupted undo reconciles", InterruptedUndo);
                Run("running game guard runs before writes", Guard);
                Run("all initial consents are off", ConsentDefaults);
                Run("Mirak actions require the corresponding reviewed plan and consent", MirakConsentGates);
                Run("a recovered recording can be stopped or cleaned up", CaptureControls);
                Run("compact UI language, review invalidation and consent gates", CompactControls);
                Assert(global::DiagnosticsTests.Main() == 0, "Diagnostic tests failed.");
                global::TraceRecorderTests.RunAll();
                global::MirakTests.RunAll();
                Console.WriteLine("PASS: " + count + " recovery/UI checks plus diagnostic checks.");
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
        }
        static void Run(string name, Action test) { test(); count++; Console.WriteLine("PASS " + name); }
        static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
        static void Throws(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("Expected a refusal."); }
        sealed class Fixture : IDisposable
        {
            public string Root = Path.Combine(Path.GetTempPath(), "BF6CrashHelper-test-" + Guid.NewGuid().ToString("N"));
            public string Cache, State; public Recovery Recovery;
            public Fixture() { Cache = Path.Combine(Root, "Battlefield 6", "cache"); State = Path.Combine(Root, "state"); Directory.CreateDirectory(Cache); File.WriteAllText(Path.Combine(Cache, "original"), "original-data"); Recovery = new Recovery(Cache, State, delegate { }); }
            public void Journal(RecoveryRecord r) { File.WriteAllText(Path.Combine(State, "recovery.json"), new JavaScriptSerializer().Serialize(new [] { r })); }
            public void Dispose() { string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; if (!Path.GetFullPath(Root).StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Root).StartsWith("BF6CrashHelper-test-", StringComparison.Ordinal)) throw new Exception("Unsafe test cleanup path"); Directory.Delete(Root, true); }
        }
        static void ResetUndo() { using (var f = new Fixture()) { var r = f.Recovery.Reset(); Assert(File.Exists(Path.Combine(r.Backup, "original")), "Original missing"); Assert(Directory.GetFileSystemEntries(f.Cache).Length == 0, "Cache not empty"); File.WriteAllText(Path.Combine(f.Cache, "rebuilt"), "new-data"); var undo = f.Recovery.Undo(); Assert(File.ReadAllText(Path.Combine(f.Cache, "original")) == "original-data", "Original changed"); Assert(File.ReadAllText(Path.Combine(undo.KeptAfterUndo, "rebuilt")) == "new-data", "Rebuilt cache lost"); Assert(f.Recovery.Latest() == null, "Not restored"); } }
        static void RepeatUndo() { using (var f = new Fixture()) { f.Recovery.Reset(); File.WriteAllText(Path.Combine(f.Cache, "second"), "second"); f.Recovery.Reset(); File.WriteAllText(Path.Combine(f.Cache, "third"), "third"); f.Recovery.Undo(); Assert(File.Exists(Path.Combine(f.Cache, "second")), "Wrong first undo"); f.Recovery.Undo(); Assert(File.Exists(Path.Combine(f.Cache, "original")), "Wrong final undo"); } }
        static void ChangedBackup() { using (var f = new Fixture()) { var r = f.Recovery.Reset(); File.WriteAllText(Path.Combine(r.Backup, "original"), "changed"); File.WriteAllText(Path.Combine(f.Cache, "current"), "keep"); Throws(delegate { f.Recovery.Undo(); }); Assert(File.ReadAllText(Path.Combine(f.Cache, "current")) == "keep", "Current data changed"); } }
        static void TamperedPath() { using (var f = new Fixture()) { var r = f.Recovery.Reset(); r.Backup = Path.GetTempPath(); f.Journal(r); Throws(delegate { f.Recovery.Undo(); }); Assert(Directory.Exists(f.Cache), "Cache removed"); } }
        static void InterruptedBackup() { using (var f = new Fixture()) { var r = f.Recovery.Reset(); Directory.Move(f.Cache, Path.Combine(f.Root, "new-cache-held")); r.State = "BackedUp"; f.Journal(r); f.Recovery.Undo(); Assert(File.Exists(Path.Combine(f.Cache, "original")), "Interrupted backup not recovered"); } }
        static void InterruptedUndo() { using (var f = new Fixture()) { f.Recovery.Reset(); var r = f.Recovery.Undo(); r.State = "UndoPending"; f.Journal(r); f.Recovery.Undo(); Assert(File.Exists(Path.Combine(f.Cache, "original")), "Interrupted undo not recovered"); Assert(f.Recovery.Latest() == null, "Journal not reconciled"); } }
        static void Guard() { using (var f = new Fixture()) { var r = new Recovery(f.Cache, f.State, delegate { throw new IOException("game is active"); }); Throws(delegate { r.Reset(); }); Assert(File.Exists(Path.Combine(f.Cache, "original")) && !Directory.Exists(f.State), "Writes before guard"); } }
        static void ConsentDefaults() { using (var form = new ToolsForm()) { foreach (string n in new [] { "scanConsent", "traceConsent", "cacheConsent", "undoConsent", "mirakConsent", "mirakUndoConsent" }) { var c = (CheckBox)typeof(ToolsForm).GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); Assert(!c.Checked, "Consent preselected: " + n); } foreach (string n in new [] { "scan", "traceStart", "reset", "undo", "export", "mirakApply", "mirakUndo" }) { var b = (Button)typeof(ToolsForm).GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); Assert(!b.Enabled, "Action initially enabled: " + n); } } }
        static void CaptureControls()
        {
            using (var form = new ToolsForm())
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var field = typeof(ToolsForm).GetField("captureDirectory", flags);
                var apply = typeof(ToolsForm).GetMethod("ApplyCaptureStatus", flags);
                var stop = (Button)typeof(ToolsForm).GetField("traceStop", flags).GetValue(form);
                var start = (Button)typeof(ToolsForm).GetField("traceStart", flags).GetValue(form);
                field.SetValue(form, "synthetic-state-no-files");
                apply.Invoke(form, new object[] { "Recording\r\nSynthetic held worker." });
                Assert(stop.Enabled && !start.Enabled, "Recovered recording has no working stop action");
                apply.Invoke(form, new object[] { "CleanupPending\r\nSynthetic orphan." });
                Assert(stop.Enabled && !start.Enabled, "Cleanup retry is not reachable");
                apply.Invoke(form, new object[] { "Completed\r\nSynthetic finished recording." });
                Assert(!stop.Enabled && field.GetValue(form) == null, "Finished recording was not released");
            }
        }
        static void MirakConsentGates()
        {
            using (var form = new ToolsForm())
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var plan = typeof(ToolsForm).GetField("mirakPlan", flags);
                var applyConsent = (CheckBox)typeof(ToolsForm).GetField("mirakConsent", flags).GetValue(form);
                var undoConsent = (CheckBox)typeof(ToolsForm).GetField("mirakUndoConsent", flags).GetValue(form);
                var apply = (Button)typeof(ToolsForm).GetField("mirakApply", flags).GetValue(form);
                var undo = (Button)typeof(ToolsForm).GetField("mirakUndo", flags).GetValue(form);
                applyConsent.Checked = undoConsent.Checked = true;
                Assert(!apply.Enabled && !undo.Enabled, "Consent alone enabled a change without review");
                plan.SetValue(form, new MirakPlan { CanApply = true });
                typeof(ToolsForm).GetMethod("UpdateMirakButtons", flags).Invoke(form, null);
                Assert(apply.Enabled && !undo.Enabled, "Apply review authorized an Undo");
                typeof(ToolsForm).GetMethod("SetBusy", flags).Invoke(form, new object[] { true, null });
                Assert(!apply.Enabled && !undo.Enabled, "Busy operation allowed a second mutation");
                plan.SetValue(form, new MirakPlan { CanUndo = true });
                typeof(ToolsForm).GetMethod("SetBusy", flags).Invoke(form, new object[] { false, null });
                Assert(!apply.Enabled && undo.Enabled, "Undo review authorized an Apply");
                undoConsent.Checked = false; Assert(!undo.Enabled, "Undo remained enabled without consent");
            }
        }
        static void RenderPreview(string directory)
        {
            Directory.CreateDirectory(directory);
            Application.EnableVisualStyles();
            using (var compact = new MainForm())
            {
                compact.StartPosition = FormStartPosition.Manual; compact.Location = new System.Drawing.Point(-30000, -30000); compact.ShowInTaskbar = false; compact.Show(); Application.DoEvents();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var picker = (ComboBox)typeof(MainForm).GetField("language", flags).GetValue(compact);
                for (int i = 0; i < 2; i++)
                {
                    picker.SelectedIndex = i; Application.DoEvents();
                    using (var bitmap = new System.Drawing.Bitmap(compact.Width, compact.Height)) { compact.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(directory, i == 0 ? "compact-en.png" : "compact-de.png")); }
                }
                typeof(MainForm).GetField("mirakPlan", flags).SetValue(compact, new MirakPlan { CanUndo = true, State = "Applying" });
                for (int i = 0; i < 2; i++)
                {
                    picker.SelectedIndex = i; typeof(MainForm).GetMethod("RenderLanguage", flags).Invoke(compact, null); Application.DoEvents();
                    using (var bitmap = new System.Drawing.Bitmap(compact.Width, compact.Height)) { compact.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(directory, i == 0 ? "compact-recovery-en.png" : "compact-recovery-de.png")); }
                }
                compact.Close();
            }
            using (var form = new ToolsForm())
            {
                form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-30000, -30000); form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                var tabs = (TabControl)typeof(ToolsForm).GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                for (int i = 0; i < tabs.TabPages.Count; i++) { form.Height = i == 0 ? 1180 : 820; tabs.SelectedIndex = i; Application.DoEvents(); using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(directory, "preview-" + i + ".png")); } }
                form.Close();
            }
        }
        static void CompactControls()
        {
            ThreadCultureGerman();
            using (var form = new MainForm())
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(MainForm);
                var language = (ComboBox)type.GetField("language", flags).GetValue(form);
                var applyConsent = (CheckBox)type.GetField("mirakConsent", flags).GetValue(form);
                var undoConsent = (CheckBox)type.GetField("mirakUndoConsent", flags).GetValue(form);
                var apply = (Button)type.GetField("mirakApply", flags).GetValue(form);
                var undo = (Button)type.GetField("mirakUndo", flags).GetValue(form);
                var plan = type.GetField("mirakPlan", flags);
                var refresh = type.GetMethod("RenderLanguage", flags);
                Assert(language.SelectedIndex == 0 && System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en", "English must be the default even on a German Windows account");
                Assert(!applyConsent.Checked && !undoConsent.Checked && !apply.Enabled && !undo.Enabled, "Compact initial consent/action gate failed");
                applyConsent.Checked = undoConsent.Checked = true;
                Assert(!apply.Enabled && !undo.Enabled, "Compact consent authorized an unreviewed operation");
                plan.SetValue(form, new MirakPlan { CanApply = true }); refresh.Invoke(form, null);
                Assert(apply.Enabled && !undo.Enabled, "Compact Apply review authorized Undo");
                language.SelectedIndex = 1;
                Assert(!applyConsent.Checked && !undoConsent.Checked && !apply.Enabled && apply.Text.Contains("anwenden"), "Language change carried consent or failed to translate");
                applyConsent.Checked = true;
                type.GetMethod("SetBusy", flags).Invoke(form, new object[] { true, null });
                Assert(!apply.Enabled && !undo.Enabled && !language.Enabled, "Busy compact UI allowed a mutation or language change");
                type.GetMethod("SetBusy", flags).Invoke(form, new object[] { false, null });
                var path = (TextBox)type.GetField("gamePath", flags).GetValue(form); path.Text = "synthetic-different-library";
                Assert(plan.GetValue(form) == null && !applyConsent.Checked && !apply.Enabled, "Changing the installation kept a stale review");
                plan.SetValue(form, new MirakPlan { CanUndo = true, State = "Applying" }); refresh.Invoke(form, null); undoConsent.Checked = true;
                Assert(undo.Enabled && !apply.Enabled && undo.Text.Contains("rückgängig"), "Interrupted recovery is unreachable or authorized Apply");
                language.SelectedIndex = 0;
                Assert(!undoConsent.Checked && !undo.Enabled && undo.Text == "Undo workaround", "Language switch failed to clear Undo consent");
                Assert(form.ClientSize.Width <= 560 && form.ClientSize.Height <= 680, "Default surface is not compact");
            }
        }
        static void ThreadCultureGerman() { System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE"); }
    }
}

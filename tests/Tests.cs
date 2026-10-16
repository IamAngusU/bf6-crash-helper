using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Bf6CrashHelper
{
    internal static class Tests
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--preview") { RenderPreview(args[1]); return 0; }
                global::MirakTests.RunAll(); CompactControls(); FixOnlySurface();
                Console.WriteLine("PASS compact UI language, consent, status recognition and fix-only assembly scope; no live game/UAC/ETW actions.");
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static MainForm EmptyForm()
        {
            string store = Path.Combine(Path.GetTempPath(), "BF6Mirak-ui-" + Guid.NewGuid().ToString("N"));
            return new MainForm(new MirakRecovery(store, MirakSpec.Production(), delegate { }, delegate { }));
        }
        static void FixOnlySurface()
        {
            var assembly = typeof(MainForm).Assembly;
            foreach (string type in new [] { "ToolsForm", "TraceRecorder", "Recovery", "Diagnostics", "ScanReport" })
                Assert(assembly.GetType("Bf6CrashHelper." + type) == null, "Unrelated tool shipped in fix-only executable: " + type);
            Assert(typeof(MainForm).GetField("tools", BindingFlags.Instance | BindingFlags.NonPublic) == null, "More tools entry still exists");
            using (var form = EmptyForm())
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var plan = typeof(MainForm).GetField("mirakPlan", flags);
                var render = typeof(MainForm).GetMethod("RenderLanguage", flags);
                var title = (Label)typeof(MainForm).GetField("statusTitle", flags).GetValue(form);
                var undo = (Button)typeof(MainForm).GetField("mirakUndo", flags).GetValue(form);
                var apply = (Button)typeof(MainForm).GetField("mirakApply", flags).GetValue(form);
                Assert(((LinkLabel)typeof(MainForm).GetField("author", flags).GetValue(form)).Text == "Powered by IamAngusU", "Attribution missing");
                plan.SetValue(form, new MirakPlan { Status = "AppliedVerified", State = "Applied", CanUndo = true, TransactionId = "known" }); render.Invoke(form, null);
                Assert(title.Text == "Workaround already applied" && !apply.Enabled, "Verified applied state is not recognized");
                plan.SetValue(form, new MirakPlan { Status = "OriginalsPresent", CanUndo = true, TransactionId = "known" }); render.Invoke(form, null);
                Assert(title.Text == "Original files already restored" && undo.Text == "Finish recovery", "Restored files mislabeled active");
                plan.SetValue(form, new MirakPlan { Status = "RecoveryBlocked", TransactionId = "known" }); render.Invoke(form, null);
                Assert(title.Text == "Recovery blocked" && !undo.Enabled && !apply.Enabled, "Blocked recovery authorized changes");
                plan.SetValue(form, new MirakPlan { Status = "UntrackedMissing" }); render.Invoke(form, null);
                Assert(title.Text == "Files absent without known backups" && !apply.Enabled && !undo.Enabled, "Missing files alone called fixed");
            }
        }
        static void RenderPreview(string directory)
        {
            Directory.CreateDirectory(directory); Application.EnableVisualStyles();
            using (var form = EmptyForm())
            {
                form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-30000, -30000); form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var picker = (ComboBox)typeof(MainForm).GetField("language", flags).GetValue(form);
                for (int state = 0; state < 2; state++)
                {
                    typeof(MainForm).GetField("mirakPlan", flags).SetValue(form, state == 0 ? null : new MirakPlan { Status = "AppliedVerified", State = "Applied", CanUndo = true, TransactionId = "synthetic-preview" });
                    for (int i = 0; i < 2; i++)
                    {
                        picker.SelectedIndex = i; typeof(MainForm).GetMethod("RenderLanguage", flags).Invoke(form, null); Application.DoEvents();
                        string name = state == 0 ? "compact-" : "applied-"; name += i == 0 ? "en.png" : "de.png";
                        using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(directory, name)); }
                    }
                }
                form.Close();
            }
        }
        static void CompactControls()
        {
            ThreadCultureGerman();
            using (var form = EmptyForm())
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

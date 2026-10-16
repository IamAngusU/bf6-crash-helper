using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using Bf6CrashHelper;

internal static class MirakTests
{
    static int count, scenarios;
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Mirak: " + message); count++; }
    static void Refused(Action action)
    { try { action(); } catch (IOException) { count++; return; } catch (Win32Exception) { count++; return; } catch (ArgumentException) { count++; return; } throw new Exception("Mirak: unsafe operation was not refused."); }
    static void Run(string name, Action body) { body(); scenarios++; Console.WriteLine("PASS Mirak " + name); }
    internal static void RunAll()
    {
        Run("apply/undo preserve bytes and backups", Happy);
        Run("review is read only; unsupported and incomplete Steam states refuse", Preflight);
        Run("running game guard precedes all writes", Running);
        Run("consent token binds installation and action", ReviewTokens);
        Run("backup and mid-removal failures automatically restore", FaultRecovery);
        Run("game starting during operation leaves recoverable state", GameStarts);
        Run("changed original during backup is preserved", ChangedDuringApply);
        Run("validated guards and backups stay locked through mutation", StableInputs);
        Run("changed guard after review prevents mutation", ChangedGuard);
        Run("corrupt/missing backup refuses entire rollback", BadBackup);
        Run("changed current file is never overwritten", CurrentConflict);
        Run("new Steam files supersede old-build backups", Updated);
        Run("update with missing files directs to Steam", UpdateMissing);
        Run("Steam-restored originals reconcile without overwriting", SteamRestored);
        Run("interrupted backup/removal/undo reconcile", Interrupted);
        Run("partial staging files do not block safe retry", PartialStaging);
        Run("tampered and malformed recovery history fail closed", JournalTampering);
        Run("concurrent operation refuses without mutation", Concurrent);
        Run("locked and read-only originals are retained", LockedOriginal);
        Run("hard linked originals are refused", HardLink);
        Run("directory links are refused", Junction);
        Run("worker request bounds, identity and expiry", Requests);
        Console.WriteLine("PASS: " + count + " Mirak assertions in " + scenarios + " scenarios; synthetic Steam libraries only, no UAC/game/ETW actions.");
    }

    sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "BF6Mirak-fixture-" + Guid.NewGuid().ToString("N"));
        internal string Game, Store, Manifest;
        internal MirakSpec Spec;
        internal byte[][] Originals;
        internal Fixture()
        {
            Game = Path.Combine(Root, "library", "steamapps", "common", "Battlefield 6"); Store = Path.Combine(Root, "independent-backup-store");
            Manifest = Path.Combine(Root, "library", "steamapps", "appmanifest_2807960.acf");
            Spec = MirakSpec.Production(); Directory.CreateDirectory(Game);
            Originals = new [] { Encoding.UTF8.GetBytes("synthetic map index"), Encoding.UTF8.GetBytes("synthetic digest") };
            Spec.TargetHashes = new string[2]; Spec.TargetSizes = new long[2];
            for (int i = 0; i < 2; i++) { Write(Target(i), Originals[i]); Spec.TargetHashes[i] = Sha(Target(i)); Spec.TargetSizes[i] = Originals[i].Length; }
            Spec.GuardHashes = new string[3]; Spec.GuardSizes = new long[3];
            for (int i = 0; i < 3; i++) { string path = Path.Combine(Game, Spec.Guards[i]); Write(path, Encoding.UTF8.GetBytes("synthetic guard " + i)); Spec.GuardHashes[i] = Sha(path); Spec.GuardSizes[i] = new FileInfo(path).Length; }
            Build(Spec.Build, "4", "Battlefield 6");
        }
        internal void Build(string build, string state, string directory)
        { File.WriteAllText(Manifest, "\"AppState\" { \"appid\" \"2807960\" \"installdir\" \"" + directory + "\" \"buildid\" \"" + build + "\" \"StateFlags\" \"" + state + "\" }"); }
        internal MirakRecovery Engine(Action<string> checkpoint = null, Action closed = null) { return new MirakRecovery(Store, Spec, closed ?? delegate { }, checkpoint ?? delegate { }); }
        internal MirakRecord Apply(MirakRecovery engine = null) { var e = engine ?? Engine(); var plan = e.Review(Game); return e.Apply(Game, plan.Fingerprint); }
        internal MirakRecord Undo() { var e = Engine(); return e.Undo(e.Review(Game).Fingerprint); }
        internal string Target(int i) { return Path.Combine(Game, Spec.Targets[i]); }
        internal string Backup(MirakRecord r, int i) { return Path.Combine(Store, r.Id, i + ".original"); }
        internal void Journal(MirakRecord record) { Directory.CreateDirectory(Store); File.WriteAllText(Path.Combine(Store, "mirak.json"), new JavaScriptSerializer().Serialize(new [] { record })); }
        internal MirakRecord Pending(string state)
        { var r = new MirakRecord { Id = Guid.NewGuid().ToString("N"), GamePath = Game, BuildId = Spec.Build, State = state, CreatedUtc = DateTime.UtcNow.ToString("o") }; Directory.CreateDirectory(Path.Combine(Store, r.Id)); Journal(r); return r; }
        internal void Original(int i) { Write(Target(i), Originals[i]); }
        internal bool Same(int i) { return File.Exists(Target(i)) && File.ReadAllBytes(Target(i)).SequenceEqual(Originals[i]); }
        public void Dispose()
        {
            string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\BF6Mirak-fixture-";
            if (!Path.GetFullPath(Root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe fixture cleanup.");
            foreach (var path in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }
    static void Write(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); }
    static string Sha(string path) { using (var file = File.OpenRead(path)) return MirakRecovery.Hash(file); }
    static void Happy()
    {
        using (var f = new Fixture())
        {
            var plan = f.Engine().Review(f.Game); Check(plan.CanApply && !Directory.Exists(f.Store), "review wrote recovery data");
            var r = f.Apply(); Check(!File.Exists(f.Target(0)) && !File.Exists(f.Target(1)), "pair not removed");
            Check(File.ReadAllBytes(f.Backup(r, 0)).SequenceEqual(f.Originals[0]) && File.ReadAllBytes(f.Backup(r, 1)).SequenceEqual(f.Originals[1]), "backup content");
            Check(r.State == "Applied" && f.Engine().Latest().Id == r.Id, "applied journal missing");
            Refused(delegate { f.Apply(); });
            Check(f.Undo().State == "Restored" && f.Same(0) && f.Same(1), "undo bytes");
            Check(File.Exists(f.Backup(r, 0)) && File.Exists(f.Backup(r, 1)) && f.Engine().Latest() == null, "backups/history lost");
            Refused(delegate { f.Undo(); });
            var second = f.Apply(); Check(second.Id != r.Id, "reapply reused history"); f.Undo(); Check(File.Exists(f.Backup(r, 0)), "reapply removed earlier backup");
        }
    }
    static void Preflight()
    {
        using (var f = new Fixture())
        {
            f.Build("99999", "4", "Battlefield 6"); Refused(delegate { f.Engine().Review(f.Game); }); Check(f.Same(0) && f.Same(1) && !Directory.Exists(f.Store), "unknown build changed data");
            f.Build(f.Spec.Build, "1028", "Battlefield 6"); Refused(delegate { f.Engine().Review(f.Game); });
            f.Build(f.Spec.Build, "4", "../outside"); Refused(delegate { f.Engine().Review(f.Game); });
            f.Build(f.Spec.Build, "4", "Battlefield 6"); File.Delete(f.Target(1)); Refused(delegate { f.Engine().Review(f.Game); }); Check(f.Same(0) && !Directory.Exists(f.Store), "partial input wrote backups");
            Refused(delegate { f.Engine().Review(Path.Combine(f.Game, new string('q', 75))); }); Check(f.Same(0) && !Directory.Exists(f.Store), "unrestorable long path permitted writes");
        }
    }
    static void Running()
    { using (var f = new Fixture()) { var e = f.Engine(null, delegate { throw new IOException("running"); }); Refused(delegate { e.Apply(f.Game, "x"); }); Check(f.Same(0) && f.Same(1) && !Directory.Exists(f.Store), "guard after writes"); } }
    static void ReviewTokens()
    {
        using (var f = new Fixture()) { Refused(delegate { f.Engine().Apply(f.Game, new string('0', 64)); }); Check(f.Same(0) && !File.Exists(Path.Combine(f.Store, "mirak.json")), "stale review mutated"); var r = f.Apply(); Refused(delegate { f.Engine().Undo(new string('0', 64)); }); Check(!File.Exists(f.Target(0)) && File.Exists(f.Backup(r, 0)), "stale undo mutated"); f.Undo(); }
    }
    static void FaultRecovery()
    {
        foreach (string phase in new [] { "Backup0", "Backup1", "BackedUp", "Removed0", "Removed1" })
            using (var f = new Fixture()) { var e = f.Engine(delegate(string at) { if (at == phase) throw new IOException("injected " + phase); }); Refused(delegate { f.Apply(e); }); Check(f.Same(0) && f.Same(1) && f.Engine().Latest() == null, "failed automatic recovery at " + phase); Check(Directory.GetFiles(f.Store, "*.original", SearchOption.AllDirectories).Length >= 1, "failure discarded backup"); }
    }
    static void GameStarts()
    {
        using (var f = new Fixture()) { bool running = false; var e = f.Engine(delegate(string phase) { if (phase == "Backup0") running = true; }, delegate { if (running) throw new IOException("game started"); }); Refused(delegate { f.Apply(e); }); Check(f.Same(0) && f.Same(1) && f.Engine().Latest() != null, "running game state not preserved"); f.Undo(); Check(f.Engine().Latest() == null, "retry after game exit failed"); }
    }
    static void ChangedDuringApply()
    {
        using (var f = new Fixture()) { var e = f.Engine(delegate(string phase) { if (phase == "Backup0") File.WriteAllText(f.Target(1), "new external data"); }); Refused(delegate { f.Apply(e); }); Check(f.Same(0) && File.ReadAllText(f.Target(1)) == "new external data", "external data overwritten"); Check(f.Engine().Latest() != null, "conflict not recoverable"); f.Original(1); f.Undo(); }
    }
    static void ChangedGuard()
    {
        using (var f = new Fixture()) { var plan = f.Engine().Review(f.Game); File.WriteAllText(Path.Combine(f.Game, f.Spec.Guards[1]), "changed map layout"); Refused(delegate { f.Engine().Apply(f.Game, plan.Fingerprint); }); Check(f.Same(0) && f.Same(1), "changed base metadata mutated targets"); }
        using (var f = new Fixture()) { f.Apply(); File.WriteAllText(Path.Combine(f.Game, f.Spec.Guards[2]), "changed base"); Refused(delegate { f.Undo(); }); Check(!File.Exists(f.Target(0)) && !File.Exists(f.Target(1)), "stale base received old overlay"); }
    }
    static void StableInputs()
    {
        using (var f = new Fixture()) { var e = f.Engine(delegate(string phase) { if (phase == "BackedUp") { var r = f.Engine().Latest(); File.WriteAllText(f.Backup(r, 0), "racing backup write"); } }); Refused(delegate { f.Apply(e); }); Check(f.Same(0) && f.Same(1) && f.Engine().Latest() == null, "backup race escaped lock or recovery"); }
        using (var f = new Fixture()) { var e = f.Engine(delegate(string phase) { if (phase == "Backup0") File.WriteAllText(Path.Combine(f.Game, f.Spec.Guards[1]), "racing guard write"); }); Refused(delegate { f.Apply(e); }); Check(f.Same(0) && f.Same(1), "guard race escaped lock"); }
    }
    static void BadBackup()
    {
        using (var f = new Fixture()) { var r = f.Apply(); File.WriteAllText(f.Backup(r, 1), "corrupt"); Refused(delegate { f.Undo(); }); Check(!File.Exists(f.Target(0)) && !File.Exists(f.Target(1)) && File.Exists(f.Backup(r, 0)), "partial restore before corrupt backup check"); }
        using (var f = new Fixture()) { var r = f.Apply(); File.Delete(f.Backup(r, 1)); Refused(delegate { f.Undo(); }); Check(!File.Exists(f.Target(0)) && !File.Exists(f.Target(1)), "partial restore before missing backup check"); }
    }
    static void CurrentConflict()
    { using (var f = new Fixture()) { var r = f.Apply(); File.WriteAllText(f.Target(1), "keep this newer file"); Refused(delegate { f.Undo(); }); Check(!File.Exists(f.Target(0)) && File.ReadAllText(f.Target(1)) == "keep this newer file" && File.Exists(f.Backup(r, 0)), "conflict overwritten"); } }
    static void Updated()
    { using (var f = new Fixture()) { var r = f.Apply(); f.Build("99999", "4", "Battlefield 6"); File.WriteAllText(f.Target(0), "new toc"); File.WriteAllText(f.Target(1), "new digest"); Check(f.Undo().State == "UpdatedFilesPreserved", "update not reconciled"); Check(File.ReadAllText(f.Target(0)) == "new toc" && File.ReadAllText(f.Target(1)) == "new digest" && File.Exists(f.Backup(r, 0)), "updated originals lost"); } }
    static void UpdateMissing()
    { using (var f = new Fixture()) { var r = f.Apply(); f.Build("99999", "4", "Battlefield 6"); File.WriteAllText(f.Target(1), "new digest"); Refused(delegate { f.Undo(); }); Check(!File.Exists(f.Target(0)) && File.ReadAllText(f.Target(1)) == "new digest" && File.Exists(f.Backup(r, 0)), "old-build content inserted"); } }
    static void SteamRestored()
    { using (var f = new Fixture()) { var r = f.Apply(); f.Original(0); f.Original(1); var time = File.GetLastWriteTimeUtc(f.Target(0)); f.Undo(); Check(f.Same(0) && f.Same(1) && File.GetLastWriteTimeUtc(f.Target(0)) == time && File.Exists(f.Backup(r, 0)), "Steam-restored data overwritten"); } }
    static void Interrupted()
    {
        using (var f = new Fixture()) { var r = f.Pending("Prepared"); File.Copy(f.Target(0), f.Backup(r, 0)); f.Undo(); Check(f.Same(0) && f.Same(1), "partial backup not reconciled"); }
        using (var f = new Fixture()) { var r = f.Pending("Applying"); File.Copy(f.Target(0), f.Backup(r, 0)); File.Copy(f.Target(1), f.Backup(r, 1)); File.Delete(f.Target(0)); f.Undo(); Check(f.Same(0) && f.Same(1), "interrupted first removal not restored"); }
        using (var f = new Fixture()) { var r = f.Apply(); f.Original(0); r.State = "UndoPending"; f.Journal(r); f.Undo(); Check(f.Same(0) && f.Same(1), "interrupted undo not resumed"); }
    }
    static void PartialStaging()
    {
        using (var f = new Fixture()) { f.Apply(); string stage = Path.Combine(Path.GetDirectoryName(f.Target(0)), ".b6-partial.tmp"); File.WriteAllText(stage, "partial"); f.Undo(); Check(f.Same(0) && f.Same(1) && File.ReadAllText(stage) == "partial", "staging loss or retry failure"); }
        using (var f = new Fixture()) { var r = f.Pending("Prepared"); string stage = Path.Combine(f.Store, r.Id, "copy-partial.tmp"); File.WriteAllText(stage, "partial"); f.Undo(); Check(f.Same(0) && f.Same(1) && File.Exists(stage), "interrupted backup stage blocked reconciliation"); }
    }
    static void JournalTampering()
    {
        using (var f = new Fixture()) { var r = f.Apply(); r.Id = "../outside"; f.Journal(r); Refused(delegate { f.Engine().Review(f.Game); }); Check(!File.Exists(f.Target(0)), "tampered history performed restore"); }
        using (var f = new Fixture()) { var r = f.Apply(); r.GamePath = f.Root; f.Journal(r); Refused(delegate { f.Undo(); }); Check(!File.Exists(f.Target(0)), "journal escaped Steam library"); }
        using (var f = new Fixture()) { Directory.CreateDirectory(f.Store); File.WriteAllText(Path.Combine(f.Store, "mirak.json"), "not json"); Refused(delegate { f.Engine().Review(f.Game); }); Check(f.Same(0) && f.Same(1), "malformed history mutated data"); }
    }
    static void Concurrent()
    { using (var f = new Fixture()) { Directory.CreateDirectory(f.Store); using (var operation = new MirakOperationLock(f.Store, f.Game)) { Refused(delegate { f.Apply(); }); Check(f.Same(0) && f.Same(1), "concurrent apply mutated"); } } }
    static void LockedOriginal()
    {
        using (var f = new Fixture()) { using (var handle = new FileStream(f.Target(0), FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { Refused(delegate { f.Apply(); }); } Check(f.Same(0) && f.Same(1), "locked original lost"); }
        using (var f = new Fixture()) { File.SetAttributes(f.Target(0), FileAttributes.ReadOnly); Refused(delegate { f.Apply(); }); Check(f.Same(0) && f.Same(1), "read-only original lost"); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateHardLinkW(string newName, string existingName, IntPtr security);
    static void HardLink()
    { using (var f = new Fixture()) { string link = Path.Combine(f.Root, "additional-link"); Check(CreateHardLinkW(link, f.Target(0), IntPtr.Zero), "hardlink fixture failed"); Refused(delegate { f.Apply(); }); Check(f.Same(0) && f.Same(1) && File.Exists(link), "hardlinked original removed"); } }
    static void Junction()
    {
        using (var f = new Fixture())
        {
            string original = Path.GetDirectoryName(f.Target(0)), outside = Path.Combine(f.Root, "held-original-map-folder"); Directory.Move(original, outside);
            using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + original + "\" \"" + outside + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })) { p.WaitForExit(); Check(p.ExitCode == 0, "junction fixture creation failed"); }
            try { Refused(delegate { f.Engine().Review(f.Game); }); Check(File.Exists(Path.Combine(outside, "mp_tungsten.toc")), "linked original changed"); }
            finally { Directory.Delete(original, false); Directory.Move(outside, original); }
        }
    }
    static void Requests()
    {
        using (var f = new Fixture())
        {
            string id = Guid.NewGuid().ToString("N"), path = Path.Combine(f.Root, "request.json");
            var r = new MirakRequest { Id = id, Action = "Apply", GamePath = f.Game, OwnerSid = WindowsIdentity.GetCurrent().User.Value, CreatedUtc = DateTime.UtcNow.ToString("o"), Review = new string('a', 64) };
            Action save = delegate { File.WriteAllText(path, new JavaScriptSerializer().Serialize(r)); };
            save(); Check(MirakWorker.ReadRequest(path, id).Action == "Apply", "valid request refused");
            r.OwnerSid = "S-1-0-0"; save(); Refused(delegate { MirakWorker.ReadRequest(path, id); }); r.OwnerSid = WindowsIdentity.GetCurrent().User.Value;
            r.Action = "DeleteAnything"; save(); Refused(delegate { MirakWorker.ReadRequest(path, id); }); r.Action = "Undo";
            r.CreatedUtc = DateTime.UtcNow.AddHours(-1).ToString("o"); save(); Refused(delegate { MirakWorker.ReadRequest(path, id); }); r.CreatedUtc = DateTime.UtcNow.ToString("o");
            r.Review = "execute me"; save(); Refused(delegate { MirakWorker.ReadRequest(path, id); });
            File.WriteAllText(path, new string('x', 20000)); Refused(delegate { MirakWorker.ReadRequest(path, id); });
            Refused(delegate { MirakWorker.RequestDirectory("../outside"); });
            string cancelled = Path.Combine(f.Root, "cancelled-request"); Directory.CreateDirectory(cancelled); MirakWorker.CancelUnstarted(cancelled);
            Check(File.Exists(Path.Combine(cancelled, "consumed.lock")) && File.Exists(Path.Combine(cancelled, "result.json")), "cancel did not consume request");
            Refused(delegate { MirakWorker.CancelUnstarted(cancelled); });
        }
    }
}

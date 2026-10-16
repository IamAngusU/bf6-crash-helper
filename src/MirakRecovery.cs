using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Bf6CrashHelper
{
    public sealed class MirakPlan
    {
        public string GamePath, BuildId, Fingerprint, BackupDirectory;
        public string State = "NotApplied", TransactionId = "", Status = "NotChecked";
        public bool CanApply, CanUndo;
        public string Message = "";
    }

    public sealed class MirakRecord
    {
        public int Schema = 1;
        public string Id, GamePath, BuildId, State, CreatedUtc, UpdatedUtc;
    }

    // Immutable allow-list. A game update needs new evidence and a reviewed release.
    internal sealed class MirakSpec
    {
        internal string Build;
        internal string[] Targets, TargetHashes, Guards, GuardHashes;
        internal long[] TargetSizes, GuardSizes;
        internal static MirakSpec Production()
        {
            const string prefix = @"Update\BF6_SAN2_DLC\Data\Win32\game\glaciermp\levels\mp_tungsten\mp_tungsten";
            return new MirakSpec {
                Build = "25851446", Targets = new [] { prefix + ".toc", prefix + ".digest" },
                TargetHashes = new [] { "9b189ac0159a5ef922134d02c51a867fe60efd223676948ac2e8113464c9cb02", "6abdeae56666b015bf1fe7948e04e1cbf41050b3f07137f9bbf4db76850b1565" },
                TargetSizes = new long[] { 4500750, 21 },
                Guards = new [] { "bf6.exe", @"Data\layout.toc", @"Data\Win32\game\glaciermp\levels\mp_tungsten\mp_tungsten.toc" },
                GuardHashes = new [] { "2714b09ae3ee889b895567235352b164bc706cbf56602688666d6fc2a98d2e48", "e1fea696e0c7e6e4c9a43b386683d3989367ba3bd9da439c59e2de62e9615bf7", "aa7c61be9f38a74d504c49e93f3e57fbb8a985b345debbaa7998168a097327ff" },
                GuardSizes = new long[] { 198225768, -1, 4591142 }
            };
        }
    }

    public sealed class MirakRecovery
    {
        readonly string store, journal;
        readonly MirakSpec spec;
        readonly Action closed;
        readonly Action<string> fault;
        internal static readonly string[] TerminalStates = { "Restored", "UpdatedFilesPreserved" };
        internal static string DefaultStore { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BF6CrashHelper", "mirak-v1"); } }
        public MirakRecovery() : this(DefaultStore, MirakSpec.Production(), EnsureClosed, delegate { }) { }
        internal MirakRecovery(string statePath, MirakSpec allowList, Action gameClosed, Action<string> checkpoint)
        { store = Path.GetFullPath(statePath).TrimEnd('\\'); journal = Path.Combine(store, "mirak.json"); spec = allowList; closed = gameClosed; fault = checkpoint; }
        public string BackupRoot { get { return store; } }
        internal static void EnsureClosed()
        {
            var games = System.Diagnostics.Process.GetProcessesByName("bf6");
            bool running = games.Length != 0; foreach (var game in games) game.Dispose();
            if (running) throw new IOException(UiLanguage.Text("BF6 zuerst regulär schließen. Das Tool beendet keine Prozesse.", "Close BF6 normally first. The helper does not stop processes."));
        }
        static bool Terminal(MirakRecord r) { return TerminalStates.Contains(r.State); }
        public MirakRecord Latest() { return Load().LastOrDefault(r => !Terminal(r)); }

        public MirakPlan Review(string selectedGame)
        {
            var records = Load();
            var active = records.LastOrDefault(r => !Terminal(r));
            if (active != null)
            {
                return InspectRecorded(active);
            }
            string game = String.IsNullOrWhiteSpace(selectedGame) ? SteamInstallation.DetectGamePath() : selectedGame;
            game = Normalize(game);
            using (var lease = new MirakPathLease(new [] { game }))
            using (var manifest = OpenManifest(game))
            {
                string build = Identity(game, manifest);
                if (build != spec.Build) throw new IOException(UiLanguage.Text("Dieser Build ist nicht freigegeben. Es wird nichts geändert. Unterstützter Steam-Build: ", "This build is not supported. Nothing will change. Supported Steam build: ") + spec.Build);
                CheckGuards(game);
                if (spec.Targets.Any(p => !File.Exists(Path.Combine(game, p))))
                    return new MirakPlan { GamePath = game, BuildId = build, State = "UntrackedMissing", Status = "UntrackedMissing", BackupDirectory = store,
                        Message = UiLanguage.Text("Die optionalen Dateien fehlen, aber diese App hat keine passende Sicherungshistorie. Ein früherer oder manueller Workaround ist möglich, jedoch nicht nachgewiesen. Es wird nichts erneut angewendet; für Originaldateien Steam-Dateiprüfung verwenden oder die vorhandene Originalsicherung behalten.", "Optional files are absent, but this app has no matching backup history. An earlier or manual workaround is possible, not established. Nothing will be reapplied; use Steam file verification for originals or keep your existing original backup.") };
                CheckTargets(game);
                return new MirakPlan { GamePath = game, BuildId = build, CanApply = true,
                    Status = "Compatible",
                    Fingerprint = Token("Apply", game, spec.Build), BackupDirectory = store,
                    Message = UiLanguage.Text("Experimenteller Rapid-Royale/Mirak-Tal-Workaround. Genau zwei optionale SAN2-Indexdateien werden nach einer geprüften Sicherung aus dem Ladepfad entfernt. Die Basiskarte bleibt erhalten. Ein erfolgreicher Test auf einem PC ist kein allgemeiner Fixnachweis. Rückgängig bleibt verfügbar.", "Experimental Rapid Royale / Mirak Valley workaround. Exactly two optional SAN2 index files are removed from the loading path after verified backups. The base map is retained. Successful play on one PC is not a universal fix. Undo remains available.") };
            }
        }

        // History-only discovery is cheap and never claims the recorded change is
        // still active. Review rechecks the live build, guards, targets and backups.
        public MirakPlan Recorded()
        {
            var active = Latest(); return active == null ? null : RecordedPlan(active);
        }
        MirakPlan RecordedPlan(MirakRecord record)
        {
            return new MirakPlan { GamePath = record.GamePath, BuildId = record.BuildId, State = record.State, Status = "Recorded",
                TransactionId = record.Id, BackupDirectory = BackupFolder(record),
                Fingerprint = Token("Undo", record.GamePath, record.Id),
                Message = UiLanguage.Text("Eine gespeicherte Änderung wurde gefunden. Der aktuelle Zustand wird vor Rückgängig erneut geprüft.", "A saved change was found. The current state is checked again before Undo.") };
        }
        MirakPlan InspectRecorded(MirakRecord record)
        {
            var plan = RecordedPlan(record);
            try
            {
                using (var lease = new MirakPathLease(new [] { record.GamePath, store, BackupFolder(record) }))
                using (var manifest = OpenManifest(record.GamePath))
                {
                    string build = Identity(record.GamePath, manifest); plan.BuildId = build;
                    for (int i = 0; i < spec.Targets.Length; i++)
                        if (File.Exists(Backup(record, i))) Verify(Backup(record, i), spec.TargetHashes[i], spec.TargetSizes[i]);
                    if (build != spec.Build)
                    {
                        if (!spec.Targets.All(p => File.Exists(Path.Combine(record.GamePath, p))))
                            throw new IOException(UiLanguage.Text("Das Spiel wurde aktualisiert und aktuelle Indexdateien fehlen. Keine alten Backups einspielen. Steam-Dateiprüfung verwenden; Sicherungen bleiben erhalten.", "The game was updated and current index files are missing. Do not insert old backups. Use Steam file verification; backups are retained."));
                        plan.Status = "UpdatedFilesPresent"; plan.CanUndo = true;
                        plan.Message = UiLanguage.Text("Neuere Spieldateien sind vorhanden. Rückgängig behält sie und schließt den alten Vorgang ab; alte Backups werden nicht eingefügt.", "Newer game files are present. Undo keeps them and closes the old record; old backups will not be inserted."); return plan;
                    }
                    CheckGuards(record.GamePath); int absent = 0;
                    for (int i = 0; i < spec.Targets.Length; i++)
                    {
                        if (File.Exists(Target(record.GamePath, i))) Verify(Target(record.GamePath, i), spec.TargetHashes[i], spec.TargetSizes[i]);
                        else { Verify(Backup(record, i), spec.TargetHashes[i], spec.TargetSizes[i]); absent++; }
                    }
                    plan.Status = absent == spec.Targets.Length && record.State == "Applied" ? "AppliedVerified" : absent == 0 ? "OriginalsPresent" : "RecoveryPending";
                    plan.CanUndo = true;
                    plan.Message = plan.Status == "AppliedVerified"
                        ? UiLanguage.Text("Workaround bereits angewendet: gespeicherter Vorgang, beide Sicherungen, aktueller Build und entfernte Indexdateien wurden geprüft. Es wird nichts doppelt angewendet. Rückgängig ist verfügbar.", "Workaround already applied: the saved record, both backups, current build and absent index files were verified. It will not be applied twice. Undo is available.")
                        : plan.Status == "OriginalsPresent"
                        ? UiLanguage.Text("Die Originaldateien sind bereits vorhanden; der Workaround ist nicht mehr aktiv. Wiederherstellung abschließen prüft sie und schließt den gespeicherten Vorgang ab.", "Original files are already present; the workaround is no longer active. Finish recovery checks them and closes the saved record.")
                        : UiLanguage.Text("Ein unterbrochener Vorgang wurde gefunden. Vorhandene Originale und benötigte Sicherungen wurden geprüft; Rückgängig kann die Wiederherstellung fortsetzen.", "An interrupted operation was found. Existing originals and required backups were checked; Undo can resume recovery.");
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is System.ComponentModel.Win32Exception) && !(error is ArgumentException)) throw;
                plan.Status = "RecoveryBlocked"; plan.CanUndo = false; plan.Message = error.Message;
            }
            return plan;
        }

        public MirakRecord Apply(string gamePath, string expectedReview)
        {
            closed(); string game = Normalize(gamePath);
            using (var operation = Lock(game))
            using (var lease = new MirakPathLease(new [] { game, store }.Concat(spec.Targets.Select(p => Path.GetDirectoryName(Path.Combine(game, p))))))
            using (var manifest = OpenManifest(game))
            {
                var records = Load();
                if (records.Any(r => !Terminal(r))) throw new IOException("An existing or interrupted change must be undone first.");
                if (records.Count >= 128) throw new IOException("The recovery history limit was reached. Backups were preserved; no new change was started.");
                string build = Identity(game, manifest);
                if (build != spec.Build || expectedReview != Token("Apply", game, spec.Build)) throw new IOException("The reviewed installation/build changed. Review again; nothing was changed.");
                using (var guardedFiles = HoldGuards(game))
                {
                    CheckTargets(game); closed();
                    var record = new MirakRecord { Id = Guid.NewGuid().ToString("N"), GamePath = game, BuildId = build,
                        State = "Prepared", CreatedUtc = DateTime.UtcNow.ToString("o") };
                    SecureDirectory(BackupFolder(record)); lease.AddPaths(new [] { BackupFolder(record) }); records.Add(record); Save(records, record);
                    try
                    {
                        // Both backups are durable and verified BEFORE either original is removed.
                        for (int i = 0; i < spec.Targets.Length; i++)
                        {
                            closed(); string staging = Path.Combine(BackupFolder(record), "copy-" + Guid.NewGuid().ToString("N") + ".tmp");
                            CopyDurable(Target(game, i), staging, spec.TargetHashes[i], spec.TargetSizes[i]);
                            File.Move(staging, Backup(record, i));
                            fault("Backup" + i);
                        }
                        record.State = "BackedUp"; Save(records, record);
                        using (var pinnedBackups = new MirakReadLease(BackupInputs(record)))
                        {
                            fault("BackedUp");
                            CheckGuards(game); CheckTargets(game);
                            record.State = "Applying"; Save(records, record);
                            for (int i = 0; i < spec.Targets.Length; i++)
                            {
                                closed(); Verify(Backup(record, i), spec.TargetHashes[i], spec.TargetSizes[i]);
                                using (var original = new MirakOriginal(Target(game, i)))
                                {
                                    original.Verify(spec.TargetHashes[i], spec.TargetSizes[i]); closed(); original.Remove();
                                }
                                fault("Removed" + i);
                            }
                            record.State = "Applied"; Save(records, record); return record;
                        }
                    }
                    catch (Exception error)
                    {
                        string recovery;
                        try { UndoRecord(records, record, manifest); recovery = " Original files restored; backups retained."; }
                        catch (Exception undoError) { recovery = " Recovery remains pending. Close BF6 and choose Undo. Backups retained. " + undoError.Message; }
                        throw new IOException("The workaround was not completed. " + error.Message + recovery, error);
                    }
                }
            }
        }

        public MirakRecord Undo(string expectedReview)
        {
            closed(); var current = Latest();
            if (current == null) throw new IOException("No pending workaround exists in this Windows user's recovery history.");
            using (var operation = Lock(current.GamePath))
            using (var lease = new MirakPathLease(new [] { current.GamePath, store, BackupFolder(current) }.Concat(spec.Targets.Select(p => Path.GetDirectoryName(Path.Combine(current.GamePath, p))))))
            using (var manifest = OpenManifest(current.GamePath))
            {
                var records = Load(); var record = records.LastOrDefault(r => !Terminal(r));
                if (record == null || expectedReview != Token("Undo", record.GamePath, record.Id)) throw new IOException("Recovery state changed. Review Undo again; nothing was restored.");
                return UndoRecord(records, record, manifest);
            }
        }

        MirakRecord UndoRecord(List<MirakRecord> records, MirakRecord record, FileStream manifest)
        {
            closed(); string currentBuild = Identity(record.GamePath, manifest);
            // Complete preflight: never restore one file and only then discover another conflict.
            for (int i = 0; i < spec.Targets.Length; i++)
                if (File.Exists(Backup(record, i))) Verify(Backup(record, i), spec.TargetHashes[i], spec.TargetSizes[i]);
            if (currentBuild != spec.Build)
            {
                if (spec.Targets.All((p) => File.Exists(Path.Combine(record.GamePath, p))))
                {
                    record.State = "UpdatedFilesPreserved"; Save(records, record); return record;
                }
                throw new IOException("The game has updated and index files are missing. Old-build backups will not be inserted. Use Steam's file verification for current originals. Your backups are retained.");
            }
            using (var guardedFiles = HoldGuards(record.GamePath))
            using (var pinnedInputs = new MirakReadLease(BackupInputs(record).Concat(Enumerable.Range(0, spec.Targets.Length)
                .Where(i => File.Exists(Target(record.GamePath, i)))
                .Select(i => new MirakReadSpec { Path = Target(record.GamePath, i), Hash = spec.TargetHashes[i], Size = spec.TargetSizes[i] }))))
            {
                for (int i = 0; i < spec.Targets.Length; i++)
                {
                    string target = Target(record.GamePath, i);
                    if (File.Exists(target)) Verify(target, spec.TargetHashes[i], spec.TargetSizes[i]);
                    else Verify(Backup(record, i), spec.TargetHashes[i], spec.TargetSizes[i]);
                }
                record.State = "UndoPending"; Save(records, record);
                for (int i = 0; i < spec.Targets.Length; i++)
                {
                    closed(); string target = Target(record.GamePath, i);
                    if (File.Exists(target)) { Verify(target, spec.TargetHashes[i], spec.TargetSizes[i]); continue; }
                    // Unique same-directory staging works even when Steam and AppData are on different drives.
                    // An interrupted partial staging file is kept; retry uses a fresh name and never trusts it.
                    string temp = Path.Combine(Path.GetDirectoryName(target), ".b6-" + Guid.NewGuid().ToString("N").Substring(0, 16) + ".tmp");
                    CopyDurable(Backup(record, i), temp, spec.TargetHashes[i], spec.TargetSizes[i]);
                    closed(); MirakPaths.CheckParents(target); File.Move(temp, target);
                    Verify(target, spec.TargetHashes[i], spec.TargetSizes[i]); fault("Restored" + i);
                }
                record.State = "Restored"; Save(records, record); return record;
            }
        }

        string Normalize(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new IOException(UiLanguage.Text("Keine Steam-Installation gefunden. Über Spielordner den Steam-Spielordner auswählen.", "No Steam installation found. Select the Steam game folder using Game folder."));
            string full = Path.GetFullPath(path.Trim().Trim('"')).TrimEnd('\\');
            if (full.Length > 220 || full.Length < 4 || full[1] != ':' || full.Substring(2).Contains(":")) throw new IOException("Only ordinary local Windows installation paths are supported.");
            var drive = new DriveInfo(Path.GetPathRoot(full));
            if (drive.DriveType != DriveType.Fixed || !String.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) throw new IOException("This experimental file recovery supports local NTFS Steam libraries only.");
            foreach (string relative in spec.Targets)
            {
                string target = Path.Combine(full, relative);
                if (target.Length > 259 || Path.GetDirectoryName(target).Length + 25 > 259) throw new IOException("The installation path is too long for safe backup and restoration. Use a shorter Steam library path; nothing was changed.");
            }
            if (store.Length + 80 > 259) throw new IOException("The local backup path is too long for safe recovery. Nothing was changed.");
            MirakPaths.CheckParents(full); return full;
        }
        FileStream OpenManifest(string game)
        {
            game = Normalize(game); var parent = Directory.GetParent(game);
            if (parent == null || !parent.Name.Equals("common", StringComparison.OrdinalIgnoreCase) || parent.Parent == null || !parent.Parent.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) throw new IOException("The selected folder must be inside a Steam steamapps/common library.");
            string manifest = Path.Combine(parent.Parent.FullName, "appmanifest_2807960.acf");
            MirakPaths.CheckParents(manifest);
            return new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        string Identity(string game, FileStream manifest)
        {
            if (manifest.Length > 1024 * 1024) throw new IOException("Unexpected Steam app manifest size.");
            var app = SteamInstallation.ReadVdf(manifest.Name).Child("AppState");
            string configured = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest.Name), "common", app.Value("installdir")));
            if (app.Value("appid") != "2807960" || !String.Equals(configured, game, StringComparison.OrdinalIgnoreCase)) throw new IOException("The Steam app manifest does not match this Battlefield installation.");
            if (app.Value("StateFlags") != "4") throw new IOException("Steam reports an unfinished/update state. Finish or pause game updates and review again.");
            string build = app.Value("buildid");
            if (String.IsNullOrEmpty(build) || build.Length > 16 || build.Any(c => c < '0' || c > '9')) throw new IOException("Steam build identity is unavailable.");
            return build;
        }
        void CheckGuards(string game)
        { for (int i = 0; i < spec.Guards.Length; i++) Verify(Path.Combine(game, spec.Guards[i]), spec.GuardHashes[i], spec.GuardSizes[i]); }
        MirakReadLease HoldGuards(string game)
        { return new MirakReadLease(Enumerable.Range(0, spec.Guards.Length).Select(i => new MirakReadSpec { Path = Path.Combine(game, spec.Guards[i]), Hash = spec.GuardHashes[i], Size = spec.GuardSizes[i] })); }
        IEnumerable<MirakReadSpec> BackupInputs(MirakRecord record)
        { return Enumerable.Range(0, spec.Targets.Length).Where(i => File.Exists(Backup(record, i))).Select(i => new MirakReadSpec { Path = Backup(record, i), Hash = spec.TargetHashes[i], Size = spec.TargetSizes[i] }); }
        void CheckTargets(string game)
        { for (int i = 0; i < spec.Targets.Length; i++) Verify(Target(game, i), spec.TargetHashes[i], spec.TargetSizes[i]); }
        string Target(string game, int i) { return Path.Combine(game, spec.Targets[i]); }
        string BackupFolder(MirakRecord r) { return Path.Combine(store, r.Id); }
        string Backup(MirakRecord r, int i) { return Path.Combine(BackupFolder(r), i.ToString(CultureInfo.InvariantCulture) + ".original"); }
        internal static string Hash(Stream stream)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        internal static void Verify(string path, string hash, long size)
        {
            MirakPaths.CheckParents(path);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                if ((size >= 0 && file.Length != size) || file.Length > 1024L * 1024L * 1024L || Hash(file) != hash) throw new IOException("A required file or backup has changed: " + Path.GetFileName(path) + ". Nothing will overwrite it.");
        }
        internal static void CopyDurable(string source, string destination, string hash, long size)
        {
            MirakPaths.CheckParents(source); MirakPaths.CheckParents(destination);
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                if ((size >= 0 && input.Length != size) || input.Length > 1024L * 1024L * 1024L) throw new IOException("Unexpected backup source size.");
                input.CopyTo(output); output.Flush(true); output.Position = 0;
                if (Hash(output) != hash) throw new IOException("Copy verification failed. Originals and any existing backup were preserved.");
            }
        }
        string Token(string action, string game, string identity)
        {
            using (var data = new MemoryStream(Encoding.UTF8.GetBytes(action + "\n" + game.ToUpperInvariant() + "\n" + identity + "\n" + String.Join("\n", spec.TargetHashes.Concat(spec.GuardHashes))))) return Hash(data);
        }
        List<MirakRecord> Load()
        {
            MirakPaths.CheckParents(store); MirakPaths.CheckParents(journal);
            if (!File.Exists(journal)) return new List<MirakRecord>();
            using (var file = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > 256 * 1024) throw new IOException("Recovery journal is too large. Backups were not changed.");
                List<MirakRecord> records;
                using (var reader = new StreamReader(file, Encoding.UTF8)) records = new JavaScriptSerializer { MaxJsonLength = 256 * 1024, RecursionLimit = 12 }.Deserialize<List<MirakRecord>>(reader.ReadToEnd());
                if (records == null || records.Count > 128) throw new IOException("Recovery journal is invalid.");
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int pending = 0;
                foreach (var r in records)
                {
                    Guid id;
                    if (r == null || r.Schema != 1 || !Guid.TryParseExact(r.Id, "N", out id) || !ids.Add(r.Id) || r.BuildId != spec.Build ||
                        !new [] { "Prepared", "BackedUp", "Applying", "Applied", "UndoPending", "Restored", "UpdatedFilesPreserved" }.Contains(r.State) ||
                        !String.Equals(Normalize(r.GamePath), r.GamePath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Recovery journal validation failed. Nothing was changed.");
                    if (!Terminal(r)) pending++;
                }
                if (pending > 1) throw new IOException("Conflicting recovery history. Backups were preserved.");
                return records;
            }
        }
        void Save(List<MirakRecord> records, MirakRecord r)
        {
            r.UpdatedUtc = DateTime.UtcNow.ToString("o");
            string temp = Path.Combine(store, "journal-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(records)); file.Write(bytes, 0, bytes.Length); file.Flush(true);
            }
            MirakPaths.CheckParents(journal); MirakPaths.CheckParents(journal + ".previous");
            if (File.Exists(journal)) File.Replace(temp, journal, journal + ".previous"); else File.Move(temp, journal);
        }
        IDisposable Lock(string game)
        {
            closed(); MirakPaths.CheckParents(store); SecureDirectory(store);
            return new MirakOperationLock(store, game);
        }
        internal static void SecureDirectory(string path)
        {
            MirakPaths.CheckParents(path);
            if (Directory.Exists(path)) return;
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            var sid = WindowsIdentity.GetCurrent().User; security.SetOwner(sid);
            foreach (var principal in new [] { sid, new SecurityIdentifier("S-1-5-18"), new SecurityIdentifier("S-1-5-32-544") })
                security.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(path, security);
        }
    }

    internal sealed class MirakReadSpec { internal string Path, Hash; internal long Size; }
    // Keep validated guards/backups/current originals stable across the whole operation,
    // including the gap between complete preflight and a later restoration copy.
    internal sealed class MirakReadLease : IDisposable
    {
        readonly List<FileStream> files = new List<FileStream>();
        internal MirakReadLease(IEnumerable<MirakReadSpec> inputs)
        {
            try
            {
                foreach (var input in inputs)
                {
                    MirakPaths.CheckParents(input.Path);
                    var file = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read); files.Add(file);
                    if ((input.Size >= 0 && file.Length != input.Size) || file.Length > 1024L * 1024L * 1024L || MirakRecovery.Hash(file) != input.Hash) throw new IOException("A guarded file or backup changed; restoration/mutation stopped.");
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (var file in files) file.Dispose(); files.Clear(); }
    }

    internal sealed class MirakOperationLock : IDisposable
    {
        Mutex mutex; FileStream file; bool owned;
        internal MirakOperationLock(string store, string game)
        {
            try
            {
                string key; using (var value = new MemoryStream(Encoding.UTF8.GetBytes(Path.GetFullPath(game).ToUpperInvariant()))) key = MirakRecovery.Hash(value);
                mutex = new Mutex(false, "Global\\BF6CrashHelper-Mirak-" + key);
                try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) throw new IOException("Another workaround operation is already active for this installation.");
                string lockPath = Path.Combine(store, "operation.lock"); MirakPaths.CheckParents(lockPath);
                file = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { if (file != null) { file.Dispose(); file = null; } if (mutex != null) { if (owned) mutex.ReleaseMutex(); mutex.Dispose(); mutex = null; } }
    }

    // Pin all existing directory ancestors against rename/reparse replacement while elevated writes run.
    internal sealed class MirakPathLease : IDisposable
    {
        readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();
        internal MirakPathLease(IEnumerable<string> paths)
        { try { AddPaths(paths); } catch { Dispose(); throw; } }
        internal void AddPaths(IEnumerable<string> paths)
        {
            try
            {
                var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string path in paths)
                    for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory != null; directory = directory.Parent)
                    {
                        if (!directory.Exists || !unique.Add(directory.FullName)) continue;
                        var h = MirakNative.Open(directory.FullName, 0x80, 3, 0x02200000);
                        handles.Add(h); MirakNative.CheckPath(h, directory.FullName, true);
                    }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (var handle in handles) handle.Dispose(); handles.Clear(); }
    }

    internal sealed class MirakOriginal : IDisposable
    {
        readonly SafeFileHandle handle;
        internal MirakOriginal(string path) { handle = MirakNative.Open(path, 0x80010000, 0, 0x00200000); try { MirakNative.CheckPath(handle, path, false); } catch { handle.Dispose(); throw; } }
        internal void Verify(string expected, long size)
        {
            using (var nonOwning = new SafeFileHandle(handle.DangerousGetHandle(), false))
            using (var stream = new FileStream(nonOwning, FileAccess.Read))
                if ((size >= 0 && stream.Length != size) || MirakRecovery.Hash(stream) != expected) throw new IOException("Original changed immediately before removal. Backup retained; no unverified file removed.");
        }
        internal void Remove() { MirakNative.Remove(handle); }
        public void Dispose() { handle.Dispose(); }
    }

    internal static class MirakNative
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct Info { public uint Attributes; public long Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
        [StructLayout(LayoutKind.Sequential)] struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder name, uint size, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref Disposition info, uint size);
        internal static SafeFileHandle Open(string path, uint access, uint share, uint flags)
        { var h = CreateFileW(path, access, share, IntPtr.Zero, 3, flags, IntPtr.Zero); if (h.IsInvalid) { int error = Marshal.GetLastWin32Error(); h.Dispose(); throw new Win32Exception(error); } return h; }
        internal static void CheckPath(SafeFileHandle handle, string expected, bool directory)
        {
            Info info; if (!GetFileInformationByHandle(handle, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var text = new StringBuilder(32768); uint count = GetFinalPathNameByHandleW(handle, text, (uint)text.Capacity, 0);
            if (count == 0 || count >= text.Capacity) throw new IOException("Cannot validate the final file handle path.");
            string actual = text.ToString(); if (actual.StartsWith(@"\\?\")) actual = actual.Substring(4);
            if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory || (!directory && info.Links != 1) ||
                !String.Equals(actual.TrimEnd('\\'), Path.GetFullPath(expected).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("A linked, multiply linked, or redirected path was refused: " + expected + " -> " + actual);
        }
        internal static void Remove(SafeFileHandle handle)
        { var value = new Disposition { Delete = true }; if (!SetFileInformationByHandle(handle, 4, ref value, 1)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }
}

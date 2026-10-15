using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Bf6CrashHelper
{
    public sealed class RecoveryRecord
    {
        public string Id, Created, Source, Backup, Fingerprint, State, KeptAfterUndo;
    }

    // Moves are journalled before execution. No game data is ever deleted.
    public sealed class Recovery
    {
        readonly string cache, stateDirectory, journal;
        readonly Action ensureClosed;
        public Recovery() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Battlefield 6", "cache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BF6CrashHelper"), EnsureGameClosed) { }

        internal Recovery(string cachePath, string statePath, Action closedCheck)
        {
            cache = Path.GetFullPath(cachePath).TrimEnd(Path.DirectorySeparatorChar);
            stateDirectory = Path.GetFullPath(statePath);
            journal = Path.Combine(stateDirectory, "recovery.json");
            ensureClosed = closedCheck;
            if (!String.Equals(Path.GetFileName(cache), "cache", StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected cache directory.");
        }

        public static void EnsureGameClosed()
        {
            Process[] processes = Process.GetProcessesByName("bf6");
            bool found = processes.Length != 0;
            foreach (Process process in processes) process.Dispose();
            if (found) throw new IOException("Close Battlefield normally before changing its cache. No process will be stopped for you.");
        }

        public string CachePath { get { return cache; } }

        public RecoveryRecord Latest()
        {
            return Load().LastOrDefault(r => r.State != "Restored");
        }

        public RecoveryRecord Reset()
        {
            ensureClosed();
            CheckParents(cache);
            CheckParents(stateDirectory);
            var records = Load();
            if (records.Any(r => r.State != "Applied" && r.State != "Restored")) throw new IOException("An interrupted operation needs recovery first. Choose Undo cache change.");
            if (!Directory.Exists(cache)) throw new IOException("There is no local Battlefield cache to reset.");
            string fingerprint = Fingerprint(cache);
            var record = new RecoveryRecord {
                Id = Guid.NewGuid().ToString("N"), Created = DateTime.UtcNow.ToString("o"), Source = cache,
                Backup = cache + ".bf6helper-backup-" + Guid.NewGuid().ToString("N"), Fingerprint = fingerprint, State = "Planned"
            };
            records.Add(record);
            Save(records);
            ensureClosed();
            if (Fingerprint(cache) != fingerprint) throw new IOException("The cache changed during review. Nothing has been moved. Choose Undo to clear this pending operation, then retry.");
            Directory.Move(cache, record.Backup);
            record.State = "BackedUp";
            Save(records);
            Directory.CreateDirectory(cache);
            record.State = "Applied";
            Save(records);
            return record;
        }

        public RecoveryRecord Undo()
        {
            ensureClosed();
            CheckParents(cache);
            var records = Load();
            RecoveryRecord record = records.LastOrDefault(r => r.State != "Restored");
            if (record == null) throw new IOException("There is no cache change to undo.");
            Validate(record);
            CheckParents(record.Backup);
            // An interruption after the old cache was put back must be reconciled before touching it again.
            if (!Directory.Exists(record.Backup))
            {
                if (record.State == "Planned" && Directory.Exists(cache))
                {
                    record.State = "Restored"; Save(records); return record;
                }
                if (record.State == "UndoPending" && Directory.Exists(cache) && Fingerprint(cache) == record.Fingerprint)
                {
                    record.State = "Restored"; Save(records); return record;
                }
                throw new IOException("The original backup is missing. Nothing has been moved. Check the recovery journal before continuing.");
            }
            if (Fingerprint(record.Backup) != record.Fingerprint) throw new IOException("The backup has changed since it was saved. Automatic undo stopped to preserve your files.");
            if (String.IsNullOrEmpty(record.KeptAfterUndo)) record.KeptAfterUndo = cache + ".bf6helper-kept-" + Guid.NewGuid().ToString("N");
            Validate(record);
            record.State = "UndoPending";
            Save(records);
            ensureClosed();
            if (Directory.Exists(cache))
            {
                if (Directory.Exists(record.KeptAfterUndo)) throw new IOException("Both a current cache and a kept cache exist. Nothing has been overwritten; inspect the recovery journal.");
                Directory.Move(cache, record.KeptAfterUndo);
            }
            Directory.Move(record.Backup, cache);
            record.State = "Restored";
            Save(records);
            return record;
        }

        List<RecoveryRecord> Load()
        {
            CheckParents(stateDirectory);
            CheckParents(journal);
            if (!File.Exists(journal)) return new List<RecoveryRecord>();
            var info = new FileInfo(journal);
            if (info.Length > 4 * 1024 * 1024) throw new IOException("Recovery journal is too large.");
            var records = new JavaScriptSerializer().Deserialize<List<RecoveryRecord>>(File.ReadAllText(journal));
            if (records == null) throw new IOException("Recovery journal is invalid.");
            foreach (var record in records) Validate(record);
            return records;
        }

        void Validate(RecoveryRecord record)
        {
            Guid id;
            if (record == null || !Guid.TryParseExact(record.Id, "N", out id) || !String.Equals(record.Source, cache, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid recovery record.");
            ValidateSibling(record.Backup, ".bf6helper-backup-");
            if (!String.IsNullOrEmpty(record.KeptAfterUndo)) ValidateSibling(record.KeptAfterUndo, ".bf6helper-kept-");
            if (!new [] { "Planned", "BackedUp", "Applied", "UndoPending", "Restored" }.Contains(record.State)) throw new IOException("Unknown recovery state.");
            if (record.Fingerprint == null || record.Fingerprint.Length != 64 || record.Fingerprint.Any(c => !Uri.IsHexDigit(c))) throw new IOException("Invalid backup fingerprint.");
        }

        void ValidateSibling(string candidate, string suffix)
        {
            if (candidate == null) throw new IOException("Missing recovery path.");
            string prefix = cache + suffix;
            Guid id;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(candidate.Substring(prefix.Length), "N", out id) ||
                !String.Equals(Path.GetDirectoryName(Path.GetFullPath(candidate)), Path.GetDirectoryName(cache), StringComparison.OrdinalIgnoreCase)) throw new IOException("Recovery path is outside the expected cache directory.");
        }

        void Save(List<RecoveryRecord> records)
        {
            CheckParents(stateDirectory);
            CheckParents(journal);
            Directory.CreateDirectory(stateDirectory);
            string temporary = Path.Combine(stateDirectory, "recovery-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(records), new UTF8Encoding(false));
            if (File.Exists(journal)) File.Replace(temporary, journal, journal + ".previous");
            else File.Move(temporary, journal);
        }

        internal static void CheckParents(string path)
        {
            for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked or redirected cache/recovery path was found. Automatic changes are disabled for this path.");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked file is not supported.");
        }

        internal static string Fingerprint(string directory)
        {
            var entries = new List<string>();
            ReadTree(directory, directory, entries);
            entries.Sort(StringComparer.Ordinal);
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(String.Join("\n", entries.ToArray()))));
        }

        static void ReadTree(string root, string directory, List<string> entries)
        {
            CheckParents(directory);
            entries.Add("D:" + directory.Substring(root.Length));
            foreach (string file in Directory.GetFiles(directory))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked file was found. Automatic cache changes are disabled.");
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create()) entries.Add("F:" + file.Substring(root.Length) + ":" + stream.Length + ":" + Hex(sha.ComputeHash(stream)));
            }
            foreach (string child in Directory.GetDirectories(directory)) ReadTree(root, child, entries);
        }

        static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
}

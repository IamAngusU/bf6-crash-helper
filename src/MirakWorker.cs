using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Bf6CrashHelper
{
    public sealed class MirakOutcome
    {
        public bool Success;
        public string State, Message, BackupDirectory;
    }
    internal sealed class MirakRequest
    {
        public string Id, Action, GamePath, Review, OwnerSid, CreatedUtc;
    }
    // Only a consented GUI action creates a request. The elevated worker accepts one
    // bounded request belonging to the same Windows user and consumes it once.
    public static class MirakWorker
    {
        const string WorkerSwitch = "--mirak-worker";
        static string Requests { get { return Path.Combine(MirakRecovery.DefaultStore, "requests"); } }
        internal static string RequestDirectory(string id)
        {
            Guid parsed; if (!Guid.TryParseExact(id, "N", out parsed)) throw new IOException("Invalid workaround request.");
            string path = Path.Combine(Requests, id); Recovery.CheckParents(path); return path;
        }
        internal static MirakRequest ReadRequest(string path, string expectedId)
        {
            Recovery.CheckParents(path);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > 16384) throw new IOException("Invalid workaround request size.");
                MirakRequest r;
                using (var text = new StreamReader(file, Encoding.UTF8)) r = new JavaScriptSerializer { MaxJsonLength = 16384, RecursionLimit = 8 }.Deserialize<MirakRequest>(text.ReadToEnd());
                DateTimeOffset created;
                if (r == null || r.Id != expectedId || (r.Action != "Apply" && r.Action != "Undo") ||
                    r.OwnerSid != WindowsIdentity.GetCurrent().User.Value || !Regex.IsMatch(r.Review ?? "", "^[a-f0-9]{64}$") ||
                    String.IsNullOrWhiteSpace(r.GamePath) || r.GamePath.Length > 220 ||
                    !DateTimeOffset.TryParse(r.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out created) ||
                    created > DateTimeOffset.UtcNow.AddMinutes(1) || created < DateTimeOffset.UtcNow.AddMinutes(-15))
                    throw new IOException("The workaround request is invalid, expired, or belongs to a different Windows account.");
                return r;
            }
        }
        internal static void WriteNewJson(string path, object value)
        {
            Recovery.CheckParents(path);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(value)); file.Write(bytes, 0, bytes.Length); file.Flush(true);
            }
        }
        internal static void CancelUnstarted(string directory)
        {
            Recovery.CheckParents(directory);
            using (var claim = new FileStream(Path.Combine(directory, "consumed.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { claim.Flush(true); }
            WriteNewJson(Path.Combine(directory, "result.json"), new MirakOutcome { State = "Cancelled", Success = false, Message = "Administrator consent cancelled; this request cannot be replayed." });
        }
        public static bool HandleWorker(string[] args)
        {
            if (args.Length == 0 || args[0] != WorkerSwitch) return false;
            string directory = null; bool claimed = false;
            try
            {
                if (args.Length != 2) throw new IOException("Invalid workaround worker arguments.");
                directory = RequestDirectory(args[1]);
                using (var lease = new MirakPathLease(new [] { directory }))
                {
                    var request = ReadRequest(Path.Combine(directory, "request.json"), args[1]);
                    using (var claim = new FileStream(Path.Combine(directory, "consumed.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        claimed = true;
                        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                        if (!principal.IsInRole(WindowsBuiltInRole.Administrator)) throw new IOException("Administrator permission is required to change or restore Steam game files.");
                        var engine = new MirakRecovery();
                        MirakRecord record = request.Action == "Apply" ? engine.Apply(request.GamePath, request.Review) : engine.Undo(request.Review);
                        WriteNewJson(Path.Combine(directory, "result.json"), new MirakOutcome { Success = true, State = record.State,
                            BackupDirectory = Path.Combine(engine.BackupRoot, record.Id), Message = "Operation completed. Backup copies are retained. A completed workaround is not proof that the crash is fixed." });
                    }
                }
            }
            catch (Exception error)
            {
                if (claimed && directory != null)
                    try { WriteNewJson(Path.Combine(directory, "result.json"), new MirakOutcome { Success = false, State = "Stopped", Message = error.Message }); } catch (IOException) { }
                Environment.ExitCode = 2;
            }
            return true;
        }
        public static async Task<MirakOutcome> Execute(MirakPlan plan, bool undo)
        {
            MirakRecovery.EnsureClosed();
            if (plan == null || (undo ? !plan.CanUndo : !plan.CanApply)) throw new IOException("Review this action first.");
            MirakRecovery.SecureDirectory(MirakRecovery.DefaultStore); MirakRecovery.SecureDirectory(Requests);
            string id = Guid.NewGuid().ToString("N"), directory = RequestDirectory(id);
            MirakRecovery.SecureDirectory(directory);
            WriteNewJson(Path.Combine(directory, "request.json"), new MirakRequest { Id = id, Action = undo ? "Undo" : "Apply", GamePath = plan.GamePath,
                Review = plan.Fingerprint, OwnerSid = WindowsIdentity.GetCurrent().User.Value, CreatedUtc = DateTimeOffset.UtcNow.ToString("o") });
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, WorkerSwitch + " " + id) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
                {
                    if (process == null) throw new IOException("The elevated helper did not start.");
                    bool ended = await Task.Run(() => process.WaitForExit(120000));
                    if (!ended) return new MirakOutcome { Success = false, State = "Unconfirmed", Message = "The worker has not confirmed completion within two minutes. Do not assume success. Keep the helper and backups; review recovery state before retrying." };
                    string receipt = Path.Combine(directory, "result.json"); Recovery.CheckParents(receipt);
                    if (!File.Exists(receipt)) return new MirakOutcome { Success = false, State = "Unconfirmed", Message = "No completion receipt was returned. Reopen/review recovery before retrying. Approve elevation using the same Windows account that opened the helper; another administrator account has a different recovery store." };
                    if (new FileInfo(receipt).Length > 16384) throw new IOException("Invalid completion receipt size.");
                    var result = new JavaScriptSerializer().Deserialize<MirakOutcome>(File.ReadAllText(receipt));
                    if (result == null || (result.Success && process.ExitCode != 0)) throw new IOException("The completion receipt and worker exit do not agree.");
                    return result;
                }
            }
            catch (Win32Exception error)
            {
                if (error.NativeErrorCode != 1223) throw;
                CancelUnstarted(directory);
                return new MirakOutcome { Success = false, State = "Cancelled", Message = Diagnostics.L("Administratorabfrage abgebrochen. Keine Änderung gestartet.", "Administrator prompt cancelled. No change started.") };
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Bf6CrashHelper;

/// <summary>Inert validation tests: never call WPR, request UAC, or start a trace.</summary>
public static class TraceRecorderTests
{
    private static int assertions;
    private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    public static void RunAll()
    {
        assertions = 0;
        RejectDuration(0); RejectDuration(31); RejectDuration(Int32.MinValue); RejectDuration(Int32.MaxValue);
        RejectPath(delegate { TraceRecorder.RequestStop(Path.GetPathRoot(Environment.SystemDirectory)); });
        RejectPath(delegate { TraceRecorder.RetryCleanup(Path.GetPathRoot(Environment.SystemDirectory)); });
        RejectPath(delegate { TraceRecorder.RequestStop(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"))); });
        RejectPath(delegate { TraceRecorder.RetryCleanup(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"))); });
        RejectPath(delegate { TraceRecorder.RequestStop("relative-output"); });
        Check(TraceRecorder.ReadStatus(Path.GetTempPath()).StartsWith("CleanupPending\r\n", StringComparison.Ordinal), "Unreadable/unvalidated status must not claim cleanup or completion.");
        Check(!TraceRecorder.HandleWorker(new string[0]), "Ordinary launch is not a worker.");
        Check(!TraceRecorder.HandleWorker(new string[] { "--unrelated" }), "Unknown switches do not start a worker.");
        Quoting(); Profile(); StateReconciliation();
        Console.WriteLine("PASS: " + assertions + " trace-recorder assertions; no ETW/UAC/process capture was performed.");
    }

    private static void RejectDuration(int minutes)
    {
        try { TraceRecorder.Start(minutes); }
        catch (ArgumentOutOfRangeException) { assertions++; return; }
        throw new Exception("Invalid duration was accepted before the capture boundary: " + minutes);
    }

    private static void RejectPath(Action action)
    {
        try { action(); }
        catch (ArgumentException) { assertions++; return; }
        catch (DirectoryNotFoundException) { assertions++; return; }
        throw new Exception("An external or noncanonical capture path was accepted.");
    }

    private static void Quoting()
    {
        MethodInfo method = typeof(TraceRecorder).GetMethod("JoinArguments", PrivateStatic);
        Check(method != null, "Argument quoting helper exists.");
        string actual = (string)method.Invoke(null, new object[] { new string[] { "a b", "x\"y", "c:\\end\\", "" } });
        Check(actual == "\"a b\" \"x\\\"y\" \"c:\\end\\\\\" \"\"", "Spaces, quotes, trailing backslashes, and empty arguments round-trip safely.");
        actual = (string)method.Invoke(null, new object[] { new string[] { "-cancel", "-instancename", "BF6CrashHelper_1234" } });
        Check(actual == "\"-cancel\" \"-instancename\" \"BF6CrashHelper_1234\"", "Every control argument is explicitly quoted.");
    }

    private static void Profile()
    {
        FieldInfo field = typeof(TraceRecorder).GetField("ProfileXml", PrivateStatic);
        Check(field != null && field.IsLiteral, "The capture profile is fixed in the binary.");
        XmlDocument document = new XmlDocument();
        document.XmlResolver = null;
        document.LoadXml((string)field.GetRawConstantValue());
        Check(document.SelectSingleNode("/WindowsPerformanceRecorder/Profiles/SystemCollector/BufferSize").Attributes["Value"].Value == "1024", "Each event buffer is 1 MiB.");
        Check(document.SelectSingleNode("/WindowsPerformanceRecorder/Profiles/SystemCollector/Buffers").Attributes["Value"].Value == "128", "The configured circular buffers total 128 MiB.");
        XmlNodeList profiles = document.SelectNodes("/WindowsPerformanceRecorder/Profiles/Profile");
        Check(profiles.Count == 1 && profiles[0].Attributes["LoggingMode"].Value == "Memory", "Only memory recording is configured.");
        Check(profiles[0].Attributes["DetailLevel"].Value == "Light", "The bounded profile uses light detail.");
        XmlNodeList keywords = document.SelectNodes("/WindowsPerformanceRecorder/Profiles/SystemProvider/Keywords/Keyword");
        string actual = "";
        foreach (XmlNode keyword in keywords) actual += (actual.Length == 0 ? "" : ",") + keyword.Attributes["Value"].Value;
        Check(actual == "ProcessThread,Loader,FileIO,FileIOInit,DiskIO", "The provider keyword allowlist contains only required process, image and file metadata.");
        Check(document.SelectNodes("//Stack | //Stacks | //EventProvider").Count == 0, "No stacks, heap or additional event providers are configured.");
    }

    private static void StateReconciliation()
    {
        using (CaptureFixture first = new CaptureFixture())
        using (CaptureFixture second = new CaptureFixture())
        {
            string awaiting = "Recording\r\nAwaiting elevated worker; no trace has started.\r\n";
            first.WriteState(awaiting);
            Check(TraceRecorder.ReadStatus(first.Root).StartsWith("Recording\r\n", StringComparison.Ordinal), "Awaiting an initial worker gets a short startup grace period.");
            Check(!File.Exists(Path.Combine(first.Root, "worker.lock")), "Reading status never creates a worker lock.");
            File.SetLastWriteTimeUtc(first.Status, DateTime.UtcNow.AddMinutes(-2));
            Check(TraceRecorder.ReadStatus(first.Root).StartsWith("Failed\r\n", StringComparison.Ordinal), "Stale awaiting status without a lock reports that no trace started.");
            Check(File.ReadAllText(first.Status) == awaiting, "Stale status inspection does not rewrite the state file.");

            first.WriteState("Recording\r\nStarting private trace instance.\r\n");
            Check(TraceRecorder.ReadStatus(first.Root).StartsWith("CleanupPending\r\n", StringComparison.Ordinal), "A started trace with a missing worker lock stays unconfirmed.");
            using (FileStream held = new FileStream(Path.Combine(first.Root, "worker.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                first.WriteState("Recording\r\nTrace is recording.\r\n");
                Check(TraceRecorder.ReadStatus(first.Root).StartsWith("Recording\r\n", StringComparison.Ordinal), "A live exclusive worker lock preserves Recording state.");
            }
            Check(TraceRecorder.ReadStatus(first.Root).StartsWith("CleanupPending\r\n", StringComparison.Ordinal), "A released worker lock recovers an orphaned recording after restart.");
            first.WriteState("");
            Check(TraceRecorder.ReadStatus(first.Root).StartsWith("CleanupPending\r\n", StringComparison.Ordinal), "An incomplete saved status never claims the private session is stopped.");

            first.WriteState("CleanupPending\r\nSynthetic older pending run.\r\n");
            File.SetLastWriteTimeUtc(first.Status, DateTime.UtcNow.AddMinutes(-10));
            second.WriteState("CleanupPending\r\nSynthetic newer pending run.\r\n");
            File.SetLastWriteTimeUtc(second.Status, DateTime.UtcNow.AddMinutes(-5));
            MethodInfo find = typeof(TraceRecorder).GetMethod("FindPendingFromDirectories", PrivateStatic);
            string selected = (string)find.Invoke(null, new object[] { new string[] { first.Root, second.Root } });
            Check(selected == second.Root, "Pending-run discovery selects the most recent owned candidate.");
            selected = (string)find.Invoke(null, new object[] { BeyondHistoryBound() });
            Check(selected == null, "History inspection is bounded at 100 candidates.");
        }
    }

    private static IEnumerable<string> BeyondHistoryBound()
    {
        for (int i = 0; i < 100; i++) yield return Path.GetTempPath();
        throw new Exception("History discovery enumerated beyond its 100-entry bound.");
    }

    private sealed class CaptureFixture : IDisposable
    {
        public readonly string Root;
        public string Status { get { return Path.Combine(Root, "status.txt"); } }
        public CaptureFixture()
        {
            Root = (string)typeof(TraceRecorder).GetMethod("CreateRunDirectory", PrivateStatic).Invoke(null, null);
        }
        public void WriteState(string state) { File.WriteAllText(Status, state); }
        public void Dispose()
        {
            string captures = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BF6CrashHelper", "captures"));
            Guid id;
            string full = Path.GetFullPath(Root);
            if (!Guid.TryParseExact(Path.GetFileName(full), "D", out id) ||
                !String.Equals(full, Path.Combine(captures, id.ToString("D")), StringComparison.OrdinalIgnoreCase) ||
                File.ReadAllText(Path.Combine(full, "capture-owner.txt")) != "BF6CrashHelper capture v1\r\n" + id.ToString("D"))
                throw new Exception("Unsafe synthetic capture cleanup path.");
            Directory.Delete(full, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Trace recorder test failed: " + message);
        assertions++;
    }
}

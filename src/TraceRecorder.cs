using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace Bf6CrashHelper
{
    /// <summary>Opt-in, local-only ETW recording. Nothing is recorded merely by loading this class.</summary>
    public static class TraceRecorder
    {
        private const string WorkerSwitch = "--capture-worker";
        private const string CleanupWorkerSwitch = "--cleanup-worker";
        private const string OwnerHeader = "BF6CrashHelper capture v1\r\n";
        private const string StatusFile = "status.txt";
        private const string StopFile = "stop.request";
        private const string ProfileFile = "Bf6FileIO.wprp";
        private const string TraceFile = "bf6-fileio.etl";
        private const string LogFile = "wpr-control.log";
        private const int ControlWaitMilliseconds = 55000;
        private const int KillWaitMilliseconds = 5000;

        // 1 MiB x 128 buffers = 128 MiB circular kernel-event buffers, plus ETW overhead.
        // No stacks, heap, registry or network providers, and no file-content payloads.
        private const string ProfileXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<WindowsPerformanceRecorder Version=""1.0"" Author=""BF6 Crash Helper"">
  <Profiles>
    <SystemCollector Id=""Bf6FileSystemCollector"" Name=""Bf6FileSystemCollector"">
      <BufferSize Value=""1024"" />
      <Buffers Value=""128"" />
    </SystemCollector>
    <SystemProvider Id=""Bf6FileSystemProvider"">
      <Keywords>
        <Keyword Value=""ProcessThread"" />
        <Keyword Value=""Loader"" />
        <Keyword Value=""FileIO"" />
        <Keyword Value=""FileIOInit"" />
        <Keyword Value=""DiskIO"" />
      </Keywords>
    </SystemProvider>
    <Profile Id=""Bf6FileIO.Light.Memory"" Name=""Bf6FileIO"" Description=""Private circular file-operation and module metadata; no file contents"" LoggingMode=""Memory"" DetailLevel=""Light"">
      <Collectors>
        <SystemCollectorId Value=""Bf6FileSystemCollector""><SystemProviderId Value=""Bf6FileSystemProvider"" /></SystemCollectorId>
      </Collectors>
    </Profile>
  </Profiles>
</WindowsPerformanceRecorder>";

        /// <summary>Call first from Main. Returns false for an ordinary GUI launch.</summary>
        public static bool HandleWorker(string[] args)
        {
            if (args == null || args.Length == 0 || (args[0] != WorkerSwitch && args[0] != CleanupWorkerSwitch))
                return false;

            string runDir = null;
            bool cleanupOnly = args[0] == CleanupWorkerSwitch;
            try
            {
                if (cleanupOnly)
                {
                    if (args.Length != 3 || args[1] != "--run")
                        throw new ArgumentException("Invalid cleanup-worker arguments.");
                    runDir = ValidateRunDirectory(args[2], true);
                    RunCleanupWorker(runDir);
                    return true;
                }
                if (args.Length != 5 || args[1] != "--run" || args[3] != "--minutes")
                    throw new ArgumentException("Invalid capture-worker arguments.");
                runDir = ValidateRunDirectory(args[2], true);
                int minutes;
                if (!Int32.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
                    throw new ArgumentException("Capture duration must be a whole number from 1 to 30 minutes.");
                ValidateMinutes(minutes);
                RunWorker(runDir, minutes);
            }
            catch (Exception ex)
            {
                // Invalid paths are never used for output, including error output.
                if (runDir != null)
                    TryStatus(runDir, cleanupOnly ? "CleanupPending" : "Failed", "Worker could not start. " + SafeMessage(ex));
                Environment.ExitCode = 2;
            }
            return true;
        }

        /// <summary>The GUI must obtain explicit capture/elevation consent before calling this.</summary>
        public static string Start(int minutes)
        {
            ValidateMinutes(minutes);
            if (GameIsRunning())
                throw new InvalidOperationException("Close Battlefield 6 before starting the recorder. Start the game only after the status says the trace is recording.");
            string wpr = GetWprPath();
            if (!File.Exists(wpr))
                throw new FileNotFoundException("The built-in Windows Performance Recorder is unavailable. No capture was started.");
            EnsureCaptureSpace();

            string runDir = CreateRunDirectory();
            WriteStatus(runDir, "Recording", "Awaiting elevated worker; no trace has started.\r\nRequested duration: " + minutes.ToString(CultureInfo.InvariantCulture) + " minute(s).");
            try
            {
                LaunchElevatedWorker(runDir, new string[] { WorkerSwitch, "--run", runDir, "--minutes", minutes.ToString(CultureInfo.InvariantCulture) });
                return runDir;
            }
            catch (Win32Exception ex)
            {
                string message = ex.NativeErrorCode == 1223
                    ? "Administrator consent was cancelled. No capture was started."
                    : "The elevated worker could not be started. " + SafeMessage(ex);
                TryStatus(runDir, "Failed", message);
                throw new InvalidOperationException(message, ex);
            }
            catch (Exception ex)
            {
                TryStatus(runDir, "Failed", "The worker could not be started. " + SafeMessage(ex));
                throw;
            }
        }

        /// <summary>Requests this run only to stop. Does not elevate or start a recorder.</summary>
        public static void RequestStop(string runDir)
        {
            runDir = ValidateRunDirectory(runDir, true);
            WriteFixedText(runDir, StopFile, "Stop requested at " + UtcNow() + "\r\n");
        }

        /// <summary>The GUI must obtain separate cleanup/elevation consent before calling this.</summary>
        public static void RetryCleanup(string runDir)
        {
            runDir = ValidateRunDirectory(runDir, true);
            if (GameIsRunning())
                throw new InvalidOperationException("Close Battlefield 6 before retrying cleanup of this capture's private trace instance.");
            if (!ReadStatus(runDir).StartsWith("CleanupPending\r\n", StringComparison.Ordinal))
                throw new InvalidOperationException("Cleanup retry is available only when cleanup is pending or unconfirmed.");
            if (!File.Exists(GetWprPath()))
                throw new FileNotFoundException("The built-in Windows Performance Recorder is unavailable. Cleanup remains pending.");
            WriteStatus(runDir, "CleanupPending", "Awaiting elevated cleanup worker.\r\nInstance: " + InstanceName(runDir));
            try
            {
                LaunchElevatedWorker(runDir, new string[] { CleanupWorkerSwitch, "--run", runDir });
            }
            catch (Win32Exception ex)
            {
                string message = ex.NativeErrorCode == 1223
                    ? "Administrator consent was cancelled. Cleanup remains pending."
                    : "The cleanup worker could not be started. " + SafeMessage(ex);
                TryStatus(runDir, "CleanupPending", message + "\r\nInstance: " + InstanceName(runDir));
                throw new InvalidOperationException(message, ex);
            }
            catch (Exception ex)
            {
                TryStatus(runDir, "CleanupPending", "The cleanup worker could not be started. " + SafeMessage(ex) + "\r\nInstance: " + InstanceName(runDir));
                throw;
            }
        }

        /// <summary>Plain text suitable for a status panel. The ETL must stay private.</summary>
        public static string ReadStatus(string runDir)
        {
            try
            {
                runDir = ValidateRunDirectory(runDir, true);
                string path = SafeChild(runDir, StatusFile);
                if (!File.Exists(path))
                    return InspectWorkerState(runDir, "Recording\r\nWaiting for worker status; no recording is confirmed.\r\n", Directory.GetCreationTimeUtc(runDir), false);
                return InspectWorkerState(runDir, ReadSmallText(path, 65536), File.GetLastWriteTimeUtc(path), true);
            }
            catch (Exception ex)
            {
                return "CleanupPending\r\nStatus and trace cleanup are unconfirmed. " + SafeMessage(ex) + "\r\n";
            }
        }

        /// <summary>Reads at most 100 initialized capture folders. Does not prompt or control WPR.</summary>
        public static string FindPendingRun()
        {
            try
            {
                string captures = CapturesRoot();
                RejectReparse(Path.GetDirectoryName(captures));
                RejectReparse(captures);
                if (!Directory.Exists(captures)) return null;
                return FindPendingFromDirectories(Directory.EnumerateDirectories(captures, "*", SearchOption.TopDirectoryOnly));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }

        private static string FindPendingFromDirectories(IEnumerable<string> directories)
        {
            string latest = null;
            DateTime latestTime = DateTime.MinValue;
            using (IEnumerator<string> entries = directories.GetEnumerator())
            {
                for (int inspected = 0; inspected < 100 && entries.MoveNext(); inspected++)
                {
                    try
                    {
                        string runDir = ValidateRunDirectory(entries.Current, true);
                        string state = ReadStatus(runDir);
                        if (!state.StartsWith("Recording\r\n", StringComparison.Ordinal) && !state.StartsWith("CleanupPending\r\n", StringComparison.Ordinal))
                            continue;
                        DateTime modified = DateTime.MinValue;
                        // Keep an owned, unconfirmed run discoverable even if its status
                        // metadata is inaccessible; never follow a redirected status file.
                        try
                        {
                            modified = Directory.GetCreationTimeUtc(runDir);
                            string statusPath = SafeChild(runDir, StatusFile);
                            if (File.Exists(statusPath)) modified = File.GetLastWriteTimeUtc(statusPath);
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        catch (System.Security.SecurityException) { }
                        if (latest == null || modified > latestTime)
                        {
                            latest = runDir;
                            latestTime = modified;
                        }
                    }
                    catch (ArgumentException) { }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (System.Security.SecurityException) { }
                }
            }
            return latest;
        }

        private static string InspectWorkerState(string runDir, string state, DateTime statusTime, bool statusExists)
        {
            if (!state.StartsWith("Recording\r\n", StringComparison.Ordinal))
            {
                if (state.StartsWith("Completed\r\n", StringComparison.Ordinal) || state.StartsWith("Failed\r\n", StringComparison.Ordinal) || state.StartsWith("CleanupPending\r\n", StringComparison.Ordinal)) return state;
                return "CleanupPending\r\nThe saved status is incomplete or unrecognized. Trace cleanup remains unconfirmed.\r\nInstance: " + InstanceName(runDir) + "\r\n";
            }
            string lockPath = SafeChild(runDir, "worker.lock");
            try
            {
                using (FileStream probe = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    // Opening an existing lock exclusively proves no worker currently owns it.
                    return "CleanupPending\r\nThe saved recording state has no active worker lock. Cleanup of this private instance is unconfirmed.\r\nInstance: " + InstanceName(runDir) + "\r\nPrevious status:\r\n" + state;
                }
            }
            catch (FileNotFoundException)
            {
                bool awaiting = statusExists && state.IndexOf("Awaiting elevated worker; no trace has started.", StringComparison.Ordinal) >= 0;
                TimeSpan age = DateTime.UtcNow - statusTime;
                if ((awaiting || !statusExists) && age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(60))
                    return state;
                if (awaiting && age > TimeSpan.FromSeconds(60))
                    return "Failed\r\nThe elevated worker did not create its lock within 60 seconds. No trace was confirmed started. Start a new capture explicitly if needed.\r\nPrevious status:\r\n" + state;
                return "CleanupPending\r\nThe recording's worker lock is missing. Its private trace cleanup is unconfirmed.\r\nInstance: " + InstanceName(runDir) + "\r\nPrevious status:\r\n" + state;
            }
            catch (IOException ex)
            {
                int code = ex.HResult & 0xffff;
                if (code == 32 || code == 33) return state; // An existing owner holds the lock.
                return "CleanupPending\r\nWorker ownership could not be checked. Cleanup remains unconfirmed. " + SafeMessage(ex) + "\r\nPrevious status:\r\n" + state;
            }
            catch (UnauthorizedAccessException ex)
            {
                return "CleanupPending\r\nWorker ownership could not be checked. Cleanup remains unconfirmed. " + SafeMessage(ex) + "\r\nPrevious status:\r\n" + state;
            }
        }

        private static void LaunchElevatedWorker(string runDir, string[] arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Assembly.GetEntryAssembly().Location;
            info.Arguments = JoinArguments(arguments);
            info.UseShellExecute = true;
            info.Verb = "runas";
            info.WindowStyle = ProcessWindowStyle.Hidden;
            Process worker = Process.Start(info);
            if (worker == null) throw new InvalidOperationException("Windows did not start the elevated worker.");
            worker.Exited += delegate
            {
                try
                {
                    string state = ReadStatus(runDir);
                    if (state.StartsWith("Recording\r\n", StringComparison.Ordinal))
                    {
                        bool neverStarted = state.IndexOf("Awaiting elevated worker; no trace has started.", StringComparison.Ordinal) >= 0;
                        TryStatus(runDir, neverStarted ? "Failed" : "CleanupPending", neverStarted
                            ? "The elevated worker exited before starting. Use administrator consent for the same Windows account. No trace was confirmed started."
                            : "The worker exited without a terminal status. Its private trace instance may need cleanup after the game is closed.\r\nInstance: " + InstanceName(runDir));
                    }
                    else if (state.IndexOf("Awaiting elevated cleanup worker.", StringComparison.Ordinal) >= 0)
                        TryStatus(runDir, "CleanupPending", "The cleanup worker exited before confirming cleanup. Another worker may still own this run, or administrator consent used a different Windows account.\r\nInstance: " + InstanceName(runDir));
                }
                finally { worker.Dispose(); }
            };
            worker.EnableRaisingEvents = true;
        }

        private static void RunCleanupWorker(string runDir)
        {
            FileStream workerLock;
            try { workerLock = new FileStream(SafeChild(runDir, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { Environment.ExitCode = 3; return; }
            using (workerLock)
            {
                string instance = InstanceName(runDir);
                try
                {
                    WindowsPrincipal principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                    if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                        throw new InvalidOperationException("Administrator consent is required for this cleanup retry.");
                    if (GameIsRunning())
                        throw new InvalidOperationException("Battlefield 6 is running. Close it before retrying cleanup.");
                    string wpr = GetWprPath();
                    if (!File.Exists(wpr)) throw new FileNotFoundException("The built-in Windows Performance Recorder is unavailable.");
                    WriteStatus(runDir, "CleanupPending", "Retrying cleanup of this private instance only.\r\nInstance: " + instance);
                    ControlResult cancel = Control(wpr, runDir, new string[] { "-cancel", "-instancename", instance });
                    if (cancel.Success && !cancel.MayStillRun)
                    {
                        TryStatus(runDir, "Failed", "The earlier capture did not complete. Cleanup of its private trace instance is now confirmed.\r\nInstance: " + instance + "\r\n" + FileDetails(runDir) + "Private ETL files remain excluded from automatic sharing.");
                    }
                    else
                    {
                        TryStatus(runDir, "CleanupPending", "Cleanup could not be confirmed. " + cancel.Summary + "\r\nInstance: " + instance + "\r\n" + FileDetails(runDir));
                        Environment.ExitCode = 4;
                    }
                }
                catch (Exception ex)
                {
                    TryStatus(runDir, "CleanupPending", "Cleanup remains unconfirmed. " + SafeMessage(ex) + "\r\nInstance: " + instance);
                    Environment.ExitCode = 4;
                }
            }
        }

        private static void RunWorker(string runDir, int minutes)
        {
            string instance = InstanceName(runDir);
            string wpr = GetWprPath();
            string failure = null;
            string reason = null;
            bool sessionPossible = false;
            bool started = false;
            bool saveRequested = false;
            bool saved = false;
            bool cleanupConfirmed = false;
            bool controlStillRunning = false;
            FileStream workerLock = null;
            try
            {
                // A second worker for the same GUID must not control the first worker's instance.
                workerLock = new FileStream(SafeChild(runDir, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                Environment.ExitCode = 3;
                return;
            }

            try
            {
                WindowsPrincipal principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("Administrator consent is required for kernel ETW tracing. No trace was started.");
                if (!File.Exists(wpr))
                    throw new FileNotFoundException("The built-in Windows Performance Recorder is unavailable. No trace was started.");
                if (GameIsRunning())
                    throw new InvalidOperationException("Battlefield 6 is already running. Close it, then explicitly start a new capture from the helper.");
                if (StopRequested(runDir))
                    throw new OperationCanceledException("Capture was stopped before recording began.");

                WriteFixedText(runDir, ProfileFile, ProfileXml);
                WriteStatus(runDir, "Recording", "Starting private trace instance; wait before launching Battlefield 6.\r\nInstance: " + instance);
                sessionPossible = true; // A failed/timed-out start can still leave a partial session.
                ControlResult start = Control(wpr, runDir, new string[] { "-start", SafeChild(runDir, ProfileFile) + "!Bf6FileIO.Light", "-instancename", instance });
                controlStillRunning = start.MayStillRun;
                if (!start.Success)
                    throw new InvalidOperationException("WPR could not start the private trace. " + start.Summary);
                started = true;
                DateTime deadline = DateTime.UtcNow.AddMinutes(minutes);
                WriteStatus(runDir, "Recording", "Trace is recording. You may now launch Battlefield 6.\r\nWaiting for bf6.exe.\r\nDeadline (UTC): " + deadline.ToString("O", CultureInfo.InvariantCulture) + "\r\nInstance: " + instance);
                bool gameSeen = false;
                while (true)
                {
                    if (StopRequested(runDir)) { reason = "User requested stop."; break; }
                    if (DateTime.UtcNow >= deadline) { reason = "Capture duration reached."; break; }
                    bool running = GameIsRunning();
                    if (running && !gameSeen)
                    {
                        gameSeen = true;
                        WriteStatus(runDir, "Recording", "Battlefield 6 was observed. Waiting for its exit, the deadline, or Stop.\r\nFirst seen (UTC): " + UtcNow() + "\r\nDeadline (UTC): " + deadline.ToString("O", CultureInfo.InvariantCulture) + "\r\nInstance: " + instance);
                    }
                    if (gameSeen && !running)
                    {
                        reason = "Battlefield 6 exited (exit alone does not prove a crash).";
                        WriteStatus(runDir, "Recording", "Battlefield 6 exited. Waiting two seconds for its crash reporter, then saving once.\r\nInstance: " + instance);
                        Thread.Sleep(2000);
                        break;
                    }
                    Thread.Sleep(500);
                }
                saveRequested = true;
            }
            catch (Exception ex)
            {
                failure = SafeMessage(ex);
            }
            finally
            {
                // Every control command names only this GUID-derived instance. Never use global cancel.
                try
                {
                    if (sessionPossible)
                    {
                        if (started && saveRequested)
                        {
                            TryStatus(runDir, "Recording", "Stopping and saving the private trace.\r\nReason: " + reason + "\r\nInstance: " + instance);
                            ControlResult stop = Control(wpr, runDir, new string[] { "-stop", SafeChild(runDir, TraceFile), "-skipPdbGen", "-instancename", instance });
                            controlStillRunning |= stop.MayStillRun;
                            cleanupConfirmed = stop.Success;
                            saved = stop.Success && TraceHasData(runDir);
                            if (!saved)
                                failure = AppendFailure(failure, stop.Success ? "WPR stopped, but no nonempty ETL file was found." : "WPR could not save the trace. " + stop.Summary);
                        }
                        if (!cleanupConfirmed)
                        {
                            ControlResult cancel = Control(wpr, runDir, new string[] { "-cancel", "-instancename", instance });
                            controlStillRunning |= cancel.MayStillRun;
                            cleanupConfirmed = cancel.Success;
                            if (!cleanupConfirmed)
                                failure = AppendFailure(failure, "Cleanup of this instance could not be confirmed. " + cancel.Summary);
                        }
                    }
                    else { cleanupConfirmed = true; }
                }
                catch (Exception ex)
                {
                    failure = AppendFailure(failure, "Saving or cleanup failed: " + SafeMessage(ex));
                    // Cancelling a fixed, derived instance needs no output path, even if the
                    // capture directory was removed or redirected while recording.
                    ControlResult cancel = Control(wpr, runDir, new string[] { "-cancel", "-instancename", instance });
                    cleanupConfirmed = cancel.Success;
                    controlStillRunning |= cancel.MayStillRun;
                }

                if (controlStillRunning)
                {
                    cleanupConfirmed = false;
                    failure = AppendFailure(failure, "A WPR control process did not confirm exit within its time bound; instance cleanup remains uncertain.");
                }

                string details = "Finished (UTC): " + UtcNow() + "\r\nInstance: " + instance + "\r\n";
                if (reason != null) details += "Reason: " + reason + "\r\n";
                if (failure != null) details += "Details: " + failure + "\r\n";
                details += FileDetails(runDir);
                if (!cleanupConfirmed)
                {
                    details += "Cleanup is still pending. Close Battlefield 6 before seeking help to stop this named instance. No other tracing sessions were cancelled.\r\n";
                    TryStatus(runDir, "CleanupPending", details);
                    Environment.ExitCode = 4;
                }
                else if (saved && failure == null)
                {
                    TryStatus(runDir, "Completed", details + "Private ETL: excluded from automatic sharing.\r\n");
                }
                else
                {
                    TryStatus(runDir, "Failed", details + "The private instance was stopped, or no session was started.\r\n");
                    Environment.ExitCode = 2;
                }
                workerLock.Dispose();
            }
        }

        private sealed class ControlResult
        {
            public bool Success;
            public bool MayStillRun;
            public string Summary;
        }

        private static ControlResult Control(string executable, string runDir, string[] arguments)
        {
            ControlResult result = new ControlResult();
            StringBuilder output = new StringBuilder();
            object sync = new object();
            Process process = null;
            Stopwatch elapsed = Stopwatch.StartNew();
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = executable;
                info.Arguments = JoinArguments(arguments);
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.WorkingDirectory = Path.GetDirectoryName(executable);
                process = new Process();
                process.StartInfo = info;
                DataReceivedEventHandler collect = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                        lock (sync) { if (output.Length < 65536) output.AppendLine(e.Data); }
                };
                process.OutputDataReceived += collect;
                process.ErrorDataReceived += collect;
                process.Start();
                result.MayStillRun = true;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                int wait = Math.Max(0, ControlWaitMilliseconds - (int)Math.Min(elapsed.ElapsedMilliseconds, ControlWaitMilliseconds));
                if (!process.WaitForExit(wait))
                    result.Summary = "Control command exceeded its 60-second bound.";
                else
                {
                    result.MayStillRun = false;
                    result.Success = process.ExitCode == 0;
                    result.Summary = "WPR exit code: " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".";
                }
            }
            catch (Exception ex) { result.Summary = "Control command failed: " + SafeMessage(ex); }
            finally
            {
                if (process != null)
                {
                    if (result.MayStillRun)
                    {
                        try
                        {
                            if (!process.HasExited) process.Kill();
                            int remaining = Math.Max(0, ControlWaitMilliseconds + KillWaitMilliseconds - (int)Math.Min(elapsed.ElapsedMilliseconds, ControlWaitMilliseconds + KillWaitMilliseconds));
                            result.MayStillRun = !process.WaitForExit(remaining);
                        }
                        catch (InvalidOperationException) { }
                        catch (Win32Exception) { }
                    }
                    process.Dispose();
                }
            }
            string text;
            lock (sync) { text = output.ToString(); }
            try
            {
                string record = UtcNow() + " " + JoinArguments(arguments) + "\r\n" + result.Summary + "\r\n" + text + "\r\n";
                WriteFixedText(runDir, LogFile, record);
                string operation = arguments[0] == "-start" ? "start" : arguments[0] == "-stop" ? "stop" : "cancel";
                WriteFixedText(runDir, "wpr-" + operation + ".log", record);
            }
            catch (Exception) { /* Trace cleanup must continue even if the local log cannot be written. */ }
            return result;
        }

        private static bool GameIsRunning()
        {
            Process[] processes = Process.GetProcessesByName("bf6");
            bool found = processes.Length != 0;
            foreach (Process process in processes) process.Dispose();
            return found;
        }

        private static bool StopRequested(string runDir)
        {
            return File.Exists(SafeChild(runDir, StopFile));
        }

        private static bool TraceHasData(string runDir)
        {
            string path = SafeChild(runDir, TraceFile);
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }

        private static string FileDetails(string runDir)
        {
            try
            {
                string trace = SafeChild(runDir, TraceFile);
                string details = "Private capture folder: " + runDir + "\r\n";
                if (File.Exists(trace))
                    details += "ETL: " + trace + "\r\nETL bytes: " + new FileInfo(trace).Length.ToString(CultureInfo.InvariantCulture) + "\r\n";
                else details += "ETL: not saved.\r\n";
                details += "Local control log: " + SafeChild(runDir, LogFile) + "\r\n";
                return details;
            }
            catch (Exception ex) { return "File details unavailable: " + SafeMessage(ex) + "\r\n"; }
        }

        private static string GetWprPath()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return Path.Combine(windows, Environment.Is64BitOperatingSystem && IntPtr.Size == 4 ? "Sysnative" : "System32", "wpr.exe");
        }

        private static string CapturesRoot()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrEmpty(local)) throw new IOException("Local AppData is unavailable.");
            local = Path.GetFullPath(local);
            if (local.StartsWith("\\\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(local)).DriveType == DriveType.Network)
                throw new IOException("Capture output must be on a local drive, not redirected network AppData.");
            // A redirected/reparse-point parent could silently put private output elsewhere.
            for (DirectoryInfo parent = new DirectoryInfo(local); parent != null; parent = parent.Parent)
                RejectReparse(parent.FullName);
            return Path.Combine(local, "BF6CrashHelper", "captures");
        }

        private static void EnsureCaptureSpace()
        {
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(CapturesRoot()));
            if (drive.AvailableFreeSpace < 512L * 1024L * 1024L)
                throw new IOException("At least 512 MiB of free space is required on the local capture drive before recording. Free some space and try again. No capture was started. This preflight is not a guarantee of the final ETL size.");
        }

        private static string CreateRunDirectory()
        {
            string captures = CapturesRoot();
            string appRoot = Path.GetDirectoryName(captures);
            RejectReparse(appRoot);
            Directory.CreateDirectory(appRoot);
            RejectReparse(appRoot);
            RejectReparse(captures);
            Directory.CreateDirectory(captures);
            RejectReparse(captures);
            string id = Guid.NewGuid().ToString("D");
            string runDir = Path.Combine(captures, id);
            Directory.CreateDirectory(runDir);
            RejectReparse(runDir);
            File.WriteAllText(Path.Combine(runDir, "capture-owner.txt"), OwnerHeader + id, new UTF8Encoding(false));
            return ValidateRunDirectory(runDir, true);
        }

        private static string ValidateRunDirectory(string runDir, bool requireOwner)
        {
            if (String.IsNullOrWhiteSpace(runDir)) throw new ArgumentException("A capture folder is required.");
            string captures = CapturesRoot();
            string full = Path.GetFullPath(runDir);
            Guid id;
            if (!Guid.TryParseExact(Path.GetFileName(full), "D", out id))
                throw new ArgumentException("The capture folder must have the helper's GUID name.");
            string expected = Path.Combine(captures, id.ToString("D"));
            if (!String.Equals(full, expected, StringComparison.OrdinalIgnoreCase) || !String.Equals(runDir, expected, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The capture folder is not a canonical helper capture path.");
            RejectReparse(Path.GetDirectoryName(captures));
            RejectReparse(captures);
            RejectReparse(expected);
            if (!Directory.Exists(expected)) throw new DirectoryNotFoundException("The helper capture folder does not exist.");
            if (requireOwner)
            {
                string marker = Path.Combine(expected, "capture-owner.txt");
                RejectReparse(marker);
                if (!File.Exists(marker) || ReadSmallText(marker, 256) != OwnerHeader + id.ToString("D"))
                    throw new ArgumentException("The folder is not an initialized helper capture.");
            }
            return expected;
        }

        private static string InstanceName(string runDir)
        {
            Guid id = Guid.ParseExact(Path.GetFileName(runDir), "D");
            return "BF6CrashHelper_" + id.ToString("N");
        }

        private static string SafeChild(string runDir, string fixedName)
        {
            runDir = ValidateRunDirectory(runDir, true);
            if (fixedName != Path.GetFileName(fixedName) || fixedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Invalid internal capture filename.");
            string path = Path.Combine(runDir, fixedName);
            RejectReparse(path);
            return path;
        }

        private static void RejectReparse(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A reparse-point capture path is not allowed.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

        private static string ReadSmallText(string path, int limit)
        {
            RejectReparse(path);
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > limit) throw new IOException("The helper state file is unexpectedly large.");
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd();
            }
        }

        private static void WriteFixedText(string runDir, string fixedName, string text)
        {
            string target = SafeChild(runDir, fixedName);
            string temporary = SafeChild(runDir, "state-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
                RejectReparse(target);
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void WriteStatus(string runDir, string state, string details)
        {
            WriteFixedText(runDir, StatusFile, state + "\r\nUpdated (UTC): " + UtcNow() + "\r\n" + details + "\r\n");
        }

        private static void TryStatus(string runDir, string state, string details)
        {
            try { WriteStatus(runDir, state, details); }
            catch (Exception) { /* Never skip the finally cleanup because status output failed. */ }
        }

        private static void ValidateMinutes(int minutes)
        {
            if (minutes < 1 || minutes > 30) throw new ArgumentOutOfRangeException("minutes", "Capture duration must be from 1 to 30 minutes.");
        }

        private static string UtcNow() { return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); }
        private static string SafeMessage(Exception ex) { return ex.Message.Replace('\r', ' ').Replace('\n', ' '); }
        private static string AppendFailure(string current, string extra) { return current == null ? extra : current + " " + extra; }

        // Windows CommandLineToArgvW-compatible quoting; no shell is involved.
        private static string JoinArguments(string[] arguments)
        {
            StringBuilder result = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (result.Length != 0) result.Append(' ');
                result.Append('"');
                int slashes = 0;
                foreach (char character in argument)
                {
                    if (character == '\\') { slashes++; continue; }
                    if (character == '"') result.Append('\\', slashes * 2 + 1);
                    else result.Append('\\', slashes);
                    result.Append(character);
                    slashes = 0;
                }
                result.Append('\\', slashes * 2);
                result.Append('"');
            }
            return result.ToString();
        }
    }
}

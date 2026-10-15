using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Bf6CrashHelper
{
    public sealed class Finding
    {
        public string Level = "Info";
        public string Title = "";
        public string Detail = "";
    }

    public sealed class CrashEvidence
    {
        public string Time = "";
        public string ExceptionCode = "";
        public string ModuleOffset = "";
        public string ReadAddress = "";
        public string Signature = "";
        public string DumpName = "";
        public string CasFileId = "";
        public string CasByteOffset = "";
        public string CasByteLength = "";
        public string CasRequestStatus = "";
        public bool KnownSignature;
    }

    public sealed class ScanReport
    {
        public const string NoOwnershipInspection = "Not determined (Steam ownership is not inspected)";
        public string Summary = "";
        // Local UI only. ToShareable deliberately omits this property and dump filenames.
        public string GamePath = "";
        public string BuildId = "";
        public string OwnershipStatus = NoOwnershipInspection;
        public List<Finding> Findings = new List<Finding>();
        public List<CrashEvidence> Crashes = new List<CrashEvidence>();

        public object ToShareable()
        {
            List<object> findings = new List<object>();
            foreach (Finding item in Findings ?? new List<Finding>())
            {
                if (item == null) continue;
                findings.Add(new { Level = ShareText(item.Level), Title = ShareText(item.Title), Detail = ShareText(item.Detail) });
            }
            List<object> crashes = new List<object>();
            foreach (CrashEvidence item in Crashes ?? new List<CrashEvidence>())
            {
                if (item == null) continue;
                crashes.Add(new
                {
                    Time = ShareText(item.Time), ExceptionCode = ShareText(item.ExceptionCode),
                    ModuleOffset = ShareText(item.ModuleOffset), ReadAddress = ShareText(item.ReadAddress),
                    Signature = ShareText(item.Signature), KnownSignature = item.KnownSignature,
                    CasFileId = ShareText(item.CasFileId), CasByteOffset = ShareText(item.CasByteOffset),
                    CasByteLength = ShareText(item.CasByteLength), CasRequestStatus = ShareText(item.CasRequestStatus)
                });
            }
            string build = Regex.IsMatch(BuildId ?? "", @"^\d{1,12}$") ? BuildId : "";
            return new
            {
                SchemaVersion = 1, Summary = ShareText(Summary), GameLocated = !String.IsNullOrEmpty(GamePath),
                BuildId = build, OwnershipStatus = NoOwnershipInspection, Findings = findings, Crashes = crashes,
                Privacy = Diagnostics.L("Absolute Pfade, Benutzernamen, Steam-Konto-IDs, Dumpnamen und rohe Dumpinhalte sind nicht enthalten.", "No absolute paths, usernames, Steam account IDs, dump names or raw dump contents are included.")
            };
        }

        private string ShareText(string value)
        {
            string text = value ?? "";
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string sensitive in new string[] { GamePath, userProfile, Environment.UserName })
            {
                if (!String.IsNullOrWhiteSpace(sensitive) && sensitive.Length >= 3)
                    text = Regex.Replace(text, Regex.Escape(sensitive), "[redacted]", RegexOptions.IgnoreCase);
            }
            // This is a second defence for future UI additions. Diagnostic messages themselves contain no paths.
            text = Regex.Replace(text, @"(?i)(?:[a-z]:[\\/]|\\\\)[^\r\n\""<>|]*", "[path redacted]");
            text = Regex.Replace(text, @"\b7656119\d{10}\b", "[Steam ID redacted]");
            return text;
        }
    }

    public static class Diagnostics
    {
        private const string AppId = "2807960";
        private const uint KnownPeTimestamp = 0x6abf2953;
        private const ulong KnownRva = 0x51ed625;
        private const int MaximumDumpCount = 24;
        private const int MaximumManifestBytes = 32 * 1024 * 1024;
        private const long MaximumTargetHashBytes = 1024L * 1024L * 1024L;
        private static readonly string[] IntegrityTargets = new string[]
        {
            @"Data\layout.toc",
            @"Update\BF6_SAN2_DLC\Data\Win32\game\glaciermp\levels\mp_tungsten\mp_tungsten.toc",
            @"Update\BF6_SAN2_DLC\Data\Win32\game\glaciermp\levels\mp_tungsten\mp_tungsten.digest",
            @"Update\BF6_HRES_SHARED_DLC\Data\Win32\installation\detail\freetrialinstallpackage\game\glaciermp\levels\mp_tungsten\mp_tungsten.toc",
            @"Update\BF6_HRES_SHARED_DLC\Data\Win32\installation\detail\freetrialinstallpackage\game\glaciermp\levels\mp_tungsten\mp_tungsten.digest",
            @"Update\BF6_SAN2_DLC\Data\Win32\installation\san2installpackage\cas_11.cas"
        };

        internal static string L(string german, string english)
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? german : english;
        }

        public static string DetectGamePath()
        {
            foreach (string library in SteamLibraries())
            {
                string manifest = Path.Combine(library, "steamapps", "appmanifest_" + AppId + ".acf");
                try
                {
                    VdfNode app = ReadVdf(manifest).Child("AppState");
                    string directory = app.Value("installdir");
                    if (String.IsNullOrWhiteSpace(directory)) continue;
                    // An appmanifest must not be allowed to escape steamapps/common.
                    string common = Path.GetFullPath(Path.Combine(library, "steamapps", "common"));
                    string game = Path.GetFullPath(Path.Combine(common, directory));
                    if (!game.StartsWith(common + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.Exists(Path.Combine(game, "bf6.exe"))) return game;
                }
                catch (Exception ex) { if (!IsExpectedFileError(ex)) throw; }
            }
            return "";
        }

        public static ScanReport Scan(string manualGamePath, Action<string> progress)
        {
            ScanReport report = new ScanReport();
            Notify(progress, L("Battlefield-Installation suchen …", "Finding the Battlefield installation …"));
            try
            {
                report.GamePath = String.IsNullOrWhiteSpace(manualGamePath) ? DetectGamePath() : NormalizeGamePath(manualGamePath);
                if (!String.IsNullOrEmpty(report.GamePath) && File.Exists(Path.Combine(report.GamePath, "bf6.exe")))
                {
                    Add(report, "Info", L("Installation gefunden", "Installation found"), L("Die ausgewählte Installation ist lokal vorhanden. Einstellungen und Spieldateien bleiben unverändert.", "The selected installation is present locally. Settings and game files remain unchanged."));
                    InspectInstallation(report, progress);
                }
                else
                {
                    report.GamePath = "";
                    Add(report, "Warning", L("Installation nicht gefunden", "Installation not found"), L("Bitte den Battlefield-6-Installationsordner auswählen. Vorhandene Crashdateien werden trotzdem geprüft.", "Select the Battlefield 6 installation folder. Existing crash files will still be checked."));
                }
            }
            catch (Exception ex)
            {
                if (!IsExpectedFileError(ex)) throw;
                Add(report, "Warning", L("Installation nicht vollständig lesbar", "Installation not fully readable"), L("Ein lokaler Datei- oder Berechtigungsfehler verhindert einen Teil der Prüfung (", "A local file or permission error prevents part of the scan (") + ex.GetType().Name + ").");
            }
            Notify(progress, L("Lokale Crashdateien prüfen …", "Checking local crash files …"));
            InspectCrashes(report);
            int known = report.Crashes.Count(delegate(CrashEvidence crash) { return crash.KnownSignature; });
            if (known != 0)
            {
                report.Summary = known.ToString(CultureInfo.InvariantCulture) + L(" Crashdatei(en) mit der bekannten gleichen Ladeabsturzsignatur gefunden. Die Signatur allein beweist keine Ursache und keinen Fix.", " crash file(s) match the known loading-crash signature. The signature alone establishes neither a cause nor a fix.");
                Add(report, "Warning", L("Beobachtung und Ursache trennen", "Observed signature, cause unconfirmed"), L("Belegt ist die gleiche Fehlerstelle im passenden Spiel-Build. Eine fehlende Laufzeiteinbindung, ein Lizenzfehler oder eine bestimmte CAS-Kennung sind aus dieser Signatur allein nicht nachgewiesen.", "The evidence establishes the same fault location in the matching game build. This signature alone does not establish a missing runtime binding, a licensing error or a particular CAS identifier."));
            }
            else if (report.Crashes.Count != 0)
                report.Summary = L("Crashdateien gefunden; keine davon erfüllt sämtliche Merkmale der bekannten Ladeabsturzsignatur. Für andere Builds werden feste Offsets nicht als Ursache interpretiert.", "Crash files were found; none matches every feature of the known loading-crash signature. Fixed offsets are not interpreted as a cause for other builds.");
            else
                report.Summary = L("Keine auswertbare Battlefield-6-Crashdatei gefunden. Das bestätigt weder Fehlerfreiheit noch einen erfolgreichen Fix.", "No readable Battlefield 6 crash file was found. This does not confirm that the game is error-free or that a fix worked.");
            Notify(progress, L("Read-only-Prüfung abgeschlossen.", "Read-only scan complete."));
            return report;
        }

        private static string NormalizeGamePath(string input)
        {
            string path = Path.GetFullPath(input.Trim().Trim('"'));
            return File.Exists(path) && String.Equals(Path.GetFileName(path), "bf6.exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(path) : path;
        }

        private static void InspectInstallation(ScanReport report, Action<string> progress)
        {
            DirectoryInfo game = new DirectoryInfo(report.GamePath);
            string library = game.Parent != null && game.Parent.Parent != null && game.Parent.Parent.Parent != null
                ? game.Parent.Parent.Parent.FullName : "";
            string manifestPath = Path.Combine(library, "steamapps", "appmanifest_" + AppId + ".acf");
            if (!File.Exists(manifestPath))
            {
                Add(report, "Info", L("Steam-Metadaten nicht verfügbar", "Steam metadata unavailable"), L("Zum ausgewählten Ordner wurde kein lokales Appmanifest gefunden. Daraus wird keine Dateiintegrität oder Besitzberechtigung abgeleitet.", "No local app manifest was found for the selected folder. No conclusion about file integrity or ownership is drawn from this."));
                return;
            }
            VdfNode app;
            try { app = ReadVdf(manifestPath).Child("AppState"); }
            catch (Exception ex)
            {
                if (!IsExpectedFileError(ex)) throw;
                Add(report, "Warning", L("Appmanifest nicht lesbar", "App manifest unreadable"), L("Die lokale Steam-Build- und Depotliste konnte nicht gelesen werden (", "The local Steam build and depot list could not be read (") + ex.GetType().Name + ").");
                return;
            }
            string build = app.Value("buildid");
            report.BuildId = Regex.IsMatch(build, @"^\d{1,12}$") ? build : "";
            VdfNode depots = app.Child("InstalledDepots");
            Add(report, "Info", L("Lokaler Steam-Build", "Local Steam build"), "Build " + (report.BuildId.Length == 0 ? L("unbekannt", "unknown") : report.BuildId) + "; " + depots.Children.Count.ToString(CultureInfo.InvariantCulture) + L(" installierte Depot-Einträge. Steam-Kontodaten und Besitzberechtigungen werden nicht geprüft.", " installed depot entries. Steam account data and ownership are not inspected."));
            InspectTargetIntegrity(report, library, depots, progress);
        }

        private static void InspectTargetIntegrity(ScanReport report, string library, VdfNode depots, Action<string> progress)
        {
            HashSet<string> wanted = new HashSet<string>(IntegrityTargets.Select(NormalizeRelative), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<ManifestExpectation>> expected = new Dictionary<string, List<ManifestExpectation>>(StringComparer.OrdinalIgnoreCase);
            int unavailable = 0, encrypted = 0, examined = 0;
            long bytesRead = 0;
            HashSet<string> cacheFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string steamLibrary in SteamLibraries().Concat(new string[] { library }))
            {
                cacheFolders.Add(Path.Combine(steamLibrary, "depotcache"));
                cacheFolders.Add(Path.Combine(steamLibrary, "steamapps", "depotcache"));
            }
            foreach (KeyValuePair<string, VdfNode> depot in depots.Children.Take(64))
            {
                string gid = depot.Value.Value("manifest");
                if (!Regex.IsMatch(depot.Key, @"^\d{1,12}$") || !Regex.IsMatch(gid, @"^\d{1,20}$")) continue;
                string cacheFile = depot.Key + "_" + gid + ".manifest";
                string cache = cacheFolders.Select(delegate(string folder) { return Path.Combine(folder, cacheFile); }).FirstOrDefault(File.Exists);
                if (cache == null) { unavailable++; continue; }
                try
                {
                    FileInfo info = new FileInfo(cache);
                    if (!info.Exists || info.Length > MaximumManifestBytes || bytesRead + info.Length > 192L * 1024L * 1024L)
                    { unavailable++; continue; }
                    byte[] bytes = ReadBoundedFile(cache, MaximumManifestBytes);
                    bytesRead += bytes.Length;
                    ManifestParseResult parsed = ReadDepotManifest(bytes, wanted);
                    if (parsed.DepotId.ToString(CultureInfo.InvariantCulture) != depot.Key || parsed.ManifestId.ToString(CultureInfo.InvariantCulture) != gid)
                        throw new InvalidDataException("Manifest identity differs from the installed depot entry.");
                    if (parsed.Encrypted) { encrypted++; continue; }
                    examined++;
                    foreach (ManifestExpectation file in parsed.Files)
                    {
                        List<ManifestExpectation> candidates;
                        if (!expected.TryGetValue(file.Name, out candidates))
                        { candidates = new List<ManifestExpectation>(); expected.Add(file.Name, candidates); }
                        candidates.Add(file);
                    }
                }
                catch (Exception ex) { if (!IsExpectedFileError(ex)) throw; unavailable++; }
            }
            if (encrypted != 0)
                Add(report, "Info", L("Verschlüsselte Depot-Dateinamen übersprungen", "Encrypted depot filenames skipped"), L("Es werden keine Schlüssel, Zugangsdaten oder Authentifizierungsdateien gelesen. Für diese Depots ist keine Integritätsaussage möglich.", "No keys, credentials or authentication files are read. File integrity cannot be assessed for these depots."));
            if (unavailable != 0)
                Add(report, "Info", L("Depotvergleich teilweise verfügbar", "Depot comparison partially available"), unavailable.ToString(CultureInfo.InvariantCulture) + L(" lokale Manifestdatei(en) fehlen, sind nicht lesbar oder überschreiten die Prüfgrenze. Diese Fälle zählen nicht als beschädigte Spieldateien.", " local manifest file(s) are missing, unreadable or exceed the scan limit. These cases do not count as damaged game files."));
            int matched = 0, mismatched = 0, missing = 0, skipped = 0;
            long hashed = 0;
            foreach (string relative in IntegrityTargets)
            {
                List<ManifestExpectation> candidates;
                if (!expected.TryGetValue(NormalizeRelative(relative), out candidates)) { skipped++; continue; }
                string path = Path.Combine(report.GamePath, relative);
                string label = TargetLabel(relative);
                try
                {
                    FileInfo before = new FileInfo(path);
                    if (!before.Exists)
                    {
                        missing++;
                        Add(report, "Warning", L("Erwartete Zieldatei fehlt", "Expected target file missing"), label + L(" steht in einem aktiven lokalen Depotmanifest, ist aber nicht vorhanden. Das ist ein konkreter Integritätsbefund, noch kein Ursachennachweis für einen Crash.", " is listed in an active local depot manifest but is absent. This is a specific integrity finding; it does not establish the cause of a crash."));
                        continue;
                    }
                    if (before.Length > MaximumTargetHashBytes || hashed + before.Length > MaximumTargetHashBytes + 64L * 1024L * 1024L)
                    { skipped++; Add(report, "Info", L("Große Zieldatei übersprungen", "Large target file skipped"), label + L(" überschreitet die Grenze der gezielten Hashprüfung. Es erfolgt kein vollständiger Spielscan.", " exceeds the targeted hash-check limit. The full game is not scanned.")); continue; }
                    long length = before.Length;
                    DateTime write = before.LastWriteTimeUtc;
                    Notify(progress, L("SHA-1 prüfen: ", "Checking SHA-1: ") + label + " (" + (length / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MiB) …");
                    byte[] hash = ComputeFileSha1(path, length);
                    hashed += length;
                    FileInfo after = new FileInfo(path);
                    if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != write)
                    { skipped++; Add(report, "Warning", L("Datei während der Prüfung geändert", "File changed during the scan"), label + L(" wurde während des Lesens verändert. Das Ergebnis wird nicht als Hashabweichung gewertet.", " changed while it was being read. The result is not counted as a hash mismatch.")); continue; }
                    if (ExpectationMatches(candidates, length, hash)) matched++;
                    else
                    {
                        mismatched++;
                        Add(report, "Warning", L("Zieldatei weicht vom Depotmanifest ab", "Target file differs from the depot manifest"), label + L(" stimmt in Größe oder SHA-1 nicht mit einem der aktiven lokalen Manifest-Einträge überein. Die App verändert oder repariert die Datei nicht.", " does not match any active local manifest entry in size or SHA-1. The app does not modify or repair the file."));
                    }
                }
                catch (Exception ex)
                {
                    if (!IsExpectedFileError(ex)) throw;
                    skipped++;
                    Add(report, "Warning", L("Zieldatei nicht prüfbar", "Target file could not be checked"), label + L(" konnte nicht vollständig gelesen werden (", " could not be read completely (") + ex.GetType().Name + L("). Dies ist keine bestätigte Inhaltsbeschädigung.", "). This is not confirmed content corruption."));
                }
            }
            if (examined != 0)
                Add(report, mismatched + missing == 0 ? "Info" : "Warning", L("Gezielter Depotvergleich", "Targeted depot comparison"), matched.ToString(CultureInfo.InvariantCulture) + L(" Zieldatei(en) stimmen in SHA-1 und Größe überein; ", " target file(s) match in SHA-1 and size; ") + mismatched + L(" Abweichung(en), ", " mismatch(es), ") + missing + L(" fehlend, ", " missing, ") + skipped + L(" nicht geprüft. Geprüft werden nur Layout, ausgewählte Tungsten-Indizes/Digests und eine CAS-Datei. Passende Bytes beweisen keinen intakten Laufzeitzustand.", " not checked. Only the layout, selected Tungsten indexes/digests and one CAS file are checked. Matching bytes do not establish a correct runtime state."));
        }

        private static string TargetLabel(string relative)
        {
            if (relative.EndsWith("cas_11.cas", StringComparison.OrdinalIgnoreCase)) return "SAN2 / cas_11.cas";
            if (relative.IndexOf("BF6_HRES_SHARED_DLC", StringComparison.OrdinalIgnoreCase) >= 0) return "HRES_SHARED / " + Path.GetFileName(relative);
            if (relative.IndexOf("BF6_SAN2_DLC", StringComparison.OrdinalIgnoreCase) >= 0) return "SAN2 / " + Path.GetFileName(relative);
            return Path.GetFileName(relative);
        }

        private static void InspectCrashes(ScanReport report)
        {
            List<FileInfo> files = new List<FileInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int unreadable = 0, overLimit = 0;
            foreach (string folder in CrashFolders())
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    int count = 0;
                    foreach (string file in Directory.EnumerateFiles(folder, "*.mdmp", SearchOption.TopDirectoryOnly))
                    {
                        if (++count > 2048) { overLimit++; break; }
                        if (!seen.Add(Path.GetFullPath(file))) continue;
                        try { files.Add(new FileInfo(file)); }
                        catch (Exception ex) { if (!IsExpectedFileError(ex)) throw; unreadable++; }
                    }
                }
                catch (Exception ex) { if (!IsExpectedFileError(ex)) throw; unreadable++; }
            }
            foreach (FileInfo file in files.OrderByDescending(delegate(FileInfo item) { return SafeWriteTime(item); }).Take(MaximumDumpCount))
            {
                try { report.Crashes.Add(ReadDump(file.FullName)); }
                catch (Exception ex) { if (!IsExpectedFileError(ex)) throw; unreadable++; }
            }
            if (unreadable != 0)
                Add(report, "Info", L("Einzelne Crashdateien übersprungen", "Some crash files skipped"), unreadable.ToString(CultureInfo.InvariantCulture) + L(" Datei- oder Ordnerzugriff(e) waren unvollständig, nicht lesbar oder kein unterstütztes Minidumpformat. Andere Dateien wurden weiter geprüft.", " file or folder access(es) were incomplete, unreadable or not a supported minidump format. Other files were still checked."));
            if (files.Count > MaximumDumpCount || overLimit != 0)
                Add(report, "Info", L("Begrenzte Crashhistorie", "Limited crash history"), L("Es werden höchstens die 24 jüngsten gefundenen Minidumps geprüft; pro bekanntem Crashordner werden höchstens 2.048 Dateinamen berücksichtigt.", "At most the 24 newest minidumps are checked; at most 2,048 filenames are considered in each known crash folder."));
        }

        private static DateTime SafeWriteTime(FileInfo info)
        {
            try { return info.LastWriteTimeUtc; }
            catch (Exception ex) { if (!IsExpectedFileError(ex)) throw; return DateTime.MinValue; }
        }

        internal static CrashEvidence ReadDump(string path)
        {
            using (BoundedReader reader = new BoundedReader(path, 512 * 1024))
            {
                byte[] header = reader.Read(0, 32);
                if (U32(header, 0) != 0x504d444d) throw new InvalidDataException("Not a minidump.");
                uint count = U32(header, 8), directory = U32(header, 12);
                if (count == 0 || count > 512 || !Range(directory, (ulong)count * 12, reader.Length))
                    throw new InvalidDataException("Invalid minidump directory.");
                Location exception = null, modules = null, memory = null, threads = null;
                byte[] entries = reader.Read(directory, checked((int)count * 12));
                for (int i = 0; i < count; i++)
                {
                    int at = i * 12;
                    uint type = U32(entries, at), size = U32(entries, at + 4), rva = U32(entries, at + 8);
                    if ((type == 4 || type == 6) && !Range(rva, size, reader.Length)) throw new InvalidDataException("Invalid stream range.");
                    if (type == 4) modules = new Location(rva, size);
                    if (type == 6) exception = new Location(rva, size);
                    if (type == 5) memory = new Location(rva, size);
                    if (type == 3) threads = new Location(rva, size);
                }
                if (exception == null || exception.Size < 168) throw new InvalidDataException("No complete exception stream.");
                byte[] detail = reader.Read(exception.Rva, 168);
                uint code = U32(detail, 8), parameters = U32(detail, 32);
                if (parameters > 15) throw new InvalidDataException("Invalid exception parameter count.");
                ulong address = U64(detail, 24), access = parameters >= 1 ? U64(detail, 40) : UInt64.MaxValue;
                ulong target = parameters >= 2 ? U64(detail, 48) : UInt64.MaxValue;
                string moduleName = "";
                uint timestamp = 0;
                ulong offset = 0;
                if (modules != null)
                {
                    if (modules.Size < 4) throw new InvalidDataException("Invalid module list.");
                    uint moduleCount = U32(reader.Read(modules.Rva, 4), 0);
                    if (moduleCount > 4096 || 4UL + (ulong)moduleCount * 108UL > modules.Size)
                        throw new InvalidDataException("Invalid module count.");
                    for (int i = 0; i < moduleCount; i++)
                    {
                        byte[] module = reader.Read(modules.Rva + 4UL + (ulong)i * 108UL, 108);
                        ulong start = U64(module, 0);
                        uint size = U32(module, 8);
                        if (address < start || address - start >= size) continue;
                        uint nameRva = U32(module, 20);
                        uint nameBytes = U32(reader.Read(nameRva, 4), 0);
                        if (nameBytes > 8192 || (nameBytes & 1) != 0 || !Range((ulong)nameRva + 4, nameBytes, reader.Length))
                            throw new InvalidDataException("Invalid module name.");
                        string name = Encoding.Unicode.GetString(reader.Read((ulong)nameRva + 4, (int)nameBytes));
                        moduleName = name.Replace('/', '\\').Split('\\').Last();
                        if (moduleName.IndexOf('\0') >= 0) throw new InvalidDataException("Invalid module name terminator.");
                        timestamp = U32(module, 16);
                        offset = address - start;
                        break;
                    }
                }
                bool isBf6 = String.Equals(moduleName, "bf6.exe", StringComparison.OrdinalIgnoreCase);
                bool known = isBf6 && timestamp == KnownPeTimestamp && offset == KnownRva && code == 0xc0000005 && parameters >= 2 && access == 0 && target == 0xa7;
                CrashEvidence result = new CrashEvidence();
                uint unix = U32(header, 20);
                result.Time = unix == 0 ? File.GetLastWriteTimeUtc(path).ToString("o", CultureInfo.InvariantCulture)
                    : new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix).ToString("o", CultureInfo.InvariantCulture);
                result.ExceptionCode = "0x" + code.ToString("x8", CultureInfo.InvariantCulture);
                result.ModuleOffset = moduleName.Length == 0 ? L("unbekannt", "unknown") : (isBf6 ? "bf6.exe" : L("anderes Modul", "other module")) + "+0x" + offset.ToString("x", CultureInfo.InvariantCulture);
                result.ReadAddress = code == 0xc0000005 && access == 0 && parameters >= 2 ? "0x" + target.ToString("x", CultureInfo.InvariantCulture) : L("nicht als Lesezugriff aufgezeichnet", "not recorded as a read access");
                result.DumpName = Path.GetFileName(path);
                result.KnownSignature = known;
                result.Signature = known
                    ? L("Bekannte gleiche Ladeabsturzsignatur (bf6.exe PE 0x6abf2953, RVA 0x51ed625, 0xc0000005, Lesen von 0xa7). Dies identifiziert das Muster, nicht dessen Ursache oder eine CAS-Kennung.", "Known matching loading-crash signature (bf6.exe PE 0x6abf2953, RVA 0x51ed625, 0xc0000005, read from 0xa7). This identifies the pattern, not its cause or a CAS identifier.")
                    : L("Keine vollständige Übereinstimmung mit der bekannten Build-spezifischen Signatur; keine Ursachenbehauptung aus festen Offsets.", "No full match for the known build-specific signature; no cause is inferred from fixed offsets.");
                result.CasRequestStatus = L("Für diese Signatur nicht ausgewertet.", "Not evaluated for this signature.");
                if (known)
                {
                    try { InspectCapturedCasRequest(reader, detail, memory, threads, result); }
                    catch (Exception ex)
                    {
                        if (!IsExpectedFileError(ex)) throw;
                        // An optional truncated memory stream must not hide the independently verified exception signature.
                        result.CasByteOffset = result.CasByteLength = "";
                        result.CasRequestStatus = L("CAS-Anfrage nicht vollständig erfasst; optionale Kontext- oder Speicherdaten sind nicht lesbar. Keine Aussage über die Ursache.", "CAS request not fully captured; optional context or memory data is unreadable. No conclusion about the cause.");
                    }
                }
                return result;
            }
        }

        // These offsets apply only to the exact PE timestamp/RVA/access signature checked by ReadDump.
        // RBP is the captured exception-context register, not an unwound caller frame. No memory search is used.
        private static void InspectCapturedCasRequest(BoundedReader reader, byte[] exception, Location memory, Location threads, CrashEvidence result)
        {
            result.CasRequestStatus = L("CAS-Anfrage unbekannt: kein passender vollständiger AMD64-Registerkontext.", "CAS request unknown: no matching complete AMD64 register context.");
            uint contextSize = U32(exception, 160), contextRva = U32(exception, 164);
            if (contextSize < 1232 || contextSize > 65536 || !Range(contextRva, contextSize, reader.Length)) return;
            byte[] context = reader.Read(contextRva, 256);
            uint flags = U32(context, 48);
            if ((flags & 0x00ff0000U) != 0x00100000U || (flags & 3U) != 3U) return; // AMD64 + CONTROL + INTEGER
            if (U64(context, 248) != U64(exception, 24) || U64(context, 176) != 7 || U64(context, 184) != UInt64.MaxValue) return;
            ulong id = U64(context, 216), rbp = U64(context, 160);
            if (id == 0) return;
            result.CasFileId = "0x" + id.ToString("x", CultureInfo.InvariantCulture);
            string mapped = id == 0xbe29da23000bUL
                ? L(" Die erfasste ID entspricht im untersuchten lokalen Layout SAN2 / cas_11.cas.", " The captured ID maps to SAN2 / cas_11.cas in the investigated local layout.") : "";
            result.CasRequestStatus = L("Nur Lookup-ID im Register erfasst; der zugehörige CAS-Anfragedatensatz fehlt oder passt nicht. Offset und Länge sind unbekannt.", "Only the register lookup ID was captured; its CAS request record is missing or inconsistent. Offset and length are unknown.") + mapped;
            if (rbp < 0x40) return;
            ulong address = rbp - 0x40;
            byte[] record = CapturedRecord(reader, memory, threads, U32(exception, 0), address, 40);
            if (record == null || U64(record, 8) != id || U32(record, 0) != 0 || U32(record, 4) != 1) return;
            uint offset = U32(record, 16), length = U32(record, 20);
            if (length == 0) return;
            result.CasByteOffset = offset.ToString(CultureInfo.InvariantCulture);
            result.CasByteLength = length.ToString(CultureInfo.InvariantCulture);
            result.CasRequestStatus = L("CAS-Anfrage aus dem aufgenommenen Registerkontext und dem Datensatz bei RBP−0x40 bestätigt; Register-ID und Datensatz-ID stimmen überein. Dies beweist weder Dateibeschädigung noch einen Lizenzfehler. Warum die Laufzeittabelle den Eintrag nicht liefert, bleibt unbekannt.", "CAS request confirmed from the captured register context and the record at RBP−0x40; register and record IDs agree. This establishes neither file corruption nor a licensing error. Why the runtime table does not return the entry remains unknown.") + mapped;
        }

        private static byte[] CapturedRecord(BoundedReader reader, Location memory, Location threads, uint threadId, ulong address, int size)
        {
            if (memory != null && memory.Size >= 4 && Range(memory.Rva, memory.Size, reader.Length))
            {
                uint count = U32(reader.Read(memory.Rva, 4), 0);
                if (count > 4096 || 4UL + (ulong)count * 16UL > memory.Size) throw new InvalidDataException("Invalid optional memory list.");
                byte[] descriptors = reader.Read(memory.Rva + 4UL, checked((int)count * 16));
                for (int i = 0; i < count; i++)
                {
                    int at = i * 16;
                    byte[] found = CapturedRange(reader, U64(descriptors, at), U32(descriptors, at + 8), U32(descriptors, at + 12), address, size);
                    if (found != null) return found;
                }
            }
            if (threads != null && threads.Size >= 4 && Range(threads.Rva, threads.Size, reader.Length))
            {
                uint count = U32(reader.Read(threads.Rva, 4), 0);
                if (count > 2048 || 4UL + (ulong)count * 48UL > threads.Size) throw new InvalidDataException("Invalid optional thread list.");
                byte[] descriptors = reader.Read(threads.Rva + 4UL, checked((int)count * 48));
                for (int i = 0; i < count; i++)
                {
                    int at = i * 48;
                    if (U32(descriptors, at) != threadId) continue;
                    return CapturedRange(reader, U64(descriptors, at + 24), U32(descriptors, at + 32), U32(descriptors, at + 36), address, size);
                }
            }
            return null;
        }

        private static byte[] CapturedRange(BoundedReader reader, ulong start, uint length, uint rva, ulong address, int size)
        {
            if (address < start || address - start > length || (ulong)size > (ulong)length - (address - start)) return null;
            if (!Range(rva, length, reader.Length)) throw new InvalidDataException("Invalid captured memory range.");
            return reader.Read((ulong)rva + (address - start), size);
        }

        internal sealed class ManifestExpectation
        {
            internal string Name;
            internal ulong Size;
            internal byte[] Sha1;
        }

        internal sealed class ManifestParseResult
        {
            internal bool Encrypted;
            internal ulong DepotId;
            internal ulong ManifestId;
            internal List<ManifestExpectation> Files = new List<ManifestExpectation>();
        }

        internal static ManifestParseResult ReadDepotManifest(byte[] bytes, ISet<string> wanted)
        {
            if (bytes == null || bytes.Length > MaximumManifestBytes) throw new InvalidDataException("Manifest limit exceeded.");
            int at = 0, payloadAt = -1, payloadSize = 0;
            bool metadataSeen = false, endSeen = false;
            ManifestParseResult result = new ManifestParseResult();
            while (at + 4 <= bytes.Length)
            {
                uint magic = U32(bytes, at); at += 4;
                if (magic == 0x32c415ab) { endSeen = true; break; }
                if (at + 4 > bytes.Length) throw new InvalidDataException("Truncated manifest section.");
                uint length = U32(bytes, at); at += 4;
                if (length > Int32.MaxValue || !Range((ulong)at, length, bytes.Length)) throw new InvalidDataException("Invalid manifest section size.");
                int end = at + (int)length;
                if (magic == 0x71f617d0) { payloadAt = at; payloadSize = (int)length; }
                else if (magic == 0x1f4812be)
                {
                    metadataSeen = true;
                    ProtoCursor metadata = new ProtoCursor(bytes, at, end);
                    while (!metadata.End)
                    {
                        int field, wire; metadata.Tag(out field, out wire);
                        if (field == 1 && wire == 0) result.DepotId = metadata.Varint();
                        else if (field == 2 && wire == 0) result.ManifestId = metadata.Varint();
                        else if (field == 4 && wire == 0) result.Encrypted = metadata.Varint() != 0;
                        else metadata.Skip(wire);
                    }
                }
                else if (magic != 0x1b81b817) throw new InvalidDataException("Unsupported manifest section.");
                at = end;
            }
            if (!endSeen || !metadataSeen || payloadAt < 0) throw new InvalidDataException("Incomplete manifest.");
            // Never attempt to decrypt filenames or read authentication material.
            if (result.Encrypted) return result;
            ProtoCursor payload = new ProtoCursor(bytes, payloadAt, payloadAt + payloadSize);
            int mappings = 0;
            while (!payload.End)
            {
                int field, wire; payload.Tag(out field, out wire);
                if (field != 1 || wire != 2) { payload.Skip(wire); continue; }
                if (++mappings > 200000) throw new InvalidDataException("Too many manifest entries.");
                ProtoCursor mapping = payload.Child();
                string name = null; ulong size = 0; byte[] sha1 = null;
                while (!mapping.End)
                {
                    mapping.Tag(out field, out wire);
                    if (field == 1 && wire == 2) name = NormalizeRelative(mapping.String(4096));
                    else if (field == 2 && wire == 0) size = mapping.Varint();
                    else if (field == 5 && wire == 2) sha1 = mapping.Bytes(20);
                    else mapping.Skip(wire);
                }
                if (name != null && wanted.Contains(name))
                {
                    if (sha1 == null || sha1.Length != 20) throw new InvalidDataException("Missing target hash.");
                    result.Files.Add(new ManifestExpectation { Name = name, Size = size, Sha1 = sha1 });
                }
            }
            return result;
        }

        internal static bool ExpectationMatches(IEnumerable<ManifestExpectation> expected, long size, byte[] hash)
        {
            if (size < 0 || hash == null || hash.Length != 20) return false;
            foreach (ManifestExpectation entry in expected)
                if (entry.Size == (ulong)size && entry.Sha1 != null && entry.Sha1.SequenceEqual(hash)) return true;
            return false;
        }

        private sealed class ProtoCursor
        {
            private readonly byte[] bytes;
            private readonly int end;
            private int at;
            internal ProtoCursor(byte[] buffer, int start, int finish)
            { if (start < 0 || finish < start || finish > buffer.Length) throw new InvalidDataException("Invalid protobuf bounds."); bytes = buffer; at = start; end = finish; }
            internal bool End { get { return at == end; } }
            internal ulong Varint()
            {
                ulong value = 0;
                for (int i = 0; i < 10; i++)
                {
                    if (at >= end) throw new InvalidDataException("Truncated varint.");
                    byte next = bytes[at++];
                    if (i == 9 && next > 1) throw new InvalidDataException("Varint overflow.");
                    value |= ((ulong)(next & 127)) << (i * 7);
                    if ((next & 128) == 0) return value;
                }
                throw new InvalidDataException("Invalid varint.");
            }
            internal void Tag(out int field, out int wire)
            {
                ulong tag = Varint();
                if ((tag >> 3) == 0 || (tag >> 3) > 536870911) throw new InvalidDataException("Invalid protobuf tag.");
                field = (int)(tag >> 3); wire = (int)(tag & 7);
            }
            private int Length()
            {
                ulong length = Varint();
                if (length > (ulong)(end - at)) throw new InvalidDataException("Invalid protobuf length.");
                return (int)length;
            }
            internal ProtoCursor Child() { int length = Length(); ProtoCursor child = new ProtoCursor(bytes, at, at + length); at += length; return child; }
            internal string String(int maximum) { byte[] value = Bytes(maximum); return new UTF8Encoding(false, true).GetString(value).TrimEnd('\0'); }
            internal byte[] Bytes(int maximum)
            {
                int length = Length();
                if (length > maximum) throw new InvalidDataException("Protobuf field limit exceeded.");
                byte[] result = new byte[length]; Buffer.BlockCopy(bytes, at, result, 0, length); at += length; return result;
            }
            internal void Skip(int wire)
            {
                if (wire == 0) { Varint(); return; }
                if (wire == 2) { int bytesToSkip = Length(); at += bytesToSkip; return; }
                int length = wire == 1 ? 8 : wire == 5 ? 4 : -1;
                if (length < 0 || end - at < length) throw new InvalidDataException("Unsupported or truncated protobuf field.");
                at += length;
            }
        }

        private sealed class Location
        {
            internal readonly ulong Rva;
            internal readonly uint Size;
            internal Location(uint rva, uint size) { Rva = rva; Size = size; }
        }

        private sealed class BoundedReader : IDisposable
        {
            private readonly FileStream stream;
            private int remaining;
            internal long Length { get { return stream.Length; } }
            internal BoundedReader(string path, int budget)
            { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); remaining = budget; }
            internal byte[] Read(ulong offset, int count)
            {
                if (count < 0 || count > remaining || !Range(offset, (ulong)count, stream.Length)) throw new InvalidDataException("Read bound exceeded.");
                remaining -= count;
                stream.Position = (long)offset;
                byte[] result = new byte[count];
                for (int read = 0; read < count; )
                { int n = stream.Read(result, read, count - read); if (n == 0) throw new EndOfStreamException(); read += n; }
                return result;
            }
            public void Dispose() { stream.Dispose(); }
        }

        internal static bool Range(ulong start, ulong count, long length)
        { return length >= 0 && start <= (ulong)length && count <= (ulong)length - start; }

        private static uint U32(byte[] bytes, int at)
        {
            if (at < 0 || at > bytes.Length - 4) throw new InvalidDataException("Missing 32-bit value.");
            return (uint)(bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16 | bytes[at + 3] << 24);
        }
        private static ulong U64(byte[] bytes, int at)
        { return U32(bytes, at) | ((ulong)U32(bytes, at + 4) << 32); }

        private static byte[] ComputeFileSha1(string path, long expectedLength)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 256 * 1024, FileOptions.SequentialScan))
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] buffer = new byte[256 * 1024];
                long remaining = expectedLength;
                while (remaining != 0)
                {
                    int read = stream.Read(buffer, 0, (int)Math.Min((long)buffer.Length, remaining));
                    if (read == 0) throw new EndOfStreamException();
                    sha1.TransformBlock(buffer, 0, read, buffer, 0);
                    remaining -= read;
                }
                sha1.TransformFinalBlock(new byte[0], 0, 0);
                return sha1.Hash;
            }
        }

        private static byte[] ReadBoundedFile(string path, int maximum)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < 0 || stream.Length > maximum) throw new InvalidDataException("File size limit exceeded.");
                byte[] bytes = new byte[(int)stream.Length];
                int at = 0;
                while (at < bytes.Length) { int n = stream.Read(bytes, at, bytes.Length - at); if (n == 0) throw new EndOfStreamException(); at += n; }
                return bytes;
            }
        }

        private static IEnumerable<string> CrashFolders()
        {
            HashSet<string> folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!String.IsNullOrWhiteSpace(documents)) folders.Add(Path.Combine(documents, "Battlefield 6", "CrashDumps"));
            foreach (string variable in new string[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                string root = Environment.GetEnvironmentVariable(variable);
                if (String.IsNullOrWhiteSpace(root)) continue;
                foreach (string name in new string[] { "Documents", "Dokumente" })
                    folders.Add(Path.Combine(root, name, "Battlefield 6", "CrashDumps"));
            }
            return folders;
        }

        private static IEnumerable<string> SteamLibraries()
        {
            HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey key = baseKey.OpenSubKey(@"Software\Valve\Steam", false))
                        {
                            if (key == null) continue;
                            foreach (string name in new string[] { "InstallPath", "SteamPath" })
                            {
                                string value = key.GetValue(name) as string;
                                if (!String.IsNullOrWhiteSpace(value)) roots.Add(Path.GetFullPath(value));
                            }
                        }
                    }
                    catch (Exception ex) { if (!IsExpectedFileError(ex)) throw; }
                }
            }
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!String.IsNullOrEmpty(programFiles)) roots.Add(Path.Combine(programFiles, "Steam"));
            HashSet<string> libraries = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
            {
                try
                {
                    VdfNode folders = ReadVdf(Path.Combine(root, "steamapps", "libraryfolders.vdf")).Child("libraryfolders");
                    foreach (KeyValuePair<string, VdfNode> item in folders.Children)
                    {
                        string value = item.Value.Value("path");
                        if (!String.IsNullOrWhiteSpace(value)) libraries.Add(Path.GetFullPath(value));
                    }
                    foreach (KeyValuePair<string, string> item in folders.Values)
                        if (Regex.IsMatch(item.Key, @"^\d+$") && !String.IsNullOrWhiteSpace(item.Value)) libraries.Add(Path.GetFullPath(item.Value));
                }
                catch (Exception ex) { if (!IsExpectedFileError(ex)) throw; }
            }
            return libraries;
        }

        private sealed class VdfNode
        {
            internal Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal Dictionary<string, VdfNode> Children = new Dictionary<string, VdfNode>(StringComparer.OrdinalIgnoreCase);
            internal string Value(string key) { string value; return Values.TryGetValue(key, out value) ? value : ""; }
            internal VdfNode Child(string key) { VdfNode value; return Children.TryGetValue(key, out value) ? value : new VdfNode(); }
        }

        private static VdfNode ReadVdf(string path)
        {
            string text = new UTF8Encoding(false, true).GetString(ReadBoundedFile(path, 1024 * 1024));
            int at = 0, tokens = 0;
            return ParseVdf(text, ref at, ref tokens, 0, false);
        }

        private static VdfNode ParseVdf(string text, ref int at, ref int tokens, int depth, bool nested)
        {
            if (depth > 20) throw new InvalidDataException("VDF depth limit.");
            VdfNode node = new VdfNode();
            while (true)
            {
                string key = VdfToken(text, ref at, ref tokens);
                if (key == null) { if (nested) throw new InvalidDataException("Truncated VDF."); return node; }
                if (key == "}") { if (!nested) throw new InvalidDataException("Unexpected VDF close."); return node; }
                if (key == "{") throw new InvalidDataException("Unexpected VDF open.");
                string value = VdfToken(text, ref at, ref tokens);
                if (value == null || value == "}") throw new InvalidDataException("Missing VDF value.");
                if (value == "{") node.Children[key] = ParseVdf(text, ref at, ref tokens, depth + 1, true);
                else if (!String.Equals(key, "LastOwner", StringComparison.OrdinalIgnoreCase) && key.IndexOf("SteamID", StringComparison.OrdinalIgnoreCase) < 0)
                    node.Values[key] = value;
            }
        }

        private static string VdfToken(string text, ref int at, ref int tokens)
        {
            if (++tokens > 100000) throw new InvalidDataException("VDF token limit.");
            while (at < text.Length)
            {
                if (Char.IsWhiteSpace(text[at]) || text[at] == '\ufeff') { at++; continue; }
                if (at + 1 < text.Length && text[at] == '/' && text[at + 1] == '/') { while (at < text.Length && text[at] != '\n') at++; continue; }
                break;
            }
            if (at == text.Length) return null;
            char start = text[at++];
            if (start == '{' || start == '}') return start.ToString();
            StringBuilder token = new StringBuilder();
            if (start == '"')
            {
                while (at < text.Length)
                {
                    char c = text[at++];
                    if (c == '"') return token.ToString();
                    if (c == '\\' && at < text.Length && (text[at] == '\\' || text[at] == '"')) c = text[at++];
                    token.Append(c);
                    if (token.Length > 32768) throw new InvalidDataException("VDF token too long.");
                }
                throw new InvalidDataException("Unterminated VDF quote.");
            }
            token.Append(start);
            while (at < text.Length && !Char.IsWhiteSpace(text[at]) && text[at] != '{' && text[at] != '}') token.Append(text[at++]);
            return token.ToString();
        }

        private static string NormalizeRelative(string name) { return name.Replace('/', '\\').TrimEnd('\0'); }
        private static void Notify(Action<string> progress, string text) { if (progress != null) { try { progress(text); } catch { } } }
        private static void Add(ScanReport report, string level, string title, string detail)
        { report.Findings.Add(new Finding { Level = level, Title = title, Detail = detail }); }
        private static bool IsExpectedFileError(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                || ex is ArgumentException || ex is NotSupportedException || ex is OverflowException;
        }
    }
}

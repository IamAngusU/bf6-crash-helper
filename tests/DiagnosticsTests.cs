using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Bf6CrashHelper;

public static class DiagnosticsTests
{
    private static int count;
    private static string temporary;

    public static int Main()
    {
        temporary = Path.Combine(Path.GetTempPath(), "Bf6CrashHelperTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            DumpRecognition(); DumpBounds(); CapturedCasRequest(); DepotParser(); Sharing(); Localization();
            Console.WriteLine("PASS: " + count + " diagnostic assertions; all dumps and manifests were synthetic.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            string expectedPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "Bf6CrashHelperTests-";
            if (Path.GetFullPath(temporary).StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) Directory.Delete(temporary, true);
        }
    }

    private static void Check(bool value, string description)
    { if (!value) throw new Exception("FAILED: " + description); count++; }

    private static void Invalid(Action action, string description)
    {
        try { action(); }
        catch (InvalidDataException) { count++; return; }
        throw new Exception("FAILED: malformed input accepted: " + description);
    }

    private static string Fixture(byte[] bytes)
    {
        string path = Path.Combine(temporary, Guid.NewGuid().ToString("N") + ".mdmp");
        File.WriteAllBytes(path, bytes); return path;
    }

    private static void W32(byte[] bytes, int at, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, at, 4); }
    private static void W64(byte[] bytes, int at, ulong value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, at, 8); }

    private static byte[] Dump()
    {
        byte[] bytes = new byte[512];
        W32(bytes, 0, 0x504d444d); W32(bytes, 4, 0xa793); W32(bytes, 8, 2); W32(bytes, 12, 32); W32(bytes, 20, 1700000000);
        W32(bytes, 32, 6); W32(bytes, 36, 168); W32(bytes, 40, 56);
        W32(bytes, 44, 4); W32(bytes, 48, 112); W32(bytes, 52, 224);
        W32(bytes, 64, 0xc0000005); W64(bytes, 80, 0x1451ed625UL); W32(bytes, 88, 2);
        W64(bytes, 96, 0); W64(bytes, 104, 0xa7);
        W32(bytes, 224, 1); W64(bytes, 228, 0x140000000UL); W32(bytes, 236, 0xc305000); W32(bytes, 244, 0x6abf2953); W32(bytes, 248, 336);
        byte[] name = Encoding.Unicode.GetBytes(@"C:\Users\private-user\Game\bf6.exe");
        W32(bytes, 336, (uint)name.Length); Buffer.BlockCopy(name, 0, bytes, 340, name.Length);
        return bytes;
    }

    private static void DumpRecognition()
    {
        CrashEvidence known = Diagnostics.ReadDump(Fixture(Dump()));
        Check(known.KnownSignature, "exact supported signature");
        Check(known.ModuleOffset == "bf6.exe+0x51ed625" && known.ReadAddress == "0xa7", "module RVA and read target");
        Check(Path.GetFileName(known.DumpName) == known.DumpName && known.DumpName.IndexOf("private-user", StringComparison.OrdinalIgnoreCase) < 0, "basename only");
        byte[] unknown = Dump(); W32(unknown, 244, 0x11111111);
        Check(!Diagnostics.ReadDump(Fixture(unknown)).KnownSignature, "unknown build is not identified by the old offset");
        byte[] write = Dump(); W64(write, 96, 1);
        Check(!Diagnostics.ReadDump(Fixture(write)).KnownSignature, "write access is not the known read signature");
        byte[] shortParameters = Dump(); W32(shortParameters, 88, 1);
        Check(!Diagnostics.ReadDump(Fixture(shortParameters)).KnownSignature, "target requires two exception parameters");
        byte[] patched = Dump(); W32(patched, 244, 0x6ac83b64); W64(patched, 80, 0x1451ed885UL);
        Check(Diagnostics.ReadDump(Fixture(patched)).KnownSignature, "updated binary requires its matching RVA");
        W64(patched, 80, 0x1451ed625UL); Check(!Diagnostics.ReadDump(Fixture(patched)).KnownSignature, "new binary does not accept old RVA");
        W32(patched, 244, 0x6abf2953); W64(patched, 80, 0x1451ed885UL); Check(!Diagnostics.ReadDump(Fixture(patched)).KnownSignature, "old binary does not accept new RVA");
    }

    private static void DumpBounds()
    {
        byte[] directory = Dump(); W32(directory, 12, UInt32.MaxValue);
        Invalid(delegate { Diagnostics.ReadDump(Fixture(directory)); }, "overflowing directory RVA");
        byte[] streams = Dump(); W32(streams, 8, 513);
        Invalid(delegate { Diagnostics.ReadDump(Fixture(streams)); }, "stream count limit");
        byte[] size = Dump(); W32(size, 36, UInt32.MaxValue);
        Invalid(delegate { Diagnostics.ReadDump(Fixture(size)); }, "exception stream range");
        byte[] modules = Dump(); W32(modules, 224, UInt32.MaxValue);
        Invalid(delegate { Diagnostics.ReadDump(Fixture(modules)); }, "module count limit");
        byte[] name = Dump(); W32(name, 336, 3);
        Invalid(delegate { Diagnostics.ReadDump(Fixture(name)); }, "odd UTF-16 module name length");
        byte[] nameRva = Dump(); W32(nameRva, 248, UInt32.MaxValue);
        Invalid(delegate { Diagnostics.ReadDump(Fixture(nameRva)); }, "module name range");
        Invalid(delegate { Diagnostics.ReadDump(Fixture(new byte[15])); }, "truncated header");
        Check(!Diagnostics.Range(UInt64.MaxValue, 4, 512), "range arithmetic never wraps");
    }

    private static byte[] DumpWithRequest(bool threadStack)
    {
        byte[] bytes = Dump(); Array.Resize(ref bytes, 2304);
        // Move the stream directory to leave the original exception/module fixtures unchanged.
        Buffer.BlockCopy(bytes, 32, bytes, 512, 24); W32(bytes, 8, 3); W32(bytes, 12, 512);
        W32(bytes, 536, threadStack ? 3U : 5U); W32(bytes, 540, threadStack ? 52U : 20U); W32(bytes, 544, 1900);
        W32(bytes, 56, 123); W32(bytes, 216, 1232); W32(bytes, 220, 576);
        W32(bytes, 576 + 48, 0x100003); W64(bytes, 576 + 160, 0x7040); W64(bytes, 576 + 176, 7);
        W64(bytes, 576 + 184, UInt64.MaxValue); W64(bytes, 576 + 216, 0xbe29da23000bUL); W64(bytes, 576 + 248, 0x1451ed625UL);
        W32(bytes, 1900, 1);
        if (threadStack)
        { W32(bytes, 1904, 123); W64(bytes, 1904 + 24, 0x7000); W32(bytes, 1904 + 32, 40); W32(bytes, 1904 + 36, 2000); }
        else
        { W64(bytes, 1904, 0x7000); W32(bytes, 1912, 40); W32(bytes, 1916, 2000); }
        W32(bytes, 2000, 0); W32(bytes, 2004, 1); W64(bytes, 2008, 0xbe29da23000bUL); W32(bytes, 2016, 12345); W32(bytes, 2020, 67);
        return bytes;
    }

    private static void CapturedCasRequest()
    {
        CrashEvidence request = Diagnostics.ReadDump(Fixture(DumpWithRequest(false)));
        Check(request.KnownSignature && request.CasFileId == "0xbe29da23000b" && request.CasByteOffset == "12345" && request.CasByteLength == "67", "captured context and memory-list record agree");
        Check(request.CasRequestStatus.IndexOf("SAN2 / cas_11.cas", StringComparison.Ordinal) >= 0, "only captured known ID receives a local-layout label");
        Check(Diagnostics.ReadDump(Fixture(DumpWithRequest(true))).CasByteLength == "67", "exception-thread stack can supply the record");
        byte[] patched = DumpWithRequest(false); W32(patched, 244, 0x6ac83b64); W64(patched, 80, 0x1451ed885UL); W64(patched, 576 + 248, 0x1451ed885UL);
        Check(Diagnostics.ReadDump(Fixture(patched)).CasByteLength == "67", "updated matching context supports the recorded request layout");
        byte[] missing = DumpWithRequest(false); W32(missing, 8, 2);
        CrashEvidence idOnly = Diagnostics.ReadDump(Fixture(missing));
        Check(idOnly.CasFileId == "0xbe29da23000b" && idOnly.CasByteLength == "", "missing memory leaves lookup ID only and no invented range");
        byte[] shortRange = DumpWithRequest(false); W32(shortRange, 1912, 24);
        Check(Diagnostics.ReadDump(Fixture(shortRange)).CasByteLength == "", "truncated captured record is not accepted");
        byte[] wrongId = DumpWithRequest(false); W64(wrongId, 2008, 0x1234000b);
        Check(Diagnostics.ReadDump(Fixture(wrongId)).CasByteOffset == "", "record ID must equal captured R12");
        byte[] genericId = DumpWithRequest(false); W64(genericId, 576 + 216, 0x1234000b); W64(genericId, 2008, 0x1234000b);
        CrashEvidence generic = Diagnostics.ReadDump(Fixture(genericId));
        Check(generic.CasFileId == "0x1234000b" && generic.CasByteLength == "67" && generic.CasRequestStatus.IndexOf("cas_11", StringComparison.Ordinal) < 0, "other captured IDs remain uninterpreted");
        byte[] wrongRegister = DumpWithRequest(false); W64(wrongRegister, 576 + 184, 0);
        Check(Diagnostics.ReadDump(Fixture(wrongRegister)).CasFileId == "", "lookup sentinel R8 must match");
        byte[] wrongRdi = DumpWithRequest(false); W64(wrongRdi, 576 + 176, 8);
        Check(Diagnostics.ReadDump(Fixture(wrongRdi)).CasFileId == "", "fault-record pointer RDI must match");
        byte[] wrongRip = DumpWithRequest(false); W64(wrongRip, 576 + 248, 0x1451ed626UL);
        Check(Diagnostics.ReadDump(Fixture(wrongRip)).CasFileId == "", "context RIP must match exception address");
        byte[] shortContext = DumpWithRequest(false); W32(shortContext, 216, 256);
        Check(Diagnostics.ReadDump(Fixture(shortContext)).CasFileId == "", "short AMD64 context is rejected");
        byte[] invalidContextRange = DumpWithRequest(false); W32(invalidContextRange, 220, UInt32.MaxValue);
        Check(Diagnostics.ReadDump(Fixture(invalidContextRange)).KnownSignature && Diagnostics.ReadDump(Fixture(invalidContextRange)).CasFileId == "", "bad context RVA preserves independent crash evidence");
        byte[] wrongArchitecture = DumpWithRequest(false); W32(wrongArchitecture, 576 + 48, 0x10003);
        Check(Diagnostics.ReadDump(Fixture(wrongArchitecture)).CasFileId == "", "non-AMD64 context is rejected");
        byte[] missingInteger = DumpWithRequest(false); W32(missingInteger, 576 + 48, 0x100001);
        Check(Diagnostics.ReadDump(Fixture(missingInteger)).CasFileId == "", "integer-register context flag is required");
        byte[] corruptMemory = DumpWithRequest(false); W32(corruptMemory, 1916, UInt32.MaxValue);
        CrashEvidence badMemory = Diagnostics.ReadDump(Fixture(corruptMemory));
        Check(badMemory.KnownSignature && badMemory.CasByteLength == "", "bad optional memory RVA does not hide known exception signature");
        byte[] hugeList = DumpWithRequest(false); W32(hugeList, 1900, UInt32.MaxValue);
        Check(Diagnostics.ReadDump(Fixture(hugeList)).CasByteLength == "", "optional memory list count is bounded");
        byte[] underflow = DumpWithRequest(false); W64(underflow, 576 + 160, 0x20);
        Check(Diagnostics.ReadDump(Fixture(underflow)).CasByteLength == "", "RBP record subtraction cannot underflow");
        byte[] absentId = DumpWithRequest(false); W64(absentId, 576 + 216, 0);
        Check(Diagnostics.ReadDump(Fixture(absentId)).CasFileId == "", "zero register ID is not presented as a captured request");
        byte[] wrongThread = DumpWithRequest(true); W32(wrongThread, 1904, 456);
        Check(Diagnostics.ReadDump(Fixture(wrongThread)).CasByteLength == "", "a different thread's stack is not used as the exception-thread fallback");
        byte[] unknownBuild = DumpWithRequest(false); W32(unknownBuild, 244, 0x11111111);
        Check(Diagnostics.ReadDump(Fixture(unknownBuild)).CasFileId == "", "unknown PE build never receives fixed-layout interpretation");
        byte[] wrongKind = DumpWithRequest(false); W32(wrongKind, 2004, 2);
        Check(Diagnostics.ReadDump(Fixture(wrongKind)).CasByteLength == "", "unexpected request-record type is not interpreted");
        byte[] emptyRange = DumpWithRequest(false); W32(emptyRange, 2020, 0);
        Check(Diagnostics.ReadDump(Fixture(emptyRange)).CasByteLength == "", "empty request range is rejected");
        ScanReport report = new ScanReport(); report.Crashes.Add(request);
        string json = new JavaScriptSerializer().Serialize(report.ToShareable());
        Check(json.IndexOf("\"CasFileId\":\"0xbe29da23000b\"", StringComparison.Ordinal) >= 0 && json.IndexOf("\"CasByteLength\":\"67\"", StringComparison.Ordinal) >= 0, "shareable report includes captured CAS evidence");
    }

    private static void Vi(Stream stream, ulong value)
    { while (value >= 128) { stream.WriteByte((byte)((value & 127) | 128)); value >>= 7; } stream.WriteByte((byte)value); }
    private static void FieldBytes(Stream stream, int field, byte[] data)
    { Vi(stream, (ulong)((field << 3) | 2)); Vi(stream, (ulong)data.Length); stream.Write(data, 0, data.Length); }
    private static void Section(BinaryWriter writer, uint magic, byte[] bytes)
    { writer.Write(magic); writer.Write(bytes.Length); writer.Write(bytes); }

    private static byte[] Manifest(byte[] content, bool encrypted)
    {
        byte[] hash; using (SHA1 sha1 = SHA1.Create()) hash = sha1.ComputeHash(content);
        byte[] map;
        using (MemoryStream mapping = new MemoryStream())
        {
            FieldBytes(mapping, 1, Encoding.UTF8.GetBytes(@"Data\layout.toc")); Vi(mapping, 16); Vi(mapping, (ulong)content.Length);
            FieldBytes(mapping, 4, new byte[20]); // unknown length-delimited field exercises correct skipping
            FieldBytes(mapping, 5, hash); FieldBytes(mapping, 6, new byte[] { 1, 2, 3 }); map = mapping.ToArray();
        }
        byte[] payload; using (MemoryStream stream = new MemoryStream()) { FieldBytes(stream, 1, map); payload = stream.ToArray(); }
        byte[] metadata;
        using (MemoryStream stream = new MemoryStream())
        { Vi(stream, 8); Vi(stream, 2807961); Vi(stream, 16); Vi(stream, 7548522330141666452UL); Vi(stream, 32); Vi(stream, encrypted ? 1UL : 0UL); metadata = stream.ToArray(); }
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        { Section(writer, 0x71f617d0, payload); Section(writer, 0x1f4812be, metadata); Section(writer, 0x1b81b817, new byte[0]); writer.Write(0x32c415abU); return stream.ToArray(); }
    }

    private static void DepotParser()
    {
        byte[] content = Encoding.ASCII.GetBytes("correct content");
        HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase); wanted.Add(@"Data\layout.toc");
        Diagnostics.ManifestParseResult result = Diagnostics.ReadDepotManifest(Manifest(content, false), wanted);
        Check(result.Files.Count == 1 && result.DepotId == 2807961 && result.ManifestId == 7548522330141666452UL, "manifest identity and target mapping");
        byte[] correct; using (SHA1 sha1 = SHA1.Create()) correct = sha1.ComputeHash(content);
        Check(Diagnostics.ExpectationMatches(result.Files, content.Length, correct), "manifest content match");
        byte[] wrong; using (SHA1 sha1 = SHA1.Create()) wrong = sha1.ComputeHash(Encoding.ASCII.GetBytes("altered content"));
        Check(!Diagnostics.ExpectationMatches(result.Files, content.Length, wrong), "hash mismatch is detected independently of size");
        Check(!Diagnostics.ExpectationMatches(result.Files, content.Length + 1, correct), "size mismatch is detected independently of hash");
        List<Diagnostics.ManifestExpectation> alternatives = new List<Diagnostics.ManifestExpectation>();
        alternatives.Add(new Diagnostics.ManifestExpectation { Name = @"Data\layout.toc", Size = (ulong)content.Length, Sha1 = wrong }); alternatives.AddRange(result.Files);
        Check(Diagnostics.ExpectationMatches(alternatives, content.Length, correct), "one matching active manifest is sufficient for duplicate paths");
        Diagnostics.ManifestParseResult encrypted = Diagnostics.ReadDepotManifest(Manifest(content, true), wanted);
        Check(encrypted.Encrypted && encrypted.Files.Count == 0, "encrypted filenames are never decrypted or interpreted");
        byte[] malformed = Manifest(content, false); W32(malformed, 4, UInt32.MaxValue);
        Invalid(delegate { Diagnostics.ReadDepotManifest(malformed, wanted); }, "manifest section overflow");
        byte[] truncated = Manifest(content, false); Array.Resize(ref truncated, truncated.Length - 4);
        Invalid(delegate { Diagnostics.ReadDepotManifest(truncated, wanted); }, "missing manifest terminator");
    }

    private static void Sharing()
    {
        ScanReport report = new ScanReport(); report.GamePath = @"C:\Users\private-user\Steam\Battlefield 6"; report.BuildId = "25684022";
        report.Summary = "No fault confirmed.";
        report.Findings.Add(new Finding { Detail = @"File C:\Users\private-user\secrets.txt; Steam ID 76561190000000000", Title = "Example" });
        report.Crashes.Add(new CrashEvidence { DumpName = "private-user-crash.mdmp", ModuleOffset = "bf6.exe+0x51ed625" });
        string json = new JavaScriptSerializer().Serialize(report.ToShareable());
        Check(json.IndexOf("private-user", StringComparison.OrdinalIgnoreCase) < 0 && json.IndexOf("secrets.txt", StringComparison.OrdinalIgnoreCase) < 0, "shareable export strips absolute paths and dump names");
        Check(json.IndexOf("76561190000000000", StringComparison.Ordinal) < 0, "shareable export strips account identifiers");
        Check(json.IndexOf("GamePath", StringComparison.Ordinal) < 0 && json.IndexOf("DumpName", StringComparison.Ordinal) < 0, "shareable schema omits local path and filename fields");
        Check(json.IndexOf(ScanReport.NoOwnershipInspection, StringComparison.Ordinal) >= 0, "ownership is explicitly not inspected");
    }

    private static void Localization()
    {
        CultureInfo original = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            Check(Diagnostics.ReadDump(Fixture(Dump())).Signature.StartsWith("Bekannte", StringComparison.Ordinal), "German dump signature follows UI culture");
            Check(Diagnostics.L("Deutsch", "English") == "Deutsch", "German diagnostic messages selected");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Check(Diagnostics.ReadDump(Fixture(Dump())).Signature.StartsWith("Known", StringComparison.Ordinal), "English dump signature follows UI culture");
            byte[] write = Dump(); W64(write, 96, 1);
            Check(Diagnostics.ReadDump(Fixture(write)).ReadAddress == "not recorded as a read access", "English read-access explanation");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Check(Diagnostics.L("Deutsch", "English") == "English", "Other UI cultures fall back to English");
        }
        finally { Thread.CurrentThread.CurrentUICulture = original; }
    }
}

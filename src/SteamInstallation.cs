using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Bf6CrashHelper
{
    // Read-only Steam installation discovery and a bounded appmanifest parser.
    // No crash parser, cache maintenance or recording functionality is included.
    internal static class SteamInstallation
    {
        const string AppId = "2807960";
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

        internal sealed class VdfNode
        {
            internal Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal Dictionary<string, VdfNode> Children = new Dictionary<string, VdfNode>(StringComparer.OrdinalIgnoreCase);
            internal string Value(string key) { string value; return Values.TryGetValue(key, out value) ? value : ""; }
            internal VdfNode Child(string key) { VdfNode value; return Children.TryGetValue(key, out value) ? value : new VdfNode(); }
        }

        internal static VdfNode ReadVdf(string path)
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

        private static bool IsExpectedFileError(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                || ex is ArgumentException || ex is NotSupportedException || ex is OverflowException;
        }
    }
}

using System;
using System.IO;

namespace Bf6CrashHelper
{
    internal static class MirakPaths
    {
        internal static void CheckParents(string path)
        {
            for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked or redirected recovery path was found. Automatic changes are disabled for this path.");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked file is not supported.");
        }
    }
}
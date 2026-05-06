using System;
using System.IO;

namespace SoundToText;

public static class AppPaths
{
    public static string LocalDataDir
    {
        get
        {
            string root;
            if (OperatingSystem.IsMacOS())
            {
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support");
            }
            else
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            var dir = Path.Combine(root, "SoundToText");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}

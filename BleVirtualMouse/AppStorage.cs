using System;
using System.IO;

namespace BleVirtualMouse;

internal static class AppStorage
{
    public static string GetLogPath(string fileName)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BleVirtualMouse");

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}

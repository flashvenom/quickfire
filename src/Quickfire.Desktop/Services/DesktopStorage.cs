using System;
using System.IO;
using Microsoft.Maui.Storage;

namespace Quickfire.Desktop.Services;

public static class DesktopStorage
{
    private const string CompanyFolder = "flashvenom";
    // Existing installations store their database and attachments under these names.
    private const string ProductFolder = "openfire";
    private const string DefaultHostFolder = "openfire-host";

    public static string GetAppDataRoot()
    {
        string baseRoot;
        if (OperatingSystem.IsWindows())
        {
            baseRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            var personal = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            baseRoot = Path.Combine(personal, "Library", "Application Support");
        }
        else
        {
            baseRoot = FileSystem.Current.AppDataDirectory;
        }

        var preferredRoot = Path.Combine(baseRoot, CompanyFolder, ProductFolder);
        var root = preferredRoot;
        Directory.CreateDirectory(root);
        return root;
    }

    public static string GetHostRoot(string? deploymentFolderName)
    {
        var appRoot = GetAppDataRoot();
        var folder = string.IsNullOrWhiteSpace(deploymentFolderName)
            ? DefaultHostFolder
            : deploymentFolderName.Trim();
        var path = Path.Combine(appRoot, folder);

        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetDataRoot(string? deploymentFolderName, string? dataDirectoryName)
    {
        var hostRoot = GetHostRoot(deploymentFolderName);
        var folder = string.IsNullOrWhiteSpace(dataDirectoryName)
            ? "data"
            : dataDirectoryName.Trim();
        var path = Path.Combine(hostRoot, folder);
        Directory.CreateDirectory(path);
        return path;
    }
}

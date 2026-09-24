namespace Quickfire.Desktop.Services
{
    public sealed class QuickfireHostOptions
    {
        public string AssetRoot { get; set; } = "QuickfireHost";
        public string ManifestFileName { get; set; } = "manifest.txt";
        // Keep the existing desktop database and attachment location.
        public string DeploymentFolderName { get; set; } = "openfire-host";
        public string ContentFolderName { get; set; } = "site";
        public string DataDirectoryName { get; set; } = "data";
        public string EnvironmentName { get; set; } = "Desktop";
        public int Port { get; set; } = 5128;
        public string ReadyPath { get; set; } = "_framework/blazor.server.js";
        public int StartupTimeoutSeconds { get; set; } = 120;
    }
}

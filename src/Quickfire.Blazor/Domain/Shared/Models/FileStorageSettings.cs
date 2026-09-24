using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quickfire.Blazor.Domain.Shared.Models
{
    public enum FileStorageMode
    {
        LocalDesktop,
        Network,
        ExternalSelfHosted
    }

    public sealed class FileStorageSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Explicitly empty roots/URLs must survive round trips instead of reviving legacy defaults.
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false
        };

        public FileStorageMode Mode { get; set; } = FileStorageMode.Network;

        // Local desktop options
        public string? LocalRootPath { get; set; }
        public string? LocalDisplayName { get; set; }
        public bool PreferFileSchemeLinks { get; set; } = true;

        // Network / on-prem options
        public string? NetworkSharePath { get; set; } = @"S:\Quickfiles";
        public bool StripUploadsFromMappedPath { get; set; } = true;

        // Shared options
        public string? ServerAbsoluteRoot { get; set; }
        public string? PublicBaseUrl { get; set; } = "https://quickfire.local";

        // External/self-hosted placeholders
        public string? ExternalPushEndpoint { get; set; }
        public string? ExternalStagingPath { get; set; }

        public FileStorageSettings Clone() => new()
        {
            Mode = Mode,
            LocalRootPath = LocalRootPath,
            LocalDisplayName = LocalDisplayName,
            PreferFileSchemeLinks = PreferFileSchemeLinks,
            NetworkSharePath = NetworkSharePath,
            StripUploadsFromMappedPath = StripUploadsFromMappedPath,
            ServerAbsoluteRoot = ServerAbsoluteRoot,
            PublicBaseUrl = PublicBaseUrl,
            ExternalPushEndpoint = ExternalPushEndpoint,
            ExternalStagingPath = ExternalStagingPath
        };

        public FileStorageSettings Normalize()
        {
            Mode = Enum.IsDefined(typeof(FileStorageMode), Mode)
                ? Mode
                : FileStorageMode.Network;

            ServerAbsoluteRoot = NormalizePath(ServerAbsoluteRoot) ?? ResolveDefaultServerRoot();
            NetworkSharePath = NormalizePath(NetworkSharePath);
            LocalRootPath = NormalizePath(LocalRootPath);
            PublicBaseUrl = NormalizeUrl(PublicBaseUrl);
            ExternalPushEndpoint = NormalizeUrl(ExternalPushEndpoint);
            ExternalStagingPath = NormalizePath(ExternalStagingPath);

            return this;
        }

        public FileStorageResolvedRoots ResolveRoots()
        {
            var normalized = Clone().Normalize();
            var absoluteRoot = normalized.ServerAbsoluteRoot ?? ResolveDefaultServerRoot();
            var mappedRoot = normalized.Mode switch
            {
                FileStorageMode.LocalDesktop => normalized.LocalRootPath ?? normalized.NetworkSharePath ?? ResolveDefaultMappedRoot(),
                FileStorageMode.Network => normalized.NetworkSharePath ?? ResolveDefaultMappedRoot(),
                FileStorageMode.ExternalSelfHosted => normalized.NetworkSharePath ?? ResolveDefaultMappedRoot(),
                _ => ResolveDefaultMappedRoot()
            };

            return new FileStorageResolvedRoots(
                absoluteRoot,
                mappedRoot,
                normalized.PublicBaseUrl,
                normalized.Mode,
                normalized.StripUploadsFromMappedPath,
                normalized.PreferFileSchemeLinks);
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(Clone().Normalize(), JsonOptions);
        }

        public static FileStorageSettings FromJson(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return CreateDefault();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<FileStorageSettings>(value, JsonOptions);
                return parsed?.Normalize() ?? CreateDefault();
            }
            catch
            {
                return CreateDefault();
            }
        }

        public static FileStorageSettings CreateDefault()
        {
            return new FileStorageSettings
            {
                Mode = FileStorageMode.Network,
                NetworkSharePath = ResolveDefaultMappedRoot(),
                ServerAbsoluteRoot = ResolveDefaultServerRoot(),
                PublicBaseUrl = "https://surefire.local",
                StripUploadsFromMappedPath = true,
                PreferFileSchemeLinks = true
            };
        }

        public static string ResolveDefaultServerRoot()
        {
            var baseDirectory = AppContext.BaseDirectory;
            var potentialRoots = new[]
            {
                Path.Combine(baseDirectory, "wwwroot"),
                Path.Combine(baseDirectory, "..", "..", "..", "wwwroot"),
                Path.Combine(baseDirectory, "..", "..", "..", "..", "wwwroot"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos", "Surefire", "Surefire", "wwwroot"),
            };

            foreach (var candidate in potentialRoots)
            {
                try
                {
                    var fullPath = Path.GetFullPath(candidate);
                    if (Directory.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    // Ignore and continue
                }
            }

            return Path.Combine(baseDirectory, "wwwroot");
        }

        public static string ResolveDefaultMappedRoot() => @"S:\Surefiles";

        private static string? NormalizePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string? NormalizeUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().TrimEnd('/');
        }
    }

    public sealed record FileStorageResolvedRoots(
        string AbsoluteRoot,
        string? MappedRoot,
        string? PublicBaseUrl,
        FileStorageMode Mode,
        bool StripUploadsFromMappedPath,
        bool PreferFileSchemeLinks);
}

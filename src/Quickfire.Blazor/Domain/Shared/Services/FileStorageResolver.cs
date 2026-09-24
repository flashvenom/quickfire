using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Quickfire.Blazor.Domain.Attachments.Models;
using Quickfire.Blazor.Domain.Shared.Models;

namespace Quickfire.Blazor.Domain.Shared.Services
{
    public interface IFileStorageResolver
    {
        FileStorageSettings CurrentSettings { get; }
        FileStorageResolvedRoots ResolvedRoots { get; }
        void Update(FileStorageSettings settings);
        string BuildMappedFilePath(Attachment attachment);
        string BuildMappedFolderPath(Attachment attachment);
        string BuildAbsolutePath(Attachment attachment);
        string BuildPublicUrl(Attachment attachment);
    }

    public sealed class FileStorageResolver : IFileStorageResolver
    {
        private FileStorageSettings _settings;

        public FileStorageResolver()
            : this(FileStorageSettings.CreateDefault())
        {
        }

        public FileStorageResolver(FileStorageSettings settings)
        {
            _settings = (settings ?? FileStorageSettings.CreateDefault()).Normalize();
        }

        public FileStorageSettings CurrentSettings => _settings;

        public FileStorageResolvedRoots ResolvedRoots => _settings.ResolveRoots();

        public void Update(FileStorageSettings settings)
        {
            _settings = (settings ?? FileStorageSettings.CreateDefault()).Normalize();
        }

        public string BuildMappedFolderPath(Attachment attachment)
        {
            return BuildMappedFolderPathInternal(attachment, includeTrailingSeparator: true);
        }

        public string BuildMappedFilePath(Attachment attachment)
        {
            var folder = BuildMappedFolderPathInternal(attachment, includeTrailingSeparator: false);
            return Path.Combine(folder, EnsureHashedFileName(attachment));
        }

        public string BuildAbsolutePath(Attachment attachment)
        {
            var roots = ResolvedRoots;
            var segments = NormalizeSegments(attachment);
            var parts = new List<string> { roots.AbsoluteRoot };
            parts.AddRange(segments);
            parts.Add(EnsureHashedFileName(attachment));
            return Path.Combine(parts.ToArray());
        }

        public string BuildPublicUrl(Attachment attachment)
        {
            var roots = ResolvedRoots;
            var segments = NormalizeSegments(attachment);
            segments.Add(EnsureHashedFileName(attachment));
            var relative = string.Join("/", segments);

            if (roots.Mode == FileStorageMode.LocalDesktop && roots.PreferFileSchemeLinks)
            {
                var absolute = BuildAbsolutePath(attachment);
                return ToFileScheme(absolute);
            }

            if (!string.IsNullOrWhiteSpace(roots.PublicBaseUrl))
            {
                return $"{roots.PublicBaseUrl.TrimEnd('/')}/{relative}";
            }

            if (roots.Mode == FileStorageMode.LocalDesktop && !roots.PreferFileSchemeLinks)
            {
                return "/" + string.Join("/", segments.Select(Uri.EscapeDataString));
            }

            return BuildMappedFilePath(attachment);
        }

        private string BuildMappedFolderPathInternal(Attachment attachment, bool includeTrailingSeparator)
        {
            var roots = ResolvedRoots;
            var mappedRoot = roots.MappedRoot ?? roots.AbsoluteRoot;
            var segments = NormalizeSegments(attachment);

            if (roots.StripUploadsFromMappedPath &&
                segments.Count > 0 &&
                segments[0].Equals("uploads", StringComparison.OrdinalIgnoreCase))
            {
                segments = segments.Skip(1).ToList();
            }

            var path = Path.Combine(new[] { mappedRoot }.Concat(segments).ToArray());
            if (includeTrailingSeparator && !path.EndsWith(Path.DirectorySeparatorChar))
            {
                path += Path.DirectorySeparatorChar;
            }

            return path;
        }

        private static List<string> NormalizeSegments(Attachment attachment)
        {
            if (attachment is null || attachment.LocalPath is null)
            {
                throw new ArgumentException("Attachment is missing the local path.");
            }

            var sanitized = attachment.LocalPath.Replace("\\", "/").Trim('/');
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return new List<string>();
            }

            return sanitized
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private static string EnsureHashedFileName(Attachment attachment)
        {
            if (string.IsNullOrWhiteSpace(attachment?.HashedFileName))
            {
                throw new ArgumentException("Attachment is missing the hashed file name.");
            }

            return attachment.HashedFileName;
        }

        private static string ToFileScheme(string absolutePath)
        {
            var normalized = absolutePath.Replace(Path.DirectorySeparatorChar, '/');
            if (!normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = "/" + normalized;
            }

            return $"file://{normalized}";
        }
    }

    public static class FileStorageResolverAccessor
    {
        private static readonly object Sync = new();
        private static IFileStorageResolver _resolver = new FileStorageResolver();

        public static IFileStorageResolver Resolver => _resolver;

        public static void Initialize(IFileStorageResolver resolver)
        {
            if (resolver is null)
            {
                return;
            }

            lock (Sync)
            {
                _resolver = resolver;
            }
        }
    }
}

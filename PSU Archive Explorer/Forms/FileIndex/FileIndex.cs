using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace psu_archive_explorer
{
    public static class FileIndex
    {
        private static Dictionary<string, List<string>> archives
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private static List<string> loose = new List<string>();
        public static bool IsLoaded { get; private set; }
        public static int TotalFileCount { get; private set; }
        public static int ArchiveCount => archives.Count;
        private class IndexDto
        {
            public Dictionary<string, List<string>> archives { get; set; }
            public List<string> loose { get; set; }
        }
        public static bool LoadFromFile(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using (var fs = File.OpenRead(path))
                    return LoadFromStream(fs);
            }
            catch
            {
                return false;
            }
        }
        public static bool LoadFromEmbeddedResource(string resourceName)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return false;
                    return LoadFromStream(stream);
                }
            }
            catch
            {
                return false;
            }
        }
        private static bool LoadFromStream(Stream compressedStream)
        {
            try
            {
                using (var gz = new GZipStream(compressedStream, CompressionMode.Decompress))
                using (var ms = new MemoryStream())
                {
                    gz.CopyTo(ms);
                    ms.Position = 0;

                    var dto = JsonSerializer.Deserialize<IndexDto>(ms.ToArray());
                    if (dto == null) return false;

                    archives = dto.archives != null
                        ? new Dictionary<string, List<string>>(dto.archives, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                    loose = dto.loose ?? new List<string>();

                    TotalFileCount = archives.Sum(kvp => kvp.Value.Count) + loose.Count;
                    IsLoaded = true;
                    return true;
                }
            }
            catch
            {
                IsLoaded = false;
                return false;
            }
        }
        public class SearchResult
        {
            public string Archive { get; set; }
            public string InnerPath { get; set; }
            public string FileName { get; set; }
            public bool IsLoose { get; set; }
            public string FriendlyName { get; set; }
        }
        public static List<SearchResult> Search(string query, int maxResults = 500)
        {
            var results = new List<SearchResult>();
            if (!IsLoaded || string.IsNullOrWhiteSpace(query)) return results;

            string q = query.Trim();

            foreach (var kvp in archives)
            {
                // Check if the archive hash itself matches
                bool archiveHashMatches = kvp.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

                // Check if this archive's hash maps to a known ADX name
                string archiveFriendly = null;
                MainForm.TryGetAdxFriendlyName(kvp.Key, out archiveFriendly);
                bool archiveFriendlyMatches = archiveFriendly != null &&
                    archiveFriendly.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

                foreach (var innerPath in kvp.Value)
                {
                    string fileName = GetFileName(innerPath);

                    // Inner file's friendly name, if it's a hashed ADX
                    string innerFriendly = null;
                    TryGetFriendlyForAdxFile(fileName, out innerFriendly);

                    bool fileNameMatches = fileName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool innerFriendlyMatches = innerFriendly != null &&
                        innerFriendly.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (fileNameMatches || archiveHashMatches || archiveFriendlyMatches || innerFriendlyMatches)
                    {
                        results.Add(new SearchResult
                        {
                            Archive = kvp.Key,
                            InnerPath = innerPath,
                            FileName = fileName,
                            IsLoose = false,
                            FriendlyName = innerFriendly ?? (archiveFriendlyMatches ? archiveFriendly : null)
                        });
                        if (results.Count >= maxResults) return results;
                    }
                }
            }
            foreach (var looseFile in loose)
            {
                string friendly = null;
                TryGetFriendlyForAdxFile(looseFile, out friendly);

                bool nameMatches = looseFile.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                bool friendlyMatches = friendly != null &&
                    friendly.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

                if (nameMatches || friendlyMatches)
                {
                    results.Add(new SearchResult
                    {
                        Archive = looseFile,
                        InnerPath = looseFile,
                        FileName = looseFile,
                        IsLoose = true,
                        FriendlyName = friendly
                    });
                    if (results.Count >= maxResults) return results;
                }
            }

            return results;
        }

        /// <summary>
        /// If the filename 32 hex chars + .adx extension,
        /// looks it up in the ADX hash map.
        /// </summary>
        private static bool TryGetFriendlyForAdxFile(string filename, out string friendly)
        {
            friendly = null;
            if (string.IsNullOrEmpty(filename)) return false;

            string stem = filename;
            if (stem.EndsWith(".adx", StringComparison.OrdinalIgnoreCase))
                stem = stem.Substring(0, stem.Length - 4);

            if (stem.Length != 32) return false;
            foreach (char c in stem)
                if (!IsHex(c)) return false;

            return MainForm.TryGetAdxFriendlyName(stem, out friendly);
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
        private static string GetFileName(string innerPath)
        {
            int slash = innerPath.LastIndexOf('/');
            return slash < 0 ? innerPath : innerPath.Substring(slash + 1);
        }
    }
}
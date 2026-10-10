using System;
using System.IO;
using EMQ.Shared.Core;

namespace EMQ.Server;

// Controls where user-uploaded files (songs, nekobako, spectrograms) are stored.
//
// "sftp" (default): uploads go to the SFTP server using EMQ_SFTP_* env vars,
//   URLs are https://emqselfhost/selfhoststorage/... (served by the production file host).
// "local": uploads are written under EMQ_LOCAL_STORAGE_DIR (see .env.example) and
//   served by this server at /selfhoststorage/... — useful for local development.
//
// To switch back to SFTP later, set EMQ_STORAGE_MODE=sftp and restart. Nothing else changes.
public static class UploadStorageHelper
{
    private static readonly string s_mode =
        (Environment.GetEnvironmentVariable("EMQ_STORAGE_MODE") ?? "sftp").Trim().ToLowerInvariant();

    private static readonly string s_localRoot =
        (Environment.GetEnvironmentVariable("EMQ_LOCAL_STORAGE_DIR") ?? "").Trim();

    private static readonly string s_localNekobakoRoot =
        (Environment.GetEnvironmentVariable("EMQ_LOCAL_NEKOBAKO_DIR") ?? "").Trim();

    private static readonly string s_localVndbImgRoot =
        (Environment.GetEnvironmentVariable("EMQ_LOCAL_VNDBIMG_DIR") ?? "").Trim();

    public static bool IsLocal => s_mode is "local" or "localdisk" or "disk" or "filesystem";

    // URLs (and SFTP remote paths) are always built from the subpath *below* selfhoststorage/,
    // e.g. "userup/<guid>.mp3" or "userup/nekobako/<guid>.webp", so they stay identical in both modes.
    public static string StoreFile(Stream stream, string subpath)
    {
        if (string.IsNullOrWhiteSpace(subpath))
        {
            throw new ArgumentException("subpath must not be empty", nameof(subpath));
        }

        string normalized = subpath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains(".."))
        {
            throw new ArgumentException("subpath must not contain '..'", nameof(subpath));
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        if (!IsLocal)
        {
            ServerUtils.SftpFileUpload(
                UploadConstants.SftpHost, UploadConstants.SftpUsername,
                UploadConstants.SftpPassword,
                stream, Path.Combine(UploadConstants.SftpUserUploadDir, normalized));
            return normalized;
        }

        // Local mode: nekobako files can be redirected to a separate root via
        // EMQ_LOCAL_NEKOBAKO_DIR; everything else goes under EMQ_LOCAL_STORAGE_DIR.
        string localRoot = s_localRoot;
        if (IsNekobakoSubpath(normalized) && !string.IsNullOrWhiteSpace(s_localNekobakoRoot))
        {
            localRoot = s_localNekobakoRoot;
        }

        if (string.IsNullOrWhiteSpace(localRoot))
        {
            throw new Exception(
                "EMQ_LOCAL_STORAGE_DIR envVar is not set (required when EMQ_STORAGE_MODE=local)" +
                (IsNekobakoSubpath(normalized) ? " (or set EMQ_LOCAL_NEKOBAKO_DIR)" : ""));
        }

        string rootFull = Path.GetFullPath(localRoot);
        string localPath = Path.GetFullPath(Path.Combine(rootFull, normalized));
        if (!localPath.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !localPath.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("subpath escapes the local storage dir", nameof(subpath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        using (var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.CopyTo(fs);
        }

        return normalized;
    }

    private static bool IsNekobakoSubpath(string normalizedSubpath)
    {
        return normalizedSubpath.StartsWith("userup/nekobako/", StringComparison.OrdinalIgnoreCase);
    }

    // Absolute local directory that maps to the /selfhoststorage/ URL prefix (null when using SFTP).
    public static string? LocalSelfhostStorageRoot => IsLocal ? s_localRoot : null;

    // Absolute local directory backing /selfhoststorage/userup/nekobako/ (null when using SFTP
    // or when no override is configured — then files live under LocalSelfhostStorageRoot).
    public static string? LocalNekobakoRoot =>
        IsLocal && !string.IsNullOrWhiteSpace(s_localNekobakoRoot) ? s_localNekobakoRoot : null;

    // Absolute local directory with VNDB image export (cv/, sf/, ch/) backing
    // /selfhoststorage/vndb-img/ (null when using SFTP or when not configured).
    public static string? LocalVndbImgRoot =>
        IsLocal && !string.IsNullOrWhiteSpace(s_localVndbImgRoot) ? s_localVndbImgRoot : null;
}

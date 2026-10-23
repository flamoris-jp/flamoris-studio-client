using System.Security.Cryptography;
using Flamoris.StudioClient.Core;

namespace Flamoris.StudioClient.Infrastructure;

public static class LocalFiles
{
    public static async Task<string> FingerprintAsync(string path, CancellationToken token = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
    public static async Task<(byte[] Bytes, string Hash)> ReadAsync(string path, CancellationToken token = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (stream.Length > TimingFormats.MaxBytes) throw new InvalidDataException("ファイルは16 MiBまでです。");
        var bytes = new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes, token);
        return (bytes, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    public static async Task<string> PublishAsync(string path, byte[] bytes, string? expectedHash, CancellationToken token = default)
    {
        if (bytes.Length > TimingFormats.MaxBytes) throw new InvalidDataException("保存内容が16 MiBを超えています。");
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        // Serializes cooperating clients; external non-cooperating writers are detected
        // optimistically before publication, not claimed as a filesystem-wide CAS.
        // CreateNew never follows or deletes a pre-existing lock/symlink object.
        await using var gate = new FileStream(path + ".flamoris.lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("リンク先への上書きはできません。");
            await CheckAsync();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            { await output.WriteAsync(bytes, token); output.Flush(true); }
            await CheckAsync(); token.ThrowIfCancellationRequested();
            File.Move(temporary, path, expectedHash is not null);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        async Task CheckAsync()
        {
            if (File.Exists(path))
            {
                if (expectedHash is null || !StringComparer.OrdinalIgnoreCase.Equals(expectedHash, await FingerprintAsync(path, token)))
                    throw new IOException("保存先が他で変更されています。別名で保存してください。");
            }
            else if (expectedHash is not null) throw new IOException("保存先が削除・移動されています。別名で保存してください。");
        }
    }
}

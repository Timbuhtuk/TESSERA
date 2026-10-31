using System.IO;

namespace DomainColorTest;

internal static class ProgressFile
{
    public static void Copy(string source, string destination, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[65536];
        long completed = 0;
        int count;
        while ((count = input.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, count);
            completed += count;
            progress?.Report((double)completed / Math.Max(1, input.Length));
        }
        output.Flush();
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(1);
    }
}

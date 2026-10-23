namespace CAP_DataAccess.Persistence;

/// <summary>Publishes a completed file without exposing a partial serialization at its destination.</summary>
internal static class AtomicFileWriter
{
    internal static async Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(path);
        var link = new FileInfo(destination);
        if (link.LinkTarget != null)
            destination = link.ResolveLinkTarget(returnFinalTarget: true)!.FullName;
        UnixFileMode? mode = !OperatingSystem.IsWindows() && File.Exists(destination)
            ? File.GetUnixFileMode(destination) : null;
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                BufferSize = 65536, Options = FileOptions.Asynchronous
            };
            // Apply private permissions at creation, before any content is written.
            if (!OperatingSystem.IsWindows() && mode.HasValue) options.UnixCreateMode = mode.Value;
            await using (var stream = new FileStream(temporary, options))
            {
                created = true;
                await write(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows() && mode.HasValue)
                File.SetUnixFileMode(temporary, mode.Value); // restore bits restricted by the process umask
            cancellationToken.ThrowIfCancellationRequested();
            // Same-volume replacement preserves Windows metadata; Unix modes are
            // carried explicitly. Failure never falls back to truncating the target.
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        finally
        {
            if (created) File.Delete(temporary);
        }
    }
}

using System.Text;
using CAP_DataAccess.Persistence;
using Shouldly;

namespace UnitTests.Persistence;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lunima-atomic-save-" + Guid.NewGuid().ToString("N"));
    public AtomicFileWriterTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialWrite_IsInvisibleUntilPublication(bool existing)
    {
        var path = Path.Combine(_directory, "design.lun");
        if (existing) await File.WriteAllTextAsync(path, "original");
        var entered = Signal();
        var release = Signal();
        var saving = AtomicFileWriter.WriteAsync(path, async (stream, token) =>
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("complete"), token);
            await stream.FlushAsync(token);
            entered.TrySetResult();
            await release.Task;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (existing) (await File.ReadAllTextAsync(path)).ShouldBe("original");
            else File.Exists(path).ShouldBeFalse("a new document is only visible once its content is complete");
        }
        finally
        {
            release.TrySetResult();
            await saving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        (await File.ReadAllTextAsync(path)).ShouldBe("complete");
        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSerialization_PreservesDestinationAndCleansTemporaryFile(bool cancellation)
    {
        var path = Path.Combine(_directory, "design.lun");
        await File.WriteAllTextAsync(path, "original");
        using var source = new CancellationTokenSource();
        var saving = AtomicFileWriter.WriteAsync(path, async (stream, token) =>
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("partial"), token);
            if (cancellation)
            {
                source.Cancel();
                token.ThrowIfCancellationRequested();
            }
            throw new IOException("controlled serializer failure");
        }, source.Token);
        if (cancellation) await Should.ThrowAsync<OperationCanceledException>(() => saving);
        else await Should.ThrowAsync<IOException>(() => saving);
        (await File.ReadAllTextAsync(path)).ShouldBe("original");
        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    [Fact]
    public async Task RenameFailure_CleansTemporaryFileAndPreservesExistingDirectory()
    {
        var path = Path.Combine(_directory, "design.lun");
        Directory.CreateDirectory(path);
        var error = await Record.ExceptionAsync(() => AtomicFileWriter.WriteAsync(path,
            (stream, token) => stream.WriteAsync(Encoding.UTF8.GetBytes("complete"), token).AsTask()));
        (error is IOException or UnauthorizedAccessException).ShouldBeTrue();
        Directory.Exists(path).ShouldBeTrue();
        Directory.GetFiles(_directory).ShouldBeEmpty();
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SymbolicLinkSave_UpdatesTargetAndPreservesLink(bool existingTarget)
    {
        var target = Path.Combine(_directory, "target.lun");
        var link = Path.Combine(_directory, "link.lun");
        if (existingTarget) await File.WriteAllTextAsync(target, "original");
        try { File.CreateSymbolicLink(link, "target.lun"); }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314)
        {
            Skip.If(true, "Windows requires symbolic-link privileges or Developer Mode (ERROR_PRIVILEGE_NOT_HELD).");
        }
        await AtomicFileWriter.WriteAsync(link,
            (stream, token) => stream.WriteAsync(Encoding.UTF8.GetBytes("complete"), token).AsTask());
        new FileInfo(link).LinkTarget.ShouldBe("target.lun", "saving must retain the user's symbolic link");
        (await File.ReadAllTextAsync(target)).ShouldBe("complete");
        Directory.GetFiles(_directory).Order().ShouldBe(new[] { link, target }.Order());
    }

    [SkippableFact]
    public async Task ReplaceOnUnix_PreservesPrivateFilePermissions()
    {
        Skip.If(OperatingSystem.IsWindows(), "Unix permissions require a Unix test host.");
        var path = Path.Combine(_directory, "private.lun");
        await File.WriteAllTextAsync(path, "original");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(path, mode);
        await AtomicFileWriter.WriteAsync(path, async (stream, token) =>
        {
            var temporary = Directory.GetFiles(_directory).Single(file => file != path);
            File.GetUnixFileMode(temporary).ShouldBe(mode, "the in-progress capture is private as well");
            await stream.WriteAsync(Encoding.UTF8.GetBytes("complete"), token);
        });
        File.GetUnixFileMode(path).ShouldBe(mode);
        (await File.ReadAllTextAsync(path)).ShouldBe("complete");
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

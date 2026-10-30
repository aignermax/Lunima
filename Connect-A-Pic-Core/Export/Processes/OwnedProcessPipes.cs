using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace CAP_Core.Export.Processes;

internal sealed class OwnedProcessPipes : IDisposable
{
    private readonly AnonymousPipeServerStream _input;
    private readonly AnonymousPipeServerStream _output;
    private readonly AnonymousPipeServerStream _error;
    internal StreamWriter Input { get; }
    internal StreamReader Output { get; }
    internal StreamReader Error { get; }
    internal nint ChildInput => _input.ClientSafePipeHandle.DangerousGetHandle();
    internal nint ChildOutput => _output.ClientSafePipeHandle.DangerousGetHandle();
    internal nint ChildError => _error.ClientSafePipeHandle.DangerousGetHandle();
    internal nint[] ChildHandles => [ChildInput, ChildOutput, ChildError];
    internal nint[] ParentHandles => [_input.SafePipeHandle.DangerousGetHandle(),
        _output.SafePipeHandle.DangerousGetHandle(), _error.SafePipeHandle.DangerousGetHandle()];

    internal OwnedProcessPipes(ProcessStartInfo info)
    {
        try
        {
            var inheritance = OperatingSystem.IsWindows() ? HandleInheritability.Inheritable : HandleInheritability.None;
            _input = new(PipeDirection.Out, inheritance);
            _output = new(PipeDirection.In, inheritance);
            _error = new(PipeDirection.In, inheritance);
            Input = new StreamWriter(_input, info.StandardInputEncoding ?? new UTF8Encoding(false), leaveOpen: false)
                { AutoFlush = true };
            Output = new StreamReader(_output, info.StandardOutputEncoding ?? Encoding.UTF8, leaveOpen: true);
            Error = new StreamReader(_error, info.StandardErrorEncoding ?? Encoding.UTF8, leaveOpen: true);
        }
        catch
        {
            _input?.Dispose();
            _output?.Dispose();
            _error?.Dispose();
            throw;
        }
    }

    internal void CloseChildCopies()
    {
        _input.DisposeLocalCopyOfClientHandle();
        _output.DisposeLocalCopyOfClientHandle();
        _error.DisposeLocalCopyOfClientHandle();
    }

    public void Dispose()
    {
        // Close native endpoints first: no disposal flush may block on a tool that stopped reading.
        _input.Dispose();
        _output.Dispose();
        _error.Dispose();
        try { Input.Dispose(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        Output.Dispose();
        Error.Dispose();
    }
}

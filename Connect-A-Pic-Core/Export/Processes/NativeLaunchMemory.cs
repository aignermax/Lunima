using System.Runtime.InteropServices;

namespace CAP_Core.Export.Processes;

internal sealed class NativeLaunchMemory : IDisposable
{
    private readonly List<nint> _allocations = new();

    internal nint Allocate(int bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes);
        _allocations.Add(pointer);
        Marshal.Copy(new byte[bytes], 0, pointer, bytes);
        return pointer;
    }

    internal nint Utf8Vector(IEnumerable<string> values)
    {
        var strings = values.ToArray();
        var vector = Allocate((strings.Length + 1) * nint.Size);
        for (var index = 0; index < strings.Length; index++)
        {
            if (strings[index].Contains('\0')) throw new ArgumentException("Native process arguments cannot contain NUL.");
            var bytes = System.Text.Encoding.UTF8.GetBytes(strings[index] + "\0");
            var text = Allocate(bytes.Length);
            Marshal.Copy(bytes, 0, text, bytes.Length);
            Marshal.WriteIntPtr(vector, index * nint.Size, text);
        }
        return vector;
    }

    public void Dispose()
    {
        foreach (var pointer in _allocations) Marshal.FreeHGlobal(pointer);
        _allocations.Clear();
    }
}

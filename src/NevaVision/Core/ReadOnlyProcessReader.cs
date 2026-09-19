using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NevaVision.Core;

/// <summary>
/// Minimal external reader. The class deliberately exposes only OpenProcess
/// with PROCESS_VM_READ and ReadProcessMemory; it has no write or injection API.
/// </summary>
public sealed class ReadOnlyProcessReader : IDisposable
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private Process? _process;
    private nint _handle;

    public bool IsAttached => _handle != nint.Zero && _process is { HasExited: false };
    public int? ProcessId => _process?.Id;

    public bool Attach(Process process, out string error)
    {
        Detach();
        try
        {
            _process = process;
            _handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, process.Id);
            if (_handle == nint.Zero)
            {
                error = $"OpenProcess не удался: Win32 {Marshal.GetLastWin32Error()}";
                Detach();
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Detach();
            return false;
        }
    }

    public bool TryGetModuleBase(string moduleName, out ulong baseAddress)
    {
        baseAddress = 0;
        if (!IsAttached || _process is null)
            return false;

        try
        {
            foreach (ProcessModule module in _process.Modules)
            {
                if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    baseAddress = unchecked((ulong)module.BaseAddress.ToInt64());
                    return baseAddress != 0;
                }
            }
        }
        catch
        {
            // Access to the module list can be denied by the game or an anti-cheat.
        }

        return false;
    }

    public bool TryReadBytes(ulong address, int count, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!IsAttached || address < 0x10000 || count <= 0 || count > 1024 * 1024)
            return false;

        var buffer = new byte[count];
        if (!ReadProcessMemory(_handle, new nint(unchecked((long)address)), buffer, count, out nint read) || read.ToInt64() != count)
            return false;

        bytes = buffer;
        return true;
    }

    public bool TryReadUInt64(ulong address, out ulong value)
    {
        value = 0;
        if (!TryReadBytes(address, sizeof(ulong), out byte[] bytes))
            return false;
        value = BitConverter.ToUInt64(bytes, 0);
        return true;
    }

    public bool TryReadInt32(ulong address, out int value)
    {
        value = 0;
        if (!TryReadBytes(address, sizeof(int), out byte[] bytes))
            return false;
        value = BitConverter.ToInt32(bytes, 0);
        return true;
    }

    public bool TryReadSingle(ulong address, out float value)
    {
        value = 0;
        if (!TryReadBytes(address, sizeof(float), out byte[] bytes))
            return false;
        value = BitConverter.ToSingle(bytes, 0);
        return true;
    }

    public bool TryReadVector3(ulong address, out (float X, float Y, float Z) value)
    {
        value = default;
        if (!TryReadBytes(address, sizeof(float) * 3, out byte[] bytes))
            return false;
        value = (BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8));
        return true;
    }

    public bool TryReadUtf8(ulong address, int maxBytes, out string value)
    {
        value = string.Empty;
        if (!TryReadBytes(address, maxBytes, out byte[] bytes))
            return false;

        int length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
            length = bytes.Length;

        value = Encoding.UTF8.GetString(bytes, 0, length).Trim();
        return value.Length > 0;
    }

    /// <summary>
    /// Resolves the same vector-of-offsets pattern used by the external reader:
    /// read a pointer, add the current offset, and repeat. The final pointer is
    /// returned after adding the final offset.
    /// </summary>
    public bool TryResolveChain(ulong startAddress, IReadOnlyList<ulong> offsets, out ulong finalAddress)
    {
        finalAddress = 0;
        if (offsets.Count == 0 || startAddress < 0x10000)
            return false;

        ulong cursor = startAddress;
        for (int i = 0; i < offsets.Count; i++)
        {
            if (!TryReadUInt64(cursor, out ulong pointer) || pointer < 0x10000)
                return false;

            cursor = unchecked(pointer + offsets[i]);
        }

        finalAddress = cursor;
        return finalAddress >= 0x10000;
    }

    public void Detach()
    {
        if (_handle != nint.Zero)
        {
            CloseHandle(_handle);
            _handle = nint.Zero;
        }

        _process?.Dispose();
        _process = null;
    }

    public void Dispose() => Detach();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, int size, out nint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

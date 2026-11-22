using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Tessera.RegressionTests;

internal static class TestDesktop
{
    private const string WorkerPrefix = "--test-desktop=";
    private const string DesktopPrefix = "TesseraTests-";
    private const uint MaximumAllowed = 0x02000000;
    private const uint WaitIndefinitely = 0xffffffff;
    private const uint UseStandardHandles = 0x100;
    private const uint DisableCursorFeedback = 0x80;
    private const uint DuplicateSameAccess = 2;
    private const int UserObjectName = 2;

    public static bool IsWorker(ref string[] args)
    {
        if (args.Length == 0 || !args[^1].StartsWith(WorkerPrefix, StringComparison.Ordinal)) return false;

        string expected = args[^1][WorkerPrefix.Length..];
        var desktop = GetThreadDesktop(GetCurrentThreadId());
        var actual = new StringBuilder(128);
        if (!GetUserObjectInformation(desktop, UserObjectName, actual, actual.Capacity * sizeof(char), out _) ||
            !expected.StartsWith(DesktopPrefix, StringComparison.Ordinal) || actual.ToString() != expected)
            throw new InvalidOperationException("The test process was not attached to its background Windows desktop.");

        args = args[..^1];
        return true;
    }

    public static int Run()
    {
        string name = DesktopPrefix + Guid.NewGuid().ToString("N");
        var desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, MaximumAllowed, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception();

        try
        {
            using var input = DuplicateStandardHandle(-10);
            using var output = DuplicateStandardHandle(-11);
            using var error = DuplicateStandardHandle(-12);
            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Desktop = name,
                Flags = UseStandardHandles | DisableCursorFeedback,
                Input = input.DangerousGetHandle(),
                Output = output.DangerousGetHandle(),
                Error = error.DangerousGetHandle()
            };

            // Preserve the host and original quoting for both apphost and `dotnet <dll>` invocations.
            var command = new StringBuilder(Marshal.PtrToStringUni(GetCommandLine()));
            command.Append(' ').Append(WorkerPrefix).Append(name);
            if (!CreateProcess(Environment.ProcessPath!, command, IntPtr.Zero, IntPtr.Zero, true, 0,
                    IntPtr.Zero, null, ref startup, out var worker))
                throw new Win32Exception();

            using var process = new SafeWaitHandle(worker.Process, ownsHandle: true);
            CloseHandle(worker.Thread);
            if (WaitForSingleObject(process, WaitIndefinitely) != 0 || !GetExitCodeProcess(process, out int exitCode))
                throw new Win32Exception();
            return exitCode;
        }
        finally { CloseDesktop(desktop); }
    }

    private static SafeFileHandle DuplicateStandardHandle(int id)
    {
        var handle = GetStdHandle(id);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return new SafeFileHandle(IntPtr.Zero, ownsHandle: true);

        var process = GetCurrentProcess();
        if (!DuplicateHandle(process, handle, process, out var copy, 0, true, DuplicateSameAccess))
            throw new Win32Exception();
        return copy;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved;
        public string Desktop;
        public IntPtr Title;
        public int X, Y, Width, Height, Columns, Rows, FillAttribute;
        public uint Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedData, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int size, out int needed);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
        bool inheritHandles, uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInfo process);

    [DllImport("kernel32.dll", EntryPoint = "GetCommandLineW")]
    private static extern IntPtr GetCommandLine();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int id);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess, out SafeFileHandle target,
        uint access, bool inherit, uint options);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint timeout);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeWaitHandle handle, out int exitCode);
}

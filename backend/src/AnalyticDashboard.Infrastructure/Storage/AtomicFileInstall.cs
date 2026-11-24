using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AnalyticDashboard.Infrastructure.Storage;

internal static class AtomicFileInstall
{
    private const uint MoveWriteThrough = 0x8;
    private const int CurrentDirectory = -100;
    private const uint RenameNoReplace = 1;
    private const int DirectoryOnly = 0x10000;
    private const int ExclusiveNonBlockingLock = 2 | 4;
    private const int SharedNonBlockingLock = 1 | 4;

    internal static void RequireExclusiveLock(FileStream stream)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Local storage namespace locking is supported on Windows and Linux.");
        }

        while (Lock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), ExclusiveNonBlockingLock) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 4)
            {
                continue;
            }
            throw new IOException("Exclusive storage namespace locking failed.", unchecked((int)0x80070000) | error);
        }
    }

    internal static void RequireSharedLock(FileStream stream)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Local storage namespace locking is supported on Windows and Linux.");
        }

        while (Lock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), SharedNonBlockingLock) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 4)
            {
                continue;
            }
            throw new IOException("Shared storage namespace locking failed.", unchecked((int)0x80070000) | error);
        }
    }

    public static void Move(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(ExtendedPath(source), ExtendedPath(destination), MoveWriteThrough))
            {
                throw new IOException(
                    "Atomic storage installation failed.",
                    new Win32Exception(Marshal.GetLastPInvokeError())
                );
            }
            return;
        }
        if (OperatingSystem.IsLinux())
        {
            try
            {
                if (RenameNoReplaceFile(CurrentDirectory, source, CurrentDirectory, destination, RenameNoReplace) != 0)
                {
                    throw new IOException(
                        "Atomic storage installation failed.",
                        new Win32Exception(Marshal.GetLastPInvokeError())
                    );
                }
                FlushDirectory(Path.GetDirectoryName(destination)!);
                FlushDirectory(Path.GetDirectoryName(source)!);
                return;
            }
            catch (EntryPointNotFoundException exception)
            {
                throw new PlatformNotSupportedException("Local storage requires renameat2 with RENAME_NOREPLACE.", exception);
            }
        }
        throw new PlatformNotSupportedException("Atomic local storage installation is supported on Windows and Linux.");
    }

    private static string ExtendedPath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;

    internal static void FlushDirectory(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var descriptor = OpenDirectory(path, DirectoryOnly);
        if (descriptor < 0)
        {
            throw new IOException("Opening a storage directory for synchronization failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        try
        {
            if (Synchronize(descriptor) != 0)
            {
                throw new IOException("Storage directory synchronization failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
            }
        }
        finally
        {
            Close(descriptor);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string source, string destination, uint flags);

    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameNoReplaceFile(int oldDirectory, string source, int newDirectory, string destination, uint flags);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenDirectory(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Synchronize(int descriptor);

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Lock(int descriptor, int operation);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int descriptor);
}

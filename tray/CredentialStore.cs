using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GitIt.Tray;

// Keeps the GitHub token in Windows Credential Manager as a generic credential,
// visible under "Windows Credentials" and stored for this machine only.
internal static partial class CredentialStore
{
    private const string Target = "GitIt.Tray:github-token";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(in Credential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr buffer);

    // Returns null when no token is stored. Throws Win32Exception on other failures.
    public static string? ReadToken()
    {
        if (!CredRead(Target, CredTypeGeneric, 0, out var ptr))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0) return null;
            return Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / sizeof(char));
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public static void WriteToken(string token)
    {
        var target = Marshal.StringToHGlobalUni(Target);
        var user = Marshal.StringToHGlobalUni("github");
        var blob = Marshal.StringToHGlobalUni(token);
        try
        {
            var cred = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                UserName = user,
                CredentialBlob = blob,
                CredentialBlobSize = (uint)(token.Length * sizeof(char)),
                Persist = CredPersistLocalMachine,
            };
            if (!CredWrite(cred, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(blob);
            Marshal.FreeHGlobal(user);
            Marshal.FreeHGlobal(target);
        }
    }
}

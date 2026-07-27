using System.Runtime.InteropServices;
using System.Text;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Camera credentials in Windows Credential Manager (CRED_TYPE_GENERIC).
/// Passwords never touch cameras.json, logs, or the GUI (shown masked only).
/// </summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public bool TryRead(string credentialRef, out string username, out string password)
    {
        username = "";
        password = "";
        if (!CredRead(credentialRef, CredTypeGeneric, 0, out var credPtr)) return false;
        try
        {
            var cred = Marshal.PtrToStructure<NativeCredential>(credPtr);
            username = Marshal.PtrToStringUni(cred.UserName) ?? "";
            if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
            {
                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
                password = Encoding.Unicode.GetString(bytes);
            }
            return true;
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public bool Write(string credentialRef, string username, string password)
    {
        var blob = Encoding.Unicode.GetBytes(password);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = Marshal.StringToHGlobalUni(credentialRef),
                UserName = Marshal.StringToHGlobalUni(username),
                CredentialBlob = blobPtr,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CredPersistLocalMachine,
            };
            try
            {
                return CredWrite(ref cred, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(cred.TargetName);
                Marshal.FreeHGlobal(cred.UserName);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public bool Delete(string credentialRef) => CredDelete(credentialRef, CredTypeGeneric, 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

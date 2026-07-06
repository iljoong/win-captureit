using System.Runtime.InteropServices;
using System.Text;

namespace CaptureIt.App.Capture;

/// <summary>
/// Stores/retrieves the AI Capture API key in Windows Credential Manager
/// (a "Generic" credential, scoped to the current Windows user) instead of the
/// plaintext settings.json file. Wraps the CredWrite/CredRead/CredDelete Win32 APIs
/// (advapi32.dll) directly — there's no managed API for the Credential Manager.
/// </summary>
public static class CredentialManagerService
{
    private const string TargetName = "CaptureIt:AiCapture:ApiKey";
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr cred);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    /// <summary>Saves (overwriting any existing) API key. Throws if Credential Manager rejects the write.</summary>
    public static void SaveApiKey(string apiKey)
    {
        var bytes = Encoding.Unicode.GetBytes(apiKey ?? string.Empty);
        var blobPtr = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        try
        {
            if (bytes.Length > 0)
            {
                Marshal.Copy(bytes, 0, blobPtr, bytes.Length);
            }

            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = TargetName,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = "CaptureIt",
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException(
                    $"Could not save the API key to Windows Credential Manager (error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    /// <summary>Returns the saved API key, or null if none has been saved yet.</summary>
    public static string? LoadApiKey()
    {
        if (!CredRead(TargetName, CRED_TYPE_GENERIC, 0, out var credentialPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    /// <summary>Removes the saved API key, if any. Safe to call when none exists.</summary>
    public static void DeleteApiKey()
    {
        CredDelete(TargetName, CRED_TYPE_GENERIC, 0);
    }

    public static bool HasApiKey() => !string.IsNullOrEmpty(LoadApiKey());
}

using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace CoverArtSaver.Interop;

/// <summary>Checks Windows code signatures (Authenticode), the same check Windows does before running a download.</summary>
internal static class Authenticode
{
    /// <summary>
    /// Who signed the file, if it has a valid signature Windows trusts; otherwise null.
    /// Revocation isn't checked online, so this works without extra network calls.
    /// </summary>
    public static string? SignerOf(string path)
    {
        if (!IsValidlySigned(path))
        {
            return null;
        }

        try
        {
#pragma warning disable SYSLIB0057 // there's no replacement API for reading the signer of a signed file
            return new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).Subject;
#pragma warning restore SYSLIB0057
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// An installer may run if this program isn't signed (then HTTPS from GitHub is all there is to go on), or if the
    /// installer is validly signed by the same publisher as this program.
    /// </summary>
    public static bool MayRun(string installer, string runningProgram)
    {
        var ours = SignerOf(runningProgram);
        return ours == null || string.Equals(SignerOf(installer), ours, StringComparison.Ordinal);
    }

    private static bool IsValidlySigned(string path)
    {
        var file = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
        };
        var fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(file, fileInfo, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = fileInfo,
                dwStateAction = WTD_STATEACTION_IGNORE,
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
            };
            var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(fileInfo);
            Marshal.FreeHGlobal(fileInfo);
        }
    }

    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_CHOICE_FILE = 1, WTD_STATEACTION_IGNORE = 0,
        WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);
}

using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Pop;

/// 更新前检查代码签名：正在运行的 Pop 带签名时，新版本必须带有效签名，而且是同一个证书主体签的
internal static class Signature
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

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
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

    /// 签名的证书主体；没有签名返回 null
    public static string? Signer(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return cert.Subject;
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or IOException)
        {
            return null;
        }
    }

    public static bool IsTrusted(string path)
    {
        var file = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,      // WTD_UI_NONE
                fdwRevocationChecks = 0,
                dwUnionChoice = 1,   // WTD_CHOICE_FILE
                pFile = filePtr,
                dwStateAction = 0,
                dwProvFlags = 0x00000040, // WTD_CACHE_ONLY_URL_RETRIEVAL，不联网查吊销
            };
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    /// 返回 null 表示可以更新，否则是拒绝的原因
    public static string? CheckUpdate(string currentExe, string newExe)
    {
        var current = Signer(currentExe);
        if (current is null)
        {
            Log.Info("当前版本没有代码签名，只检查校验和");
            return null;
        }
        var next = Signer(newExe);
        if (next is null) return "新版本没有代码签名，已放弃更新";
        if (!IsTrusted(newExe)) return "新版本的代码签名无效，已放弃更新";
        if (!string.Equals(current, next, StringComparison.Ordinal)) return $"新版本换了签名证书（{next}），已放弃更新";
        return null;
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace D7.Tools.Security;

public sealed record AuthenticodeVerificationResult(
    bool Checked,
    bool HasSignature,
    bool Trusted,
    string? SignerSubject,
    string Detail);

[SupportedOSPlatform("windows")]
public static class AuthenticodeVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionIgnore = 0;
    private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;
    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustEProviderUnknown = unchecked((int)0x800B0001);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);

    public static AuthenticodeVerificationResult Verify(string filePath)
    {
        if (!OperatingSystem.IsWindows())
            return new AuthenticodeVerificationResult(false, false, false, null, "Authenticode verification is only available on Windows.");
        if (!File.Exists(filePath))
            return new AuthenticodeVerificationResult(true, false, false, null, "File does not exist.");

        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPtr = IntPtr.Zero;
        try
        {
            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var trustData = new WinTrustData(fileInfoPtr);
            var action = WintrustActionGenericVerifyV2;
            var status = WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);

            if (status == 0)
            {
                return new AuthenticodeVerificationResult(
                    true,
                    true,
                    true,
                    TryReadSignerSubject(filePath),
                    "Authenticode signature is trusted by Windows WinVerifyTrust.");
            }

            if (status is TrustENoSignature or TrustEProviderUnknown or TrustESubjectFormUnknown)
            {
                return new AuthenticodeVerificationResult(
                    true,
                    false,
                    false,
                    null,
                    $"No trusted Authenticode signature was found (0x{status:X8}).");
            }

            return new AuthenticodeVerificationResult(
                true,
                true,
                false,
                TryReadSignerSubject(filePath),
                $"Authenticode signature validation failed (0x{status:X8}).");
        }
        catch (Exception ex) when (ex is CryptographicException or ExternalException or ArgumentException)
        {
            return new AuthenticodeVerificationResult(true, false, false, null, ex.Message);
        }
        finally
        {
            if (fileInfoPtr != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPtr);
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }
    }

    private static string? TryReadSignerSubject(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
            return certificate.Subject;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [In] ref Guid pgActionId,
        [In] ref WinTrustData pWvtData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;

        public WinTrustFileInfo(string filePath)
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            pcwszFilePath = filePath;
            hFile = IntPtr.Zero;
            pgKnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
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

        public WinTrustData(IntPtr fileInfo)
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustData>();
            pPolicyCallbackData = IntPtr.Zero;
            pSIPClientData = IntPtr.Zero;
            dwUIChoice = WtdUiNone;
            fdwRevocationChecks = WtdRevokeNone;
            dwUnionChoice = WtdChoiceFile;
            pFile = fileInfo;
            dwStateAction = WtdStateActionIgnore;
            hWVTStateData = IntPtr.Zero;
            pwszURLReference = IntPtr.Zero;
            dwProvFlags = WtdCacheOnlyUrlRetrieval;
            dwUIContext = 0;
        }
    }
}

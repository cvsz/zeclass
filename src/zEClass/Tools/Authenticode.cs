using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace zEClass.Tools;

/// <summary>Signature state of one file.</summary>
public enum SignatureState
{
    Valid,
    NotSigned,
    Invalid,
}

/// <summary>
/// Verifies Authenticode signatures through wintrust.dll, following Microsoft's own
/// "Verifying the Signature of a PE File" example: a single call, no UI, no revocation
/// check (classroom machines are often offline), and the return compared to zero only,
/// with the TRUST_E_NOSIGNATURE plus GetLastError refinement separating unsigned from
/// invalid exactly as the example does.
///
/// Verified scope, probed live on Windows 11: embedded signatures verify (a garbage
/// binary reads back Invalid, a real unsigned binary reads back NotSigned).
/// Catalog-only system files read back as NotSigned through this path, so absence
/// here is never proof of tampering and never proof of absence — only Valid vouches.
/// </summary>
internal static class Authenticode
{
    // WINTRUST_ACTION_GENERIC_VERIFY_V2 from Softpub.h.
    private static readonly Guid GenericVerifyV2 = new(0x00aac56b, 0xcd44, 0x11d0,
        0x8c, 0xc2, 0x00, 0xc0, 0x4f, 0xc2, 0x95, 0xee);

    private const uint TrustENoSignature = 0x800B0100;
    private const uint TrustESubjectFormUnknown = 0x800B0003;
    private const uint TrustEProviderUnknown = 0x800B0001;

    // File-read failures from the provider itself. Verified by probe: an exclusively
    // locked file returns CRYPT_E_FILE_ERROR here. These mean "could not read", never
    // "signature bad", so they are the only results worth retrying.
    private const uint CryptEFileError = 0x80092003;
    private const uint ErrorSharingViolation = 0x80070020;
    private const uint ErrorLockViolation = 0x80070021;

    // A freshly written executable can be momentarily locked by an antivirus scan on
    // close. Without a bounded retry that transient collides into a permanent-looking
    // Invalid verdict (observed as a flaky NDI hash-pin test). Genuinely bad signatures
    // fail every attempt identically, so the verdict logic is unchanged.
    private const int VerifyAttempts = 3;
    private const int VerifyRetryDelayMs = 100;

    private const int WtdUiNone = 2;
    private const int WtdRevokeNone = 0;
    private const int WtdChoiceFile = 1;
    private const int WtdStateActionIgnore = 0;

    // LONG, compared to zero only: SUCCEEDED-style macros do not apply here.
    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, IntPtr data);

    public static SignatureState Verify(string path) => Verify(path, observer: null);

    /// <param name="observer">Test seam: invoked with the 0-based attempt index
    /// before every native attempt. A parameter (not static state) so parallel tests
    /// cannot interfere. Production callers pass null.</param>
    internal static SignatureState Verify(string path, Action<int>? observer)
    {
        // Bounded retry for transient file-read collisions only. Anything the provider
        // actually adjudicates (Valid, NotSigned, Invalid) returns immediately.
        for (var attempt = 0; ; attempt++)
        {
            observer?.Invoke(attempt);
            var state = TryVerifyOnce(path, out var transient);
            if (!transient || attempt + 1 >= VerifyAttempts)
            {
                return state;
            }

            Thread.Sleep(VerifyRetryDelayMs);
        }
    }

    private static SignatureState TryVerifyOnce(string path, out bool transient)
    {
        transient = false;
        IntPtr filePathPtr = IntPtr.Zero;
        IntPtr fileInfoPtr = IntPtr.Zero;
        IntPtr dataPtr = IntPtr.Zero;
        try
        {
            filePathPtr = Marshal.StringToHGlobalUni(path);

            var measurer = new BlockWriter();
            FillFileInfo(ref measurer, IntPtr.Zero);
            fileInfoPtr = Marshal.AllocHGlobal(measurer.Offset);
            var fileInfo = new BlockWriter { Base = fileInfoPtr };
            FillFileInfo(ref fileInfo, filePathPtr);

            measurer = new BlockWriter();
            FillData(ref measurer, IntPtr.Zero);
            dataPtr = Marshal.AllocHGlobal(measurer.Offset);
            var data = new BlockWriter { Base = dataPtr };
            FillData(ref data, fileInfoPtr);

            var action = GenericVerifyV2;
            var status = WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);
            if (status == 0)
            {
                return SignatureState.Valid;
            }

            if (unchecked((uint)status) == TrustENoSignature)
            {
                var last = unchecked((uint)Marshal.GetLastWin32Error());
                if (last is TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown)
                {
                    return SignatureState.NotSigned;
                }
            }

            // File-read collisions (proven by probe: exclusive lock surfaces as
            // CRYPT_E_FILE_ERROR) are the caller's cue to retry; every other failure
            // is a verdict and returns immediately.
            if (unchecked((uint)status) is CryptEFileError or ErrorSharingViolation
                or ErrorLockViolation)
            {
                transient = true;
            }

            return SignatureState.Invalid;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                       or OutOfMemoryException or ArgumentException)
        {
            // No trust provider on this machine: fail closed, never claim validity.
            CrashLog.Info($"Authenticode verification unavailable for {path}: {ex.Message}");
            return SignatureState.Invalid;
        }
        finally
        {
            if (filePathPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filePathPtr);
            }

            if (fileInfoPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }

            if (dataPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(dataPtr);
            }
        }
    }

    /// <summary>Signer subject of a signed file, or null when absent or unreadable.</summary>
    public static string? PublisherOf(string path)
    {
        try
        {
            // Justification for SYSLIB0057 (scoped to this method only): the replacement
            // API surface (X509CertificateLoader) has no signed-file loader, and parsing
            // the PE certificate table by hand to feed LoadCertificate(byte[]) would add
            // an adversarial-input parser for an informational log line. CreateFromSignedFile
            // remains functional on .NET 10; the certificate is disposed immediately with
            // no key-storage side effects. Revisit if the API is ever removed: replace
            // with wintrust subject-query P/Invoke (see Verify above for the pattern).
#pragma warning disable SYSLIB0057 // Type or member is obsolete
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057 // Type or member is obsolete
            return certificate.Subject;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    private struct BlockWriter
    {
        public IntPtr Base;
        public int Offset;

        public void U32(int value)
        {
            if (Base != IntPtr.Zero)
            {
                Marshal.WriteInt32(Base, Offset, value);
            }

            Offset += 4;
        }

        public void Ptr(IntPtr value)
        {
            Offset = (Offset + IntPtr.Size - 1) & ~(IntPtr.Size - 1);
            if (Base != IntPtr.Zero)
            {
                Marshal.WriteIntPtr(Base, Offset, value);
            }

            Offset += IntPtr.Size;
        }
    }

    /// <summary>WINTRUST_FILE_INFO layout; call once to measure, once to fill.</summary>
    private static void FillFileInfo(ref BlockWriter writer, IntPtr pathPtr)
    {
        var sizeOffset = writer.Offset;
        writer.U32(0);
        writer.Ptr(pathPtr);
        writer.Ptr(IntPtr.Zero); // hFile
        writer.Ptr(IntPtr.Zero); // pgKnownSubject
        if (writer.Base != IntPtr.Zero)
        {
            Marshal.WriteInt32(writer.Base, sizeOffset, writer.Offset - sizeOffset);
        }
    }

    /// <summary>WINTRUST_DATA layout; call once to measure, once to fill.</summary>
    private static void FillData(ref BlockWriter writer, IntPtr fileInfoPtr)
    {
        var sizeOffset = writer.Offset;
        writer.U32(0);
        writer.Ptr(IntPtr.Zero); // pPolicyCallbackData
        writer.Ptr(IntPtr.Zero); // pSIPClientData
        writer.U32(WtdUiNone);
        writer.U32(WtdRevokeNone);
        writer.U32(WtdChoiceFile);
        writer.Ptr(fileInfoPtr);
        writer.U32(WtdStateActionIgnore);
        writer.Ptr(IntPtr.Zero); // hWVTStateData
        writer.Ptr(IntPtr.Zero); // pwszURLReference
        writer.U32(0); // dwProvFlags
        writer.U32(0); // dwUIContext
        writer.Ptr(IntPtr.Zero); // pvContext
        if (writer.Base != IntPtr.Zero)
        {
            Marshal.WriteInt32(writer.Base, sizeOffset, writer.Offset - sizeOffset);
        }
    }
}

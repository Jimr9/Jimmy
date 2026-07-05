using System;
using System.Security.Cryptography;
using System.Text;

namespace WSJTX_Controller
{
    // Encrypts credential strings at rest using Windows DPAPI (CurrentUser scope), so the
    // INI file no longer holds passwords/API keys in plain text -- the ciphertext is only
    // usable by the same Windows user account on the same machine.
    //
    // Stored values are prefixed with "enc:". A value without that prefix is treated as
    // legacy plain text from before this feature existed (or an unreadable/foreign-machine
    // blob) and is returned as-is; Controller re-saves it encrypted the next time settings
    // are written, so existing installs upgrade silently on first save.
    public static class CredentialProtector
    {
        private const string Prefix = "enc:";

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(cipherBytes);
        }

        public static string Unprotect(string storedValue)
        {
            if (string.IsNullOrEmpty(storedValue)) return "";
            if (!storedValue.StartsWith(Prefix, StringComparison.Ordinal)) return storedValue;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(storedValue.Substring(Prefix.Length));
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                // Can't decrypt (e.g. INI copied from another machine/user profile) --
                // there is no way to recover the original value.
                return "";
            }
        }
    }
}

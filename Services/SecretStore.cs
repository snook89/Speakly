using System;
using System.Security.Cryptography;
using System.Text;

namespace Speakly.Services
{
    public static class SecretStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Speakly.Local.SecretStore.v1");

        public static string Protect(string? plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return string.Empty;

            var bytes = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string Unprotect(string? protectedBase64)
        {
            if (string.IsNullOrWhiteSpace(protectedBase64)) return string.Empty;

            try
            {
                var protectedBytes = Convert.FromBase64String(protectedBase64);
                var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}

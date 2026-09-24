using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Quickfire.Shared.Bridge
{
    public sealed class NativeCredential
    {
        public Uri Origin { get; set; }
        public string Token { get; set; }
    }

    public sealed class NativeCredentialStore
    {
        private readonly string _path;
        private readonly string _profile;
        public NativeCredentialStore(string profile, string storageDirectory = null)
        {
            if (profile != "tray" && profile != "desktop" && profile != "call") throw new ArgumentException("Unknown native profile.");
            _profile = profile;
            // Keep the existing storage location so the project rename preserves saved pairings.
            var directory = storageDirectory == null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Openfire", "Native")
                : Path.GetFullPath(storageDirectory);
            _path = Path.Combine(directory, profile + ".pairing");
        }

        public void Save(string server, string token)
        {
            var origin = NativeBridgeSafety.ServerOrigin(server);
            ValidateToken(token);
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy(origin), DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, origin.AbsoluteUri + "\n" + Convert.ToBase64String(encrypted));
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public NativeCredential Load()
        {
            if (!File.Exists(_path)) return null;
            try
            {
                if (new FileInfo(_path).Length > 8192) throw new InvalidDataException();
                var lines = File.ReadAllLines(_path);
                if (lines.Length != 2) throw new InvalidDataException();
                var origin = NativeBridgeSafety.ServerOrigin(lines[0]);
                var plain = ProtectedData.Unprotect(Convert.FromBase64String(lines[1]), Entropy(origin), DataProtectionScope.CurrentUser);
                var token = Encoding.UTF8.GetString(plain);
                Array.Clear(plain, 0, plain.Length);
                ValidateToken(token);
                return new NativeCredential { Origin = origin, Token = token };
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is ArgumentException || ex is InvalidDataException)
            {
                throw new InvalidOperationException("The saved pairing cannot be used by this Windows account. Pair the helper again.");
            }
        }

        public void Forget() { if (File.Exists(_path)) File.Delete(_path); }
        // This persisted encryption discriminator must match credentials saved by earlier builds.
        private byte[] Entropy(Uri origin) { return Encoding.UTF8.GetBytes("Openfire.Native/" + _profile + "/" + origin.AbsoluteUri); }
        private static void ValidateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 1024 || token.IndexOfAny(new[] { '\r', '\n', ' ', '\t' }) >= 0)
                throw new ArgumentException("Paste the device credential created in your Quickfire profile.");
        }
    }
}

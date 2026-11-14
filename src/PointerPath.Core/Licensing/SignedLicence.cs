using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PointerPath.Core.Licensing
{
    /// <summary>The public half of the key the website signs licences with (EC P-256).</summary>
    public sealed class LicencePublicKey
    {
        private readonly byte[] _x;
        private readonly byte[] _y;

        /// <param name="x">The key's "x", base64url, as in its JWK.</param>
        /// <param name="y">The key's "y", base64url.</param>
        public LicencePublicKey(string? x, string? y)
        {
            _x = Base64Url.TryDecode(x) ?? new byte[0];
            _y = Base64Url.TryDecode(y) ?? new byte[0];
        }

        public bool IsSet => _x.Length == 32 && _y.Length == 32;

        /// <summary>Whether <paramref name="signature"/> (64 bytes: r then s) is this key's signature of <paramref name="data"/>.</summary>
        internal bool Verify(byte[] data, byte[] signature)
        {
            if (!IsSet || signature.Length != 64)
            {
                return false;
            }

            try
            {
                using (ECDsa ecdsa = ECDsa.Create())
                {
                    ecdsa.ImportParameters(new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        Q = new ECPoint { X = _x, Y = _y },
                    });
                    return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// A licence as the website issues it: a JWT signed with ES256, saying which account and
    /// which computer it is for and when it runs out. Checking it needs only the public key, so
    /// PointerPath can run without a connection until it expires.
    /// </summary>
    public sealed class SignedLicence
    {
        private const string Issuer = "pointerpath";

        private SignedLicence()
        {
        }

        public string Email { get; private set; } = "";

        public string LicenceId { get; private set; } = "";

        /// <summary>"perpetual" or "subscription".</summary>
        public string Kind { get; private set; } = "";

        /// <summary>The computer the licence was issued to.</summary>
        public string MachineId { get; private set; } = "";

        public DateTime IssuedUtc { get; private set; }

        public DateTime ExpiresUtc { get; private set; }

        /// <summary>
        /// Reads a licence and checks its signature. Null, with the reason, when it is not a
        /// licence this key signed. Expiry and the computer are for the caller to check.
        /// </summary>
        public static SignedLicence? TryRead(string? token, LicencePublicKey key, out string problem)
        {
            problem = "The licence could not be read.";
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string[] parts = token!.Trim().Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            byte[]? header = Base64Url.TryDecode(parts[0]);
            byte[]? payload = Base64Url.TryDecode(parts[1]);
            byte[]? signature = Base64Url.TryDecode(parts[2]);
            if (header == null || payload == null || signature == null)
            {
                return null;
            }

            try
            {
                using (JsonDocument h = JsonDocument.Parse(header))
                {
                    // Only the one algorithm: a licence claiming any other (or none) is refused.
                    if (Text(h.RootElement, "alg") != "ES256")
                    {
                        return null;
                    }
                }

                if (!key.Verify(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature))
                {
                    problem = "The licence's signature is not valid.";
                    return null;
                }

                using (JsonDocument p = JsonDocument.Parse(payload))
                {
                    JsonElement root = p.RootElement;
                    if (Text(root, "iss") != Issuer || !Seconds(root, "exp", out DateTime expires) || Text(root, "mid").Length == 0)
                    {
                        return null;
                    }

                    Seconds(root, "iat", out DateTime issued);
                    problem = "";
                    return new SignedLicence
                    {
                        Email = Text(root, "email"),
                        LicenceId = Text(root, "lic"),
                        Kind = Text(root, "kind"),
                        MachineId = Text(root, "mid"),
                        IssuedUtc = issued,
                        ExpiresUtc = expires,
                    };
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Text(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        private static bool Seconds(JsonElement element, string name, out DateTime utc)
        {
            utc = default;
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(name, out JsonElement value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt64(out long seconds)
                || seconds < 0
                || seconds > 253402300799)
            {
                return false;
            }

            utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
            return true;
        }
    }

    internal static class Base64Url
    {
        public static byte[]? TryDecode(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            string s = text!.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: return null;
            }

            try
            {
                return Convert.FromBase64String(s);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}

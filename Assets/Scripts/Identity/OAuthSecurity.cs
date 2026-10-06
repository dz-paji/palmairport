using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IslandAirport
{
    public enum FirebaseAuthPlatform
    {
        Unsupported = 0,
        Desktop = 1,
        Android = 2
    }

    public sealed class FirebaseAuthConfiguration
    {
        private string _apiKey = string.Empty;

        public string ProjectId { get; private set; }
        public string PackageName { get; private set; }
        public string AndroidClientId { get; private set; }
        public string DesktopClientId { get; private set; }
        public string RedirectScheme { get; private set; }
        public bool HasApiKey { get { return !string.IsNullOrEmpty(_apiKey); } }

        internal string ApiKey { get { return _apiKey; } }

        public static FirebaseAuthConfiguration Parse(string json, string environmentApiKey)
        {
            FirebaseAuthConfiguration config = new FirebaseAuthConfiguration();
            Dictionary<string, object> values = MiniJson.ParseObject(json);
            config.ProjectId = MiniJson.GetString(values, "projectId", string.Empty).Trim();
            config.PackageName = MiniJson.GetString(values, "packageName", string.Empty).Trim();
            config.AndroidClientId = MiniJson.GetString(values, "androidClientId", string.Empty).Trim();
            config.DesktopClientId = MiniJson.GetString(values, "desktopClientId", string.Empty).Trim();
            config.RedirectScheme = MiniJson.GetString(values, "redirectScheme", string.Empty).Trim();
            string environmentKey = environmentApiKey == null ? string.Empty : environmentApiKey.Trim();
            config._apiKey = environmentKey.Length > 0
                ? environmentKey
                : MiniJson.GetString(values, "apiKey", string.Empty).Trim();
            return config;
        }

        public bool IsConfiguredFor(FirebaseAuthPlatform platform)
        {
            if (!HasApiKey || string.IsNullOrEmpty(ProjectId) || string.IsNullOrEmpty(PackageName))
            {
                return false;
            }

            if (platform == FirebaseAuthPlatform.Desktop)
            {
                return !string.IsNullOrEmpty(DesktopClientId);
            }

            if (platform == FirebaseAuthPlatform.Android)
            {
                return !string.IsNullOrEmpty(AndroidClientId) && IsValidScheme(RedirectScheme);
            }

            return false;
        }

        public string ClientIdFor(FirebaseAuthPlatform platform)
        {
            if (platform == FirebaseAuthPlatform.Desktop)
            {
                return DesktopClientId;
            }

            if (platform == FirebaseAuthPlatform.Android)
            {
                return AndroidClientId;
            }

            return string.Empty;
        }

        public string AndroidRedirectUri
        {
            get { return IsValidScheme(RedirectScheme) ? RedirectScheme + ":/oauth2redirect" : string.Empty; }
        }

        public static bool IsValidScheme(string scheme)
        {
            if (string.IsNullOrEmpty(scheme) || !IsAsciiLetter(scheme[0]))
            {
                return false;
            }

            for (int i = 1; i < scheme.Length; i++)
            {
                char c = scheme[i];
                if (!IsAsciiLetter(c) && (c < '0' || c > '9') && c != '+' && c != '.' && c != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }
    }

    public sealed class OAuthCallback
    {
        public string Code { get; internal set; }
        public string Error { get; internal set; }
    }

    public static class OAuthSecurity
    {
        public static string CreateVerifier()
        {
            return RandomBase64Url(32);
        }

        public static string CreateState()
        {
            return RandomBase64Url(32);
        }

        public static string CreateNonce()
        {
            return RandomBase64Url(32);
        }

        public static string CreateChallenge(string verifier)
        {
            if (string.IsNullOrEmpty(verifier))
            {
                throw new ArgumentException("Verifier is required", "verifier");
            }

            using (SHA256 sha = SHA256.Create())
            {
                return ToBase64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            }
        }

        public static string BuildAuthorizeUri(string clientId, string redirectUri, string state,
            string nonce, string challenge)
        {
            StringBuilder url = new StringBuilder("https://accounts.google.com/o/oauth2/v2/auth?");
            AppendParameter(url, "client_id", clientId, false);
            AppendParameter(url, "redirect_uri", redirectUri, true);
            AppendParameter(url, "response_type", "code", true);
            AppendParameter(url, "scope", "openid email profile", true);
            AppendParameter(url, "state", state, true);
            AppendParameter(url, "nonce", nonce, true);
            AppendParameter(url, "code_challenge", challenge, true);
            AppendParameter(url, "code_challenge_method", "S256", true);
            AppendParameter(url, "access_type", "offline", true);
            AppendParameter(url, "prompt", "select_account", true);
            return url.ToString();
        }

        public static bool TryParseCallback(string callbackUri, string expectedRedirectUri, string expectedState,
            out OAuthCallback callback, out string failure)
        {
            callback = null;
            failure = "redirect";
            Uri actual;
            Uri expected;
            if (!Uri.TryCreate(callbackUri, UriKind.Absolute, out actual) ||
                !Uri.TryCreate(expectedRedirectUri, UriKind.Absolute, out expected) ||
                !MatchesRedirect(actual, expected))
            {
                return false;
            }

            Dictionary<string, string> query;
            if (!TryParseQuery(actual.Query, out query))
            {
                failure = "response";
                return false;
            }

            string state;
            if (!query.TryGetValue("state", out state) || !FixedTimeEquals(state, expectedState))
            {
                failure = "state";
                return false;
            }

            string code;
            string error;
            bool hasCode = query.TryGetValue("code", out code) && !string.IsNullOrEmpty(code);
            bool hasError = query.TryGetValue("error", out error) && !string.IsNullOrEmpty(error);
            if (hasCode == hasError)
            {
                failure = "response";
                return false;
            }

            callback = new OAuthCallback();
            callback.Code = hasCode ? code : string.Empty;
            callback.Error = hasError ? error : string.Empty;
            failure = string.Empty;
            return true;
        }

        public static bool ValidateGoogleIdToken(string idToken, string expectedNonce, string expectedClientId,
            DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(expectedNonce) ||
                string.IsNullOrEmpty(expectedClientId))
            {
                return false;
            }

            string[] parts = idToken.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            byte[] payloadBytes;
            if (!TryFromBase64Url(parts[1], out payloadBytes))
            {
                return false;
            }

            Dictionary<string, object> payload;
            try
            {
                payload = MiniJson.ParseObject(Encoding.UTF8.GetString(payloadBytes));
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentNullException)
            {
                return false;
            }

            string nonce = MiniJson.GetString(payload, "nonce", string.Empty);
            string issuer = MiniJson.GetString(payload, "iss", string.Empty);
            if (!FixedTimeEquals(nonce, expectedNonce) ||
                (issuer != "https://accounts.google.com" && issuer != "accounts.google.com") ||
                !AudienceMatches(payload, expectedClientId))
            {
                return false;
            }

            double expiration = MiniJson.GetNumber(payload, "exp", 0);
            double now = (nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime())
                .Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return expiration > now;
        }

        public static string ToBase64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string RandomBase64Url(int bytes)
        {
            byte[] value = new byte[bytes];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(value);
            }

            return ToBase64Url(value);
        }

        private static void AppendParameter(StringBuilder url, string key, string value, bool separator)
        {
            if (separator)
            {
                url.Append('&');
            }

            url.Append(Uri.EscapeDataString(key));
            url.Append('=');
            url.Append(Uri.EscapeDataString(value ?? string.Empty));
        }

        private static bool MatchesRedirect(Uri actual, Uri expected)
        {
            if (!string.IsNullOrEmpty(actual.UserInfo) || !string.IsNullOrEmpty(actual.Fragment) ||
                !string.IsNullOrEmpty(expected.Query) || !string.IsNullOrEmpty(expected.Fragment) ||
                !string.Equals(actual.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(actual.Host, expected.Host, StringComparison.OrdinalIgnoreCase) ||
                actual.Port != expected.Port ||
                !string.Equals(actual.AbsolutePath, expected.AbsolutePath, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(expected.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(expected.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
                    actual.IsLoopback && expected.IsLoopback;
            }

            return true;
        }

        private static bool TryParseQuery(string queryString, out Dictionary<string, string> query)
        {
            query = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(queryString) || queryString[0] != '?')
            {
                return false;
            }

            string[] pairs = queryString.Substring(1).Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                if (pairs[i].Length == 0)
                {
                    continue;
                }

                int equals = pairs[i].IndexOf('=');
                string rawKey = equals < 0 ? pairs[i] : pairs[i].Substring(0, equals);
                string rawValue = equals < 0 ? string.Empty : pairs[i].Substring(equals + 1);
                string key;
                string value;
                if (!TryDecode(rawKey, out key) || !TryDecode(rawValue, out value) || query.ContainsKey(key))
                {
                    return false;
                }

                query.Add(key, value);
            }

            return true;
        }

        private static bool TryDecode(string value, out string decoded)
        {
            decoded = string.Empty;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '%' && (i + 2 >= value.Length || !IsHex(value[i + 1]) || !IsHex(value[i + 2])))
                {
                    return false;
                }

                if (value[i] == '%')
                {
                    i += 2;
                }
            }

            try
            {
                decoded = Uri.UnescapeDataString(value.Replace('+', ' '));
                return true;
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            byte[] leftBytes = Encoding.UTF8.GetBytes(left);
            byte[] rightBytes = Encoding.UTF8.GetBytes(right);
            int difference = leftBytes.Length ^ rightBytes.Length;
            int max = Math.Max(leftBytes.Length, rightBytes.Length);
            for (int i = 0; i < max; i++)
            {
                byte a = i < leftBytes.Length ? leftBytes[i] : (byte)0;
                byte b = i < rightBytes.Length ? rightBytes[i] : (byte)0;
                difference |= a ^ b;
            }

            return difference == 0;
        }

        private static bool AudienceMatches(IDictionary<string, object> payload, string clientId)
        {
            object audience;
            bool found = false;
            if (payload.TryGetValue("aud", out audience))
            {
                string audienceString = audience as string;
                if (audienceString != null)
                {
                    found = string.Equals(audienceString, clientId, StringComparison.Ordinal);
                }
                else
                {
                    List<object> audienceList = audience as List<object>;
                    if (audienceList != null)
                    {
                        for (int i = 0; i < audienceList.Count; i++)
                        {
                            if (string.Equals(audienceList[i] as string, clientId, StringComparison.Ordinal))
                            {
                                found = true;
                            }
                        }
                    }
                }
            }

            if (!found)
            {
                return false;
            }

            List<object> list = audience as List<object>;
            if (list != null && list.Count > 1)
            {
                string authorizedParty = MiniJson.GetString(payload, "azp", string.Empty);
                return string.Equals(authorizedParty, clientId, StringComparison.Ordinal);
            }

            return true;
        }

        private static bool TryFromBase64Url(string value, out byte[] decoded)
        {
            decoded = null;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string base64 = value.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 0: break;
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
                default: return false;
            }

            try
            {
                decoded = Convert.FromBase64String(base64);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}

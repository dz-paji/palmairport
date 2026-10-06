using System;
using System.Collections.Generic;
using System.Globalization;

namespace IslandAirport
{
    public sealed class FirebaseTokenData
    {
        public string Uid;
        public string IdToken;
        public string RefreshToken;
        public string DisplayName;
        public int ExpiresInSeconds;

        public static bool TryParseSignIn(string json, out FirebaseTokenData token)
        {
            return TryParse(json, "localId", "idToken", "refreshToken", "expiresIn", out token);
        }

        public static bool TryParseRefresh(string json, out FirebaseTokenData token)
        {
            return TryParse(json, "user_id", "id_token", "refresh_token", "expires_in", out token);
        }

        private static bool TryParse(string json, string uidKey, string idTokenKey, string refreshTokenKey,
            string expiresKey, out FirebaseTokenData token)
        {
            token = null;
            Dictionary<string, object> values;
            try
            {
                values = MiniJson.ParseObject(json);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentNullException)
            {
                return false;
            }

            string uid = MiniJson.GetString(values, uidKey, string.Empty);
            string idToken = MiniJson.GetString(values, idTokenKey, string.Empty);
            string refreshToken = MiniJson.GetString(values, refreshTokenKey, string.Empty);
            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(refreshToken))
            {
                return false;
            }

            string expiresText = MiniJson.GetString(values, expiresKey, string.Empty);
            double expires;
            if (!double.TryParse(expiresText, NumberStyles.Float, CultureInfo.InvariantCulture, out expires) ||
                expires <= 0 || double.IsInfinity(expires) || double.IsNaN(expires))
            {
                expires = 3600;
            }

            token = new FirebaseTokenData();
            token.Uid = uid;
            token.IdToken = idToken;
            token.RefreshToken = refreshToken;
            token.DisplayName = MiniJson.GetString(values, "displayName", MiniJson.GetString(values, "email", string.Empty));
            token.ExpiresInSeconds = (int)Math.Min(expires, 31536000d);
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using IslandAirport;

internal static class AuthProtocolTests
{
    private static int _assertions;

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
        {
            throw new Exception("FAIL: " + message);
        }
    }

    private static void TestPkce()
    {
        string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        AssertEqual("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            OAuthSecurity.CreateChallenge(verifier), "RFC 7636 challenge");

        string randomVerifier = OAuthSecurity.CreateVerifier();
        Assert(randomVerifier.Length == 43, "PKCE verifier uses 32 random bytes");
        Assert(randomVerifier.IndexOf('=') < 0, "PKCE verifier is unpadded base64url");
        Assert(OAuthSecurity.CreateState().Length == 43, "state uses unpredictable-length encoding");
        Assert(OAuthSecurity.CreateNonce().Length == 43, "nonce uses unpredictable-length encoding");

        string url = OAuthSecurity.BuildAuthorizeUri("desktop-client", "http://127.0.0.1:1234/oauth2callback",
            "random-state", "random-nonce", "challenge-value");
        Assert(url.Contains("response_type=code") && url.Contains("code_challenge_method=S256"),
            "authorization request uses code flow with S256");
        Assert(url.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A1234%2Foauth2callback"),
            "authorization request encodes redirect URI");
    }

    private static void TestCallbackValidation()
    {
        OAuthCallback callback;
        string failure;
        string androidRedirect = "com.example.palmbay:/oauth2redirect";
        bool valid = OAuthSecurity.TryParseCallback(androidRedirect + "?code=abc%2B123&state=nonce-state",
            androidRedirect, "nonce-state", out callback, out failure);
        Assert(valid, "Android callback accepted");
        AssertEqual("abc+123", callback.Code, "callback code decoded");
        AssertEqual(string.Empty, callback.Error, "success has no provider error");

        Assert(!OAuthSecurity.TryParseCallback(androidRedirect + "?code=x&state=wrong", androidRedirect,
            "nonce-state", out callback, out failure), "wrong state rejected");
        AssertEqual("state", failure, "state rejection is classified");
        Assert(!OAuthSecurity.TryParseCallback("com.attacker.app:/oauth2redirect?code=x&state=nonce-state",
            androidRedirect, "nonce-state", out callback, out failure), "wrong deep link scheme rejected");
        Assert(!OAuthSecurity.TryParseCallback(androidRedirect + "?code=x&state=nonce-state&state=nonce-state",
            androidRedirect, "nonce-state", out callback, out failure), "duplicate state rejected");
        Assert(!OAuthSecurity.TryParseCallback(androidRedirect + "?code=x&state=nonce-state#fragment",
            androidRedirect, "nonce-state", out callback, out failure), "callback fragment rejected");
        Assert(OAuthSecurity.TryParseCallback(androidRedirect + "?error=access_denied&state=nonce-state",
            androidRedirect, "nonce-state", out callback, out failure), "provider denial callback is parsed");
        AssertEqual("access_denied", callback.Error, "provider denial code retained for cancellation handling");

        string desktopRedirect = "http://127.0.0.1:4321/oauth2callback";
        Assert(OAuthSecurity.TryParseCallback(desktopRedirect + "?code=local&state=s",
            desktopRedirect, "s", out callback, out failure), "loopback callback accepted");
        Assert(!OAuthSecurity.TryParseCallback("http://127.0.0.2:4321/oauth2callback?code=x&state=s",
            desktopRedirect, "s", out callback, out failure), "non-loopback callback rejected");
        Assert(!OAuthSecurity.TryParseCallback("http://127.0.0.1:4322/oauth2callback?code=x&state=s",
            desktopRedirect, "s", out callback, out failure), "wrong loopback port rejected");
        Assert(!OAuthSecurity.TryParseCallback("http://127.0.0.1:4321/other?code=x&state=s",
            desktopRedirect, "s", out callback, out failure), "wrong loopback path rejected");
    }

    private static void TestIdTokenClaims()
    {
        DateTime now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Dictionary<string, object> payload = new Dictionary<string, object>();
        payload["nonce"] = "expected-nonce";
        payload["iss"] = "https://accounts.google.com";
        payload["aud"] = "client-id";
        payload["exp"] = (double)(now.AddMinutes(5) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        string token = "header." + OAuthSecurity.ToBase64Url(Encoding.UTF8.GetBytes(MiniJson.Serialize(payload))) + ".signature";

        Assert(OAuthSecurity.ValidateGoogleIdToken(token, "expected-nonce", "client-id", now),
            "nonce, audience, issuer and expiry accepted");
        Assert(!OAuthSecurity.ValidateGoogleIdToken(token, "wrong-nonce", "client-id", now), "wrong ID token nonce rejected");
        Assert(!OAuthSecurity.ValidateGoogleIdToken(token, "expected-nonce", "other-client", now), "wrong ID token audience rejected");
        Assert(!OAuthSecurity.ValidateGoogleIdToken(token, "expected-nonce", "client-id", now.AddMinutes(10)),
            "expired ID token rejected");

        payload["aud"] = new List<object> { "client-id", "other-client" };
        token = "header." + OAuthSecurity.ToBase64Url(Encoding.UTF8.GetBytes(MiniJson.Serialize(payload))) + ".signature";
        Assert(!OAuthSecurity.ValidateGoogleIdToken(token, "expected-nonce", "client-id", now),
            "multi-audience token without matching azp rejected");
        payload["azp"] = "client-id";
        token = "header." + OAuthSecurity.ToBase64Url(Encoding.UTF8.GetBytes(MiniJson.Serialize(payload))) + ".signature";
        Assert(OAuthSecurity.ValidateGoogleIdToken(token, "expected-nonce", "client-id", now),
            "multi-audience token with matching azp accepted");
    }

    private static void TestConfiguration()
    {
        const string json = "{\"projectId\":\"project\",\"apiKey\":\"file-key\",\"packageName\":\"com.example.app\",\"androidClientId\":\"android-client\",\"redirectScheme\":\"com.example.client\"}";
        FirebaseAuthConfiguration fromFile = FirebaseAuthConfiguration.Parse(json, null);
        Assert(fromFile.IsConfiguredFor(FirebaseAuthPlatform.Android), "Android config uses required values");
        Assert(!fromFile.IsConfiguredFor(FirebaseAuthPlatform.Desktop), "desktop remains unconfigured without its client");
        AssertEqual("com.example.client:/oauth2redirect", fromFile.AndroidRedirectUri, "Android custom redirect URI");

        FirebaseAuthConfiguration fromEnvironment = FirebaseAuthConfiguration.Parse(json, "environment-key");
        Assert(fromEnvironment.HasApiKey, "environment API key is accepted");
        AssertEqual("environment-key", fromEnvironment.ApiKey, "environment API key takes precedence");
        Assert(!FirebaseAuthConfiguration.IsValidScheme("1bad.scheme"), "scheme with numeric prefix rejected");
        Assert(!FirebaseAuthConfiguration.IsValidScheme("https://example.com"), "full URL is not accepted as a scheme");

        const string desktopJson = "{\"projectId\":\"project\",\"apiKey\":\"file-key\",\"packageName\":\"com.example.app\",\"androidClientId\":\"android-client\",\"redirectScheme\":\"com.example.client\",\"desktopClientId\":\"desktop-client\"}";
        FirebaseAuthConfiguration desktop = FirebaseAuthConfiguration.Parse(desktopJson, null);
        Assert(desktop.IsConfiguredFor(FirebaseAuthPlatform.Desktop), "desktop client enables loopback sign in");
    }

    private static void TestFirebaseResponseFormats()
    {
        const string signInJson = "{\"localId\":\"uid-signin\",\"idToken\":\"id-token\",\"refreshToken\":\"refresh-token\",\"expiresIn\":\"3600\",\"email\":\"player@example.invalid\",\"displayName\":\"Player\"}";
        FirebaseTokenData token;
        Assert(FirebaseTokenData.TryParseSignIn(signInJson, out token), "signInWithIdp camelCase response parsed");
        AssertEqual("uid-signin", token.Uid, "signInWithIdp localId mapped");
        AssertEqual("id-token", token.IdToken, "signInWithIdp idToken mapped");
        AssertEqual("refresh-token", token.RefreshToken, "signInWithIdp refreshToken mapped");
        AssertEqual(3600, token.ExpiresInSeconds, "signInWithIdp expiresIn mapped");
        AssertEqual("Player", token.DisplayName, "signInWithIdp display name mapped");
        Assert(!FirebaseTokenData.TryParseRefresh(signInJson, out token), "refresh parser rejects sign-in field names");

        const string refreshJson = "{\"user_id\":\"uid-refresh\",\"id_token\":\"refreshed-id-token\",\"refresh_token\":\"rotated-refresh-token\",\"expires_in\":\"3600\",\"token_type\":\"Bearer\",\"project_id\":\"project\"}";
        Assert(FirebaseTokenData.TryParseRefresh(refreshJson, out token), "securetoken snake_case response parsed");
        AssertEqual("uid-refresh", token.Uid, "securetoken user_id mapped");
        AssertEqual("refreshed-id-token", token.IdToken, "securetoken id_token mapped");
        AssertEqual("rotated-refresh-token", token.RefreshToken, "securetoken refresh_token mapped");
        AssertEqual(3600, token.ExpiresInSeconds, "securetoken expires_in mapped");
        Assert(!FirebaseTokenData.TryParseSignIn(refreshJson, out token), "sign-in parser rejects refresh field names");
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        Assert(EqualityComparer<T>.Default.Equals(expected, actual), message + " (expected=" + expected + ", actual=" + actual + ")");
    }

    public static int Main()
    {
        TestPkce();
        TestCallbackValidation();
        TestIdTokenClaims();
        TestConfiguration();
        TestFirebaseResponseFormats();
        Console.WriteLine("Auth protocol tests: " + _assertions + " assertions passed.");
        return 0;
    }
}

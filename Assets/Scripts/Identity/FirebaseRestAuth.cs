using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace IslandAirport
{
    public sealed class FirebaseRestAuth : IAuthService, IDisposable
    {
        private enum RequestKind
        {
            None,
            GoogleToken,
            FirebaseSignIn,
            FirebaseRestore,
            FirebaseRefresh
        }

        private readonly Queue<string> _events = new Queue<string>();
        private readonly FirebaseAuthPlatform _platform;
        private readonly bool _isConfigured;
        private readonly string _availabilityReason;
        private UnityWebRequest _request;
        private RequestKind _requestKind;
        private LoopbackOAuthServer _loopbackServer;
        private string _redirectUri = string.Empty;
        private string _state = string.Empty;
        private string _nonce = string.Empty;
        private string _verifier = string.Empty;
        private string _pendingRefreshToken = string.Empty;
        private DateTime _operationDeadlineUtc;
        private DateTime _expiresAtUtc = DateTime.MaxValue;
        private DateTime _nextRefreshAttemptUtc = DateTime.MinValue;
        private bool _isRestore;
        private bool _isAutomaticRefresh;
        private bool _callbackAccepted;
        private bool _disposed;

        public FirebaseAuthConfiguration Configuration { get; private set; }
        public FirebaseAuthPlatform Platform { get { return _platform; } }
        public bool IsConfigured { get { return _isConfigured; } }
        public string AvailabilityReason { get { return _availabilityReason; } }
        public float BrowserTimeoutSeconds = 180f;
        public float NetworkTimeoutSeconds = 45f;
        public AuthUser User { get; private set; }
        public bool Busy { get; private set; }
        public event Action<AuthUser> TokenRefreshed;

        public FirebaseRestAuth()
            : this(LoadConfiguration(), DetectPlatform())
        {
        }

        public FirebaseRestAuth(FirebaseAuthConfiguration configuration, FirebaseAuthPlatform platform)
        {
            Configuration = configuration;
            _platform = platform;
            bool configurationReady = configuration != null && configuration.IsConfiguredFor(platform);
            bool packageMatches = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (platform == FirebaseAuthPlatform.Android && configuration != null)
            {
                packageMatches = string.Equals(configuration.PackageName, Application.identifier,
                    StringComparison.Ordinal);
            }
#endif
            _isConfigured = configurationReady && packageMatches;
            _availabilityReason = _isConfigured ? "configured" :
                (platform == FirebaseAuthPlatform.Unsupported ? "unsupported_platform" :
                    (configurationReady && !packageMatches ? "package_mismatch" : "unconfigured"));
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_platform == FirebaseAuthPlatform.Android)
            {
                Application.deepLinkActivated += OnDeepLink;
            }
#endif
        }

        public void SignInGoogle()
        {
            if (_disposed || Busy)
            {
                return;
            }

            if (!_isConfigured)
            {
                _events.Enqueue("fail:unconfigured");
                return;
            }

            _isRestore = false;
            _isAutomaticRefresh = false;
            _callbackAccepted = false;
            _pendingRefreshToken = string.Empty;
            _state = OAuthSecurity.CreateState();
            _nonce = OAuthSecurity.CreateNonce();
            _verifier = OAuthSecurity.CreateVerifier();
            Busy = true;

            try
            {
                _redirectUri = _platform == FirebaseAuthPlatform.Android
                    ? Configuration.AndroidRedirectUri
                    : StartLoopbackRedirect();
                string authorizeUri = OAuthSecurity.BuildAuthorizeUri(
                    Configuration.ClientIdFor(_platform), _redirectUri, _state, _nonce,
                    OAuthSecurity.CreateChallenge(_verifier));
                SetDeadline(BrowserTimeoutSeconds);
                Application.OpenURL(authorizeUri);
            }
            catch (Exception)
            {
                Fail("browser");
            }
        }

        public void Cancel()
        {
            if (!Busy || _disposed)
            {
                return;
            }

            CancelOperation();
            _events.Enqueue("cancelled");
        }

        public void SignOut()
        {
            if (_disposed)
            {
                return;
            }

            if (Busy)
            {
                CancelOperation();
            }

            User = null;
            _expiresAtUtc = DateTime.MaxValue;
            _nextRefreshAttemptUtc = DateTime.MinValue;
            _events.Enqueue("out");
        }

        public void Restore(string refreshToken)
        {
            if (_disposed || Busy)
            {
                return;
            }

            if (!_isConfigured)
            {
                _events.Enqueue("fail:unconfigured");
                return;
            }

            if (string.IsNullOrEmpty(refreshToken))
            {
                _events.Enqueue("fail:token");
                return;
            }

            _isRestore = true;
            _isAutomaticRefresh = false;
            _pendingRefreshToken = refreshToken;
            Busy = true;
            BeginRefreshRequest(RequestKind.FirebaseRestore, refreshToken);
        }

        public bool TryDequeue(out string result)
        {
            if (_events.Count > 0)
            {
                result = _events.Dequeue();
                return true;
            }

            result = null;
            return false;
        }

        public void Pump(float dt)
        {
            if (_disposed)
            {
                return;
            }

            if (Busy)
            {
                if (_request != null)
                {
                    if (_request.isDone)
                    {
                        ProcessRequest();
                    }
                    else if (DateTime.UtcNow >= _operationDeadlineUtc)
                    {
                        Fail("timeout");
                    }
                }
                else if (_loopbackServer != null)
                {
                    PumpLoopback();
                    if (Busy && DateTime.UtcNow >= _operationDeadlineUtc)
                    {
                        Fail("timeout");
                    }
                }
                else if (DateTime.UtcNow >= _operationDeadlineUtc)
                {
                    Fail("timeout");
                }

                return;
            }

            if (User != null && !string.IsNullOrEmpty(User.RefreshToken) &&
                DateTime.UtcNow >= _nextRefreshAttemptUtc &&
                DateTime.UtcNow >= _expiresAtUtc.AddSeconds(-60))
            {
                _isRestore = false;
                _isAutomaticRefresh = true;
                _pendingRefreshToken = User.RefreshToken;
                Busy = true;
                BeginRefreshRequest(RequestKind.FirebaseRefresh, _pendingRefreshToken);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CancelOperation();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_platform == FirebaseAuthPlatform.Android)
            {
                Application.deepLinkActivated -= OnDeepLink;
            }
#endif
            _disposed = true;
        }

        private static FirebaseAuthConfiguration LoadConfiguration()
        {
            string environmentKey = Environment.GetEnvironmentVariable("PALMBAY_FIREBASE_KEY");
            TextAsset asset = Resources.Load<TextAsset>("palmbay-auth");
            if (asset == null)
            {
                return null;
            }

            try
            {
                return FirebaseAuthConfiguration.Parse(asset.text, environmentKey);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ArgumentNullException)
            {
                return null;
            }
        }

        private static FirebaseAuthPlatform DetectPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return FirebaseAuthPlatform.Android;
#elif UNITY_EDITOR || UNITY_STANDALONE_OSX
            return FirebaseAuthPlatform.Desktop;
#else
            return FirebaseAuthPlatform.Unsupported;
#endif
        }

        private string StartLoopbackRedirect()
        {
            _loopbackServer = new LoopbackOAuthServer(_state, HandleOAuthCallback, OnLoopbackRejected);
            return _loopbackServer.Start();
        }

        private void PumpLoopback()
        {
            LoopbackOAuthServer server = _loopbackServer;
            if (server != null && !server.Pump())
            {
                Fail("network");
            }
        }

        private void OnLoopbackRejected(string reason)
        {
            Fail(reason == "state" ? "state" : "oauth");
        }

        private void OnDeepLink(string url)
        {
            if (!Busy || _platform != FirebaseAuthPlatform.Android || _disposed)
            {
                return;
            }

            OAuthCallback callback;
            string failure;
            if (OAuthSecurity.TryParseCallback(url, _redirectUri, _state, out callback, out failure))
            {
                HandleOAuthCallback(callback);
            }
            else if (failure != "redirect")
            {
                Fail(failure == "state" ? "state" : "oauth");
            }
        }

        private void HandleOAuthCallback(OAuthCallback callback)
        {
            if (!Busy || _callbackAccepted)
            {
                return;
            }

            _callbackAccepted = true;
            StopLoopback();
            if (!string.IsNullOrEmpty(callback.Error))
            {
                if (callback.Error == "access_denied")
                {
                    CancelOperation();
                    _events.Enqueue("cancelled");
                }
                else
                {
                    Fail("oauth");
                }

                return;
            }

            Dictionary<string, string> fields = new Dictionary<string, string>();
            fields["client_id"] = Configuration.ClientIdFor(_platform);
            fields["code"] = callback.Code;
            fields["code_verifier"] = _verifier;
            fields["grant_type"] = "authorization_code";
            fields["redirect_uri"] = _redirectUri;
            StartRequest("https://oauth2.googleapis.com/token", EncodeForm(fields), RequestKind.GoogleToken,
                "application/x-www-form-urlencoded");
        }

        private void BeginRefreshRequest(RequestKind kind, string refreshToken)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>();
            fields["grant_type"] = "refresh_token";
            fields["refresh_token"] = refreshToken;
            string url = "https://securetoken.googleapis.com/v1/token?key=" +
                Uri.EscapeDataString(Configuration.ApiKey);
            StartRequest(url, EncodeForm(fields), kind, "application/x-www-form-urlencoded");
        }

        private void StartRequest(string url, string body, RequestKind kind, string contentType)
        {
            StopLoopback();
            UnityWebRequest request = null;
            try
            {
                request = new UnityWebRequest(url, "POST");
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", contentType);
                request.timeout = Math.Max(1, (int)NetworkTimeoutSeconds);
                _request = request;
                _requestKind = kind;
                SetDeadline(NetworkTimeoutSeconds);
                request.SendWebRequest();
            }
            catch (Exception)
            {
                if (request != null)
                {
                    request.Dispose();
                }

                _request = null;
                _requestKind = RequestKind.None;
                Fail("network");
            }
        }

        private void ProcessRequest()
        {
            UnityWebRequest request = _request;
            RequestKind kind = _requestKind;
            _request = null;
            _requestKind = RequestKind.None;
            string response = request.downloadHandler == null ? string.Empty : request.downloadHandler.text;
            UnityWebRequest.Result result = request.result;
            request.Dispose();

            if (result != UnityWebRequest.Result.Success)
            {
                string reason = kind == RequestKind.FirebaseRestore || kind == RequestKind.FirebaseRefresh
                    ? ClassifyRefreshError(response)
                    : "network";
                Fail(reason);
                return;
            }

            Dictionary<string, object> values;
            try
            {
                values = MiniJson.ParseObject(response);
            }
            catch (FormatException)
            {
                Fail("response");
                return;
            }
            catch (ArgumentNullException)
            {
                Fail("response");
                return;
            }

            if (kind == RequestKind.GoogleToken)
            {
                ProcessGoogleToken(values);
            }
            else
            {
                ProcessFirebaseToken(values, kind);
            }
        }

        private void ProcessGoogleToken(IDictionary<string, object> values)
        {
            string googleIdToken = MiniJson.GetString(values, "id_token", string.Empty);
            if (!OAuthSecurity.ValidateGoogleIdToken(googleIdToken, _nonce,
                Configuration.ClientIdFor(_platform), DateTime.UtcNow))
            {
                Fail("provider");
                return;
            }

            Dictionary<string, object> body = new Dictionary<string, object>();
            body["requestUri"] = "http://localhost";
            body["postBody"] = "id_token=" + Uri.EscapeDataString(googleIdToken) + "&providerId=google.com";
            body["returnIdpCredential"] = true;
            body["returnSecureToken"] = true;
            string url = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=" +
                Uri.EscapeDataString(Configuration.ApiKey);
            StartRequest(url, MiniJson.Serialize(body), RequestKind.FirebaseSignIn, "application/json");
        }

        private void ProcessFirebaseToken(IDictionary<string, object> values, RequestKind kind)
        {
            FirebaseTokenData token;
            string json = MiniJson.Serialize(values);
            bool refreshResponse = kind == RequestKind.FirebaseRestore || kind == RequestKind.FirebaseRefresh;
            bool parsed = refreshResponse
                ? FirebaseTokenData.TryParseRefresh(json, out token)
                : FirebaseTokenData.TryParseSignIn(json, out token);
            if (!parsed)
            {
                Fail("response");
                return;
            }

            AuthUser previous = User;
            AuthUser user = new AuthUser();
            user.Uid = token.Uid;
            user.IdToken = token.IdToken;
            user.RefreshToken = token.RefreshToken;
            user.DisplayName = token.DisplayName;
            if (previous != null && string.Equals(previous.Uid, user.Uid, StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(previous.DisplayName))
                {
                    user.DisplayName = previous.DisplayName;
                }

                user.AgeVerified = previous.AgeVerified;
            }

            User = user;
            _expiresAtUtc = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds);
            _nextRefreshAttemptUtc = DateTime.MinValue;
            bool automaticRefresh = kind == RequestKind.FirebaseRefresh;
            Busy = false;
            ClearTransientSecrets();

            if (automaticRefresh)
            {
                Action<AuthUser> handler = TokenRefreshed;
                if (handler != null)
                {
                    handler(User);
                }

                _events.Enqueue("refreshed:" + User.Uid);
            }
            else if (kind == RequestKind.FirebaseRestore)
            {
                _events.Enqueue("restored:" + User.Uid);
            }
            else
            {
                _events.Enqueue("ok:" + User.Uid);
            }
        }

        private void Fail(string reason)
        {
            bool automaticRefresh = _isAutomaticRefresh;
            bool restoring = _isRestore;
            AbortRequest();
            StopLoopback();
            Busy = false;
            ClearTransientSecrets();

            if (automaticRefresh && reason == "network" && DateTime.UtcNow < _expiresAtUtc)
            {
                _nextRefreshAttemptUtc = DateTime.UtcNow.AddSeconds(30);
                return;
            }

            if (automaticRefresh || restoring)
            {
                if (reason == "token" || DateTime.UtcNow >= _expiresAtUtc)
                {
                    User = null;
                    _expiresAtUtc = DateTime.MaxValue;
                }
            }

            _events.Enqueue("fail:" + reason);
        }

        private string ClassifyRefreshError(string response)
        {
            try
            {
                Dictionary<string, object> root = MiniJson.ParseObject(response);
                Dictionary<string, object> error = MiniJson.GetObject(root, "error");
                string message = MiniJson.GetString(error, "message", string.Empty);
                if (message == "INVALID_REFRESH_TOKEN" || message == "TOKEN_EXPIRED" ||
                    message == "USER_DISABLED" || message == "INVALID_GRANT")
                {
                    return "token";
                }

                if (message == "API_KEY_INVALID" || message == "OPERATION_NOT_ALLOWED")
                {
                    return "configuration";
                }
            }
            catch (FormatException)
            {
            }
            catch (ArgumentNullException)
            {
            }

            return "network";
        }

        private void CancelOperation()
        {
            AbortRequest();
            StopLoopback();
            Busy = false;
            ClearTransientSecrets();
        }

        private void AbortRequest()
        {
            if (_request != null)
            {
                try
                {
                    _request.Abort();
                }
                catch (Exception)
                {
                }

                _request.Dispose();
                _request = null;
            }

            _requestKind = RequestKind.None;
        }

        private void StopLoopback()
        {
            if (_loopbackServer != null)
            {
                _loopbackServer.Dispose();
                _loopbackServer = null;
            }
        }

        private void ClearTransientSecrets()
        {
            _state = string.Empty;
            _nonce = string.Empty;
            _verifier = string.Empty;
            _pendingRefreshToken = string.Empty;
            _redirectUri = string.Empty;
            _isRestore = false;
            _isAutomaticRefresh = false;
            _callbackAccepted = false;
        }

        private void SetDeadline(float seconds)
        {
            double safeSeconds = float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 1f
                ? 1d
                : Math.Min(seconds, 3600f);
            _operationDeadlineUtc = DateTime.UtcNow.AddSeconds(safeSeconds);
        }

        private static string EncodeForm(IDictionary<string, string> fields)
        {
            StringBuilder body = new StringBuilder();
            bool first = true;
            foreach (KeyValuePair<string, string> field in fields)
            {
                if (!first)
                {
                    body.Append('&');
                }

                first = false;
                body.Append(Uri.EscapeDataString(field.Key));
                body.Append('=');
                body.Append(Uri.EscapeDataString(field.Value ?? string.Empty));
            }

            return body.ToString();
        }
    }
}

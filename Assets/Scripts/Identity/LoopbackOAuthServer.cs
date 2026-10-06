using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IslandAirport
{
    public sealed class LoopbackOAuthServer : IDisposable
    {
        private const int MaxRequestBytes = 16384;
        private const string AcceptedMessage = "Authentication complete. You can return to the game.";
        private const string RejectedMessage = "Authentication request rejected. You can return to the game.";

        private readonly string _expectedState;
        private readonly Action<OAuthCallback> _onAccepted;
        private readonly Action<string> _onRejected;
        private readonly MemoryStream _requestBuffer = new MemoryStream(4096);
        private TcpListener _listener;
        private TcpClient _client;
        private byte[] _response;
        private int _responseOffset;
        private Action _afterResponse;
        private bool _disposed;

        public string RedirectUri { get; private set; }

        public LoopbackOAuthServer(string expectedState, Action<OAuthCallback> onAccepted,
            Action<string> onRejected)
        {
            _expectedState = expectedState ?? string.Empty;
            _onAccepted = onAccepted;
            _onRejected = onRejected;
        }

        public string Start()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("LoopbackOAuthServer");
            }

            if (_listener != null)
            {
                return RedirectUri;
            }

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1);
            IPEndPoint endpoint = (IPEndPoint)_listener.LocalEndpoint;
            RedirectUri = "http://127.0.0.1:" + endpoint.Port.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + "/oauth2callback";
            return RedirectUri;
        }

        public bool Pump()
        {
            if (_disposed || _listener == null)
            {
                return false;
            }

            if (!TryAcceptClient())
            {
                return false;
            }

            if (_client == null)
            {
                return true;
            }

            if (_response == null)
            {
                ReadRequest();
            }

            if (_client != null && _response != null)
            {
                SendResponse();
            }

            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _requestBuffer.Dispose();
            _disposed = true;
        }

        private bool TryAcceptClient()
        {
            if (_client != null)
            {
                return true;
            }

            try
            {
                if (_listener.Pending())
                {
                    _client = _listener.AcceptTcpClient();
                    _client.Client.Blocking = false;
                }

                return true;
            }
            catch (SocketException exception)
            {
                return IsWouldBlock(exception);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private void ReadRequest()
        {
            Socket socket = _client.Client;
            try
            {
                if (!socket.Poll(0, SelectMode.SelectRead))
                {
                    return;
                }

                int available = socket.Available;
                if (available == 0)
                {
                    ResetClient();
                    return;
                }

                byte[] chunk = new byte[Math.Min(available, 8192)];
                int read = socket.Receive(chunk, 0, chunk.Length, SocketFlags.None);
                if (read <= 0)
                {
                    ResetClient();
                    return;
                }

                if (_requestBuffer.Length + read > MaxRequestBytes)
                {
                    QueueResponse(RejectedMessage, null);
                    return;
                }

                _requestBuffer.Write(chunk, 0, read);
                byte[] requestBytes = _requestBuffer.ToArray();
                int headerEnd = FindHeaderEnd(requestBytes);
                if (headerEnd >= 0)
                {
                    LoopbackReply reply = HandleRequest(Encoding.ASCII.GetString(requestBytes, 0,
                        headerEnd + 4));
                    QueueResponse(reply.Body, reply.AfterResponse);
                }
            }
            catch (SocketException exception)
            {
                if (!IsWouldBlock(exception))
                {
                    ResetClient();
                }
            }
            catch (ObjectDisposedException)
            {
                ResetClient();
            }
        }

        private void SendResponse()
        {
            Socket socket = _client.Client;
            try
            {
                int sent = socket.Send(_response, _responseOffset, _response.Length - _responseOffset,
                    SocketFlags.None);
                if (sent <= 0)
                {
                    ResetClient();
                    return;
                }

                _responseOffset += sent;
                if (_responseOffset == _response.Length)
                {
                    Action afterResponse = _afterResponse;
                    ResetClient();
                    if (afterResponse != null)
                    {
                        afterResponse();
                    }
                }
            }
            catch (SocketException exception)
            {
                if (!IsWouldBlock(exception))
                {
                    ResetClient();
                }
            }
            catch (ObjectDisposedException)
            {
                ResetClient();
            }
        }

        private LoopbackReply HandleRequest(string request)
        {
            string[] lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] firstLine = lines.Length > 0 ? lines[0].Split(' ') : new string[0];
            string host = string.Empty;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                {
                    host = lines[i].Substring(5).Trim();
                    break;
                }
            }

            Uri redirect;
            if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out redirect) || firstLine.Length != 3 ||
                firstLine[0] != "GET" || !string.Equals(host, redirect.Authority,
                    StringComparison.OrdinalIgnoreCase) ||
                !firstLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            {
                return new LoopbackReply(RejectedMessage, null);
            }

            string requestTarget = firstLine[1];
            int queryIndex = requestTarget.IndexOf('?');
            string path = queryIndex < 0 ? requestTarget : requestTarget.Substring(0, queryIndex);
            if (!string.Equals(path, redirect.AbsolutePath, StringComparison.Ordinal) || queryIndex < 0)
            {
                return new LoopbackReply(RejectedMessage, null);
            }

            string callbackUri = RedirectUri + requestTarget.Substring(queryIndex);
            OAuthCallback callback;
            string failure;
            bool accepted = OAuthSecurity.TryParseCallback(callbackUri, RedirectUri, _expectedState,
                out callback, out failure);
            if (accepted)
            {
                return new LoopbackReply(AcceptedMessage, delegate
                {
                    if (_onAccepted != null)
                    {
                        _onAccepted(callback);
                    }
                });
            }

            Action afterResponse = null;
            if (failure != "redirect" && _onRejected != null)
            {
                string reason = failure == "state" ? "state" : "oauth";
                afterResponse = delegate { _onRejected(reason); };
            }

            return new LoopbackReply(RejectedMessage, afterResponse);
        }

        private void QueueResponse(string bodyText, Action afterResponse)
        {
            byte[] body = Encoding.UTF8.GetBytes(bodyText ?? string.Empty);
            string header = "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\n" +
                "Content-Length: " + body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "\r\nConnection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            _response = new byte[headerBytes.Length + body.Length];
            Buffer.BlockCopy(headerBytes, 0, _response, 0, headerBytes.Length);
            Buffer.BlockCopy(body, 0, _response, headerBytes.Length, body.Length);
            _responseOffset = 0;
            _afterResponse = afterResponse;
        }

        private void ResetClient()
        {
            if (_client != null)
            {
                _client.Close();
                _client = null;
            }

            _requestBuffer.SetLength(0);
            _response = null;
            _responseOffset = 0;
            _afterResponse = null;
        }

        private static int FindHeaderEnd(byte[] bytes)
        {
            for (int i = 0; i <= bytes.Length - 4; i++)
            {
                if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsWouldBlock(SocketException exception)
        {
            return exception.SocketErrorCode == SocketError.WouldBlock ||
                exception.SocketErrorCode == SocketError.IOPending;
        }

        private void Stop()
        {
            ResetClient();
            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch (SocketException)
                {
                }

                _listener = null;
            }
        }

        private sealed class LoopbackReply
        {
            public readonly string Body;
            public readonly Action AfterResponse;

            public LoopbackReply(string body, Action afterResponse)
            {
                Body = body;
                AfterResponse = afterResponse;
            }
        }
    }
}

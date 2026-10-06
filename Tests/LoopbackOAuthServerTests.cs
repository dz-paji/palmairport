using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using IslandAirport;

public static class LoopbackOAuthServerTests
{
    private static int _passed;
    private static int _failed;
    private static int _assertions;

    public static void Main()
    {
        Run("local OAuth callback response precedes success", TestAcceptedCallback);
        Run("local OAuth callback rejects an invalid state", TestRejectedState);
        Console.WriteLine("Loopback OAuth tests: {0} passed, {1} failed, {2} assertions.",
            _passed, _failed, _assertions);
        if (_failed != 0)
        {
            Environment.Exit(1);
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS  " + name);
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine("FAIL  " + name + ": " + exception.Message);
            Console.WriteLine(exception.ToString());
        }
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void TestAcceptedCallback()
    {
        bool handled = false;
        OAuthCallback received = null;
        using (LoopbackOAuthServer server = new LoopbackOAuthServer("expected-state",
            delegate(OAuthCallback callback)
            {
                received = callback;
                handled = true;
            }, delegate(string reason)
            {
                throw new Exception("Unexpected rejection: " + reason);
            }))
        {
            string redirectUri = server.Start();
            using (TcpClient client = SendCallback(redirectUri, "unit-code", "expected-state"))
            {
                PumpUntil(server, delegate { return handled; });
                string response = ReadResponse(client.GetStream());
                Assert(response.Contains("Authentication complete."), "success response is sent");
                Assert(received != null, "callback is delivered");
                Assert(string.Equals("unit-code", received.Code, StringComparison.Ordinal), "code is parsed");
            }
        }
    }

    private static void TestRejectedState()
    {
        bool rejected = false;
        string reason = string.Empty;
        using (LoopbackOAuthServer server = new LoopbackOAuthServer("expected-state",
            delegate(OAuthCallback callback)
            {
                throw new Exception("Unexpected accepted callback");
            }, delegate(string failure)
            {
                rejected = true;
                reason = failure;
            }))
        {
            string redirectUri = server.Start();
            using (TcpClient client = SendCallback(redirectUri, "unit-code", "wrong-state"))
            {
                PumpUntil(server, delegate { return rejected; });
                string response = ReadResponse(client.GetStream());
                Assert(response.Contains("Authentication request rejected."), "rejection response is sent");
                Assert(string.Equals("state", reason, StringComparison.Ordinal), "invalid state is reported");
            }
        }
    }

    private static TcpClient SendCallback(string redirectUri, string code, string state)
    {
        Uri redirect = new Uri(redirectUri);
        TcpClient client = new TcpClient();
        client.Connect(IPAddress.Loopback, redirect.Port);
        string target = redirect.AbsolutePath + "?code=" + Uri.EscapeDataString(code) + "&state=" +
            Uri.EscapeDataString(state);
        string request = "GET " + target + " HTTP/1.1\r\nHost: " + redirect.Authority +
            "\r\nConnection: close\r\n\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(request);
        client.GetStream().Write(bytes, 0, bytes.Length);
        return client;
    }

    private static void PumpUntil(LoopbackOAuthServer server, Func<bool> completed)
    {
        for (int attempt = 0; attempt < 200 && !completed(); attempt++)
        {
            Assert(server.Pump(), "loopback server remains available");
            Thread.Sleep(1);
        }

        Assert(completed(), "loopback callback completes");
    }

    private static string ReadResponse(Stream stream)
    {
        MemoryStream response = new MemoryStream();
        byte[] buffer = new byte[1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            response.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(response.ToArray());
    }
}

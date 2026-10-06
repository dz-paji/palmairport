using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IslandAirport
{
    /// <summary>真实 UDP 套接实现：非阻塞 + Poll(0)，无线程。</summary>
    public class UdpSocketReal : IUdpSocket
    {
        private readonly UdpClient _client;
        private bool _closed;

        /// <summary>绑定本地端口（0=系统分配）。enableBroadcast 用于 beacon。</summary>
        public UdpSocketReal(int localPort, bool enableBroadcast)
        {
            _client = new UdpClient(localPort);
            _client.Client.Blocking = false;
            if (enableBroadcast)
            {
                _client.EnableBroadcast = true;
            }
        }

        public int LocalPort
        {
            get
            {
                if (_closed)
                {
                    return 0;
                }

                IPEndPoint endPoint = _client.Client.LocalEndPoint as IPEndPoint;
                return endPoint != null ? endPoint.Port : 0;
            }
        }

        public void Send(string address, int port, string payload)
        {
            if (_closed || payload == null)
            {
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            try
            {
                _client.Send(bytes, bytes.Length, address, port);
            }
            catch (SocketException)
            {
                // 目标不可达等瞬时错误即丢即弃。
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public bool TryReceive(out UdpDatagram datagram)
        {
            datagram = new UdpDatagram();
            if (_closed)
            {
                return false;
            }

            try
            {
                if (!_client.Client.Poll(0, SelectMode.SelectRead) || _client.Available == 0)
                {
                    return false;
                }

                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = _client.Receive(ref remote);
                datagram.Address = remote.Address.ToString();
                datagram.Port = remote.Port;
                datagram.Payload = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            try
            {
                _client.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>
    /// 定向 UDP 会话：固定远端端点，收尽对端包后解析为协议消息。
    /// host 端按 join 来源地址建会话；client 端指向 host 地址。
    /// 全部计时与驱动走外部 Pump 节奏，无线程。
    /// </summary>
    public class UdpSession : ISession
    {
        private readonly IUdpSocket _socket;
        private readonly string _remoteAddress;
        private readonly int _remotePort;
        private bool _ownsSocket;

        /// <summary>在既有套接上创建会话（不拥有套接）。</summary>
        public UdpSession(IUdpSocket socket, string remoteAddress, int remotePort)
            : this(socket, remoteAddress, remotePort, false)
        {
        }

        /// <summary>ownsSocket=true 时 Close 会连带关闭套接。</summary>
        public UdpSession(IUdpSocket socket, string remoteAddress, int remotePort, bool ownsSocket)
        {
            _socket = socket ?? throw new ArgumentNullException("socket");
            _remoteAddress = remoteAddress ?? string.Empty;
            _remotePort = remotePort;
            _ownsSocket = ownsSocket;
            Alive = true;
        }

        public bool Alive { get; private set; }

        public string RemoteAddress
        {
            get { return _remoteAddress; }
        }

        public IUdpSocket Socket
        {
            get { return _socket; }
        }

        public bool TryReceive(out NetMessage message)
        {
            message = null;
            if (!Alive)
            {
                return false;
            }

            UdpDatagram datagram;
            while (_socket.TryReceive(out datagram))
            {
                // 只收固定远端端点的包，其余即丢即弃。
                if (!string.Equals(datagram.Address, _remoteAddress, StringComparison.Ordinal) ||
                    datagram.Port != _remotePort)
                {
                    continue;
                }

                NetMessage parsed = NetProtocol.Parse(datagram.Payload);
                if (parsed == null)
                {
                    continue;
                }

                message = parsed;
                return true;
            }

            return false;
        }

        public void SendTo(string payload)
        {
            if (!Alive)
            {
                return;
            }

            _socket.Send(_remoteAddress, _remotePort, payload);
        }

        public void Close()
        {
            if (!Alive)
            {
                return;
            }

            Alive = false;
            if (_ownsSocket)
            {
                _socket.Close();
            }
        }
    }
}

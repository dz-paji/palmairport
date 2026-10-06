using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>
    /// 内存 UDP 集线器：同进程内互联多个 LoopbackSocket，确定性注入
    /// 延迟/抖动/丢包/重复/乱序/断线。种子确定，全部推进走 Pump(dt)。
    /// 控制面重试/幂等逻辑的故障注入抓手。
    /// </summary>
    public class LoopbackHub
    {
        private class Pending
        {
            public string FromAddress;
            public int FromPort;
            public LoopbackSocket To;
            public string Payload;
            public float DeliverAt;
        }

        private readonly List<LoopbackSocket> _sockets = new List<LoopbackSocket>();
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly HashSet<LoopbackSocket> _cut = new HashSet<LoopbackSocket>();
        private readonly Random _random;

        public LoopbackHub(int seed)
        {
            Seed = seed;
            _random = new Random(seed);
        }

        /// <summary>确定性种子。</summary>
        public int Seed { get; private set; }

        /// <summary>基础单向延迟（秒）。</summary>
        public float Latency;

        /// <summary>抖动上限（秒），实际延迟 = Latency + [0,Jitter)。</summary>
        public float Jitter;

        /// <summary>丢包率 [0,1]，丢包即弃。</summary>
        public float LossRate;

        /// <summary>重复包概率 [0,1]，命中时多投递一份。</summary>
        public float DuplicateRate;

        /// <summary>开启乱序：新包插入待投递队列随机位置而非队尾。</summary>
        public bool Reorder;

        /// <summary>集线器本地时钟（Pump 累计）。</summary>
        public float Now { get; private set; }

        /// <summary>创建一个绑定到集线器的内存套接。</summary>
        public LoopbackSocket CreateSocket(string address, int port)
        {
            LoopbackSocket socket = new LoopbackSocket(this, address, port);
            _sockets.Add(socket);
            return socket;
        }

        /// <summary>切断某端点的双向流量（断线注入），直到 Uncut。</summary>
        public void Cut(LoopbackSocket endpoint)
        {
            if (endpoint != null)
            {
                _cut.Add(endpoint);
            }
        }

        /// <summary>恢复某端点流量。</summary>
        public void Uncut(LoopbackSocket endpoint)
        {
            if (endpoint != null)
            {
                _cut.Remove(endpoint);
            }
        }

        /// <summary>端点当前是否处于切断状态。</summary>
        public bool IsCut(LoopbackSocket endpoint)
        {
            return endpoint != null && _cut.Contains(endpoint);
        }

        internal void Unregister(LoopbackSocket socket)
        {
            _sockets.Remove(socket);
            _cut.Remove(socket);
        }

        internal void Route(LoopbackSocket from, string address, int port, string payload)
        {
            if (IsCut(from))
            {
                return;
            }

            if (address == "255.255.255.255")
            {
                // 广播：投递给所有同端口套接（含回环给自身的语义与真网一致地排除自身）。
                for (int i = 0; i < _sockets.Count; i++)
                {
                    LoopbackSocket target = _sockets[i];
                    if (target != from && target.Port == port)
                    {
                        Enqueue(from, target, payload);
                    }
                }

                return;
            }

            for (int i = 0; i < _sockets.Count; i++)
            {
                LoopbackSocket target = _sockets[i];
                if (target.Port == port && string.Equals(target.Address, address, StringComparison.Ordinal))
                {
                    Enqueue(from, target, payload);
                }
            }
        }

        private void Enqueue(LoopbackSocket from, LoopbackSocket to, string payload)
        {
            if (IsCut(to))
            {
                return;
            }

            if (LossRate > 0f && _random.NextDouble() < LossRate)
            {
                return;
            }

            float delay = Latency + (Jitter > 0f ? (float)_random.NextDouble() * Jitter : 0f);
            Insert(from, to, payload, Now + delay);

            if (DuplicateRate > 0f && _random.NextDouble() < DuplicateRate)
            {
                Insert(from, to, payload, Now + delay);
            }
        }

        private void Insert(LoopbackSocket from, LoopbackSocket to, string payload, float deliverAt)
        {
            Pending packet = new Pending();
            packet.FromAddress = from.Address;
            packet.FromPort = from.Port;
            packet.To = to;
            packet.Payload = payload;
            packet.DeliverAt = deliverAt;

            if (Reorder && _pending.Count > 0)
            {
                _pending.Insert(_random.Next(_pending.Count + 1), packet);
            }
            else
            {
                _pending.Add(packet);
            }
        }

        /// <summary>推进集线器时钟并投递到期包。</summary>
        public void Pump(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                return;
            }

            Now += dt;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending packet = _pending[i];
                if (packet.DeliverAt > Now)
                {
                    continue;
                }

                _pending.RemoveAt(i);
                if (IsCut(packet.To) || packet.To.Closed)
                {
                    continue;
                }

                packet.To.Accept(packet.FromAddress, packet.FromPort, packet.Payload);
            }
        }
    }

    /// <summary>LoopbackHub 上的内存套接，实现 IUdpSocket 供会话层复用。</summary>
    public class LoopbackSocket : IUdpSocket
    {
        private readonly LoopbackHub _hub;
        private readonly Queue<UdpDatagram> _inbox = new Queue<UdpDatagram>();

        internal LoopbackSocket(LoopbackHub hub, string address, int port)
        {
            _hub = hub;
            Address = address ?? string.Empty;
            Port = port;
        }

        public string Address { get; private set; }

        public int Port { get; private set; }

        public bool Closed { get; private set; }

        public int LocalPort
        {
            get { return Closed ? 0 : Port; }
        }

        public void Send(string address, int port, string payload)
        {
            if (Closed || payload == null)
            {
                return;
            }

            _hub.Route(this, address, port, payload);
        }

        internal void Accept(string fromAddress, int fromPort, string payload)
        {
            UdpDatagram datagram = new UdpDatagram();
            datagram.Address = fromAddress;
            datagram.Port = fromPort;
            datagram.Payload = payload;
            _inbox.Enqueue(datagram);
        }

        public bool TryReceive(out UdpDatagram datagram)
        {
            if (Closed || _inbox.Count == 0)
            {
                datagram = new UdpDatagram();
                return false;
            }

            datagram = _inbox.Dequeue();
            return true;
        }

        public void Close()
        {
            if (Closed)
            {
                return;
            }

            Closed = true;
            _inbox.Clear();
            _hub.Unregister(this);
        }
    }
}

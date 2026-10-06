using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>
    /// UDP 广播 beacon 开播端：1s 周期广播房间信息。
    /// 目标地址可注入（生产 255.255.255.255，测试 127.0.0.1）。全部计时走 Pump(dt)。
    /// </summary>
    public class BeaconAnnouncer
    {
        private readonly IUdpSocket _socket;
        private float _sinceSend = float.MaxValue; // 首 Pump 立即开播
        private bool _announcing;

        /// <summary>socket：发送用套接（可与会话共用或独立）。</summary>
        public BeaconAnnouncer(IUdpSocket socket)
        {
            if (socket == null)
            {
                throw new ArgumentNullException("socket");
            }

            _socket = socket;
        }

        /// <summary>广播目标地址。</summary>
        public string TargetAddress = "255.255.255.255";

        /// <summary>广播周期（秒）。</summary>
        public float Interval = 1f;

        /// <summary>当前广播的房间信息（外部按房间状态更新 State/Taken）。</summary>
        public RoomInfo Room { get; private set; }

        public bool Announcing
        {
            get { return _announcing; }
        }

        /// <summary>累计发送次数（批测断言用）。</summary>
        public int SentCount { get; private set; }

        /// <summary>开始广播指定房间。重复调用幂等更新内容。</summary>
        public void Start(RoomInfo room)
        {
            Room = room;
            _announcing = room != null;
            _sinceSend = float.MaxValue;
        }

        /// <summary>停止广播。</summary>
        public void Stop()
        {
            _announcing = false;
            Room = null;
        }

        /// <summary>推进广播计时，到点发一包。</summary>
        public void Pump(float dt)
        {
            if (!_announcing || Room == null)
            {
                return;
            }

            if (dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt))
            {
                _sinceSend += dt;
            }

            if (_sinceSend < Interval)
            {
                return;
            }

            _sinceSend = 0f;
            _socket.Send(TargetAddress, NetProtocol.BeaconPort, NetProtocol.BuildBeacon(Room));
            SentCount++;
        }
    }

    /// <summary>
    /// UDP 广播 beacon 收听端：收包入发现表，5s 未见剔除。全部计时走 Pump(dt)。
    /// </summary>
    public class BeaconListener
    {
        /// <summary>挂接点：Android 由 Unity 层注入 MulticastLock 获取。</summary>
        public static Action BeforeListen;

        private readonly IUdpSocket _socket;
        private readonly Dictionary<string, RoomInfo> _rooms = new Dictionary<string, RoomInfo>();

        public BeaconListener(IUdpSocket socket)
        {
            if (socket == null)
            {
                throw new ArgumentNullException("socket");
            }

            Action hook = BeforeListen;
            if (hook != null)
            {
                hook();
            }

            _socket = socket;
        }

        /// <summary>过期时间（秒）。</summary>
        public float ExpireAfter = 5f;

        /// <summary>本地时钟（Pump 累计）。</summary>
        public float Now { get; private set; }

        /// <summary>发现表当前房间数。</summary>
        public int Count
        {
            get { return _rooms.Count; }
        }

        /// <summary>发现表快照（按地址排序，稳定输出）。</summary>
        public List<RoomInfo> Rooms()
        {
            List<RoomInfo> list = new List<RoomInfo>(_rooms.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.Address, b.Address));
            return list;
        }

        /// <summary>推进时钟：收尽 beacon 包入表，剔除过期。</summary>
        public void Pump(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                return;
            }

            Now += dt;

            UdpDatagram datagram;
            while (_socket.TryReceive(out datagram))
            {
                NetMessage message = NetProtocol.Parse(datagram.Payload);
                if (message == null || message.Type != "beacon")
                {
                    continue;
                }

                RoomInfo room;
                if (!_rooms.TryGetValue(datagram.Address, out room))
                {
                    room = new RoomInfo();
                    room.Address = datagram.Address;
                    _rooms[datagram.Address] = room;
                }

                room.Name = message.GetString("name", string.Empty);
                room.HostName = message.GetString("host", string.Empty);
                room.State = message.GetString("state", "lobby");
                room.Port = message.GetInt("port", NetProtocol.SessionPort);
                room.Seats = message.GetInt("seats", NetProtocol.SeatCount);
                room.Taken = message.GetInt("taken", 1);
                room.LastSeen = Now;
            }

            _expired.Clear();
            foreach (KeyValuePair<string, RoomInfo> pair in _rooms)
            {
                if (Now - pair.Value.LastSeen > ExpireAfter)
                {
                    _expired.Add(pair.Key);
                }
            }

            for (int i = 0; i < _expired.Count; i++)
            {
                _rooms.Remove(_expired[i]);
            }
        }

        private readonly List<string> _expired = new List<string>();
    }
}

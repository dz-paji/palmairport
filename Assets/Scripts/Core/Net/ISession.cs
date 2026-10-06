namespace IslandAirport
{
    /// <summary>带地址对的数据报：一条消息 + 来源/目标地址。</summary>
    public struct UdpDatagram
    {
        public string Address;
        public int Port;
        public string Payload;
    }

    /// <summary>
    /// 底层 UDP 套接抽象（真实 UdpClient 非阻塞实现 + 内存 fake 共用）。
    /// 数据报语义：一次 Send = 一个包，天然分帧，无编帧层。
    /// </summary>
    public interface IUdpSocket
    {
        /// <summary>本地绑定端口；0 表示未绑定。</summary>
        int LocalPort { get; }

        /// <summary>发送一条数据报到指定地址。</summary>
        void Send(string address, int port, string payload);

        /// <summary>非阻塞尝试收一条数据报；无数据返回 false。</summary>
        bool TryReceive(out UdpDatagram datagram);

        /// <summary>关闭并释放底层资源，可重复调用。</summary>
        void Close();
    }

    /// <summary>
    /// 定向会话：固定远端端点的消息通道。
    /// 游戏消息（in/snap/ping/pong）去重去旧、即丢即弃由上层负责。
    /// </summary>
    public interface ISession
    {
        /// <summary>会话是否仍可用。</summary>
        bool Alive { get; }

        /// <summary>远端地址（IP 字符串）。</summary>
        string RemoteAddress { get; }

        /// <summary>非阻塞尝试收一条消息；无数据返回 false。</summary>
        bool TryReceive(out NetMessage message);

        /// <summary>发送一条消息（已序列化的协议文本）。</summary>
        void SendTo(string payload);

        /// <summary>关闭会话，可重复调用。</summary>
        void Close();
    }
}

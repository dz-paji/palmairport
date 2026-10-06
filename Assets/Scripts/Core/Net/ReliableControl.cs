using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>
    /// 控制消息可靠性层（join/accept/reject/roster/start/leave/close）。
    /// 发送侧：待发控制消息表，retry 计时重发、ackKey 匹配 ack、超时报废；
    /// 也支持无 ack 的定时连发（close/leave 300ms 内连发 3 次）。
    /// 接收侧：按控制 seq 去重，供上层实现幂等处理器。
    /// 与传输/RoomManager 解耦：构造注入发送委托，console 可单测。全部计时走 Pump(dt)。
    /// </summary>
    public class ReliableControl
    {
        private class Entry
        {
            public string AckKey;
            public string Payload;
            public float RetryInterval;
            public float Timeout;
            public float SinceSend;
            public float Age;
            public int BurstsLeft;
        }

        private readonly List<Entry> _pending = new List<Entry>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Action<string> _send;

        /// <summary>send：实际发出一条协议文本的委托。</summary>
        public ReliableControl(Action<string> send)
        {
            if (send == null)
            {
                throw new ArgumentNullException("send");
            }

            _send = send;
        }

        /// <summary>当前待发消息数（含 burst）。</summary>
        public int PendingCount
        {
            get { return _pending.Count; }
        }

        /// <summary>累计发送次数（含重发，批测断言用）。</summary>
        public int SendCount { get; private set; }

        /// <summary>
        /// 登记一条需要 ack 的控制消息：立即首发，之后每 retryInterval 重发，
        /// 直到 Ack(ackKey) 或 timeout 报废。timeout&lt;=0 用默认 JoinTimeout。
        /// </summary>
        public void Track(string ackKey, string payload, float retryInterval, float timeout)
        {
            if (string.IsNullOrEmpty(ackKey) || payload == null)
            {
                return;
            }

            // 同 ackKey 已存在时幂等替换，避免重复登记。
            Ack(ackKey);
            Entry entry = new Entry();
            entry.AckKey = ackKey;
            entry.Payload = payload;
            entry.RetryInterval = retryInterval > 0f ? retryInterval : NetProtocol.ControlRetry;
            entry.Timeout = timeout > 0f ? timeout : NetProtocol.JoinTimeout;
            entry.SinceSend = 0f;
            entry.Age = 0f;
            entry.BurstsLeft = -1;
            _pending.Add(entry);
            _send(payload);
            SendCount++;
        }

        /// <summary>
        /// 登记一条无 ack 的定时连发消息：立即首发，之后每 interval 发一次，共 count 次。
        /// </summary>
        public void TrackBurst(string payload, int count, float interval)
        {
            if (payload == null || count <= 0)
            {
                return;
            }

            Entry entry = new Entry();
            entry.AckKey = null;
            entry.Payload = payload;
            entry.RetryInterval = interval > 0f ? interval : 0.15f;
            entry.Timeout = 0f;
            entry.SinceSend = 0f;
            entry.Age = 0f;
            entry.BurstsLeft = count - 1;
            _pending.Add(entry);
            _send(payload);
            SendCount++;
        }

        /// <summary>匹配 ack：移除该 ackKey 的待发消息。返回是否命中。</summary>
        public bool Ack(string ackKey)
        {
            if (ackKey == null)
            {
                return false;
            }

            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].AckKey == ackKey)
                {
                    _pending.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>按前缀批量 ack（例如 start:* 被首帧 in 确认）。返回命中数。</summary>
        public int AckPrefix(string ackKeyPrefix)
        {
            int removed = 0;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                string key = _pending[i].AckKey;
                if (key != null && key.StartsWith(ackKeyPrefix, StringComparison.Ordinal))
                {
                    _pending.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>推进重发计时：到点重发；超时（无 burst 且 Age 超过 Timeout）报废。</summary>
        public void Pump(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                return;
            }

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Entry entry = _pending[i];
                entry.Age += dt;
                entry.SinceSend += dt;

                if (entry.BurstsLeft < 0 && entry.Timeout > 0f && entry.Age >= entry.Timeout)
                {
                    _pending.RemoveAt(i);
                    continue;
                }

                if (entry.SinceSend < entry.RetryInterval)
                {
                    continue;
                }

                entry.SinceSend = 0f;
                _send(entry.Payload);
                SendCount++;

                if (entry.BurstsLeft > 0)
                {
                    entry.BurstsLeft--;
                    if (entry.BurstsLeft == 0)
                    {
                        _pending.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>接收幂等：key 首次出现返回 true 并记录；重复返回 false。</summary>
        public bool FirstSeen(string key)
        {
            if (key == null)
            {
                return true;
            }

            return _seen.Add(key);
        }

        /// <summary>清空去重记录（新会话/重连时调用）。</summary>
        public void ResetSeen()
        {
            _seen.Clear();
        }

        /// <summary>清空待发与去重记录。</summary>
        public void Reset()
        {
            _pending.Clear();
            _seen.Clear();
        }
    }
}

using System;

namespace IslandAirport
{
    /// <summary>
    /// 快照插值缓冲：环形 64 槽，时钟偏移法。
    /// 首包定 offset（本地钟 − 服务器钟），之后小包幅 Lerp 跟随；
    /// renderServerTime = localNow − offset − 2·Period（≈2 帧 ≈133ms 缓冲）。
    /// 按 Tick 去重去旧；采样位置推进不倒退（乱序/丢包下钳制）。
    /// </summary>
    public class SnapshotBuffer
    {
        public const int Capacity = 64;

        private readonly Snapshot[] _snapshots = new Snapshot[Capacity];
        private readonly bool[] _occupied = new bool[Capacity];

        /// <summary>快照周期（服务器钟 = Tick · Period）。</summary>
        public float Period = NetProtocol.SnapshotPeriod;

        /// <summary>渲染延迟（秒），默认 2 帧。</summary>
        public float Delay = 2f * NetProtocol.SnapshotPeriod;

        /// <summary>offset 跟随速率（每包）。</summary>
        public float OffsetLerp = 0.1f;

        private int _round;
        private int _epoch;
        private string _match;
        private float _offset;
        private bool _hasOffset;
        private float _lastRenderServerTime = float.MinValue;

        /// <summary>当前缓冲快照数。</summary>
        public int Count { get; private set; }

        /// <summary>最新入包 Tick；无包为 -1。</summary>
        public int NewestTick { get; private set; }

        public SnapshotBuffer()
        {
            NewestTick = -1;
        }

        /// <summary>时钟偏移是否已定（首包到达后可用）。</summary>
        public bool HasOffset
        {
            get { return _hasOffset; }
        }

        /// <summary>入包：去重去旧（Tick 不新即弃）。返回是否入库。</summary>
        public bool Add(Snapshot snap, float localNow)
        {
            if (snap == null)
            {
                return false;
            }

            if (snap.Tick < 0) return false;
            if (Count > 0)
            {
                if (snap.RoundId < _round || snap.AuthorityEpoch < _epoch) return false;
                if (snap.RoundId == _round && snap.AuthorityEpoch == _epoch && snap.MatchId != _match) return false;
                if (snap.RoundId > _round || snap.AuthorityEpoch > _epoch) Reset();
            }
            _round = snap.RoundId;
            _epoch = snap.AuthorityEpoch;
            _match = snap.MatchId;
            if (NewestTick >= 0 && snap.Tick <= NewestTick)
            {
                return false;
            }

            int slot = snap.Tick % Capacity;
            if (!_occupied[slot])
            {
                Count++;
            }

            _occupied[slot] = true;
            _snapshots[slot] = snap;
            NewestTick = snap.Tick;

            float measured = localNow - snap.Tick * Period;
            if (!_hasOffset)
            {
                _offset = measured;
                _hasOffset = true;
            }
            else
            {
                _offset += (measured - _offset) * OffsetLerp;
            }

            return true;
        }

        /// <summary>
        /// 采样：返回夹住渲染时刻的两帧 a/b 与插值系数 t∈[0,1]。
        /// 不足两帧时 a==b；无包返回 false。采样位置单调不倒退。
        /// </summary>
        public bool Sample(float localNow, out Snapshot a, out Snapshot b, out float t)
        {
            a = null;
            b = null;
            t = 0f;
            if (Count == 0 || !_hasOffset)
            {
                return false;
            }

            float renderServerTime = localNow - _offset - Delay;
            if (renderServerTime < _lastRenderServerTime)
            {
                renderServerTime = _lastRenderServerTime;
            }

            _lastRenderServerTime = renderServerTime;

            Snapshot before = null;
            int beforeTick = int.MinValue;
            Snapshot after = null;
            int afterTick = int.MaxValue;

            for (int i = 0; i < Capacity; i++)
            {
                if (!_occupied[i])
                {
                    continue;
                }

                Snapshot snap = _snapshots[i];
                float serverTime = snap.Tick * Period;
                if (serverTime <= renderServerTime && snap.Tick > beforeTick)
                {
                    before = snap;
                    beforeTick = snap.Tick;
                }

                if (serverTime >= renderServerTime && snap.Tick < afterTick)
                {
                    after = snap;
                    afterTick = snap.Tick;
                }
            }

            if (before == null && after == null)
            {
                return false;
            }

            if (before == null)
            {
                before = after;
            }

            if (after == null)
            {
                after = before;
            }

            a = before;
            b = after;
            if (a == b)
            {
                t = 0f;
            }
            else
            {
                float span = (b.Tick - a.Tick) * Period;
                t = span > 0f ? (renderServerTime - a.Tick * Period) / span : 0f;
                if (t < 0f)
                {
                    t = 0f;
                }
                else if (t > 1f)
                {
                    t = 1f;
                }
            }

            return true;
        }

        /// <summary>清空缓冲与时钟状态（重连时调用）。</summary>
        public void Reset()
        {
            for (int i = 0; i < Capacity; i++)
            {
                _occupied[i] = false;
                _snapshots[i] = null;
            }

            _round = 0;
            _epoch = 0;
            _match = null;
            Count = 0;
            NewestTick = -1;
            _offset = 0f;
            _hasOffset = false;
            _lastRenderServerTime = float.MinValue;
        }
    }
}

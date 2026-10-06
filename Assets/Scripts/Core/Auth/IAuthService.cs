using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>登录态用户。AgeVerified=已通过 13 岁准入。</summary>
    public class AuthUser
    {
        public string Uid = string.Empty;
        public string DisplayName = string.Empty;
        public string IdToken = string.Empty;
        public string RefreshToken = string.Empty;
        public bool AgeVerified;
    }

    /// <summary>
    /// 认证服务抽象。事件风格与 RoomManager 一致：异步结果走 TryDequeue
    /// （ok:uid / restored:uid / cancelled / fail:reason / out），驱动走 Pump(dt)。
    /// </summary>
    public interface IAuthService
    {
        /// <summary>当前用户；未登录为 null。</summary>
        AuthUser User { get; }

        /// <summary>是否有登录/续期请求进行中。</summary>
        bool Busy { get; }

        /// <summary>发起 Google 一键登录。</summary>
        void SignInGoogle();

        /// <summary>登出并清本地态。</summary>
        void SignOut();

        /// <summary>用本地保存的 refreshToken 静默续期。</summary>
        void Restore(string refreshToken);

        /// <summary>取一条异步结果事件。</summary>
        bool TryDequeue(out string result);

        /// <summary>推进内部计时（无墙钟依赖）。</summary>
        void Pump(float dt);
    }

    /// <summary>
    /// 确定性假认证：注入 取消/网络错误/未配置 故障，console 可测。
    /// 成功路径签发确定性 fake 凭据，Restore 仅接受自己签发的 refreshToken。
    /// </summary>
    public class FakeAuthService : IAuthService
    {
        /// <summary>注入故障模式。</summary>
        public enum FakeMode
        {
            Normal = 0,
            Cancel = 1,
            NetworkError = 2,
            Unconfigured = 3
        }

        private readonly Queue<string> _events = new Queue<string>();
        private float _timer = -1f;
        private FakeMode _pendingMode;
        private int _signInCount;
        private string _lastRefreshToken = string.Empty;

        public FakeMode Mode = FakeMode.Normal;

        /// <summary>假登录耗时（秒），Pump 累计。</summary>
        public float Delay = 0.2f;

        /// <summary>签发的显示名。</summary>
        public string FakeDisplayName = "FakeUser";

        public AuthUser User { get; private set; }

        public bool Busy
        {
            get { return _timer >= 0f; }
        }

        public void SignInGoogle()
        {
            if (Busy)
            {
                return;
            }

            if (Mode == FakeMode.Unconfigured)
            {
                _events.Enqueue("fail:unconfigured");
                return;
            }

            _pendingMode = Mode;
            _timer = 0f;
        }

        public void SignOut()
        {
            User = null;
            _timer = -1f;
            _events.Enqueue("out");
        }

        public void Restore(string refreshToken)
        {
            if (Busy)
            {
                return;
            }

            if (string.IsNullOrEmpty(refreshToken) || refreshToken != _lastRefreshToken || User == null)
            {
                _events.Enqueue("fail:token");
                return;
            }

            _events.Enqueue("restored:" + User.Uid);
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
            if (_timer < 0f)
            {
                return;
            }

            if (dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt))
            {
                _timer += dt;
            }

            if (_timer < Delay)
            {
                return;
            }

            _timer = -1f;
            switch (_pendingMode)
            {
                case FakeMode.Cancel:
                    _events.Enqueue("cancelled");
                    break;
                case FakeMode.NetworkError:
                    _events.Enqueue("fail:network");
                    break;
                default:
                    _signInCount++;
                    AuthUser user = new AuthUser();
                    user.Uid = "fake-uid-" + _signInCount;
                    user.DisplayName = FakeDisplayName;
                    user.IdToken = "fake-id-" + _signInCount;
                    user.RefreshToken = "fake-refresh-" + _signInCount;
                    User = user;
                    _lastRefreshToken = user.RefreshToken;
                    _events.Enqueue("ok:" + user.Uid);
                    break;
            }
        }
    }
}

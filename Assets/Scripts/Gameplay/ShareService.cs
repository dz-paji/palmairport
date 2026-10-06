using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace IslandAirport
{
    public enum InviteRole
    {
        Meals = 0,
        Baggage = 1,
        Fuel = 2
    }

    public enum ShareActionResult
    {
        ChooserOpened = 0,
        CopiedToClipboard = 1,
        UnsupportedPlatform = 2,
        InvalidRole = 3,
        InvalidSourceId = 4,
        Failed = 5
    }

    public static class ShareService
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        [DllImport("PalmBayVideoShare", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PalmBayShareVideo([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
#endif

        /// <summary>Prefer this entry point so file copying and waiting for the native UI never stall a Unity frame.</summary>
        public static Task<ShareActionResult> ShareVideoAsync(string path)
        {
            return Task.Run(delegate
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                bool attached = false;
                try
                {
                    attached = AndroidJNI.AttachCurrentThread() == 0;
                    return attached ? ShareVideo(path) : ShareActionResult.Failed;
                }
                catch (Exception) { return ShareActionResult.Failed; }
                finally { if (attached) AndroidJNI.DetachCurrentThread(); }
#else
                return ShareVideo(path);
#endif
            });
        }

        /// <summary>Returns chooser-open status only. The recipient app controls posting and completion.</summary>
        public static ShareActionResult ShareVideo(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path) ||
                    !string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase) ||
                    new FileInfo(path).Length < 32) return ShareActionResult.Failed;
#if UNITY_ANDROID && !UNITY_EDITOR
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaClass nativeShare = new AndroidJavaClass("com.palmbay.sharing.ReplayVideoShare"))
                {
                    return nativeShare.CallStatic<bool>("share", activity, Path.GetFullPath(path))
                        ? ShareActionResult.ChooserOpened : ShareActionResult.Failed;
                }
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                return PalmBayShareVideo(Path.GetFullPath(path)) == 1 ? ShareActionResult.ChooserOpened : ShareActionResult.Failed;
#else
                return ShareActionResult.UnsupportedPlatform;
#endif
            }
            catch (Exception) { return ShareActionResult.Failed; }
        }

        public static string BuildInviteMessage(InviteRole role, string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                return string.Empty;
            }

            string roleLabel;
            string roleCode;
            if (!TryGetRole(role, out roleLabel, out roleCode))
            {
                return string.Empty;
            }

            return "PALM BAY 小岛机场班岗缺人！来开" + roleLabel + "车。\n" +
                "向邀请人获取同版安装包，连接同一个 Wi-Fi。双方登录并验证年龄后，" +
                "一人建房，另一人加入；找不到房间可输入房主局域网 IP。";
        }

        public static bool RecordSharePanelOpened(GameEventQueue events, string actorId, string pairId,
            string matchId)
        {
            if (events == null)
            {
                return false;
            }

            string previousActorId = events.ActorId;
            string previousPairId = events.PairId;
            string previousMatchId = events.MatchId;
            try
            {
                events.ActorId = actorId ?? string.Empty;
                events.PairId = pairId ?? string.Empty;
                events.MatchId = matchId ?? string.Empty;
                return events.Enqueue(GameEventQueue.SharePanelOpened);
            }
            finally
            {
                events.ActorId = previousActorId;
                events.PairId = previousPairId;
                events.MatchId = previousMatchId;
            }
        }

        public static ShareActionResult ShareInvite(InviteRole role, string sourceId)
        {
            string inviteMessage = BuildInviteMessage(role, sourceId);
            if (string.IsNullOrEmpty(sourceId))
            {
                return ShareActionResult.InvalidSourceId;
            }

            if (string.IsNullOrEmpty(inviteMessage))
            {
                return ShareActionResult.InvalidRole;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent"))
                using (AndroidJavaObject sendIntent = new AndroidJavaObject("android.content.Intent",
                    intentClass.GetStatic<string>("ACTION_SEND")))
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    sendIntent.Call<AndroidJavaObject>("setType", "text/plain");
                    sendIntent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"),
                        inviteMessage);
                    using (AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser",
                        sendIntent, "邀请搭档"))
                    {
                        activity.Call("startActivity", chooser);
                    }
                }

                return ShareActionResult.ChooserOpened;
            }
            catch (Exception)
            {
                return ShareActionResult.Failed;
            }
#elif UNITY_EDITOR || UNITY_STANDALONE_OSX
            try
            {
                GUIUtility.systemCopyBuffer = inviteMessage;
                return ShareActionResult.CopiedToClipboard;
            }
            catch (Exception)
            {
                return ShareActionResult.Failed;
            }
#else
            return ShareActionResult.UnsupportedPlatform;
#endif
        }

        public static string GetResultMessage(ShareActionResult result)
        {
            switch (result)
            {
                case ShareActionResult.ChooserOpened:
                    return "已打开系统分享选择器，请选择应用和接收人。";
                case ShareActionResult.CopiedToClipboard:
                    return "邀请文案已复制，可粘贴到聊天应用发送。";
                case ShareActionResult.UnsupportedPlatform:
                    return "此平台暂不支持分享。";
                case ShareActionResult.InvalidRole:
                    return "无法识别邀请岗位。";
                case ShareActionResult.InvalidSourceId:
                    return "缺少邀请来源 ID。";
                default:
                    return "未能打开分享，请稍后重试。";
            }
        }

        private static bool TryGetRole(InviteRole role, out string roleLabel, out string roleCode)
        {
            switch (role)
            {
                case InviteRole.Meals:
                    roleLabel = "餐食";
                    roleCode = "meals";
                    return true;
                case InviteRole.Baggage:
                    roleLabel = "行李";
                    roleCode = "baggage";
                    return true;
                case InviteRole.Fuel:
                    roleLabel = "燃油";
                    roleCode = "fuel";
                    return true;
                default:
                    roleLabel = string.Empty;
                    roleCode = string.Empty;
                    return false;
            }
        }
    }
}

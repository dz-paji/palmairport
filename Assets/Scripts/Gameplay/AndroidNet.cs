using System;
using UnityEngine;

namespace IslandAirport
{
    public static class AndroidNet
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private static readonly object Sync = new object();
        private static AndroidJavaObject _multicastLock;
#endif

        public static void Install()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            BeaconListener.BeforeListen = AcquireMulticastLock;
#endif
        }

        public static void Release()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Action hook = AcquireMulticastLock;
            if (BeaconListener.BeforeListen == hook)
            {
                BeaconListener.BeforeListen = null;
            }

            lock (Sync)
            {
                AndroidJavaObject multicastLock = _multicastLock;
                _multicastLock = null;
                if (multicastLock == null)
                {
                    return;
                }

                try
                {
                    if (multicastLock.Call<bool>("isHeld"))
                    {
                        multicastLock.Call("release");
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("AndroidNet: MulticastLock 释放失败。" + exception.Message);
                }
                finally
                {
                    multicastLock.Dispose();
                }
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void AcquireMulticastLock()
        {
            lock (Sync)
            {
                AndroidJavaObject pendingLock = null;
                try
                {
                    if (_multicastLock != null)
                    {
                        if (!_multicastLock.Call<bool>("isHeld"))
                        {
                            _multicastLock.Call("acquire");
                        }

                        return;
                    }

                    using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (AndroidJavaObject wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
                    {
                        pendingLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "palmbay.beacon");
                        pendingLock.Call("setReferenceCounted", false);
                        pendingLock.Call("acquire");
                        _multicastLock = pendingLock;
                        pendingLock = null;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("AndroidNet: MulticastLock 获取失败。" + exception.Message);
                }
                finally
                {
                    if (pendingLock != null)
                    {
                        pendingLock.Dispose();
                    }
                }
            }
        }
#endif
    }
}

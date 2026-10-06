using System;
using System.Xml;

namespace IslandAirport.Editor
{
    public static class AndroidManifestXml
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        private const string OAuthFilterXPath =
            "/manifest/application/activity[@android:name='com.unity3d.player.UnityPlayerActivity']" +
            "/intent-filter[action[@android:name='android.intent.action.VIEW'] and " +
            "category[@android:name='android.intent.category.DEFAULT'] and " +
            "category[@android:name='android.intent.category.BROWSABLE'] and " +
            "data[@android:path='/oauth2redirect']]";

        public static void ConfigureRedirectFilter(XmlDocument manifest, string redirectScheme)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }

            if (string.IsNullOrEmpty(redirectScheme))
            {
                throw new ArgumentException("Redirect scheme is required.", "redirectScheme");
            }

            XmlNamespaceManager namespaces = new XmlNamespaceManager(manifest.NameTable);
            namespaces.AddNamespace("android", AndroidNamespace);
            XmlNodeList filters = manifest.SelectNodes(OAuthFilterXPath, namespaces);
            if (filters == null || filters.Count != 1)
            {
                throw new InvalidOperationException("Android Manifest 未声明唯一的 OAuth 回跳 intent-filter。");
            }

            XmlNodeList dataNodes = filters[0].SelectNodes("data[@android:path='/oauth2redirect']", namespaces);
            if (dataNodes == null || dataNodes.Count != 1)
            {
                throw new InvalidOperationException("Android Manifest OAuth intent-filter 缺少唯一回跳路径。");
            }

            XmlAttribute scheme = dataNodes[0].Attributes["scheme", AndroidNamespace];
            if (scheme == null)
            {
                throw new InvalidOperationException("Android Manifest OAuth intent-filter 缺少 scheme。");
            }

            scheme.Value = redirectScheme;
        }
    }
}

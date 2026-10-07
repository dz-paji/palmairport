using System;
using System.IO;
using System.Text;
using System.Xml;
using IslandAirport;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace IslandAirport.Editor
{
    public sealed class AndroidManifestPreprocessor : IPreprocessBuildWithReport
    {
        private const string ManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        private const string ExpectedPackageName = "com.palmbay.islandairport";
        private const string DisabledRedirectScheme = "palmbay-auth-disabled";
        private const string OAuthPath = "/oauth2redirect";

        public int callbackOrder
        {
            get { return 0; }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
            {
                return;
            }

            FirebaseAuthConfiguration configuration = LoadConfiguration();
            bool authConfigured = configuration != null &&
                configuration.IsConfiguredFor(FirebaseAuthPlatform.Android);
            ValidateConfiguredPackage(configuration);

            if (configuration != null && !string.IsNullOrEmpty(configuration.AndroidClientId) &&
                !string.IsNullOrEmpty(configuration.RedirectScheme) &&
                !FirebaseAuthConfiguration.IsValidScheme(configuration.RedirectScheme))
            {
                throw new BuildFailedException("Android OAuth 回跳 scheme 格式无效。");
            }

            string redirectScheme = authConfigured
                ? GetConfiguredRedirectScheme(configuration)
                : DisabledRedirectScheme;

            if (!File.Exists(ManifestPath))
            {
                throw new BuildFailedException("Android Manifest 缺失：" + ManifestPath);
            }

            XmlDocument manifest = new XmlDocument();
            manifest.PreserveWhitespace = true;
            try
            {
                manifest.Load(ManifestPath);
            }
            catch (XmlException)
            {
                throw new BuildFailedException("Android Manifest XML 格式无效。");
            }

            try
            {
                AndroidManifestXml.ConfigureRedirectFilter(manifest, redirectScheme);
            }
            catch (InvalidOperationException exception)
            {
                throw new BuildFailedException(exception.Message);
            }

            if (!authConfigured)
            {
                Debug.LogWarning("Android OAuth 配置缺失或未完成，真实登录已禁用；游客单人模式仍可构建和使用。");
            }

            using (MemoryStream stream = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = new UTF8Encoding(false);
                settings.Indent = true;
                settings.OmitXmlDeclaration = true;
                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                {
                    manifest.Save(writer);
                }

                string updatedManifest = Encoding.UTF8.GetString(stream.ToArray());
                string currentManifest = File.ReadAllText(ManifestPath);
                if (!string.Equals(currentManifest, updatedManifest, StringComparison.Ordinal))
                {
                    File.WriteAllText(ManifestPath, updatedManifest, new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        private static FirebaseAuthConfiguration LoadConfiguration()
        {
            TextAsset configurationAsset = Resources.Load<TextAsset>("palmbay-auth");
            if (configurationAsset == null)
            {
                return null;
            }

            try
            {
                string environmentApiKey = Environment.GetEnvironmentVariable("PALMBAY_FIREBASE_KEY");
                return FirebaseAuthConfiguration.Parse(configurationAsset.text, environmentApiKey);
            }
            catch (Exception)
            {
                Debug.LogWarning("Android OAuth 配置无法读取，真实登录已禁用；游客单人模式仍可构建和使用。");
                return null;
            }
        }

        private static void ValidateConfiguredPackage(FirebaseAuthConfiguration configuration)
        {
            if (configuration == null || string.IsNullOrEmpty(configuration.PackageName))
            {
                return;
            }

            string buildPackageName = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (!string.Equals(configuration.PackageName, ExpectedPackageName, StringComparison.Ordinal) ||
                !string.Equals(configuration.PackageName, buildPackageName, StringComparison.Ordinal))
            {
                throw new BuildFailedException("Android 包名与 Firebase 配置不匹配。");
            }
        }

        private static string GetConfiguredRedirectScheme(FirebaseAuthConfiguration configuration)
        {
            Uri redirectUri;
            if (!Uri.TryCreate(configuration.AndroidRedirectUri, UriKind.Absolute, out redirectUri) ||
                !string.Equals(redirectUri.Scheme, configuration.RedirectScheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(redirectUri.AbsolutePath, OAuthPath, StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(redirectUri.Host))
            {
                throw new BuildFailedException("Android Manifest 回跳 URI 与 Firebase 配置不匹配。");
            }

            return configuration.RedirectScheme;
        }
    }
}

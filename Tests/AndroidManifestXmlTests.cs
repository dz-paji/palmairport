using System;
using System.Xml;
using IslandAirport.Editor;

public static class AndroidManifestXmlTests
{
    private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
    private static int _passed;
    private static int _failed;
    private static int _assertions;

    public static int Main(string[] args)
    {
        if (args == null || args.Length != 1)
        {
            Console.Error.WriteLine("Expected path to AndroidManifest.xml.");
            return 2;
        }

        Run("current manifest locates and updates one OAuth filter", delegate
        {
            TestConfigureFilter(args[0]);
        });
        Run("duplicate OAuth filters are rejected", delegate
        {
            TestRejectsDuplicateFilter(args[0]);
        });
        Console.WriteLine("Android manifest XML tests: {0} passed, {1} failed, {2} assertions.",
            _passed, _failed, _assertions);
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS  " + name);
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine("FAIL  " + name + ": " + exception.Message);
            Console.WriteLine(exception.ToString());
        }
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void TestConfigureFilter(string manifestPath)
    {
        XmlDocument manifest = LoadManifest(manifestPath);
        AndroidManifestXml.ConfigureRedirectFilter(manifest, "test.palmbay.oauth");

        XmlNamespaceManager namespaces = new XmlNamespaceManager(manifest.NameTable);
        namespaces.AddNamespace("android", AndroidNamespace);
        XmlNodeList filters = manifest.SelectNodes(
            "/manifest/application/activity[@android:name='com.unity3d.player.UnityPlayerActivity']/" +
            "intent-filter[action[@android:name='android.intent.action.VIEW'] and " +
            "category[@android:name='android.intent.category.DEFAULT'] and " +
            "category[@android:name='android.intent.category.BROWSABLE'] and " +
            "data[@android:path='/oauth2redirect']]", namespaces);
        Assert(filters != null && filters.Count == 1, "one OAuth filter is found in the current manifest");

        XmlNode data = filters[0].SelectSingleNode("data[@android:path='/oauth2redirect']", namespaces);
        XmlAttribute scheme = data.Attributes["scheme", AndroidNamespace];
        XmlAttribute path = data.Attributes["path", AndroidNamespace];
        Assert(scheme != null && scheme.Value == "test.palmbay.oauth", "configured scheme is written");
        Assert(path != null && path.Value == "/oauth2redirect", "Firebase redirect path is preserved");

        XmlNode launcher = manifest.SelectSingleNode(
            "/manifest/application/activity[@android:name='com.unity3d.player.UnityPlayerActivity']/" +
            "intent-filter[action[@android:name='android.intent.action.MAIN'] and " +
            "category[@android:name='android.intent.category.LAUNCHER']]", namespaces);
        Assert(launcher != null, "Unity launcher filter is preserved");
        XmlNode activity = manifest.SelectSingleNode(
            "/manifest/application/activity[@android:name='com.unity3d.player.UnityPlayerActivity']", namespaces);
        XmlAttribute theme = activity == null ? null : activity.Attributes["theme", AndroidNamespace];
        Assert(theme != null && theme.Value == "@style/TuanjieThemeSelector",
            "Unity activity uses the installed Tuanjie theme resource");
        Assert(activity != null && activity.Attributes["exported", AndroidNamespace].Value == "true",
            "Unity activity remains exported");
    }

    private static void TestRejectsDuplicateFilter(string manifestPath)
    {
        XmlDocument manifest = LoadManifest(manifestPath);
        XmlNamespaceManager namespaces = new XmlNamespaceManager(manifest.NameTable);
        namespaces.AddNamespace("android", AndroidNamespace);
        XmlNode filter = manifest.SelectSingleNode(
            "/manifest/application/activity[@android:name='com.unity3d.player.UnityPlayerActivity']/" +
            "intent-filter[action[@android:name='android.intent.action.VIEW'] and " +
            "category[@android:name='android.intent.category.DEFAULT'] and " +
            "category[@android:name='android.intent.category.BROWSABLE'] and " +
            "data[@android:path='/oauth2redirect']]", namespaces);
        filter.ParentNode.AppendChild(filter.CloneNode(true));

        bool rejected = false;
        try
        {
            AndroidManifestXml.ConfigureRedirectFilter(manifest, "test.palmbay.oauth");
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert(rejected, "multiple matching OAuth filters fail validation");
    }

    private static XmlDocument LoadManifest(string path)
    {
        XmlDocument manifest = new XmlDocument();
        manifest.Load(path);
        return manifest;
    }
}

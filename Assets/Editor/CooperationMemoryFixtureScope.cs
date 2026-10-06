using System;
using System.Reflection;
using IslandAirport;

/// <summary>Editor fixture history stays in memory; product per-device JSON is never written.</summary>
public sealed class CooperationMemoryFixtureScope : IDisposable
{
    static readonly FieldInfo StoreField = typeof(AppState).GetField("botMemoryStore", BindingFlags.Instance | BindingFlags.NonPublic);
    readonly AppState state;
    readonly BotMemoryStore originalStore;
    bool disposed;

    public CooperationMemoryFixtureScope(AppState application)
    {
        if (application == null) throw new ArgumentNullException("application");
        if (StoreField == null) throw new InvalidOperationException("AppState cooperation store is unavailable");
        state = application;
        originalStore = (BotMemoryStore)StoreField.GetValue(application);
        string fixtureJson = string.Empty;
        StoreField.SetValue(application, new BotMemoryStore(() => fixtureJson, json => fixtureJson = json));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (state != null) StoreField.SetValue(state, originalStore);
    }
}

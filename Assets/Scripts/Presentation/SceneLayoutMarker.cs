using UnityEngine;

namespace IslandAirport
{
    /// <summary>
    /// Baked into a saved MainLoop scene so a scene produced by an older
    /// AirportWorld/Level1Map pair can be identified after the project is
    /// reopened.  The marker is intentionally tiny and contains no gameplay
    /// state; generated meshes and materials remain ordinary scene assets.
    /// </summary>
    public sealed class SceneLayoutMarker : MonoBehaviour
    {
        public int Version;
        public string Source;
    }
}

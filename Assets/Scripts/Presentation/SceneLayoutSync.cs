using System;
using UnityEngine;

namespace IslandAirport
{
    /// <summary>
    /// Keeps the hand-authored MainLoop scene on the same layout as PalmBay.
    /// MainLoop is saved with a baked world for editor inspection, while the
    /// runtime path can replace an old baked world before MainLoopGame starts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneLayoutSync : MonoBehaviour
    {
        public const string WorldName = "Airport World";
        public const string SourceId = "level1-figma";

        [SerializeField] int appliedVersion;

        public int AppliedVersion { get { return appliedVersion; } }

        /// <summary>
        /// Called from MainLoopGame.Start.  Keeping the call in Start means an
        /// old scene with no SceneLayoutSync component is repaired as soon as
        /// it is played, without requiring a manual scene rebuild first.
        /// </summary>
        public static void Ensure(MainLoopGame game)
        {
            if (!game) return;
            SceneLayoutSync sync = game.GetComponent<SceneLayoutSync>();
            if (!sync) sync = game.gameObject.AddComponent<SceneLayoutSync>();
            sync.Apply(game);
        }

        /// <summary>
        /// Adds the marker used by both the editor builder and the runtime
        /// repair path.  The returned root can be saved into a scene.
        /// </summary>
        public static Transform MarkBakedWorld(Transform world)
        {
            if (!world) return null;
            SceneLayoutMarker marker = world.GetComponent<SceneLayoutMarker>();
            if (!marker) marker = world.gameObject.AddComponent<SceneLayoutMarker>();
            marker.Version = Level1Map.LayoutVersion;
            marker.Source = SourceId;
            return world;
        }

        /// <summary>Apply the current world and actor placement to MainLoop.</summary>
        public void Apply(MainLoopGame game)
        {
            Transform world = FindWorld(game.gameObject.scene);
            SceneLayoutMarker marker = world ? world.GetComponent<SceneLayoutMarker>() : null;
            bool rebuild = !world || !marker || marker.Version != Level1Map.LayoutVersion;
            if (rebuild) world = RebuildWorld(game.gameObject.scene);

            RemoveLegacyServiceMarkers(game.gameObject.scene);
            RelocateActors(game);
            appliedVersion = Level1Map.LayoutVersion;
        }

        static Transform FindWorld(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid()) return null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i] && roots[i].name == WorldName) return roots[i].transform;
            return null;
        }

        static Transform RebuildWorld(UnityEngine.SceneManagement.Scene scene)
        {
            Transform old = FindWorld(scene);
            if (old)
            {
                old.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(old.gameObject);
                else UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            return MarkBakedWorld(AirportWorld.Build());
        }

        static void RelocateActors(MainLoopGame game)
        {
            if (game.Plane)
            {
                game.Plane.position = Level1Map.PlanePark(0);
                game.Plane.rotation = AircraftMotion.NoseRotation(
                    AircraftMotion.FinalDirection(Level1Map.TaxiInPath(0)));
            }

            if (game.Players != null)
            {
                for (int i = 0; i < game.Players.Length; i++)
                {
                    Transform player = game.Players[i];
                    if (!player) continue;
                    player.position = Level1Map.CrewSpawn(i);
                    player.rotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }

            if (game.Vehicles != null)
            {
                for (int i = 0; i < game.Vehicles.Length && i < 3; i++)
                {
                    Transform vehicle = game.Vehicles[i];
                    if (!vehicle) continue;
                    vehicle.position = Level1Map.CartPark((ServiceKind)i);
                    vehicle.rotation = Quaternion.identity;
                }
            }

            if (game.View)
            {
                Level1Map.ConfigureCamera(game.View, false);
                game.View.backgroundColor = AirportStyle.Sky;
            }
            // 布局版本升级时也顺带统一光照（旧烘焙场景自愈的一部分）。
            AirportWorld.SetupLighting();
        }

        static void RemoveLegacyServiceMarkers(UnityEngine.SceneManagement.Scene scene)
        {
            Transform[] transforms = FindObjectsOfType<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform item = transforms[i];
                if (!item || (scene.IsValid() && item.gameObject.scene != scene)) continue;
                string name = item.name ?? string.Empty;
                bool legacy = item.parent == null &&
                    (name.StartsWith("Aircraft service point ", StringComparison.Ordinal) ||
                    name.StartsWith("SERVICE ", StringComparison.Ordinal) ||
                    name.StartsWith("World Label - SERVICE ", StringComparison.Ordinal));
                if (!legacy) continue;
                item.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(item.gameObject);
                else UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    /// <summary>
    /// Shared geometry helpers for the procedural aircraft animations.
    ///
    /// AirportWorld.CreatePlane builds the model with its nose on local +X,
    /// while Unity's LookRotation treats local +Z as forward. Keeping this
    /// conversion here prevents each gameplay scene from growing its own
    /// subtly different aircraft heading fix.
    /// </summary>
    public static class AircraftMotion
    {
        const float Epsilon = 0.0001f;

        /// <summary>
        /// Removes climb/descent from a path tangent. Taxiway paths live on
        /// the xz plane; the first arrival point may be above the ground as a
        /// visual approach marker and must not tilt the aircraft downwards.
        /// </summary>
        public static Vector3 Horizontal(Vector3 direction)
        {
            direction.y = 0f;
            return direction;
        }

        /// <summary>
        /// Returns the rotation that points the generated aircraft's local +X
        /// nose along a world-space direction.
        /// </summary>
        public static Quaternion NoseRotation(Vector3 direction)
        {
            direction = Horizontal(direction);
            if (direction.sqrMagnitude < Epsilon)
            {
                return Quaternion.identity;
            }

            // LookRotation aligns local +Z. A -90 degree local yaw maps the
            // generated model's +X nose to that same path tangent.
            return Quaternion.LookRotation(direction.normalized, Vector3.up)
                * Quaternion.Euler(0f, -90f, 0f);
        }

        /// <summary>
        /// Reads the generated model's horizontal nose direction from a
        /// rotation. Useful when a pushback segment must preserve the parked
        /// heading while its position moves in reverse.
        /// </summary>
        public static Vector3 NoseDirection(Quaternion rotation)
        {
            return Horizontal(rotation * Vector3.right).normalized;
        }

        /// <summary>
        /// Returns the first non-zero horizontal tangent in a path range.
        /// </summary>
        public static Vector3 FirstDirection(Vector3[] path, int startIndex = 0,
            Vector3 fallback = default(Vector3))
        {
            if (fallback.sqrMagnitude < Epsilon)
            {
                fallback = Vector3.right;
            }

            if (path == null || path.Length < 2)
            {
                return Horizontal(fallback).normalized;
            }

            int start = Mathf.Clamp(startIndex, 0, path.Length - 2);
            for (int i = start + 1; i < path.Length; i++)
            {
                Vector3 direction = Horizontal(path[i] - path[i - 1]);
                if (direction.sqrMagnitude >= Epsilon)
                {
                    return direction.normalized;
                }
            }

            return Horizontal(fallback).normalized;
        }

        /// <summary>
        /// Returns the last non-zero horizontal tangent in a path range.
        /// </summary>
        public static Vector3 FinalDirection(Vector3[] path,
            Vector3 fallback = default(Vector3))
        {
            if (fallback.sqrMagnitude < Epsilon)
            {
                fallback = Vector3.right;
            }

            if (path == null || path.Length < 2)
            {
                return Horizontal(fallback).normalized;
            }

            for (int i = path.Length - 1; i > 0; i--)
            {
                Vector3 direction = Horizontal(path[i] - path[i - 1]);
                if (direction.sqrMagnitude >= Epsilon)
                {
                    return direction.normalized;
                }
            }

            return Horizontal(fallback).normalized;
        }

        /// <summary>
        /// Measures a path range by horizontal distance.
        /// </summary>
        public static float PathLength(Vector3[] path, int startIndex = 0,
            int endIndex = -1)
        {
            if (path == null || path.Length < 2)
            {
                return 0f;
            }

            int start = Mathf.Clamp(startIndex, 0, path.Length - 1);
            int end = endIndex < 0 ? path.Length - 1 : Mathf.Clamp(endIndex, 0, path.Length - 1);
            if (end <= start)
            {
                return 0f;
            }

            float length = 0f;
            for (int i = start + 1; i <= end; i++)
            {
                length += Horizontal(path[i] - path[i - 1]).magnitude;
            }
            return length;
        }

        /// <summary>
        /// Samples a polyline by normalized horizontal distance and returns
        /// the active segment's tangent. Zero-length points are skipped.
        /// </summary>
        public static Vector3 PointAlong(Vector3[] path, float t, out Vector3 direction,
            Vector3 fallback = default(Vector3))
        {
            return PointAlongRange(path, 0, path == null ? 0 : path.Length - 1, t,
                out direction, fallback);
        }

        /// <summary>
        /// Samples only a contiguous path range. This lets the departure
        /// animation consume the pushback entry point and then traverse the
        /// forward taxi route without assuming how many bends it contains.
        /// </summary>
        public static Vector3 PointAlongRange(Vector3[] path, int startIndex, int endIndex,
            float t, out Vector3 direction, Vector3 fallback = default(Vector3))
        {
            if (fallback.sqrMagnitude < Epsilon)
            {
                fallback = Vector3.right;
            }

            direction = Horizontal(fallback).normalized;
            if (path == null || path.Length == 0)
            {
                return Vector3.zero;
            }

            int start = Mathf.Clamp(startIndex, 0, path.Length - 1);
            int end = endIndex < 0 ? path.Length - 1 : Mathf.Clamp(endIndex, 0, path.Length - 1);
            if (end <= start)
            {
                return path[start];
            }

            float total = PathLength(path, start, end);
            if (total < Epsilon)
            {
                return path[end];
            }

            float remaining = Mathf.Clamp01(t) * total;
            for (int i = start + 1; i <= end; i++)
            {
                Vector3 segment = Horizontal(path[i] - path[i - 1]);
                float length = segment.magnitude;
                if (length < Epsilon)
                {
                    continue;
                }

                direction = segment / length;
                if (remaining <= length)
                {
                    // Preserve any explicit y on the source path while the
                    // direction itself remains horizontal.
                    return Vector3.Lerp(path[i - 1], path[i], remaining / length);
                }
                remaining -= length;
            }

            direction = FinalDirectionRange(path, start, end, direction);
            return path[end];
        }

        /// <summary>
        /// Finds the point where the authored departure path first turns out
        /// of its straight ramp run. Pushback continues through that point so
        /// the entire aircraft clears the ramp before the nose is turned into
        /// the first taxiway tangent. This is based on path tangents rather
        /// than a world-space heading or a stand number.
        /// </summary>
        public static int PushbackEndIndex(Vector3[] departurePath)
        {
            if (departurePath == null || departurePath.Length < 2)
            {
                return 0;
            }

            Vector3 previous = FirstDirection(departurePath, 0);
            // Keep at least two authored forward segments in the reverse
            // phase. The first one or two points are commonly still inside a
            // stand ramp, as in the level-1 SVG layout.
            for (int segmentIndex = 1; segmentIndex < departurePath.Length - 1; segmentIndex++)
            {
                Vector3 current = Horizontal(departurePath[segmentIndex + 1] - departurePath[segmentIndex]);
                if (current.sqrMagnitude < Epsilon)
                {
                    continue;
                }

                current.Normalize();
                // The ramp-to-horizontal join can be a shallow diagonal; do
                // not turn there. The first real taxiway bend is the sharper
                // heading change that follows the clear-ramp run.
                if (segmentIndex >= 2 && Vector3.Dot(previous, current) < 0.80f)
                {
                    return segmentIndex;
                }
                previous = current;
            }

            // A straight route has no corner from which to infer a safe
            // turn point. Three points is the smallest useful clear-ramp
            // runway while remaining safe for short custom paths.
            return Mathf.Min(3, departurePath.Length - 1);
        }

        /// <summary>
        /// Builds the explicit reverse phase used when leaving a stand:
        /// first back along the final arrival/ramp segment, then continue
        /// backing along TaxiOutPath until its first clear taxiway bend. The
        /// curved reverse segment aligns the nose before the caller starts
        /// forward taxiing from <see cref="PushbackEndIndex"/>.
        ///
        /// TaxiOutPath remains the source of truth for the forward taxi route;
        /// this reverse prefix only makes the pushback phase visible and keeps
        /// the implementation independent of a fixed world heading.
        /// </summary>
        public static Vector3[] BuildPushbackPath(Vector3[] arrivalPath,
            Vector3[] departurePath)
        {
            var points = new List<Vector3>(12);
            Vector3 park = departurePath != null && departurePath.Length > 0
                ? departurePath[0]
                : LastPoint(arrivalPath);
            points.Add(park);

            // This is the ramp segment the aircraft just used to enter the
            // stand. Traversing it from the park point is an unmistakable
            // reverse/pushback motion regardless of stand orientation.
            Vector3 rampExit = PreviousDistinctPoint(arrivalPath, park);
            if ((rampExit - points[points.Count - 1]).sqrMagnitude >= Epsilon)
            {
                points.Add(rampExit);
            }

            // Continue backing along the authored departure route until its
            // first meaningful bend. On the level-1 map this carries the
            // aircraft beyond the ramp's east edge before it turns toward the
            // shared diagonal taxiway.
            if (departurePath != null && departurePath.Length > 1)
            {
                int endIndex = PushbackEndIndex(departurePath);
                for (int i = 1; i <= endIndex; i++)
                {
                    Vector3 taxiPoint = departurePath[i];
                    if ((taxiPoint - points[points.Count - 1]).sqrMagnitude >= Epsilon)
                    {
                        points.Add(taxiPoint);
                    }
                }

                // Continue through the first shared-taxiway corner with a
                // short path-tangent-driven curve. The final reverse tangent
                // is deliberately opposite the next forward segment, so the
                // nose is already aligned for forward taxi when this curve
                // ends. The distance is capped at the authored taxiway half
                // width used by the SVG map and scales down for short paths.
                if (endIndex + 2 < departurePath.Length)
                {
                    Vector3 curveStart = departurePath[endIndex];
                    Vector3 corner = departurePath[endIndex + 1];
                    Vector3 next = Horizontal(departurePath[endIndex + 2] - corner);
                    Vector3 intoCorner = Horizontal(corner - curveStart);
                    if (next.sqrMagnitude >= Epsilon && intoCorner.sqrMagnitude >= Epsilon)
                    {
                        next.Normalize();
                        intoCorner.Normalize();
                        Vector3 reverse = -next;
                        float nextLength = Horizontal(departurePath[endIndex + 2] - corner).magnitude;
                        float turnDistance = Mathf.Min(1.2f, Mathf.Max(.25f, nextLength * .35f));
                        Vector3 curveEnd = corner + reverse * turnDistance;
                        float handleIn = Mathf.Min(.9f, intoCorner.magnitude + turnDistance * .35f);
                        float handleOut = Mathf.Min(.9f, turnDistance * .8f);
                        Vector3 control1 = curveStart + intoCorner * handleIn;
                        Vector3 control2 = curveEnd - reverse * handleOut;
                        // Keep enough samples that the final chord already
                        // represents the reverse tangent for consumers that
                        // read a polyline's last segment.
                        AddCurve(points, curveStart, control1, control2, curveEnd, 24);
                    }
                }
            }

            return points.ToArray();
        }

        /// <summary>
        /// Creates the forward route that follows a curved pushback prefix.
        /// When the prefix ends on the shared taxiway before a corner, the
        /// first forward segment returns to that authored corner, then uses
        /// the remaining TaxiOutPath points unchanged. Returning to the corner
        /// is itself forward along the same taxiway and avoids any lateral
        /// position snap after the reverse turn.
        /// </summary>
        public static Vector3[] BuildForwardTaxiPath(Vector3[] departurePath,
            int pushbackEndIndex, Vector3[] pushbackPath)
        {
            var points = new List<Vector3>(departurePath == null ? 1 : departurePath.Length + 1);
            if (pushbackPath != null && pushbackPath.Length > 0)
            {
                points.Add(pushbackPath[pushbackPath.Length - 1]);
            }
            else if (departurePath != null && departurePath.Length > 0)
            {
                points.Add(departurePath[Mathf.Clamp(pushbackEndIndex, 0, departurePath.Length - 1)]);
            }

            if (departurePath == null || departurePath.Length == 0)
            {
                return points.ToArray();
            }

            int endIndex = Mathf.Clamp(pushbackEndIndex, 0, departurePath.Length - 1);
            // A curved pushback ends at the point just beyond this corner;
            // begin forward taxi by returning to the corner itself.
            int forwardFirst = endIndex + 1 < departurePath.Length
                ? endIndex + 1
                : endIndex;
            AddDistinct(points, departurePath[forwardFirst]);
            for (int i = forwardFirst + 1; i < departurePath.Length; i++)
            {
                AddDistinct(points, departurePath[i]);
            }
            return points.ToArray();
        }

        static void AddCurve(List<Vector3> points, Vector3 p0, Vector3 p1,
            Vector3 p2, Vector3 p3, int subdivisions)
        {
            for (int i = 1; i <= subdivisions; i++)
            {
                float t = i / (float)subdivisions;
                float oneMinus = 1f - t;
                Vector3 point = oneMinus * oneMinus * oneMinus * p0
                    + 3f * oneMinus * oneMinus * t * p1
                    + 3f * oneMinus * t * t * p2
                    + t * t * t * p3;
                AddDistinct(points, point);
            }
        }

        static void AddDistinct(List<Vector3> points, Vector3 point)
        {
            if (points.Count == 0 || (point - points[points.Count - 1]).sqrMagnitude >= Epsilon)
            {
                points.Add(point);
            }
        }

        static Vector3 FinalDirectionRange(Vector3[] path, int start, int end,
            Vector3 fallback)
        {
            for (int i = end; i > start; i--)
            {
                Vector3 direction = Horizontal(path[i] - path[i - 1]);
                if (direction.sqrMagnitude >= Epsilon)
                {
                    return direction.normalized;
                }
            }
            return fallback.sqrMagnitude >= Epsilon ? fallback.normalized : Vector3.right;
        }

        static Vector3 LastPoint(Vector3[] path)
        {
            return path != null && path.Length > 0 ? path[path.Length - 1] : Vector3.zero;
        }

        static Vector3 PreviousDistinctPoint(Vector3[] path, Vector3 point)
        {
            if (path == null || path.Length < 2)
            {
                return point;
            }

            for (int i = path.Length - 2; i >= 0; i--)
            {
                if ((path[i] - point).sqrMagnitude >= Epsilon)
                {
                    return path[i];
                }
            }
            return point;
        }
    }
}

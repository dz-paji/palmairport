using UnityEngine;

namespace IslandAirport
{
    public static partial class AirportWorld
    {
        // Palette layouts are inspired by liveries; none contains airline lettering or emblems.
        public static readonly string[] AircraftPaletteNames = { "NavyCoral", "PorcelainRed", "JadeIvory", "BurgundySilver" };
        public static readonly string[] AircraftConfigurationNames = { "B737_800", "A380_800", "Comet4" };
        public const float GameplayAircraftScale = .56f;

        sealed class AirframeSpec
        {
            public string Id;
            public int Type, Windows;
            public float Length, Ry, Rz, BodyY, NoseLength, TailLength, Span, WingY, TailTop;
            public float Half { get { return Length * .5f; } }
            public float NoseStart { get { return Half - NoseLength; } }
            public float TailStart { get { return -Half + TailLength; } }
            public float FinBase { get { return BodyY + Ry * .35f; } }
            public float FinWidth { get { return Type == 1 ? 2.05f : Type == 0 ? 1.40f : 1.13f; } }
            public float FinThickness { get { return Type == 1 ? .24f : .16f; } }
        }
        static readonly AirframeSpec[] Airframes =
        {
            new AirframeSpec { Id="737",Type=0,Length=5.8f,Ry=.61f,Rz=.60f,BodyY=1.16f,NoseLength=1.12f,TailLength=1.5f,Span=5.3f,WingY=1.0f,TailTop=2.65f,Windows=12 },
            new AirframeSpec { Id="380",Type=1,Length=8.6f,Ry=1.08f,Rz=.95f,BodyY=1.48f,NoseLength=1.37f,TailLength=2.0f,Span=8.9f,WingY=1.19f,TailTop=3.65f,Windows=17 },
            new AirframeSpec { Id="comet",Type=2,Length=5.15f,Ry=.51f,Rz=.49f,BodyY=1.04f,NoseLength=.89f,TailLength=1.19f,Span=5.2f,WingY=.91f,TailTop=2.30f,Windows=11 }
        };
        sealed class AirframePaint
        {
            public Color Body, Belly, Tail, Engine, Accent, Trim;
            public int Index;
        }
        static AirframePaint AircraftPaint(int index)
        {
            switch(index)
            {
                case 1: return new AirframePaint { Index=1,Body=Hex("FCFCF5"),Belly=Hex("D7DFE1"),Tail=Hex("FCFCF5"),Engine=Hex("FCFCF5"),Accent=Hex("CE3448"),Trim=Hex("BFC9CF") };
                case 2: return new AirframePaint { Index=2,Body=Hex("F8FBF4"),Belly=Hex("E0E6E4"),Tail=Hex("006B68"),Engine=Hex("F8FBF4"),Accent=Hex("006B68"),Trim=Hex("B5C3C4") };
                case 3: return new AirframePaint { Index=3,Body=Hex("C8CDD2"),Belly=Hex("F7F7F1"),Tail=Hex("C8CDD2"),Engine=Hex("C8CDD2"),Accent=Hex("792641"),Trim=Hex("99A7B0") };
                default:return new AirframePaint { Index=0,Body=Hex("FCFAF2"),Belly=Hex("162A51"),Tail=Hex("162A51"),Engine=Hex("162A51"),Accent=Hex("D63849"),Trim=Hex("BAC7CB") };
            }
        }
        public static int AircraftVariantIndex(string flightId)
        {
            uint value=2166136261;
            unchecked { foreach(char c in flightId??"") value=(value^c)*16777619; }
            return (int)(value%12);
        }
        /// <summary>Identical ID selects identical geometry and paint on host, client and replay.</summary>
        public static Transform CreatePlane(string name,Vector3 position)
        {
            int index=AircraftVariantIndex(name);
            Transform plane=CreatePlaneVariant(name,index%4,index/4,position);
            // Common scale, never per-aircraft normalization: A380 stays visibly larger.
            // Keeps the largest 4.98-unit wingspan inside the existing adjacent stand spacing.
            plane.localScale=Vector3.one*GameplayAircraftScale;
            return plane;
        }
        public static Transform CreatePlaneVariant(string name,int palette,int configuration,Vector3 position)
        {
            return BuildAirframe(name,Airframes[Mathf.Clamp(configuration,0,2)],AircraftPaint(Mathf.Clamp(palette,0,3)),position);
        }
        // Retain the original custom-color factory for existing model inspection tooling.
        public static Transform CreatePlane(string name,Color color,Vector3 position)
        {
            AirframePaint paint=AircraftPaint(0);paint.Belly=color;paint.Engine=color;paint.Tail=color;
            return BuildAirframe(name,Airframes[0],paint,position);
        }
    }
}

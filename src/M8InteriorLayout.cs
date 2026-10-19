using System;
using UnityEngine;

namespace M8LOSATM2
{
    // Gameplay layout fitted to the user's reference and our exterior, NOT a claim
    // about measured historical armor or the exact production vehicle's internals.
    internal static class M8InteriorLayout
    {
        internal static readonly string[] Parts = {
            "Engine_Cummings-VT903", "Gearbox_HMPT-500", "Engine_CoolantTank",
            "Radiator_001", "FuelTank_001", "FuelTank_002", "Driver_Controls",
            "Driver_Seat", "Gunner_Seat", "Commander_Seat", "Gunner_Optics_Primary",
            "Gunner_TurretControls", "Radio", "Turret_Drive"
        };
        internal static readonly Vector3[] Centers = {
            new Vector3(0,1.04f,-2.02f), new Vector3(0,.72f,-2.73f),
            new Vector3(.78f,1.18f,-2.07f), new Vector3(0,1.48f,-2.28f),
            new Vector3(-.99f,.99f,-1.83f), new Vector3(.99f,.99f,-1.83f),
            new Vector3(0,.98f,1.83f), new Vector3(0,.61f,1.46f),
            new Vector3(0,.95f,.54f), new Vector3(0,.95f,-.43f),
            new Vector3(0,2.03f,1.46f), new Vector3(0,1.29f,.94f),
            new Vector3(.29f,1.24f,-.54f), new Vector3(0,1.43f,-.08f)
        };
        internal static readonly Vector3[] Sizes = {
            new Vector3(1.42f,.69f,1.11f), new Vector3(1.48f,.42f,.38f),
            new Vector3(.20f,.30f,.53f), new Vector3(1.45f,.15f,.86f),
            new Vector3(.27f,.56f,1.12f), new Vector3(.27f,.56f,1.12f),
            new Vector3(.40f,.34f,.22f), new Vector3(.48f,.43f,.51f),
            new Vector3(.45f,.49f,.48f), new Vector3(.45f,.49f,.48f),
            new Vector3(.28f,.32f,.27f), new Vector3(.35f,.24f,.18f),
            new Vector3(.22f,.22f,.25f), new Vector3(.42f,.16f,.42f)
        };
        internal static bool TurretPart(int index) { return index >= 8; }
        internal static Vector3 CrewHead(int role)
        {
            if(role==0)return new Vector3(0,1.33f,1.46f); // driver, hull; lowered .10 m with controls/seat
            return role==1?new Vector3(0,1.97f,.54f):new Vector3(0,1.97f,-.43f);
        }
        internal static bool IsInterior(string name)
        {
            if(Array.IndexOf(Parts,name)>=0)return true;
            return name.StartsWith("Ammo",StringComparison.Ordinal)||
                name.StartsWith("Inf_",StringComparison.Ordinal)||
                name.StartsWith("Int_",StringComparison.Ordinal)||
                name.StartsWith("Commander_",StringComparison.Ordinal)||
                name.StartsWith("Gunner_",StringComparison.Ordinal)||
                name=="Gunner/Commander_AuxSight"||
                name.StartsWith("Driver_",StringComparison.Ordinal)||
                name.StartsWith("Gun_",StringComparison.Ordinal)||
                name.StartsWith("MG_Coax_",StringComparison.Ordinal)||
                name.StartsWith("ElevationMechanism (",StringComparison.Ordinal)||
                name.StartsWith("SheetMetal (",StringComparison.Ordinal)||
                name.StartsWith("Turret_Ring (",StringComparison.Ordinal)||
                name=="GunneryControlsBox"||name=="Radiator/Exhaust?"||
                name=="This is a radiator or an air filter or something";
        }
        internal const float MissileRadius=.080f;
        internal const float MissileRear=-1.40f;
        internal const float MissileTip=1.475f;
        internal static Vector3 MissilePoint(int tube,float angle,float z,float radius)
        {var p=M8Hardpoints.Positions[tube];return new Vector3(p.x+(float)Math.Cos(angle)*radius,p.y+(float)Math.Sin(angle)*radius,z);}
    }
}

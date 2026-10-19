using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using MelonLoader;
using UnityEngine;


namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private const string Prefix = "[M8 LOSAT Fire Control] ";
        // Release build: compile out diagnostic-only branches. Target discovery,
        // occlusion, damage checks and essential failure reporting remain active.
        private const bool ShotDiagnostics = false;
        private const bool LaunchDiagnostics = false;
        private const float AccelerationDistance = 1000f;
        private const float MaximumSpeed = 1500f;
        private const float MaximumPenetration = 1600f;
        private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod("MemberwiseClone", AnyInstance);
        internal HarmonyLib.Harmony HarmonyInstance;
        internal LosatService(HarmonyLib.Harmony harmony) { HarmonyInstance = harmony; }
        private readonly HashSet<int> _patchedVehicles = new HashSet<int>();
        private readonly List<TrackedMissile> _missiles = new List<TrackedMissile>();
        private static readonly List<AmmoCodexScriptable> OwnedAmmoCodices=new List<AmmoCodexScriptable>();
        private readonly Dictionary<LiveRound,TrackedMissile> _missileLookup=new Dictionary<LiveRound,TrackedMissile>();
        private float _sceneStartTime;

        public void OnInitializeMelon()
        {
            InitializeLaunchAudioSettings();
            InstallSalvoHooks();
            InstallDiscoveryHooks();
            InstallTargetLockHooks();
            InstallSightFrameProjection();
            M8ReticleDrawGate.Install(HarmonyInstance);
            InstallMissileReplayHooks();
            InstallLaunchAudioHook();
            InstallReloadAudioHook();
            InstallLaunchEffectsHook();
        }

        public void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _patchedVehicles.Clear();
            _missiles.Clear();
            _missileLookup.Clear();
            foreach(var codex in OwnedAmmoCodices)if(codex!=null)UnityEngine.Object.Destroy(codex);
            OwnedAmmoCodices.Clear();
            _earlyReplayShots.Clear();
            _m8Bindings.Clear();
            SalvoWeapons.Clear();
            SalvoFeeds.Clear();
            ResetLOSATOptics();
            ClearTargetLock();
            ResetLockConfirmation();
            ResetAntiEra();
            ResetLaunchAudio();
            ResetSonicBoomAudio();
            ResetLaunchEffects();
            ResetCenterLockCaches();
            ResetDiscovery();
            _sceneStartTime = Time.unscaledTime;
        }

        public void OnUpdate()
        {
            UpdateSalvos();
            UpdateLOSATOptics();
            UpdateTargetLock();
            UpdateSightFeedback();
            // M8 turns its launcher; HMMWV's spoken hull-turn cues do not apply.
            MaintainMissileSpeed();
            UpdateDiscovery();
        }

        public void OnLateUpdate()
        {
            MaintainNativeReticles();
            UpdateTargetSalvo();
            // Refresh once more after the game's projectile batch update so the
            // displayed/current KE velocity remains consistent with the flight speed.
            UpdateMissiles();
            // Sonic boom disabled for the user's rapid-fire audio trial.
        }

        // Vehicle conversion is explicitly requested by the M8 visual binder.
        // Incremental discovery remains active for target/occlusion caches only.
        private static bool IsLiveTarget(Vehicle vehicle) { return false; }
        private bool PatchVehicle(Vehicle vehicle) { return false; }

        private static WeaponSystem FindGuidedWeapon(Component root)
        {
            WeaponSystem fallback = null;
            foreach (WeaponSystem weapon in root.GetComponentsInChildren<WeaponSystem>(true))
            {
                if (weapon == null || weapon.Feed == null) continue;
                AmmoType ammo = weapon.Feed.AmmoTypeInBreech ?? weapon.CurrentAmmoType ??
                                FirstAmmoType(weapon.Feed.ReadyRack == null ? null : weapon.Feed.ReadyRack.ClipTypes);
                if (ammo != null && ammo.Guidance != AmmoType.GuidanceType.Unguided) return weapon;

                string identity = ((weapon.name ?? string.Empty) + " " + (weapon.MetaName ?? string.Empty) + " " +
                    (weapon.WeaponData == null ? string.Empty : weapon.WeaponData.FriendlyName)).ToUpperInvariant();
                if (fallback == null && (identity.Contains("MISSILE") || identity.Contains("TOW") ||
                    identity.Contains("M220") || identity.Contains("M232") || identity.Contains("BGM"))) fallback = weapon;
            }
            return fallback;
        }

        private static AmmoType FirstAmmoType(AmmoType.AmmoClip[] clips)
        {
            if (clips == null) return null;
            foreach (AmmoType.AmmoClip clip in clips)
                if (clip != null && clip.MinimalPattern != null)
                    foreach (AmmoCodexScriptable codex in clip.MinimalPattern)
                        if (codex != null && codex.AmmoType != null) return codex.AmmoType;
            return null;
        }

        private static AmmoType CloneAmmoType(AmmoType source)
        {
            if (source == null) return null;
            return (AmmoType)MemberwiseCloneMethod.Invoke(source, null);
        }

        private static void ConfigureKineticRound(AmmoType ammo, float launchSpeed)
        {
            ConfigureDirectFlight(ammo);
            if(SalvoOwner!=null) SalvoOwner.RegisterAntiEraAmmo(ammo);
            ammo.Name = "MGM-166";
            ammo.Category = AmmoType.AmmoCategory.Penetrator;
            ammo.ShortName = AmmoType.AmmoShortName.Missile;
            ammo.RhaPenetration = MaximumPenetration;
            ammo.MuzzleVelocity = launchSpeed;
            ammo.TntEquivalentKg = 0f;
            ammo.AlwaysProduceBlast = false;
            ammo.DetonateSpallCount = 0;
            ammo.DoScabEffect = false;
            ammo.ImpactFuseTime = 0f;
            ammo.RangedFuseTime = 0f;
            ammo.RhaToFuse = 0f;
            ammo.SphericalSpall = false;
            ammo.NoPenSpall = false;
            ammo.NoMuzzleEffects = false;
        }

        private static Dictionary<AmmoType.AmmoClip, AmmoType.AmmoClip> ReplaceRackClips(
            GHPC.Weapons.AmmoRack rack, AmmoType source, AmmoType replacement)
        {
            Dictionary<AmmoType.AmmoClip, AmmoType.AmmoClip> map =
                new Dictionary<AmmoType.AmmoClip, AmmoType.AmmoClip>();
            AmmoType.AmmoClip[] oldClips = rack.ClipTypes;
            if (oldClips == null) return map;

            AmmoType.AmmoClip[] newClips = new AmmoType.AmmoClip[oldClips.Length];
            for (int i = 0; i < oldClips.Length; i++)
            {
                AmmoType.AmmoClip oldClip = oldClips[i];
                if (oldClip == null) continue;
                AmmoCodexScriptable[] pattern = ClonePattern(oldClip.MinimalPattern, source, replacement);
                AmmoType.AmmoClip newClip = new AmmoType.AmmoClip
                {
                    Name = oldClip.Name,
                    Capacity = oldClip.Capacity,
                    MinimalPattern = pattern
                };
                newClips[i] = newClip;
                map[oldClip] = newClip;
            }
            rack.ClipTypes = newClips;

            if (rack.StoredClips != null)
            {
                for (int i = 0; i < rack.StoredClips.Count; i++)
                {
                    AmmoType.AmmoClip stored = rack.StoredClips[i];
                    AmmoType.AmmoClip mapped;
                    if (stored != null && map.TryGetValue(stored, out mapped)) rack.StoredClips[i] = mapped;
                    else if (stored != null)
                    {
                        rack.StoredClips[i] = new AmmoType.AmmoClip
                        {
                            Name = stored.Name,
                            Capacity = stored.Capacity,
                            MinimalPattern = ClonePattern(stored.MinimalPattern, source, replacement)
                        };
                    }
                }
            }
            return map;
        }

        private static AmmoCodexScriptable[] ClonePattern(
            AmmoCodexScriptable[] pattern, AmmoType source, AmmoType replacement)
        {
            if (pattern == null) return null;
            AmmoCodexScriptable[] result = new AmmoCodexScriptable[pattern.Length];
            for (int i = 0; i < pattern.Length; i++)
            {
                AmmoCodexScriptable codex = pattern[i];
                if (codex == null) continue;
                if (codex.AmmoType == null || codex.AmmoType.Guidance == AmmoType.GuidanceType.Unguided)
                {
                    result[i] = codex;
                    continue;
                }
                AmmoCodexScriptable clone = UnityEngine.Object.Instantiate(codex);
                OwnedAmmoCodices.Add(clone);
                clone.name = codex.name + "_LOSAT_KE_Test";
                clone.hideFlags = HideFlags.HideAndDontSave;
                if (codex.AmmoType == source) clone.AmmoType = replacement;
                else
                {
                    clone.AmmoType = CloneAmmoType(codex.AmmoType);
                    ConfigureKineticRound(clone.AmmoType,
                        Mathf.Clamp(codex.AmmoType.MuzzleVelocity, 1f, MaximumSpeed));
                }
                result[i] = clone;
            }
            return result;
        }

        private static void ReplaceFeedState(AmmoFeed feed, AmmoType source, AmmoType replacement,
            Dictionary<AmmoType.AmmoClip, AmmoType.AmmoClip> clipMap)
        {
            if (feed.AmmoTypeInBreech == source) SetField(feed, "<AmmoTypeInBreech>k__BackingField", replacement);
            ReplaceQueue(feed, "_feedClipMain", source, replacement);
            ReplaceQueue(feed, "_feedClipAux", source, replacement);
            ReplaceClipBacking(feed, "<LoadedClipType>k__BackingField", clipMap);
            ReplaceClipBacking(feed, "<QueuedClipType>k__BackingField", clipMap);
            ReplaceClipBacking(feed, "_queuedClipTypeLockedIn", clipMap);
        }

        private static void ReplaceQueue(object owner, string fieldName, AmmoType source, AmmoType replacement)
        {
            Queue<AmmoType> queue = GetField(owner, fieldName) as Queue<AmmoType>;
            if (queue == null || queue.Count == 0) return;
            AmmoType[] items = queue.ToArray();
            queue.Clear();
            foreach (AmmoType item in items) queue.Enqueue(item == source ? replacement : item);
        }

        private static void ReplaceClipBacking(object owner, string fieldName,
            Dictionary<AmmoType.AmmoClip, AmmoType.AmmoClip> map)
        {
            AmmoType.AmmoClip current = GetField(owner, fieldName) as AmmoType.AmmoClip;
            AmmoType.AmmoClip replacement;
            if (current != null && map.TryGetValue(current, out replacement)) SetField(owner, fieldName, replacement);
        }

        private void TrackMissile(LiveRound round, AmmoType vehicleAmmo)
        {
            if (round == null) return;
            try
            {
                float launchSpeed = Mathf.Clamp(vehicleAmmo.MuzzleVelocity, 1f, MaximumSpeed);
                AmmoType perMissile = CloneAmmoType(vehicleAmmo);
                perMissile.MuzzleVelocity = launchSpeed;
                round.Info = perMissile;
                round.CurrentSpeed = launchSpeed;
                round.MaxSpeed = MaximumSpeed;
                round.UseGravity = false;
                RestoreNativeTowSmoke(round);
                MuteNativeRocketLoop(round);
                TrackedMissile tracked = new TrackedMissile
                {
                    Round = round,
                    RoundId = round.ID,
                    RoundTransform = round.transform,
                    Ammo = perMissile,
                    LastPosition = round.transform.position,
                    Distance = 0f,
                    LaunchSpeed = launchSpeed,
                    FlightSpeed = launchSpeed
                };
                _missiles.Add(tracked);
                _missileLookup[round]=tracked;
            }
            catch (Exception ex)
            {
                MelonLogger.Error(Prefix + "could not attach the LOSAT speed profile: " + ex);
            }
        }

        private static void RemoveMissileBeam(LiveRound round)
        {
            foreach (GHPC.Effects.DynamicTracer tracer in round.GetComponentsInChildren<GHPC.Effects.DynamicTracer>(true))
            {
                tracer.enabled = false;
                // Active=false hides the whole visual tree, including TOW smoke.
                if (tracer.VisualTransform != null)
                    foreach(Renderer renderer in tracer.VisualTransform.GetComponentsInChildren<Renderer>(true))
                        if(!(renderer is ParticleSystemRenderer)) renderer.enabled=false;
            }

            foreach (TrailRenderer trail in round.GetComponentsInChildren<TrailRenderer>(true))
            {
                if (trail == null) continue;
                trail.Clear();
                trail.enabled = false;
            }
        }

        private void UpdateMissiles()
        {
            for (int i = _missiles.Count - 1; i >= 0; i--)
            {
                TrackedMissile tracked = _missiles[i];
                LiveRound round = tracked.Round;
                if (round == null || round.Pooled || round.IsDestroyed || round.ID != tracked.RoundId)
                {
                    FinishGuidance(tracked,false);
                    TrackedMissile current;
                    if(!ReferenceEquals(round,null) && _missileLookup.TryGetValue(round,out current) && current==tracked) _missileLookup.Remove(round);
                    _missiles.RemoveAt(i);
                    continue;
                }

                Vector3 position = tracked.RoundTransform.position;
                UpdateGuidanceChannel(tracked);
                // Native penetration speed loss must survive the next frame.
                if (!MayRefreshMissileMotor(tracked)) continue;
                float step = Vector3.Distance(tracked.LastPosition, position);
                if (!float.IsNaN(step) && !float.IsInfinity(step) && step < 500f) tracked.Distance += step;
                tracked.LastPosition = position;

                float ratio = Mathf.Clamp01(tracked.Distance / AccelerationDistance);
                float speed = Mathf.Lerp(tracked.LaunchSpeed, MaximumSpeed, ratio);
                tracked.FlightSpeed = speed;
                tracked.Ammo.MuzzleVelocity = speed;
                round.CurrentSpeed = speed;
                round.MaxSpeed = MaximumSpeed;

                if (!tracked.ReportedMaximum && ratio >= 1f)
                {
                    tracked.ReportedMaximum = true;
                }
            }
        }

        private void MaintainMissileSpeed()
        {
            // Preserve the native pre-projectile speed refresh without calculating
            // distance, reading transforms or evaluating the acceleration curve twice.
            foreach (TrackedMissile tracked in _missiles)
            {
                LiveRound round = tracked.Round;
                if (round == null || round.Pooled || round.IsDestroyed || round.ID != tracked.RoundId) continue;
                if (!MayRefreshMissileMotor(tracked)) continue;
                round.CurrentSpeed = tracked.FlightSpeed;
                round.MaxSpeed = MaximumSpeed;
            }
        }

        private static object GetField(object owner, string fieldName)
        {
            if (owner == null) return null;
            FieldInfo field = owner.GetType().GetField(fieldName, AnyInstance);
            return field == null ? null : field.GetValue(owner);
        }

        private static void SetField(object owner, string fieldName, object value)
        {
            if (owner == null) return;
            FieldInfo field = owner.GetType().GetField(fieldName, AnyInstance);
            if (field != null) field.SetValue(owner, value);
        }

        private sealed class TrackedMissile
        {
            public WeaponSystem SourceWeapon;
            public bool GuidanceReleased;
            public bool NativeImpact;
            public bool GuidanceObscured;
            public float NextGuidanceVisibility;
            public float HardOccludedSince=-1f;
            public bool UseShallowArc;
            public bool ManualSightGuidance;
            public bool ManualAimValid;
            public float ManualAimDistance;
            public float NextManualAim;
            public bool ManualCommandValid;
            public float ManualCommandTime;
            public Vector3 ManualCommand;
            public bool ManualArcStarted;
            public bool ManualArcEnded;
            public Vector3 ManualArcAxis;
            public float ManualArcRange;
            public bool PassedTarget;
            public Vector3 LaunchPosition;
            public bool ImpactShown;
            public bool TankImpactShown;
            public MissileTargetAssignment TargetAssignment;
            public TargetLock AttackSelection;
            public Vehicle TargetVehicle;
            public Vector3 TargetLocalPoint;
            public Vector3 LastTargetPosition;
            public MissileGuidanceUnit TargetGuidance;
            public LiveRound Round;
            public int RoundId;
            public Transform RoundTransform;
            public AmmoType Ammo;
            public Vector3 LastPosition;
            public float Distance;
            public float LaunchSpeed;
            public float FlightSpeed;
            public bool ReportedMaximum;
            public SonicBoomState SonicBoom;
        }
    }
}

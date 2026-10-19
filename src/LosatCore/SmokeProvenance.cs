using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GHPC.Weaponry;
using HarmonyLib;
using UnityEngine;
using MelonLoader;
namespace M8LOSATM2.LosatCore
{
    // Attached only to a real deployed smoke grenade's effect instance. Never
    // modify shared smoke prefabs, particle emitters, native AI or global LOS.
    internal sealed class LockSmokeSource:MonoBehaviour {}
    internal sealed partial class LosatService
    {
        [ThreadStatic] private static bool _deployingSmoke;
        private static Type _sharedSmokeMarkerType;
        private void InstallSmokeSourceHook()
        {
            // All MOD assemblies are loaded before their initializers. Hook the
            // existing HMMWV factory, not Explode a second time. This works even
            // if M8 initializes first: HMMWV can still rewrite the untouched
            // native Instantiate call later. No reference to its DLL is needed.
            _sharedSmokeMarkerType=null;
            MethodInfo shared=FindSharedSmokeFactory();
            if(shared!=null)
            {
                HarmonyInstance.Patch(shared,
                    postfix:new HarmonyMethod(typeof(LosatService),nameof(SharedSmokeEffectPostfix)));
                MelonLogger.Msg(Prefix+"smoke source: sharing HMMWV factory; no duplicate native smoke transpiler.");
                return;
            }
            HarmonyInstance.Patch(AccessTools.Method(typeof(BasicGrenadeExplosionBehaviour),"Explode"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(SmokeDeployPrefix)),
                transpiler:new HarmonyMethod(typeof(LosatService),nameof(TrackSmokeInstantiation)),
                finalizer:new HarmonyMethod(typeof(LosatService),nameof(SmokeDeployFinalizer)));
            MelonLogger.Msg(Prefix+"smoke source: standalone native factory.");
        }
        private static MethodInfo FindSharedSmokeFactory()
        {
            foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type owner=assembly.GetType("M151LOSATTest.Mod",false);
                Type marker=assembly.GetType("M151LOSATTest.LockSmokeSource",false);
                if(owner==null||marker==null||!typeof(MonoBehaviour).IsAssignableFrom(marker))continue;
                MethodInfo factory=owner.GetMethod("InstantiateSmokeEffect",BindingFlags.Static|BindingFlags.NonPublic,
                    null,new[]{typeof(GameObject),typeof(Vector3),typeof(Quaternion)},null);
                if(factory==null||factory.IsGenericMethod||factory.ReturnType!=typeof(GameObject))continue;
                _sharedSmokeMarkerType=marker;
                return factory;
            }
            return null;
        }
        private static void SharedSmokeEffectPostfix(GameObject __result)
        {
            // The provider already distinguished real smoke from blast dust.
            // Tag only that exact returned instance; never instantiate again,
            // scan the world, change prefabs or touch the provider's marker.
            if(__result!=null&&_sharedSmokeMarkerType!=null&&__result.GetComponent(_sharedSmokeMarkerType)!=null&&
                __result.GetComponent<LockSmokeSource>()==null)__result.AddComponent<LockSmokeSource>();
        }
        private static void SmokeDeployPrefix(Grenade __0,out bool __state)
        {__state=_deployingSmoke;_deployingSmoke=__0 is SmokeGrenade;}
        private static void SmokeDeployFinalizer(bool __state) {_deployingSmoke=__state;}
        private static IEnumerable<CodeInstruction> TrackSmokeInstantiation(IEnumerable<CodeInstruction> instructions)
        {
            int replaced=0;
            foreach(CodeInstruction op in instructions)
            {
                MethodInfo call=op.operand as MethodInfo;
                if(op.opcode==OpCodes.Call && call!=null && call.DeclaringType==typeof(UnityEngine.Object) &&
                    call.Name=="Instantiate" && call.IsGenericMethod && call.GetGenericArguments()[0]==typeof(GameObject) &&
                    call.GetParameters().Length==3 && call.GetParameters()[1].ParameterType==typeof(Vector3) &&
                    call.GetParameters()[2].ParameterType==typeof(Quaternion))
                {op.operand=AccessTools.Method(typeof(LosatService),nameof(InstantiateSmokeEffect));replaced++;}
                yield return op;
            }
            if(replaced!=1) throw new InvalidOperationException("Native smoke effect instantiation changed; source tagging cannot be verified.");
        }
        private static GameObject InstantiateSmokeEffect(GameObject prefab,Vector3 position,Quaternion rotation)
        {
            GameObject effect=UnityEngine.Object.Instantiate(prefab,position,rotation);
            if(_deployingSmoke && effect!=null) effect.AddComponent<LockSmokeSource>();
            return effect;
        }
    }
}

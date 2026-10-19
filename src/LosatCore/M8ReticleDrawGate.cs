using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace M8LOSATM2.LosatCore
{
    // PostMeshRenderer deliberately draws registered renderers even when normal
    // visibility flags are off. Filter only owned obsolete reticle subtrees at
    // that final draw call, including stale registrations and late child meshes.
    internal static class M8ReticleDrawGate
    {
        private static readonly HashSet<Transform> Roots=new HashSet<Transform>();
        private static readonly MethodInfo NativeDraw=typeof(CommandBuffer).GetMethod("DrawRenderer",new[]{typeof(Renderer),typeof(Material),typeof(int),typeof(int)});
        private static readonly MethodInfo FilteredDraw=typeof(M8ReticleDrawGate).GetMethod("Draw",BindingFlags.Static|BindingFlags.NonPublic);
        private static bool _reported;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                if(NativeDraw==null||FilteredDraw==null)throw new MissingMethodException("Native reticle draw signature changed.");
                var target=AccessTools.Method(typeof(PostMeshRenderer),"Render");
                var rewrite=AccessTools.Method(typeof(M8ReticleDrawGate),"Transpiler");
                if(target==null||rewrite==null)throw new MissingMethodException("Native reticle rendering entry unavailable.");
                var patches=HarmonyLib.Harmony.GetPatchInfo(target);
                if(patches!=null&&patches.Transpilers.Any(p=>p.owner==harmony.Id&&p.PatchMethod==rewrite))return;
                harmony.Patch(target,transpiler:new HarmonyMethod(rewrite));
                MelonLogger.Msg("[M8 LOSAT Fire Control] owned reticle draw gate installed.");
            }
            catch(Exception e){MelonLogger.Warning("[M8 LOSAT Fire Control] reticle draw gate unavailable: "+e.Message);}
        }
        internal static void AddRoot(Transform root){if(root!=null)Roots.Add(root);}
        internal static void Clear(){Roots.Clear();_reported=false;}
        private static bool Owned(Renderer renderer)
        {
            if(renderer==null)return false;
            for(var node=renderer.transform;node!=null;node=node.parent)if(Roots.Contains(node))return true;
            return false;
        }
        private static void Draw(CommandBuffer command,Renderer renderer,Material material,int submesh,int pass)
        {
            if(Owned(renderer))
            {
                if(!_reported){_reported=true;MelonLogger.Msg("[M8 LOSAT Fire Control] blocked obsolete native reticle draw: "+renderer.name);}
                return;
            }
            command.DrawRenderer(renderer,material,submesh,pass);
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
        {
            var code=new List<CodeInstruction>(input);
            var matches=code.Where(i=>(i.opcode==OpCodes.Callvirt||i.opcode==OpCodes.Call)&&Equals(i.operand,NativeDraw)).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("Expected exactly one native reticle DrawRenderer call.");
            // Same stack signature including the former instance receiver; labels
            // and exception blocks stay on the original instruction object.
            matches[0].opcode=OpCodes.Call;matches[0].operand=FilteredDraw;
            return code;
        }
    }
}

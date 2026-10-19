using UnityEngine;
using Reticle;

namespace M8LOSATM2.LosatCore
{
    internal sealed class NativeReticleVisibility
    {
        internal readonly Renderer Renderer;
        private PostMeshComp _post;
        private bool _hidden,_enabledBefore,_forceBefore,_postEnabledBefore;
        internal NativeReticleVisibility(Renderer renderer)
        {Renderer=renderer;RefreshPostProcessor();}
        internal bool HasPostProcessor {get {return _post!=null;}}
        internal void RefreshPostProcessor()
        {
            if(Renderer==null || _post!=null) return;
            _post=Renderer.GetComponent<PostMeshComp>();
            if(_post!=null && _hidden)
            {
                _postEnabledBefore=_post.enabled;
                // Native OnEnable disables ordinary rendering before registering it.
                if(_post.enabled) _enabledBefore=false;
                Hide();
            }
        }
        internal void Hide()
        {
            if(Renderer==null) return;
            if(!_hidden)
            {
                _enabledBefore=Renderer.enabled;_forceBefore=Renderer.forceRenderingOff;
                _postEnabledBefore=_post!=null && _post.enabled;
            }
            // PostMeshRenderer explicitly DrawRenderer's its registry regardless of
            // Renderer.enabled. OnDisable removes this one entry from that registry.
            // OnDisable also re-enables Renderer, so suppress it AFTER unregistering.
            if(_post!=null && _post.enabled) _post.enabled=false;
            Renderer.enabled=false;Renderer.forceRenderingOff=true;_hidden=true;
        }
        internal void Restore()
        {
            if(!_hidden || Renderer==null) return;
            // OnEnable registers again and disables Renderer: restore the saved
            // ordinary-renderer flags afterwards, in this order, only once.
            if(_post!=null) _post.enabled=_postEnabledBefore;
            Renderer.enabled=_enabledBefore;Renderer.forceRenderingOff=_forceBefore;
            _hidden=false;
        }
    }
}

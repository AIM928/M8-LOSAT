using GHPC.Camera;
using GHPC.Thermals;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private void InstallSightFrameProjection()
        {
            Camera.onPostRender-=CaptureSightFrames;
            Camera.onPostRender+=CaptureSightFrames;
        }
        private void CaptureSightFrames(Camera rendered)
        {
            if(_targetLocks.Count==0) return;
            OpticsUpgrade sight=CurrentLockSight();
            Camera main=CameraManager.MainCam;
            if(sight==null || sight!=_lockOwner || main==null) return;
            CameraSlot slot=CameraSlot.ActiveInstance;
            Camera image=main;
            if(slot==sight.ThermalSlot)
            {
                FLIRCamera flir=FLIRCamera.Instance;
                if(flir==null || !flir.enabled) return;
                // Native FLIRCamera.Awake uses this same component. It renders
                // manually in LateUpdate; the main camera is not the image pose.
                image=flir.GetComponent<Camera>();
            }
            if(rendered==null || rendered!=image) return;
            Rect pixels=main.pixelRect;
            HudBox view=new HudBox(pixels.x,Screen.height-pixels.yMax,pixels.width,pixels.height);
            for(int i=0;i<_targetLocks.Count;i++)
            {
                TargetLock target=_targetLocks[i];
                if(target.Vehicle==null) {target.RenderOnScreen=false;continue;}
                // Keep the original lost-target policy: no live hidden-target
                // coordinates. Only the last confirmed frame may blink.
                if(!target.Visible) {if(target.RenderFrame>=0) target.RenderFrame=Time.frameCount;continue;}
                ProjectRenderedFrame(image,target,view);
                target.RenderFrame=Time.frameCount;
            }
        }
        private static void ProjectRenderedFrame(Camera image,TargetLock target,HudBox view)
        {
            Vector3 center=image.WorldToViewportPoint(target.Vehicle.transform.TransformPoint(target.LocalAimPoint));
            target.RenderOnScreen=center.z>0f && center.x>=0f && center.x<=1f && center.y>=0f && center.y<=1f;
            if(!target.RenderOnScreen) return;
            float minX=float.PositiveInfinity,minY=float.PositiveInfinity,maxX=float.NegativeInfinity,maxY=float.NegativeInfinity;
            for(int i=0;i<8;i++)
            {
                Vector3 local=target.LocalBounds.center+Vector3.Scale(target.LocalBounds.extents,
                    new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                Vector3 p=image.WorldToViewportPoint(target.Vehicle.transform.TransformPoint(local));
                if(p.z<=0f) continue;
                float x=SightFrameProjectionRules.GuiX(p.x,view),y=SightFrameProjectionRules.GuiY(p.y,view);
                minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);
                minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);
            }
            if(float.IsInfinity(minX) || float.IsInfinity(minY)) {target.RenderOnScreen=false;return;}
            float w=SightHudRules.FrameWidth(maxX-minX),h=SightHudRules.FrameHeight(maxY-minY);
            target.RenderScreenRect=new Rect((minX+maxX-w)*.5f,(minY+maxY-h)*.5f,w,h);
        }
        private static bool RenderedFrameAvailable(TargetLock target)
        {return target.RenderOnScreen && SightFrameProjectionRules.Fresh(target.RenderFrame,Time.frameCount);}
        private static Rect RenderedLockDisplayRect(TargetLock target)
        {
            Rect r=target.RenderScreenRect;
            if(!target.Reacquiring) return r;
            float x=LockRecoveryRules.SearchPadding(r.width),y=LockRecoveryRules.SearchPadding(r.height);
            return new Rect(r.x-x,r.y-y,r.width+2f*x,r.height+2f*y);
        }
    }
}

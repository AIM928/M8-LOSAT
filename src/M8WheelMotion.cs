using System;
namespace M8LOSATM2
{
    internal static class M8WheelMotion
    {
        internal const float VisualRadius=.304f;
        internal static float Distance(float angularVelocity,float nativeRadius,float dt)
        {
            if(!Finite(angularVelocity)||!Finite(nativeRadius)||!Finite(dt)||nativeRadius<=0||dt<=0)return 0;
            float distance=angularVelocity*nativeRadius*dt;
            return Finite(distance)&&Math.Abs(distance)<2f?distance:0f;
        }
        internal static float AdvanceAngle(float previous,float distance)
        {
            if(!Finite(previous))previous=0f;
            if(!Finite(distance))return previous;
            float angle=(previous+distance/VisualRadius*(180f/(float)Math.PI))%360f;
            return angle<0?angle+360f:angle;
        }
        private static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
}

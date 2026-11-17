#ifndef FISHMMO_TARGET_OUTLINE_MASK_INCLUDED
#define FISHMMO_TARGET_OUTLINE_MASK_INCLUDED

// The target outline's mask fragment (TargetOutlineFeature), shared so a shader that deforms its vertices can draw its own
// silhouette into the mask: add a pass tagged "LightMode" = "FishOutlineMask" whose vertex poses the mesh exactly as the
// lit pass does and whose fragment returns FishOutlineMaskFragment. The feature draws that pass instead of the generic
// mask (which knows nothing of the pose: a dead animal's outline stood up, 11-16). The tag is unknown to URP, so the pass
// is never drawn by anything else.

TEXTURE2D(_FishOutlineDepth);

// Mask pass: rgb the outline colour (linear), a its width / _FishOutlineSize.y.
float4 _FishOutlineMaskColor;
// x: 1 to draw the whole silhouette over whatever is in front of it.
float4 _FishOutlineMaskParams;

// The parts behind the scene's depth are left out unless the outline shows through walls.
half4 FishOutlineMaskFragment(float4 positionCS)
{
    if (_FishOutlineMaskParams.x < 0.5)
    {
        float scene = LOAD_TEXTURE2D(_FishOutlineDepth, uint2(positionCS.xy)).r;
        float self = positionCS.z;
        if (unity_OrthoParams.w > 0.5)
        {
            #if UNITY_REVERSED_Z
            if (scene - self > 1e-4) discard;
            #else
            if (self - scene > 1e-4) discard;
            #endif
        }
        else
        {
            // Its own surface is in the depth texture too: a little slack for the copy and for MSAA edges.
            float sceneEye = LinearEyeDepth(scene, _ZBufferParams);
            float selfEye = LinearEyeDepth(self, _ZBufferParams);
            if (selfEye > sceneEye * 1.005 + 0.05) discard;
        }
    }
    return half4(_FishOutlineMaskColor);
}

#endif

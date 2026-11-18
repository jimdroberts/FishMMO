#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Shared.CustomBuildTool
{
	/// <summary>Keep shadow modes supported by at least one fixed player quality profile. Editor cascade preview remains unrestricted.</summary>
	public sealed class QualityShadowVariantFilter : IPreprocessShaders
	{
	    public int callbackOrder => int.MaxValue-2;
	    public void OnProcessShader(Shader shader,ShaderSnippetData snippet,IList<ShaderCompilerData> variants)
	    {
	        if (!AssetDatabase.GetAssetPath(shader).StartsWith("Assets/Prefabs/Client/Weather/Shaders/", System.StringComparison.Ordinal)) return;
	        var profiles=new List<(int cascades,bool additional,bool ao)>();
	        var assets=new HashSet<RenderPipelineAsset>{GraphicsSettings.defaultRenderPipeline};
	        for(int i=0;i<QualitySettings.names.Length;i++)assets.Add(QualitySettings.GetRenderPipelineAssetAt(i));
	        foreach(var asset in assets)
	        {
	            if(asset==null)continue;
	            if(!(asset is UniversalRenderPipelineAsset urp))return;
	            if(!urp.supportsMainLightShadows)continue;
	            bool ao=false;
	            using(var serialized=new SerializedObject(urp))
	            {
	                var renderers=serialized.FindProperty("m_RendererDataList");
	                if(renderers==null)ao=true;
	                else for(int r=0;r<renderers.arraySize;r++)
	                    if(renderers.GetArrayElementAtIndex(r).objectReferenceValue is ScriptableRendererData data)
	                        foreach(var feature in data.rendererFeatures)
	                        {
	                            if(feature==null || !feature.isActive || feature.GetType().Name!="ScreenSpaceAmbientOcclusion")continue;
	                            using(var settings=new SerializedObject(feature))
	                            {
	                                var after=settings.FindProperty("m_Settings.AfterOpaque");
	                                ao|=after==null || !after.boolValue;
	                            }
	                        }
	            }
	            profiles.Add((urp.shadowCascadeCount,urp.supportsAdditionalLightShadows,ao));
	        }
	        if(profiles.Count==0)return;
	        var cascades=new ShaderKeyword(shader,"_MAIN_LIGHT_SHADOWS_CASCADE");
	        var single=new ShaderKeyword(shader,"_MAIN_LIGHT_SHADOWS");
	        var additional=new ShaderKeyword(shader,"_ADDITIONAL_LIGHT_SHADOWS");
	        var occlusion=new ShaderKeyword(shader,"_SCREEN_SPACE_OCCLUSION");
	        for(int i=variants.Count-1;i>=0;i--)
	        {
	            var v=variants[i];if(v.shaderCompilerPlatform!=ShaderCompilerPlatform.WebGPU)continue;
	            bool c=v.shaderKeywordSet.IsEnabled(cascades),s=v.shaderKeywordSet.IsEnabled(single);
	            if(!c&&!s)continue;
	            bool a=v.shaderKeywordSet.IsEnabled(additional),o=v.shaderKeywordSet.IsEnabled(occlusion);
	            if(!profiles.Any(p=>(p.cascades>1)==c && (!a||p.additional) && (!o||p.ao)))variants.RemoveAt(i);
	        }
	    }
	}

}
#endif

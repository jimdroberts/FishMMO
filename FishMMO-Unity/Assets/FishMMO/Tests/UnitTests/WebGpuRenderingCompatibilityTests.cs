using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Client;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
    public class WebGpuRenderingCompatibilityTests
    {

        [Test]
        public void LoadedBilinearHeightsPreserveFloatPrecisionSlicesAndClampEdges()
        {
            if(!SystemInfo.supportsComputeShaders)Assert.Ignore("GPU required");
            const int size=9,count=4096;
            var texture=new Texture2DArray(size,size,2,TextureFormat.RFloat,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ProjectFiles.Path("Assets/FishMMO/Tests/UnitTests/Probes/GroundHeightLoadsProbe.compute")));
            try
            {
                var random=new System.Random(3771);var values=new float[2][];
                for(int slice=0;slice<2;slice++){values[slice]=Enumerable.Range(0,size*size).Select(_=>(float)random.NextDouble()).ToArray();texture.SetPixelData(values[slice],0,slice);}
                texture.Apply();
                var points=new Vector2[count];
                for(int i=0;i<count;i++)points[i]=i<8?new Vector2((i&1)==0?0:1,(i&2)==0?0:1):new Vector2((float)random.NextDouble()*2-0.5f,(float)random.NextDouble()*2-0.5f);
                using var input=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,8);input.SetData(points);
                using var output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,8);
                int kernel=shader.FindKernel("Probe");shader.SetTexture(kernel,"_Heights",texture);shader.SetBuffer(kernel,"_Points",input);shader.SetBuffer(kernel,"_Results",output);shader.SetInt("_Count",count);shader.SetInt("_Size",size);shader.Dispatch(kernel,count/64,1,1);
                var actual=new Vector2[count];output.GetData(actual);
                for(int i=0;i<count;i++)
                {
                    float px=Mathf.Clamp01(points[i].x)*(size-1),py=Mathf.Clamp01(points[i].y)*(size-1);int x=(int)px,y=(int)py,x1=Math.Min(x+1,size-1),y1=Math.Min(y+1,size-1);var a=values[i%2];
                    float expected=Mathf.Lerp(Mathf.Lerp(a[y*size+x],a[y*size+x1],px-x),Mathf.Lerp(a[y1*size+x],a[y1*size+x1],px-x),py-y);
                    Assert.That(actual[i].x,Is.EqualTo(expected).Within(5e-7f),"float interpolation "+i);
                    // Hardware filtering can quantize interpolation weights; the load path retains full float weights.
                    Assert.That(actual[i].x,Is.EqualTo(actual[i].y).Within(1f/256+5e-7f),"hardware interpolation "+i);
                }
            }
            finally{Object.DestroyImmediate(shader);Object.DestroyImmediate(texture);}
        }

        /// <summary>WebGPU's guaranteed maxUniformBuffersPerShaderStage: a stage needing more cannot create its bind group layout.</summary>
        const int WebGpuUniformBuffersPerStage = 12;
        static readonly string[] Twins = {"FishWeatherLitIndirect","FishVegetationIndirect","FishVegetationPrime"};
        // The heaviest lighting state the browser scene runs (Forward+, cascades, additional-light shadows, probe blending,
        // SSAO, light layers) and the material features that add bindings.
        static readonly string[] HeavyKeywords = {"_CLUSTER_LIGHT_LOOP","_MAIN_LIGHT_SHADOWS_CASCADE","_ADDITIONAL_LIGHT_SHADOWS","_SHADOWS_SOFT",
            "_REFLECTION_PROBE_BLENDING","_REFLECTION_PROBE_BOX_PROJECTION","_REFLECTION_PROBE_ATLAS","_SCREEN_SPACE_OCCLUSION","_LIGHT_LAYERS",
            "_NORMALMAP","_EMISSION","_ALPHATEST_ON","_FISH_LOOK_TABLE","_FISH_LOOK_ARRAY","_CASTING_PUNCTUAL_LIGHT_SHADOW"};

        static string[] Buffers(ShaderData.Pass pass,ShaderType stage,string[] keywords,ShaderCompilerPlatform api,BuildTarget target,string label)
        {
            var result=pass.CompileVariant(stage,keywords,api,target);
            Assert.That(result.Success,Is.True,label+": "+string.Join("\n",result.Messages.Select(m=>m.message)));
            var names=result.ConstantBuffers.Select(b=>b.Name+"("+b.Size+")").ToArray();
            Debug.Log("[WebGPU uniform budget] "+label+" "+api+" "+stage+" "+names.Length+": "+string.Join(", ",names));
            return result.ConstantBuffers.Select(b=>b.Name).ToArray();
        }

        // Unity compiles a WebGPU program's two stages together and reports the UNION of their constant buffers (the Fragment
        // query is empty). Each stage's own $Globals is VGlobals<hash> / PGlobals<hash>, so a stage binds at most the union
        // without the other stage's globals: that bound is what must fit (the browser counts per stage, the original
        // "uniform buffers (13) in the Fragment stage" error).
        static void AssertStagesFit(string[] union,string label)
        {
            Assert.That(union.Length,Is.GreaterThan(0),label+": no reflection, the budget would pass vacuously");
            int fragment=union.Count(n=>!n.StartsWith("VGlobals")),vertex=union.Count(n=>!n.StartsWith("PGlobals"));
            Assert.That(fragment,Is.LessThanOrEqualTo(WebGpuUniformBuffersPerStage),label+" fragment bound: "+string.Join(", ",union));
            Assert.That(vertex,Is.LessThanOrEqualTo(WebGpuUniformBuffersPerStage),label+" vertex bound: "+string.Join(", ",union));
        }

        static IEnumerable<ShaderData.Pass> Passes(string name,Func<string,bool> wanted)
        {
            var data=ShaderUtil.GetShaderData(AssetDatabase.LoadAssetAtPath<Shader>(ProjectFiles.Path("Assets/Prefabs/Client/Weather/Shaders/"+name+".shader"))).GetSubshader(0);
            for(int pass=0;pass<data.PassCount;pass++)if(wanted(data.GetPass(pass).Name))yield return data.GetPass(pass);
        }

        // The BatchRendererGroup path on WebGPU (TerrainGpuRenderer.SupportsBrg): every twin pass's DOTS variant within the
        // 12 uniform buffers a WebGPU stage may bind, without the two metadata cbuffers that made the lit fragment 13.
        [TestCase(false)]
        [TestCase(true)]
        public void TwinDotsVariantsFitWebGpuUniformBuffersPerStage(bool firstInstance)
        {
            foreach(string name in Twins)
                foreach(var p in Passes(name,n=>n=="ForwardLit"||n=="ShadowCaster"||n=="DepthOnly"||n=="DepthNormals"||n=="VegetationPrime"))
                {
                    var keywords=HeavyKeywords.Concat(new[]{"DOTS_INSTANCING_ON"});
                    // GPU-drawn plants never cast into a point or spot light (FishVegetationPasses.hlsl): that caster is an empty
                    // program, which reads no instance data at all. The directional caster is the one to check.
                    if(name=="FishVegetationIndirect"&&p.Name=="ShadowCaster")keywords=keywords.Where(k=>k!="_CASTING_PUNCTUAL_LIGHT_SHADOW");
                    if(firstInstance)keywords=keywords.Concat(new[]{WebGpuDrawCapabilities.FirstInstanceKeyword});
                    string label=name+" "+p.Name+" DOTS";
                    var union=Buffers(p,ShaderType.Vertex,keywords.ToArray(),ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL,label);
                    AssertStagesFit(union,label);
                    Assert.That(union.Any(n=>n.Contains("MaterialPropertyMetadata")||n.Contains("UserPropertyMetadata")),Is.False,label+" binds a metadata cbuffer WebGPU drops");
                    Assert.That(union.Any(n=>n.Contains("BuiltinPropertyMetadata")),Is.True,label+" is not a DOTS program: "+string.Join(", ",union));
                }
        }

        // The same budget for the RenderMeshIndirect (procedural) variants, which every WebGPU device can still fall back to.
        [TestCase(false)]
        [TestCase(true)]
        public void TwinProceduralVariantsFitWebGpuUniformBuffersPerStage(bool firstInstance)
        {
            foreach(string name in Twins)
                foreach(var p in Passes(name,n=>n=="ForwardLit"||n=="ShadowCaster"||n=="DepthOnly"||n=="VegetationPrime"))
                {
                    var keywords=HeavyKeywords.Concat(new[]{"PROCEDURAL_INSTANCING_ON"});
                    if(firstInstance)keywords=keywords.Concat(new[]{WebGpuDrawCapabilities.FirstInstanceKeyword});
                    string label=name+" "+p.Name+" procedural";
                    AssertStagesFit(Buffers(p,ShaderType.Vertex,keywords.ToArray(),ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL,label),label);
                }
        }

        // Native keeps its DOTS interface unchanged: the material and user metadata stay (per-instance overrides possible).
        // Vulkan reports no constant-buffer reflection here, so this reads the preprocessed source; WebGPU's absence of both
        // is asserted on its compiled reflection (TwinDotsVariantsFitWebGpuUniformBuffersPerStage).
        [Test]
        public void NativeDotsVariantsKeepTheirMetadataCbuffers()
        {
            var keywords=HeavyKeywords.Concat(new[]{"DOTS_INSTANCING_ON"}).ToArray();
            foreach(var p in Passes("FishWeatherLitIndirect",n=>n=="ForwardLit"))
            {
                string native=p.PreprocessVariant(ShaderType.Vertex,keywords,ShaderCompilerPlatform.Vulkan,BuildTarget.StandaloneLinux64,false).PreprocessedCode;
                // The preprocessed program is the whole pass source (both entry points), whichever stage is asked for.
                Assert.That(native.Length,Is.GreaterThan(1000),"no Vulkan preprocessed source");
                Assert.That(native,Does.Contain("UnityDOTSInstancing_MaterialPropertyMetadata"),"Vulkan lost the material metadata");
                Assert.That(native,Does.Contain("UnityDOTSInstancing_UserPropertyMetadata"),"Vulkan lost the user metadata");
                return;
            }
            Assert.Fail("no ForwardLit pass");
        }

        /// <summary>
        /// WebGPU's maxDynamicUniformBuffersPerPipelineLayout on the tested device (and its adapter's maximum). Unity's WebGPU
        /// backend gives only a pipeline's first this-many uniform bindings a dynamic offset; every later one becomes a
        /// dedicated buffer re-created on each pass application .
        /// </summary>
        const int WebGpuDynamicUniformBuffersPerPipeline = 10;
        /// <summary>The cbuffers URP's LIGHT_SHADOWS_NO_CBUFFER turns into plain globals (FishShadowVariants.hlsl on WebGPU).</summary>
        static readonly string[] LightingCbuffers = {"LightShadows","LightCookies","AdditionalLights","urp_ReflectionProbeBuffer"};

        // The hot browser forward pipelines (ranked by the uniform audit, Scripts/Rendering/audit_webgpu_uniforms.mjs): every
        // uniform binding of the pipeline (vertex ∪ fragment, which is what Unity reports for WebGPU) within the dynamic limit.
        [TestCase("FishWeatherLitIndirect","ForwardLit","DOTS_INSTANCING_ON")]
        [TestCase("FishWeatherLitIndirect","ForwardLit","PROCEDURAL_INSTANCING_ON")]
        [TestCase("FishVegetationIndirect","ForwardLit","DOTS_INSTANCING_ON")]
        [TestCase("FishVegetationIndirect","ForwardLit","PROCEDURAL_INSTANCING_ON")]
        [TestCase("FishWeatherLit","ForwardLit",null)]
        [TestCase("FishVegetation","ForwardLit",null)]
        [TestCase("FishWeatherTerrainArrayGpu","ForwardLit",null)]
        [TestCase("FishBackdropGround","ForwardLit",null)]
        public void HotForwardPipelinesFitWebGpuDynamicUniformLimit(string shader,string pass,string instancing)
        {
            // FishWeatherTerrainArrayGpu is the Terrain module's.
            if(shader=="FishWeatherTerrainArrayGpu"&&!InstalledModules.Has("Terrain"))Assert.Ignore("The Terrain module is not installed.");
            var keywords=HeavyKeywords.Concat(new[]{"_ADDITIONAL_LIGHTS"}).ToList();
            if(instancing!=null)keywords.Add(instancing);
            bool found=false;
            foreach(var p in Passes(shader,n=>n==pass))
            {
                found=true;
                foreach(bool firstInstance in new[]{false,true})
                {
                    var k=firstInstance?keywords.Concat(new[]{WebGpuDrawCapabilities.FirstInstanceKeyword}).ToArray():keywords.ToArray();
                    string label=shader+" "+pass+" "+(instancing??"plain")+(firstInstance?" first-instance":"");
                    var union=Buffers(p,ShaderType.Vertex,k,ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL,label);
                    Assert.That(union.Length,Is.GreaterThan(0),label+": no reflection");
                    Assert.That(union.Length,Is.LessThanOrEqualTo(WebGpuDynamicUniformBuffersPerPipeline),label+": "+string.Join(", ",union));
                    Assert.That(union.Intersect(LightingCbuffers),Is.Empty,label+" still binds URP's lighting cbuffers");
                }
            }
            Assert.That(found,Is.True,shader+" has no "+pass+" pass");
        }

        /// <summary>
        /// The texture parameters Unity's WebGPU backend lets one program bind; a program with more loads with the warning
        /// "Shader '...' uses N texture parameters, more than the 32 supported by the current graphics device".
        /// </summary>
        const int WebGpuTextureParameters = 32;

        // Every pass of the lit shaders the browser draws, at the heaviest lighting state with light cookies, within the 32
        // texture parameters (vertex ∪ fragment, as Unity reports WebGPU programs).
        [TestCase("FishWeatherLit",null)]
        [TestCase("FishWeatherLit","INSTANCING_ON")]
        [TestCase("FishWeatherLitIndirect","PROCEDURAL_INSTANCING_ON")]
        [TestCase("FishWeatherLitIndirect","DOTS_INSTANCING_ON")]
        [TestCase("FishVegetationIndirect","PROCEDURAL_INSTANCING_ON")]
        [TestCase("FishVegetationIndirect","DOTS_INSTANCING_ON")]
        public void LitPassesFitWebGpuTextureParameters(string shader,string instancing)
        {
            var keywords=HeavyKeywords.Concat(new[]{"_ADDITIONAL_LIGHTS","_LIGHT_COOKIES"}).ToList();
            if(instancing!=null)keywords.Add(instancing);
            var failures=new List<string>();
            int passes=0;
            foreach(var p in Passes(shader,n=>n!="Meta"&&n!="Universal2D"))
            {
                passes++;
                foreach(bool firstInstance in new[]{false,true})
                {
                    var k=firstInstance?keywords.Concat(new[]{WebGpuDrawCapabilities.FirstInstanceKeyword}).ToArray():keywords.ToArray();
                    string label=shader+" "+p.Name+" "+(instancing??"plain")+(firstInstance?" first-instance":"");
                    var result=p.CompileVariant(ShaderType.Vertex,k,ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL);
                    Assert.That(result.Success,Is.True,label+": "+string.Join("\n",result.Messages.Select(m=>m.message)));
                    var textures=result.TextureBindings.Select(t=>t.Name).ToArray();
                    var resources=result.ResourceBindings.Select(r=>r.Name+"["+r.Kind+"]").ToArray();
                    Debug.Log("[WebGPU texture budget] "+label+" textures "+textures.Length+", resources "+resources.Length+": "+string.Join(", ",resources));
                    if(textures.Length>WebGpuTextureParameters)failures.Add(label+" "+textures.Length+": "+string.Join(", ",textures));
                }
            }
            Assert.That(passes,Is.GreaterThan(0),shader+" has no passes");
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }

        // The batch group's draws (DOTS) do the terrain's work exactly as RenderMeshIndirect's (procedural): a rock's ground band
        // and crevices, the stones' flora, the buildings' coat, the plants' ground contact (FISH_INDIRECT_TERRAIN). They once
        // compiled it for the procedural setup only, so every batch-group draw went without, on WebGPU and Vulkan alike.
        [TestCase("FishWeatherLitIndirect","_FishRockSdf")]
        [TestCase("FishVegetationIndirect","_FishGroundHeights")]
        public void DotsForwardBindsWhatTheProceduralForwardBinds(string shader,string terrainTexture)
        {
            var keywords=HeavyKeywords.Concat(new[]{"_ADDITIONAL_LIGHTS","_LIGHT_COOKIES"}).ToArray();
            bool found=false;
            foreach(var p in Passes(shader,n=>n=="ForwardLit"))
            {
                found=true;
                foreach(var api in new[]{ShaderCompilerPlatform.WebGPU,ShaderCompilerPlatform.Vulkan})
                {
                    var target=api==ShaderCompilerPlatform.WebGPU?BuildTarget.WebGL:BuildTarget.StandaloneLinux64;
                    string[] Textures(string instancing)
                    {
                        var result=p.CompileVariant(ShaderType.Vertex,keywords.Concat(new[]{instancing}).ToArray(),api,target);
                        Assert.That(result.Success,Is.True,shader+" "+instancing+" "+api+": "+string.Join("\n",result.Messages.Select(m=>m.message)));
                        return result.TextureBindings.Select(t=>t.Name).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
                    }
                    var procedural=Textures("PROCEDURAL_INSTANCING_ON");
                    var dots=Textures("DOTS_INSTANCING_ON");
                    if(procedural.Length==0&&dots.Length==0)continue; // no reflection on this API
                    Assert.That(dots,Does.Contain(terrainTexture),shader+" DOTS "+api+" skips the terrain's work");
                    Assert.That(dots,Is.EquivalentTo(procedural),shader+" "+api+": DOTS "+string.Join(", ",dots)+"\nprocedural "+string.Join(", ",procedural));
                }
            }
            Assert.That(found,Is.True,shader+" has no ForwardLit pass");
        }

        // Native keeps URP's lighting cbuffers: the switch is WebGPU-only.
        [Test]
        public void NativeForwardKeepsUrpLightingCbuffers()
        {
            foreach(var p in Passes("FishWeatherLitIndirect",n=>n=="ForwardLit"))
            {
                string native=p.PreprocessVariant(ShaderType.Vertex,HeavyKeywords.Concat(new[]{"PROCEDURAL_INSTANCING_ON"}).ToArray(),ShaderCompilerPlatform.Vulkan,BuildTarget.StandaloneLinux64,false).PreprocessedCode;
                Assert.That(native.Length,Is.GreaterThan(1000),"no Vulkan preprocessed source");
                foreach(string name in LightingCbuffers)Assert.That(native,Does.Match(@"cbuffer\s+"+name+@"\b"),"Vulkan lost "+name);
                return;
            }
            Assert.Fail("no ForwardLit pass");
        }

        // The switch only works where FishShadowVariants.hlsl comes before anything that declares URP's lighting cbuffers.
        [Test]
        public void ShadowVariantsPrecedeUrpLightingIncludesInEveryPass()
        {
            string include=ProjectFiles.ReadAllText("Assets/FishMMO/Shaders/FishShadowVariants.hlsl");
            StringAssert.Contains("#if defined(SHADER_API_WEBGPU) && !defined(LIGHT_SHADOWS_NO_CBUFFER)",include);
            StringAssert.Contains("#define LIGHT_SHADOWS_NO_CBUFFER",include);
            // Includes that declare (or pull in) Input.hlsl, Shadows.hlsl or LightCookieInput.hlsl.
            // FishIndirectPerDraw.hlsl is empty off DXC, and under DXC declares only UnityInput.hlsl (no lighting cbuffer).
            var allowedBefore=new[]{"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl","Assets/FishMMO/Shaders/FishShaderDiagnostics.hlsl","Assets/FishMMO/Shaders/FishIndirectPerDraw.hlsl"};
            int passes=0;
            foreach(string file in System.IO.Directory.GetFiles(ProjectFiles.Folder("Assets"),"*.shader",System.IO.SearchOption.AllDirectories))
            {
                string source=ProjectFiles.ReadAllText(file);
                foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source,"HLSLPROGRAM(.*?)ENDHLSL",System.Text.RegularExpressions.RegexOptions.Singleline))
                {
                    var includes=System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value,"#include(?:_with_pragmas)?\\s+\"([^\"]+)\"").Cast<System.Text.RegularExpressions.Match>().Select(x=>x.Groups[1].Value).ToList();
                    int at=includes.FindIndex(x=>x.EndsWith("FishShadowVariants.hlsl"));
                    if(at<0)continue;
                    passes++;
                    foreach(string before in includes.Take(at))
                        Assert.That(allowedBefore,Does.Contain(before),file+": "+before+" comes before FishShadowVariants.hlsl, so URP's lighting cbuffers would be declared first");
                }
            }
            Assert.That(passes,Is.GreaterThan(InstalledModules.Has("Terrain")&&InstalledModules.Has("WorldSimulation")?20:6));
        }

        [TestCase(ShaderCompilerPlatform.Vulkan,BuildTarget.StandaloneLinux64,false)]
        [TestCase(ShaderCompilerPlatform.D3D,BuildTarget.StandaloneWindows64,false)]
        [TestCase(ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL,false)]
        [TestCase(ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL,true)]
        public void PropAndTreeForwardDepthAndShadowVariantsCompile(ShaderCompilerPlatform api,BuildTarget target,bool firstInstance)
        {
            foreach(string name in new[]{"FishWeatherLitIndirect","FishVegetationIndirect","FishVegetationPrime"})
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(ProjectFiles.Path("Assets/Prefabs/Client/Weather/Shaders/"+name+".shader"));
                var data=ShaderUtil.GetShaderData(shader).GetSubshader(0);
                for(int pass=0;pass<data.PassCount;pass++)
                {
                    var p=data.GetPass(pass);
                    if(name!="FishVegetationPrime"&&p.Name!="ForwardLit"&&p.Name!="DepthOnly"&&p.Name!="ShadowCaster")continue;
                    foreach(var stage in new[]{ShaderType.Vertex,ShaderType.Fragment})
                    {
                        var keywords = new[]{"PROCEDURAL_INSTANCING_ON","_CLUSTER_LIGHT_LOOP","_MAIN_LIGHT_SHADOWS_CASCADE"};
                        if (firstInstance) keywords = keywords.Concat(new[]{WebGpuDrawCapabilities.FirstInstanceKeyword}).ToArray();
                        var result=p.CompileVariant(stage,keywords,api,target);
                        Assert.That(result.Success,Is.True,name+" "+p.Name+": "+string.Join("\n",result.Messages.Select(m=>m.message)));
                    }
                }
            }
        }
    }
}

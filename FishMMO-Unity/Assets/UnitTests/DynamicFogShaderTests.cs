using System;
using System.IO;
using System.Linq;
using FishMMO.Client;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FishMMO.UnitTests
{
    public class DynamicFogShaderTests
    {
        [TestCase("FishMMO/Weather Lit")]
        [TestCase("FishMMO/Weather Lit Indirect")]
        [TestCase("FishMMO/Vegetation")]
        [TestCase("FishMMO/Vegetation Indirect")]
        public void ShippingShadersKeepEveryFogModeAsADynamicKeyword(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, shaderName);
            foreach (string name in new[] { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" })
            {
                var keyword = shader.keywordSpace.FindKeyword(name);
                Assert.That(keyword.isValid, Is.True, name);
                Assert.That(keyword.isDynamic, Is.True, name + " must not multiply compiled variants");
            }
        }

        [Test]
        public void AllOwnedWeatherAndWaterFogPragmasUseTheDynamicConfiguration()
        {
            foreach (string folder in new[] { "Assets/Prefabs/Client/Weather/Shaders", "Assets/Plugins/FishMMO Water/Shaders" })
            foreach (string path in Directory.GetFiles(folder, "*.shader"))
            {
                string source = File.ReadAllText(path);
                Assert.That(source.Contains("#pragma multi_compile_fog"), Is.False, path);
                if (!source.Contains("ShaderLibrary/Fog.hlsl")) continue;
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                foreach (string name in new[] { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" })
                    Assert.That(shader.keywordSpace.FindKeyword(name).isDynamic, Is.True, path + ": " + name);
            }
        }

        [Test]
        public void PrewarmIgnoresDynamicKeywordsAndDoesNotSwitchTheirRuntimeState()
        {
            string id = Guid.NewGuid().ToString("N").ToUpperInvariant();
            string dynamicName = "FISH_DYNAMIC_" + id;
            string staticName = "FISH_STATIC_" + id;
            string folder = "Assets/DynamicFogFixture_" + id;
            Directory.CreateDirectory(folder);
            try
            {
                string path = folder + "/Fixture.shader";
                File.WriteAllText(path, "Shader \"Hidden/FishMMO/DynamicFog/" + id + "\" { SubShader { Pass { CGPROGRAM\n" +
                    "#pragma vertex vert\n#pragma fragment frag\n#pragma dynamic_branch _ " + dynamicName + "\n" +
                    "#pragma multi_compile _ " + staticName + "\n" +
                    "float4 vert(float4 p:POSITION):SV_POSITION{return p;}\n" +
                    "float4 frag():SV_Target{if(" + dynamicName + ")return 1;return 0;}\nENDCG\n} } }");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Assert.That(shader.keywordSpace.FindKeyword(dynamicName).isDynamic, Is.True);
                var expanded = ShaderPrewarm.WithEveryShadowState(shader, new[] { dynamicName, staticName }).ToArray();
                Assert.That(expanded.Length, Is.EqualTo(1));
                Assert.That(expanded[0], Is.EqualTo(new[] { staticName }));
                Shader.EnableKeyword(dynamicName);
                Shader.EnableKeyword(staticName);
                using (new ShaderPrewarm.KeywordScope(shader))
                {
                    Assert.That(Shader.IsKeywordEnabled(dynamicName), Is.True, "uniform fog state must remain unchanged");
                    Assert.That(Shader.IsKeywordEnabled(staticName), Is.False, "only static compilation overrides are cleared");
                }
                Assert.That(Shader.IsKeywordEnabled(staticName), Is.True);
            }
            finally
            {
                Shader.DisableKeyword(dynamicName);
                Shader.DisableKeyword(staticName);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}

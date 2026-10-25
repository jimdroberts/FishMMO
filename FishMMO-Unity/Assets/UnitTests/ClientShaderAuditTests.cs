using System;
using System.IO;
using FishMMO.Shared.CustomBuildTool.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FishMMO.UnitTests
{
	public class ClientShaderAuditTests
	{
		[TestCase("TMP_SDF-HDRP LIT.shadergraph", false)]
		[TestCase("TMP_SDF-HDRP UNLIT.shadergraph", false)]
		[TestCase("TMP_SDF-URP Lit.shadergraph", true)]
		[TestCase("TMP_SDF-URP Unlit.shadergraph", true)]
		public void AuditResolvesTheActualTextMeshProGraphPipeline(string name, bool expected)
		{
			var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Plugins/TextMesh Pro/Shaders/" + name);
			Assert.That(shader, Is.Not.Null);
			Assert.That(ClientShaderBuildAudit.HasCompatibleSubshader(shader, "UniversalPipeline"), Is.EqualTo(expected));
		}

		[TestCase("", true)]
		[TestCase("UniversalPipeline", true)]
		[TestCase("HDRenderPipeline", false)]
		[TestCase("HDRenderPipeline,UniversalPipeline", true)]
		public void AuditExcludesOnlyShadersWithExplicitlyIncompatibleSubshaders(string pipelines, bool expected)
		{
			string folder = "Assets/ShaderAuditFixture_" + Guid.NewGuid().ToString("N");
			Directory.CreateDirectory(folder);
			try
			{
				string source = "Shader \"Hidden/FishMMO/AuditFixture\" {";
				foreach (string pipeline in pipelines.Split(','))
				{
					string tag = string.IsNullOrEmpty(pipeline) ? "" : "Tags {\"RenderPipeline\"=\"" + pipeline + "\"}";
					source += "SubShader {" + tag + "Pass {HLSLPROGRAM\n#pragma vertex Vert\n#pragma fragment Frag\n" +
						"float4 Vert(float4 p:POSITION):SV_POSITION{return p;}\nfloat4 Frag():SV_Target{return 1;}\nENDHLSL\n}}";
				}
				source += "}";
				string path = folder + "/Fixture.shader";
				File.WriteAllText(path, source);
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
				var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
				Assert.That(shader, Is.Not.Null);
				Assert.That(ClientShaderBuildAudit.HasCompatibleSubshader(shader, "UniversalPipeline"), Is.EqualTo(expected));
			}
			finally { AssetDatabase.DeleteAsset(folder); }
		}
	}
}

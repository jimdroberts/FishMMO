using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>
	/// Build diagnostics: with FISHMMO_SHADER_VARIANT_LOG=&lt;file&gt;, every variant of the indirect twins that reaches FishMMO's
	/// stripper ("in", after URP's) and every one that leaves it ("out") is appended to the file, one tab-separated line each:
	/// stage, shader, pass, shader stage, platform, sorted keywords. The variant inventory of
	/// Docs/Rendering/2026-11-03/shader-compile-cost is read from it. Off (the default) it does nothing.
	/// </summary>
	public abstract class ShaderVariantLog : IPreprocessShaders
	{
		public const string Variable = "FISHMMO_SHADER_VARIANT_LOG";
		private static readonly object Gate = new object();
		protected abstract string Stage { get; }
		public abstract int callbackOrder { get; }

		public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> variants)
		{
			string path = Environment.GetEnvironmentVariable(Variable);
			if (string.IsNullOrEmpty(path) || !FishMMO.Shared.CustomBuildTool.ClientShaderBuildProcessor.IsBrgTwin(shader.name)) return;
			var text = new StringBuilder();
			foreach (ShaderCompilerData variant in variants)
			{
				string keywords = string.Join(" ", variant.shaderKeywordSet.GetShaderKeywords().Select(k => k.name).OrderBy(k => k, StringComparer.Ordinal));
				text.Append(Stage).Append('\t').Append(shader.name).Append('\t').Append(snippet.passName).Append('\t').Append(snippet.shaderType)
					.Append('\t').Append(variant.shaderCompilerPlatform).Append('\t').Append(keywords).Append('\n');
			}
			lock (Gate) File.AppendAllText(path, text.ToString());
		}
	}

	/// <summary>What FishMMO's stripper receives (ClientShaderBuildProcessor runs at int.MaxValue - 1).</summary>
	public sealed class ShaderVariantLogIn : ShaderVariantLog
	{
		protected override string Stage => "in";
		public override int callbackOrder => int.MaxValue - 2;
	}

	/// <summary>What is compiled.</summary>
	public sealed class ShaderVariantLogOut : ShaderVariantLog
	{
		protected override string Stage => "out";
		public override int callbackOrder => int.MaxValue;
	}
}

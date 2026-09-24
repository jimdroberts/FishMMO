using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.Client
{
	/// <summary>
	/// The world-systems audit's client half: whether the URP renderers carry the weather's render
	/// passes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Clouds, height fog, volumetric fog and the screen overlay are renderer features, and a
	/// renderer that has none of them draws a scene that is configured perfectly as though there
	/// were no weather at all — no error, no warning, just a clear sky over a rainstorm. The three
	/// renderers in the project carry all four today; a quality tier added later would not, and
	/// nothing would say so.
	/// </para>
	/// <para>
	/// Registered into the shared audit rather than called by it, because the feature types live in
	/// the client assembly and the audit lives in <c>FishMMO.Shared.Tools.Editor</c>, which the
	/// client references and not the other way round. A server-subtarget editor compiles this
	/// assembly out, and the audit simply runs without it.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public static class WeatherRendererChecks
	{
		static WeatherRendererChecks()
		{
			WorldSystemsAudit.ProjectChecks.Add(Inspect);
		}

		/// <summary>The passes a renderer must carry, and what each one draws.</summary>
		private static readonly (System.Type Feature, string Name, string Draws)[] Passes =
		{
			(typeof(FishCloudsFeature), "Fish Clouds", "the volumetric clouds"),
			(typeof(FishHeightFogFeature), "Fish Height Fog", "the fog lying in the low ground"),
			(typeof(FishVolumetricFogFeature), "Fish Volumetric Fog", "the god rays and the light shafts"),
			(typeof(FishWeatherOverlayFeature), "Fish Weather Overlay", "rain and snow on the camera"),
		};

		private static void Inspect(List<WorldSystemProblem> into)
		{
			var missing = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
				if (data == null)
				{
					continue;
				}
				foreach ((System.Type feature, string name, string draws) in Passes)
				{
					if (Carries(data, feature))
					{
						continue;
					}
					missing.Add($"{Path.GetFileNameWithoutExtension(path)} has no {name} pass, so it draws none of {draws}");
				}
			}

			if (missing.Count == 0)
			{
				return;
			}

			into.Add(new WorldSystemProblem
			{
				Id = WorldSystemCheck.MissingRendererPass,
				SceneName = null,
				Severity = WorldSystemSeverity.Warning,
				Message = $"is missing {missing.Count} weather render pass(es): {string.Join("; ", missing)}.",
				Remedy = "Add the weather's cloud, height fog, volumetric fog and overlay passes to every URP renderer.",
				CanFix = true,
				WritesScene = false,
				ProjectFix = AddPasses,
			});
		}

		private static bool Carries(ScriptableRendererData data, System.Type feature)
		{
			foreach (ScriptableRendererFeature existing in data.rendererFeatures)
			{
				if (existing != null && feature.IsInstanceOfType(existing))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Puts every weather pass on every renderer that lacks one.
		/// </summary>
		/// <remarks>
		/// The cloud pass goes on through <see cref="WeatherRenderAssets.Ensure"/> rather than
		/// directly: it needs the cloud material, which is made and wired by the render profile, so
		/// adding the pass without it would leave a renderer carrying a feature that draws nothing.
		/// </remarks>
		private static bool AddPasses()
		{
			WeatherRenderAssets.Ensure();
			CloudRendererSetup.EnsureHeightFog();
			CloudRendererSetup.EnsureVolumetricFog();
			CloudRendererSetup.EnsureOverlay();
			AssetDatabase.SaveAssets();

			/* Asked of the renderers rather than counted from the calls above, because Ensure()
			 * adds the cloud pass itself: a run that needed only that one would have every count
			 * come back zero and report that it had done nothing. */
			var remaining = new List<WorldSystemProblem>();
			Inspect(remaining);
			return remaining.Count == 0;
		}
	}
}

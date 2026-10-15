using FishMMO.Shared;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A/B and diagnostics for the biome looks (<see cref="FloraPaletteMap"/>): every biome's twelve looks for its grass,
	/// flowers, small plants, fungi, stones and animals, against the one look everything had before.
	/// </summary>
	public static class FloraPaletteToggle
	{
		[DashboardTool(DashboardToolAttribute.Biomes, "Toggle biome looks (play mode A/B)", Section = "Diagnostics", Order = 120, AllowInPlayMode = true,
			Tooltip = "Switches the biome looks off and on: off, grass, flowers, small plants, fungi and stones draw as they did before (one look for every biome and world). Animals already placed keep their look until they are placed again.")]
		public static void Toggle()
		{
			if (!Application.isPlaying)
			{
				Debug.Log("[Flora] The biome looks switch in play mode only.");
				return;
			}
			FloraPaletteMap.Enabled = !FloraPaletteMap.Enabled;
			SceneView.RepaintAll();
			Debug.Log($"[Flora] Biome looks {(FloraPaletteMap.Enabled ? "ON" : "OFF (as before)")}: {FloraPaletteMap.Describe()}.");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Describe biome looks under the camera (play mode)", Section = "Diagnostics", Order = 121, AllowInPlayMode = true,
			Tooltip = "Logs the world and biomes the looks were built for, and the twelve looks of the biome under the main camera with their weights and colours.")]
		public static void Describe()
		{
			Camera camera = Camera.main;
			if (!Application.isPlaying || camera == null || !FloraPaletteMap.LookAt(camera.transform.position, out Shared.Biomes.BiomeLooks.Look look))
			{
				Debug.Log($"[Flora] {FloraPaletteMap.Describe()}; nothing under the camera.");
				return;
			}
			var text = new System.Text.StringBuilder();
			text.Append($"[Flora] {FloraPaletteMap.Describe()}. Under the camera: {look.Class}");
			float previous = 0f;
			for (int i = 0; i < look.Variants.Length; i++)
			{
				Shared.Biomes.BiomeLooks.Variant v = look.Variants[i];
				text.Append($"\n  {i,2} {v.Name,-13} {(look.Cumulative[i] - previous) * 100f,4:0}%  leaves #{ColorUtility.ToHtmlStringRGB(v.Foliage)}" +
					$" ×{v.Height:0.00} tall, flowers #{ColorUtility.ToHtmlStringRGB(v.Bloom)}, fungi #{ColorUtility.ToHtmlStringRGB(v.Accent)}," +
					$" animals #{ColorUtility.ToHtmlStringRGB(v.Coat)} {v.Pattern}");
				previous = look.Cumulative[i];
			}
			Debug.Log(text.ToString());
		}
	}
}

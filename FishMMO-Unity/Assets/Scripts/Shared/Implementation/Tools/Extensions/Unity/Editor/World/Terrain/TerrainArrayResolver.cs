#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Where one texture of a baked layer came from.</summary>
	public enum TerrainArraySource
	{
		/// <summary>The committed art: nothing on this machine overrides it.</summary>
		Committed,
		/// <summary>A whole TerrainLayer from the biome's <see cref="BiomeLocalArt"/> sidecar.</summary>
		SidecarLayer,
		/// <summary>A single texture from the biome's sidecar.</summary>
		SidecarTexture,
		/// <summary>A LOCAL TerrainLayer with the committed layer's name.</summary>
		NamedLayer,
		/// <summary>A LOCAL texture with the committed texture's file name.</summary>
		NamedTexture,
		/// <summary>Nothing: the slice is filled with a neutral value.</summary>
		None,
	}

	/// <summary>
	/// The questions LOCAL resolution asks of the project, behind an interface so the order can be
	/// tested without files on disk.
	/// </summary>
	public interface ITerrainArrayLocalLookup
	{
		/// <summary>The biome's sidecar override for a slot, or null.</summary>
		BiomeLocalArt.SlotOverride Sidecar(BiomeTemplate biome, string slot);

		/// <summary>A LOCAL terrain layer standing in for a committed one by name, or null.</summary>
		TerrainLayer LayerOverride(TerrainLayer committed);

		/// <summary>A LOCAL texture standing in for a committed one by file name, or null.</summary>
		Texture2D TextureOverride(Texture2D committed);
	}

	/// <summary>The project's own LOCAL folders, by the paths <see cref="BiomeLocalArt"/> names.</summary>
	public sealed class ProjectTerrainArrayLocalLookup : ITerrainArrayLocalLookup
	{
		public BiomeLocalArt.SlotOverride Sidecar(BiomeTemplate biome, string slot) => BiomeLocalArtIndex.Find(biome, slot);

		public TerrainLayer LayerOverride(TerrainLayer committed)
		{
			if (committed == null || BiomeLocalArtIndex.IsLocal(committed))
			{
				return null;
			}
			string path = $"{BiomeLocalArt.TerrainLayersFolder}/{committed.name}.terrainlayer";
			return AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
		}

		public Texture2D TextureOverride(Texture2D committed)
		{
			if (committed == null || BiomeLocalArtIndex.IsLocal(committed))
			{
				return null;
			}
			string committedPath = AssetDatabase.GetAssetPath(committed);
			string name = string.IsNullOrEmpty(committedPath) ? committed.name : Path.GetFileNameWithoutExtension(committedPath);
			foreach (string extension in BiomeLocalArt.TextureExtensions)
			{
				// Both cases: the file system here is case-sensitive, and art arrives as .PNG as often as .png.
				Texture2D found = AssetDatabase.LoadAssetAtPath<Texture2D>($"{BiomeLocalArt.TexturesFolder}/{name}{extension}")
					?? AssetDatabase.LoadAssetAtPath<Texture2D>($"{BiomeLocalArt.TexturesFolder}/{name}{extension.ToUpperInvariant()}");
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}
	}

	/// <summary>One palette layer's art after LOCAL resolution: what the baker puts in slice <see cref="LayerIndex"/>.</summary>
	public sealed class ResolvedTerrainArrayLayer
	{
		public int LayerIndex;
		/// <summary>The committed layer the terrain paints with.</summary>
		public TerrainLayer Committed;
		/// <summary>The layer whose tiling, normal scale and remaps the ground draws with.</summary>
		public TerrainLayer ParamsLayer;
		public TerrainArraySource ParamsSource;
		public Texture2D Albedo;
		public Texture2D Normal;
		public Texture2D Mask;
		public TerrainArraySource AlbedoSource;
		public TerrainArraySource NormalSource;
		public TerrainArraySource MaskSource;
		/// <summary>The biome slot whose sidecar won, or null when none had one.</summary>
		public TerrainArrayLayerUse SidecarWinner;

		/// <summary>One line for the log: the layer and where each part came from.</summary>
		public string Describe()
		{
			string name = Committed != null ? Committed.name : $"layer {LayerIndex}";
			string winner = SidecarWinner != null && SidecarWinner.Biome != null ? $" (sidecar of {SidecarWinner.Biome.name} {SidecarWinner.Slot})" : "";
			return $"{name}: layer {ParamsSource}, albedo {AlbedoSource}, normal {NormalSource}, mask {MaskSource}{winner}";
		}
	}

	/// <summary>
	/// Resolves a palette layer's art LOCAL-first, in the order the LOCAL override contract fixes.
	/// </summary>
	/// <remarks>
	/// <para>The order, for one palette layer:</para>
	/// <list type="number">
	/// <item>The biome's <see cref="BiomeLocalArt"/> sidecar for the slot: its whole TerrainLayer if
	/// it has one (textures and numbers both), else its single textures over what follows.</item>
	/// <item><c>Assets/LOCAL/Biomes/TerrainLayers/&lt;layer name&gt;.terrainlayer</c>: a whole-layer
	/// override by name. Its textures are used as they are — a LOCAL layer without a normal map means
	/// flat ground, not the committed normal map.</item>
	/// <item>Per texture, <c>Assets/LOCAL/Biomes/Textures/&lt;committed file name&gt;.{png,jpg,jpeg,tga,psd,exr}</c>.</item>
	/// <item>The committed texture.</item>
	/// </list>
	/// <para>
	/// <b>Shared layers.</b> Several biomes can paint with one palette layer. Their uses are listed
	/// with the biome covering most of the scene first, and the first use whose sidecar has anything
	/// for its slot wins — an explicit override beats no override. A later use whose sidecar would
	/// bake different art is a conflict, reported, never silently merged.
	/// </para>
	/// </remarks>
	public static class TerrainArrayResolver
	{
		public static ResolvedTerrainArrayLayer Resolve(int layerIndex, TerrainArrayLayerSource source, ITerrainArrayLocalLookup lookup, List<string> conflicts)
		{
			TerrainLayer committed = source != null ? source.Layer : null;
			var result = new ResolvedTerrainArrayLayer
			{
				LayerIndex = layerIndex,
				Committed = committed,
				ParamsLayer = committed,
				ParamsSource = TerrainArraySource.Committed,
			};

			// 1. The sidecars, the biome covering most of the scene first.
			BiomeLocalArt.SlotOverride winner = null;
			if (source != null && source.Uses != null && lookup != null)
			{
				foreach (TerrainArrayLayerUse use in source.Uses)
				{
					if (use == null || use.Biome == null)
					{
						continue;
					}
					BiomeLocalArt.SlotOverride candidate = lookup.Sidecar(use.Biome, use.Slot);
					if (candidate == null || candidate.IsEmpty)
					{
						continue;
					}
					if (winner == null)
					{
						winner = candidate;
						result.SidecarWinner = use;
					}
					else if (!candidate.SameArtAs(winner))
					{
						conflicts?.Add($"Layer {layerIndex} '{(committed != null ? committed.name : "?")}': the LOCAL override of {use.Biome.name} ({use.Slot}) differs from that of {result.SidecarWinner.Biome.name} ({result.SidecarWinner.Slot}); {result.SidecarWinner.Biome.name} covers more of the scene and wins.");
					}
				}
			}

			if (winner != null && winner.TerrainLayer != null)
			{
				TerrainLayer layer = winner.TerrainLayer;
				result.ParamsLayer = layer;
				result.ParamsSource = TerrainArraySource.SidecarLayer;
				result.Albedo = layer.diffuseTexture;
				result.Normal = layer.normalMapTexture;
				result.Mask = layer.maskMapTexture;
				result.AlbedoSource = result.Albedo != null ? TerrainArraySource.SidecarLayer : TerrainArraySource.None;
				result.NormalSource = result.Normal != null ? TerrainArraySource.SidecarLayer : TerrainArraySource.None;
				result.MaskSource = result.Mask != null ? TerrainArraySource.SidecarLayer : TerrainArraySource.None;
				FallBackToCommittedAlbedo(result, committed, conflicts);
				return result;
			}

			// 2. A LOCAL layer by name.
			TerrainLayer named = lookup != null ? lookup.LayerOverride(committed) : null;
			bool baseIsLocal = named != null;
			TerrainLayer baseLayer = baseIsLocal ? named : committed;
			if (baseIsLocal)
			{
				result.ParamsLayer = named;
				result.ParamsSource = TerrainArraySource.NamedLayer;
			}

			// 1 (single textures), then 2, 3, 4 for each texture on its own.
			result.Albedo = Pick(winner?.Albedo, baseLayer != null ? baseLayer.diffuseTexture : null, committed != null ? committed.diffuseTexture : null, baseIsLocal, lookup, out result.AlbedoSource);
			result.Normal = Pick(winner?.Normal, baseLayer != null ? baseLayer.normalMapTexture : null, committed != null ? committed.normalMapTexture : null, baseIsLocal, lookup, out result.NormalSource);
			result.Mask = Pick(winner?.Mask, baseLayer != null ? baseLayer.maskMapTexture : null, committed != null ? committed.maskMapTexture : null, baseIsLocal, lookup, out result.MaskSource);
			FallBackToCommittedAlbedo(result, committed, conflicts);
			return result;
		}

		private static Texture2D Pick(Texture2D sidecar, Texture2D baseTexture, Texture2D committedTexture, bool baseIsLocal,
			ITerrainArrayLocalLookup lookup, out TerrainArraySource source)
		{
			if (sidecar != null)
			{
				source = TerrainArraySource.SidecarTexture;
				return sidecar;
			}
			if (baseIsLocal)
			{
				source = baseTexture != null ? TerrainArraySource.NamedLayer : TerrainArraySource.None;
				return baseTexture;
			}
			Texture2D named = lookup != null ? lookup.TextureOverride(committedTexture) : null;
			if (named != null)
			{
				source = TerrainArraySource.NamedTexture;
				return named;
			}
			source = committedTexture != null ? TerrainArraySource.Committed : TerrainArraySource.None;
			return committedTexture;
		}

		/// <summary>An override with no albedo would bake a blank slice; the committed albedo stands in, and it is reported.</summary>
		private static void FallBackToCommittedAlbedo(ResolvedTerrainArrayLayer result, TerrainLayer committed, List<string> conflicts)
		{
			if (result.Albedo != null || committed == null || committed.diffuseTexture == null)
			{
				return;
			}
			result.Albedo = committed.diffuseTexture;
			result.AlbedoSource = TerrainArraySource.Committed;
			conflicts?.Add($"Layer {result.LayerIndex} '{committed.name}': its LOCAL override has no albedo, so the committed one is used.");
		}
	}
}
#endif

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The <see cref="TerrainLayer"/> a biome's texture layer draws with.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The layer's own asset first.</b> <see cref="TerrainTextureLayer.terrainLayer"/> is where
	/// art is assigned and swapped, so when it is set (and is art, below) nothing else is consulted.
	/// </para>
	/// <para>
	/// <b>Otherwise one built from the texture fields, once.</b> Biomes authored before the field
	/// existed hold loose albedo, normal and mask textures. Those become a TerrainLayer asset named
	/// for the textures and keyed by everything that affects how they draw, so two biomes using
	/// the same texture the same way share one layer — and one palette channel — instead of
	/// cross-fading two identical copies. The asset is written beside the others, never inside
	/// a scene's folder, because it belongs to the biome rather than to any one place.
	/// </para>
	/// <para>
	/// <b>Built layers are build output, never committed</b> (<see cref="BuiltFolder"/> is gitignored).
	/// Committed terrain data references them, so they must come out identical on every clone: the path
	/// is a function of the content (the textures' committed GUIDs and the draw settings), and the GUID
	/// a function of the path (<see cref="ProceduralArtPayload.GuidFor"/>), with the fixed main file ID
	/// every TerrainLayer has. The generator re-creates them on a fresh clone (<see cref="EnsureAll"/>),
	/// and so does any repaint.
	/// </para>
	/// <para>
	/// <b>Only real, committed art counts.</b> A placeholder swatch
	/// (<see cref="BiomeArtAuthoring.IsPlaceholderTexture"/>, the flat <c>BiomeTexture_*</c> colour
	/// keys) and anything under <c>Assets/LOCAL</c> (<see cref="BiomeLocalArtIndex.IsLocal"/>) are NO
	/// art here. A swatch painted as ground is a flat colour pretending to be finished; and a LOCAL
	/// texture is on this machine only, so a layer built from it — referenced by committed terrain
	/// data — would differ from what every other clone builds. LOCAL art reaches the screen through
	/// the texture arrays, which read the sidecars; the terrain's own layers stay committed art.
	/// </para>
	/// <para>
	/// <b>A placeholder only for a biome with no main art at all</b>, chosen from where the biome
	/// lives, so a scene still reads as beach, grass, rock or snow while its art is missing — and
	/// the generator reports which biomes those were, rather than painting them convincingly wrong.
	/// </para>
	/// </remarks>
	public static class BiomeTerrainLayers
	{
		/// <summary>Where layers built from loose biome textures are written. Gitignored.</summary>
		public const string BuiltFolder = "Assets/Prefabs/Shared/Biomes/TerrainLayers/Built";

		private static readonly Dictionary<string, TerrainLayer> built = new Dictionary<string, TerrainLayer>();

		/// <summary>The terrain layer a texture layer draws with, or null when it has no art.</summary>
		public static TerrainLayer Resolve(TerrainTextureLayer source)
		{
			return Resolve(source, BiomeLocalArtIndex.IsLocal);
		}

		/// <summary><see cref="Resolve(TerrainTextureLayer)"/> with the LOCAL test supplied, so a test can run it on in-memory objects.</summary>
		public static TerrainLayer Resolve(TerrainTextureLayer source, System.Func<Object, bool> isLocal)
		{
			if (source == null)
			{
				return null;
			}
			if (source.terrainLayer != null && !IsLocal(source.terrainLayer, isLocal) && IsArt(source.terrainLayer.diffuseTexture, isLocal))
			{
				return source.terrainLayer;
			}
			return IsArt(source.albedoTexture, isLocal) ? Build(source, isLocal) : null;
		}

		/// <summary>
		/// True when a texture is art a committed layer may draw: present, not a placeholder swatch,
		/// and not under Assets/LOCAL.
		/// </summary>
		public static bool IsArt(Texture texture, System.Func<Object, bool> isLocal)
		{
			return texture != null && !BiomeArtAuthoring.IsPlaceholderTexture(texture) && !IsLocal(texture, isLocal);
		}

		private static bool IsLocal(Object asset, System.Func<Object, bool> isLocal) => asset != null && isLocal != null && isLocal(asset);

		/// <summary>
		/// A plain stand-in for a biome with no main art: sand by the water, snow where it is cold,
		/// rock up high, grass otherwise.
		/// </summary>
		public static TerrainLayer Placeholder(BiomeTemplate biome)
		{
			TerrainLayer[] layers = GeneratedTerrainLayers.Ensure();
			if (biome == null)
			{
				return layers[GeneratedTerrainLayers.Grass];
			}
			if (biome.MaxTemperature < -0.3f)
			{
				return layers[GeneratedTerrainLayers.Snow];
			}
			if (biome.ElevationTier <= 3)
			{
				return layers[GeneratedTerrainLayers.Sand];
			}
			if (biome.ElevationTier >= 7 && biome.ElevationTier != 9)
			{
				return layers[GeneratedTerrainLayers.Rock];
			}
			return biome.MaxHumidity < -0.2f ? layers[GeneratedTerrainLayers.Sand] : layers[GeneratedTerrainLayers.Grass];
		}

		/// <summary>Forgets the layers found so far; they are found again on disk on the next ask.</summary>
		public static void ClearCache()
		{
			built.Clear();
		}

		/// <summary>
		/// Builds every layer any biome template's loose textures would resolve to, so a fresh clone
		/// has the built layers its committed terrain data references before anything is repainted.
		/// Returns their paths.
		/// </summary>
		/// <remarks>
		/// Exactly the layers a repaint would build from the templates as they are now; a layer some
		/// scene was painted with from an older template is not rebuilt (repaint that scene).
		/// </remarks>
		public static List<string> EnsureAll(List<string> problems)
		{
			ClearCache();
			var paths = new SortedSet<string>(System.StringComparer.Ordinal);
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
			{
				var biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (biome == null)
				{
					continue;
				}
				foreach (string slot in BiomeLocalArt.SlotsOf(biome))
				{
					TerrainTextureLayer layer = BiomeLocalArt.CommittedLayer(biome, slot);
					if (layer == null)
					{
						continue;
					}
					TerrainLayer resolved = Resolve(layer);
					string path = resolved != null ? AssetDatabase.GetAssetPath(resolved) : null;
					if (path != null && path.StartsWith(BuiltFolder + "/", System.StringComparison.Ordinal))
					{
						paths.Add(path);
					}
					else if (resolved == null && IsArt(layer.albedoTexture, BiomeLocalArtIndex.IsLocal))
					{
						problems?.Add($"{biome.name} {slot}: its built terrain layer could not be created");
					}
				}
			}
			return new List<string>(paths);
		}

		/// <summary>The path a built layer for these inputs lives at: a function of its content, so every clone agrees.</summary>
		public static string BuiltPath(string albedoName, string key)
		{
			return $"{BuiltFolder}/{WorldEditorAssets.Sanitize(albedoName)} {Hash(key)}.terrainlayer";
		}

		private static TerrainLayer Build(TerrainTextureLayer source, System.Func<Object, bool> isLocal)
		{
			// A LOCAL normal or mask beside a committed albedo is dropped, not built in.
			Texture2D normal = IsLocal(source.normalTexture, isLocal) ? null : source.normalTexture;
			Texture2D mask = IsLocal(source.maskTexture, isLocal) ? null : source.maskTexture;
			string key = Key(source, normal, mask);
			if (built.TryGetValue(key, out TerrainLayer cached) && cached != null)
			{
				return cached;
			}

			string path = BuiltPath(source.albedoTexture.name, key);
			// One made before IDs were deterministic keeps its GUID until ProceduralArtMigration re-addresses it.
			var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
			if (layer == null)
			{
				WorldEditorAssets.EnsureFolder(BuiltFolder);
				var fresh = new TerrainLayer
				{
					diffuseTexture = source.albedoTexture,
					normalMapTexture = normal,
					maskMapTexture = mask,
					tileSize = new Vector2(Mathf.Max(0.01f, source.tileSize.x), Mathf.Max(0.01f, source.tileSize.y)),
					normalScale = normal != null ? 1f : 0f,
					metallic = source.metallic,
					smoothness = source.smoothness,
				};
				var problems = new List<string>();
				layer = ProceduralArtPayload.CreateNative(fresh, path, ProceduralArtPayload.TerrainLayerFileId, problems);
				ProceduralArtPayload.ClearStaging();
				foreach (string problem in problems)
				{
					Debug.LogWarning("[Biome terrain layers] " + problem);
				}
				if (layer == null)
				{
					return null;
				}
			}
			built[key] = layer;
			return layer;
		}

		/// <summary>Everything that changes how a built layer draws, so equal inputs share one asset.</summary>
		private static string Key(TerrainTextureLayer source, Texture2D normal, Texture2D mask)
		{
			var key = new StringBuilder();
			key.Append(Guid(source.albedoTexture)).Append('|')
				.Append(Guid(normal)).Append('|')
				.Append(Guid(mask)).Append('|')
				.Append(source.tileSize.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
				.Append(source.tileSize.y.ToString("R", CultureInfo.InvariantCulture)).Append('|')
				.Append(source.metallic.ToString("R", CultureInfo.InvariantCulture)).Append('|')
				.Append(source.smoothness.ToString("R", CultureInfo.InvariantCulture));
			return key.ToString();
		}

		private static string Guid(Object asset)
		{
			return asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long _) ? guid : "-";
		}

		private static string Hash(string key)
		{
			return unchecked((uint)key.GetDeterministicHashCode()).ToString("x8");
		}
	}
}
#endif

using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// One scene's baked terrain art: the albedo, normal and mask texture arrays the array terrain
	/// shader samples, and each layer's tiling and remaps.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Build output, never source.</b> <c>TerrainArrayBaker</c> writes one of these per world scene
	/// into <see cref="BakedDirectory"/>, which is gitignored, from art that may live under
	/// <c>Assets/LOCAL</c> and must never be referenced by anything committed. Every machine bakes its
	/// own: opening a scene whose set is missing or stale rebakes it, and a client build bakes every
	/// world scene before it builds.
	/// </para>
	/// <para>
	/// <b>Found by convention, never by reference.</b> No scene points at a set: a committed scene
	/// holding the GUID of a file each machine regenerates would dangle on every other clone. The
	/// binder finds its scene's set by the scene's name — straight off disk in the editor, by
	/// <see cref="AddressOf"/> in a player (the build puts the sets in a client-only addressable group).
	/// </para>
	/// </remarks>
	public sealed class TerrainArraySet : ScriptableObject
	{
		/// <summary>Where sets are baked. Gitignored: this is build output.</summary>
		public const string BakedDirectory = "Assets/Prefabs/Client/TerrainArrays";

		/// <summary>
		/// The addressable group a client build registers the sets in, and removes again afterwards.
		/// "Client" in the name keeps it out of server bundles, which exclude groups by that substring.
		/// </summary>
		public const string AddressableGroupName = "ClientTerrainArrays";

		/// <summary>A scene name as a file name: the characters a file system refuses become underscores.</summary>
		public static string FileNameOf(string sceneName)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return "_";
			}
			foreach (char c in System.IO.Path.GetInvalidFileNameChars())
			{
				sceneName = sceneName.Replace(c, '_');
			}
			return sceneName;
		}

		/// <summary>The folder one scene's arrays are baked into.</summary>
		public static string BakedFolder(string sceneName) => $"{BakedDirectory}/{FileNameOf(sceneName)}";

		/// <summary>The set asset of one scene.</summary>
		public static string BakedAssetPath(string sceneName) => $"{BakedFolder(sceneName)}/{FileNameOf(sceneName)} Terrain Arrays.asset";

		/// <summary>The address a player loads a scene's set by.</summary>
		public static string AddressOf(string sceneName) => $"TerrainArrays/{sceneName}";

		[Tooltip("The scene this was baked for; its address is built from it.")]
		public string SceneName;

		[Tooltip("Layer i of the scene's terrains is slice i. sRGB; alpha holds smoothness.")]
		public Texture2DArray Albedo;
		[Tooltip("Tangent-space normals as plain RGB. Null when no layer has a normal map.")]
		public Texture2DArray Normal;
		[Tooltip("R metallic, G occlusion, B height, A smoothness. Null when no layer has a mask map.")]
		public Texture2DArray Mask;

		[Tooltip("How many layers the arrays hold.")]
		public int LayerCount;

		[Tooltip("Each layer's name as it was baked, for the inspector and the logs.")]
		public string[] LayerNames = new string[0];

		[Tooltip("Where each layer's art came from (committed, LOCAL sidecar, LOCAL by name), for the logs.")]
		public string[] LayerSources = new string[0];

		// The five per-layer arrays, packed by TerrainArrayLayerParams: TerrainArrayLayerParams.TableLength(LayerCount) long
		// (32 for a scene of 32 layers or fewer, as when they were the shader's fixed-size uniforms). The shader reads them
		// from the layer table (TerrainLayerTable) the binder builds from them.
		public Vector4[] LayerST = new Vector4[0];
		public Vector4[] LayerTint = new Vector4[0];
		public Vector4[] LayerMaskOffset = new Vector4[0];
		public Vector4[] LayerMaskScale = new Vector4[0];
		public Vector4[] LayerSurface = new Vector4[0];

		[Tooltip("A hash of everything the bake read. A different hash means the set is stale.")]
		public string ContentHash;

		/// <summary>True when the albedo exists and every per-layer array covers every layer.</summary>
		public bool IsUsable =>
			Albedo != null && LayerCount > 0 && LayerCount <= TerrainArrayLayerParams.MaximumLayers
			&& LayerST != null && LayerST.Length >= LayerCount
			&& LayerTint != null && LayerTint.Length >= LayerCount
			&& LayerMaskOffset != null && LayerMaskOffset.Length >= LayerCount
			&& LayerMaskScale != null && LayerMaskScale.Length >= LayerCount
			&& LayerSurface != null && LayerSurface.Length >= LayerCount;

		[Tooltip("The shader that bakes each tile's index map from its alphamaps (TerrainIndexMapBaker). Referenced here so a player build carries it with the arrays; written by the bake.")]
		public Shader IndexBakeShader;

		[Tooltip("What each layer's ground is made of, from the biome slot it was painted from (TerrainTextureLayer.GroundMakeup): x living, y lichen, z mineral, w 1 where the slot had one (0: read from the layer's name). Written by the bake; not part of the content hash, refreshed in place when only it changed.")]
		public Vector4[] LayerMakeup = new Vector4[0];

		[System.NonSerialized] private Vector4[] layerFlora;
		[System.NonSerialized] private string[] layerFloraNames;
		[System.NonSerialized] private Vector4[] layerFloraMakeup;
		[System.NonSerialized] private bool warnedNameFallback;

		/// <summary>
		/// Per layer (<see cref="TerrainArrayLayerParams.TableLength"/> long) what it is made of as the planet's looks see
		/// it: x living, y lichen, z mineral, w 0. The terrain shader's <c>_FishLayerFlora</c>, the grass's colour read and the
		/// contact blend all take it from here, so the three agree on every layer.
		/// </summary>
		/// <remarks>
		/// <para>
		/// From the biome slot each layer was painted from (<see cref="LayerMakeup"/>, baked from the template's
		/// <see cref="Biomes.TerrainTextureLayer.GroundMakeup"/>; 2026-10-15). A layer with none — a set baked before then, or
		/// hand-made art whose slot has no makeup — is read from its name as it always was
		/// (<see cref="Biomes.BiomeLooks.GroundShareOf"/>). For a generated ground (Ground_*) the name gives exactly what
		/// authoring writes on its slot (pinned by BiomeTemplateLooksTests), so that fallback is silent; only hand-made
		/// layers read by name get the one warning for the set, since only theirs can be wrong.
		/// </para>
		/// <para>
		/// Worked out on first use and kept until the names or the makeup change (a rebake writes new ones).
		/// </para>
		/// </remarks>
		public Vector4[] LayerFlora()
		{
			if (layerFlora != null && ReferenceEquals(layerFloraNames, LayerNames) && ReferenceEquals(layerFloraMakeup, LayerMakeup))
			{
				return layerFlora;
			}
			var flora = new Vector4[TerrainArrayLayerParams.TableLength(LayerCount)];
			int count = Mathf.Min(LayerCount, flora.Length);
			int byName = 0;
			string first = null;
			for (int i = 0; i < count; i++)
			{
				if (LayerMakeup != null && i < LayerMakeup.Length && LayerMakeup[i].w > 0.5f)
				{
					flora[i] = new Vector4(LayerMakeup[i].x, LayerMakeup[i].y, LayerMakeup[i].z, 0f);
					continue;
				}
				string name = LayerNames != null && i < LayerNames.Length ? LayerNames[i] : null;
				Biomes.BiomeLooks.GroundShare share = Biomes.BiomeLooks.GroundShareOf(name);
				flora[i] = new Vector4(share.Foliage, share.Accent, share.Mineral, 0f);
				if (name == null || !name.StartsWith("Ground_", System.StringComparison.Ordinal))
				{
					byName++;
					first ??= name;
				}
			}
			if (byName > 0 && !warnedNameFallback)
			{
				warnedNameFallback = true;
				Debug.LogWarning($"[Terrain arrays] '{SceneName}': {byName} hand-made layer(s) of {count} have no ground makeup on their biome slot (e.g. '{first}'), so what they are made of was guessed from their names. Set Ground Makeup on their slot, then repaint (or rebake the arrays).");
			}
			layerFlora = flora;
			layerFloraNames = LayerNames;
			layerFloraMakeup = LayerMakeup;
			return flora;
		}
	}
}

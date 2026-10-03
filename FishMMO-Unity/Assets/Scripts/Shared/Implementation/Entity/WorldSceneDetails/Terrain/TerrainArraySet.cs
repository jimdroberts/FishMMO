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

		// The shader's five per-layer arrays, packed by TerrainArrayLayerParams.
		public Vector4[] LayerST = new Vector4[0];
		public Vector4[] LayerTint = new Vector4[0];
		public Vector4[] LayerMaskOffset = new Vector4[0];
		public Vector4[] LayerMaskScale = new Vector4[0];
		public Vector4[] LayerSurface = new Vector4[0];

		[Tooltip("A hash of everything the bake read. A different hash means the set is stale.")]
		public string ContentHash;

		/// <summary>True when the per-layer arrays are the full shader length and the albedo exists.</summary>
		public bool IsUsable =>
			Albedo != null && LayerCount > 0
			&& LayerST != null && LayerST.Length == TerrainArrayLayerParams.MaximumLayers
			&& LayerTint != null && LayerTint.Length == TerrainArrayLayerParams.MaximumLayers
			&& LayerMaskOffset != null && LayerMaskOffset.Length == TerrainArrayLayerParams.MaximumLayers
			&& LayerMaskScale != null && LayerMaskScale.Length == TerrainArrayLayerParams.MaximumLayers
			&& LayerSurface != null && LayerSurface.Length == TerrainArrayLayerParams.MaximumLayers;
	}
}

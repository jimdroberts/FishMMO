using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Atlas
{
	/// <summary>
	/// The world atlas: which solar system the world lives in and the layers every body has.
	/// Each scene's place on its body is a <see cref="WorldAtlasScene"/>.
	/// </summary>
	/// <remarks>
	/// Loaded on server and client alike. The designer is the FishMMO Dashboard → World → World
	/// Atlas page; there is one atlas per project.
	/// </remarks>
	[CreateAssetMenu(fileName = "World Atlas", menuName = "FishMMO/World/World Atlas", order = 14)]
	public class WorldAtlas : CachedScriptableObject<WorldAtlas>, ICachedObject
	{
		public SolarSystemProfile SolarSystem;
		[Tooltip("Layers a body has when it names none of its own. The first is where new scenes go.")]
		public List<WorldAtlasLayer> DefaultLayers = new List<WorldAtlasLayer>();
		[Tooltip("Layer for scenes that are dungeons. Empty: the second default layer, if any.")]
		public WorldAtlasLayer DungeonLayer;

		/// <summary>The loaded atlas, if any.</summary>
		public static WorldAtlas Active => GetFirst<WorldAtlas>();

		/// <summary>The layers of a body, in sort order.</summary>
		public List<WorldAtlasLayer> LayersOf(WorldBody body)
		{
			var result = new List<WorldAtlasLayer>();
			List<WorldAtlasLayer> source = body != null && body.Layers != null && body.Layers.Count > 0 ? body.Layers : DefaultLayers;
			if (source != null)
			{
				foreach (WorldAtlasLayer layer in source)
				{
					if (layer != null && !result.Contains(layer))
					{
						result.Add(layer);
					}
				}
			}
			result.Sort((a, b) => a.SortOrder != b.SortOrder ? a.SortOrder.CompareTo(b.SortOrder) : string.CompareOrdinal(a.name, b.name));
			return result;
		}

		/// <summary>The layer new scenes on a body start in.</summary>
		public WorldAtlasLayer SurfaceLayerOf(WorldBody body)
		{
			List<WorldAtlasLayer> layers = LayersOf(body);
			foreach (WorldAtlasLayer layer in layers)
			{
				if (!layer.Underground)
				{
					return layer;
				}
			}
			return layers.Count > 0 ? layers[0] : null;
		}

		/// <summary>The layer dungeon scenes start in.</summary>
		public WorldAtlasLayer DungeonLayerOf(WorldBody body)
		{
			if (DungeonLayer != null)
			{
				return DungeonLayer;
			}
			foreach (WorldAtlasLayer layer in LayersOf(body))
			{
				if (layer.Underground)
				{
					return layer;
				}
			}
			return SurfaceLayerOf(body);
		}
	}
}

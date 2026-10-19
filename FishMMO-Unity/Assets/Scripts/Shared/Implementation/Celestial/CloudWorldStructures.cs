using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>Where on a cloud world a way light stands (<see cref="CloudWorldWayLight"/>).</summary>
	/// <remarks>Append-only: the values are serialized.</remarks>
	public enum CloudWorldWayPlace : byte
	{
		/// <summary>Beside each end of each rope bridge between the islands, on the island's grass behind its posts.</summary>
		BridgeEnd = 0,
		/// <summary>Beside the landward end of each landing stage the airships berth at.</summary>
		LandingStage = 1,
	}

	/// <summary>A cloud world's light at one kind of place on its ways (<see cref="CloudWorldStructures.WayLights"/>).</summary>
	[Serializable]
	public struct CloudWorldWayLight
	{
		[Tooltip("Where the light stands.")]
		public CloudWorldWayPlace Place;
		[Tooltip("The light laid there (a kit piece standing on the ground: a lantern post, a torch post, a brazier, a crystal post…; a wall or hanging light is not laid out in the open).")]
		public BiomeWayLightKind Light;

		public CloudWorldWayLight(CloudWorldWayPlace place, BiomeWayLightKind light)
		{
			Place = place;
			Light = light;
		}

		/// <summary>The kit piece query the light is laid by ("=LanternPost").</summary>
		public string PieceQuery => BiomeWayLights.PieceQuery(Light);

		public override string ToString() => Place + "=" + Light;
	}

	/// <summary>
	/// What a cloud world's own structures are built of and lit by (2026-10-18, I6e; decided 2026-10-16): its landing stages,
	/// its airships and the rope bridges between its islands, which stand in no biome (the islands' sites take their own biome's
	/// palette). The world body is the source; nothing authors it, so an empty palette is the structure kit's own materials and
	/// an empty light list lays no lights, exactly what a cloud world was built of before.
	/// </summary>
	/// <remarks>
	/// The palette is a biome template's (<see cref="BiomeStructurePalette"/>): an entry per structure style and role, any role
	/// with no entry built as the kit builds it, so a designer names only what differs (the airships' and stages' timber, the
	/// bridges' rope). Its materials are made by the biome art generation with every other palette's.
	/// </remarks>
	[Serializable]
	public sealed class CloudWorldStructures
	{
		[Tooltip("What the cloud world's landing stages, airships and rope bridges are built of: an entry per structure style and role, as a biome template's structure palette. Empty: the structure kit's own materials.")]
		public BiomeStructurePalette Palette = new BiomeStructurePalette();

		[Tooltip("The lights along the cloud world's ways: one entry per place (bridge ends, landing stages). Empty: none, as the kit lays.")]
		public List<CloudWorldWayLight> WayLights = new List<CloudWorldWayLight>();

		/// <summary>True when the palette names nothing: the cloud world is built in the kit's own materials.</summary>
		public bool BuildsAsKit => Palette == null || Palette.IsEmpty;

		/// <summary>
		/// The palette the cut lays the world's structures in: null for the kit's own materials (none named), so a cut that is
		/// given none lays exactly what it always has.
		/// </summary>
		public BiomeStructurePalette Laid => BuildsAsKit ? null : Palette;

		/// <summary>The light a place is lit by: the first entry naming it; false when none does (not lit).</summary>
		public bool TryLight(CloudWorldWayPlace place, out CloudWorldWayLight light)
		{
			if (WayLights != null)
			{
				foreach (CloudWorldWayLight entry in WayLights)
				{
					if (entry.Place == place)
					{
						light = entry;
						return true;
					}
				}
			}
			light = default;
			return false;
		}

		/// <summary>A body's cloud-world palette as the cut lays it (<see cref="Laid"/>); null for no body or the kit's.</summary>
		public static BiomeStructurePalette PaletteOf(WorldBody body) => body != null && body.CloudWorldStructures != null ? body.CloudWorldStructures.Laid : null;

		/// <summary>Fills an empty palette with the kit's own entries (every style and role), so a designer has every row to edit.</summary>
		public bool FillWithKit()
		{
			if (Palette != null && !Palette.IsEmpty)
			{
				return false;
			}
			Palette = BiomeStructurePalette.Kit();
			return true;
		}
	}
}

using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>The body a sea creature is built and animated as (SeaCreatureMeshes, FishSeaLife.hlsl's <c>_Mode</c>).</summary>
	public enum SeaCreatureShape
	{
		/// <summary>A slender fish: baitfish, jacks.</summary>
		Fish,
		/// <summary>A deep-bodied reef fish.</summary>
		ReefFish,
		/// <summary>A shark: pointed snout, tall dorsal, the upper tail lobe longer.</summary>
		Shark,
		/// <summary>A whale: horizontal flukes beating up and down.</summary>
		Whale,
		/// <summary>A manta or eagle ray: a flat diamond flying on its wings.</summary>
		Ray,
		/// <summary>A sea turtle: a domed shell, front flippers beating together.</summary>
		Turtle,
		/// <summary>A jellyfish: a pulsing bell trailing tentacles, drawn translucent.</summary>
		Jelly,
		/// <summary>A crab, walking the sea floor.</summary>
		Crab,
	}

	/// <summary>How a kind moves about its home (SeaLifePlacement.Evaluate).</summary>
	public enum SeaCreatureBehaviour
	{
		/// <summary>A tight school sweeping a wide loop through open water.</summary>
		School,
		/// <summary>One or two animals cruising a wide, slow loop.</summary>
		Cruise,
		/// <summary>A loose group hovering and darting over the reef, close to the bed.</summary>
		Hover,
		/// <summary>Drifting with the water, barely going anywhere: jellies.</summary>
		Drift,
		/// <summary>Walking slow small loops on the sea floor.</summary>
		Crawl,
	}

	/// <summary>One kind of background sea creature: what it looks like, where it lives and how it moves.</summary>
	[Serializable]
	public class SeaCreatureKind
	{
		public string Name = "Fish";
		public SeaCreatureShape Shape = SeaCreatureShape.Fish;
		public SeaCreatureBehaviour Behaviour = SeaCreatureBehaviour.School;
		[Tooltip("Off: none of this kind are placed or drawn.")]
		public bool Enabled = true;

		[Header("Size and numbers")]
		[Tooltip("Body length in metres (a ray's span, a jelly's bell, a crab's width), min..max per animal.")]
		public Vector2 Length = new Vector2(0.12f, 0.18f);
		[Tooltip("Animals in a group, min..max.")]
		public Vector2Int GroupSize = new Vector2Int(30, 80);
		[Tooltip("Groups per square kilometre of water where the kind is at home (thinned where it is less so).")]
		[Min(0f)] public float GroupsPerKm2 = 10f;
		[Tooltip("How far a group's members spread about its centre, metres (a school's radius, a reef group's patch).")]
		[Min(0.1f)] public float Spread = 2.5f;

		[Header("Movement")]
		[Tooltip("Cruising speed, metres a second.")]
		[Min(0.01f)] public float Speed = 1.2f;
		[Tooltip("The radius of the loop a group swims about its home, metres, min..max.")]
		public Vector2 Roam = new Vector2(8f, 18f);
		[Tooltip("Tail beats (wing beats, pulses, steps) a second at cruising speed.")]
		[Min(0.01f)] public float StrokeHz = 3f;
		[Tooltip("The stroke's swing, as a share of the body's length (FishSeaLife.hlsl _Amplitude).")]
		[Range(0f, 0.5f)] public float Stroke = 0.08f;
		[Tooltip("The body wave's length in body lengths (fish and whales).")]
		[Range(0.2f, 3f)] public float WaveLength = 0.9f;

		[Header("Habitat")]
		[Tooltip("Metres below the sea's mean surface the kind swims between (min..max). For Hover and Crawl, the band its home's sea floor must lie in.")]
		public Vector2 Depth = new Vector2(2f, 25f);
		[Tooltip("The least water over the sea floor anywhere on its loop, metres: it never swims into the shallows.")]
		[Min(0f)] public float MinWater = 4f;
		[Tooltip("Metres above the sea floor it keeps (Hover: the height band it hovers in, min..max).")]
		public Vector2 FloorClearance = new Vector2(1.5f, 3f);
		[Tooltip("The water's temperature it lives in, -1 coldest .. 1 hottest (the scene's climate at the place).")]
		public Vector2 Temperature = new Vector2(-1f, 1f);
		[Tooltip("Biomes (asset names) where it is most at home: groups there are this many times as common.")]
		public string[] Prefers = new string[0];
		[Min(1f)] public float PreferWeight = 3f;
		[Tooltip("Elsewhere, its density is multiplied by this (0: it lives only in its preferred biomes).")]
		[Range(0f, 1f)] public float ElsewhereWeight = 1f;

		[Header("Look")]
		[Tooltip("The back's colour (countershaded toward the belly).")]
		public Color Back = new Color(0.25f, 0.32f, 0.4f);
		public Color Belly = new Color(0.85f, 0.88f, 0.9f);
		[Tooltip("When set, every member takes one of these as its colour (reef fish, jellies); else the back and belly with a little variation.")]
		public Color[] Palette = new Color[0];
		[Range(0f, 1f)] public float Smoothness = 0.6f;
		[Tooltip("Casts shadows on the sea floor (big animals).")]
		public bool CastShadows;
	}

	/// <summary>
	/// The background life of the sea (<see cref="SeaLifeSystem"/>): which kinds there are and how far they are
	/// drawn. On the Weather Render Profile ("Sea life"); read every frame, so changes apply while playing
	/// (except the kinds' placement, which a scene works out once as the camera reaches it).
	/// </summary>
	[Serializable]
	public class SeaLifeSettings
	{
		[Tooltip("Off: no sea creatures, and the sea's globals (FishSea.hlsl) are still published for the plants.")]
		public bool Enabled = true;
		[Tooltip("Metres from the camera the creatures are drawn to; past it they are not even placed.")]
		[Min(10f)] public float DrawDistance = 70f;
		[Tooltip("The last share of the draw distance over which they dissolve.")]
		[Range(0.05f, 0.5f)] public float FadeBand = 0.2f;
		[Tooltip("Metres a creature casts its shadow to (only kinds that cast shadows).")]
		[Min(0f)] public float ShadowDistance = 40f;
		[Tooltip("The placement grid, metres: each cell decides its own groups from its coordinates, so a cell's life is the same whenever and wherever it is first seen.")]
		[Min(16f)] public float CellMetres = 96f;
		[Tooltip("The camera this far above the sea's surface, or more, sees none (nothing is placed or drawn).")]
		[Min(0f)] public float MaxCameraHeight = 120f;

		public SeaCreatureKind[] Kinds = Defaults();

		/// <remarks>
		/// Zero by default, not <see cref="CurrentVersion"/>: a profile saved before the field existed has no value for it,
		/// and Unity gives a missing field its initializer, so a current initializer would read every old profile as
		/// already upgraded. Fresh settings "upgrade" harmlessly: none of their numbers are the old defaults.
		/// </remarks>
		[Tooltip("Which defaults these settings were written against (Upgrade); leave it be.")]
		public int Version;

		/// <summary>The defaults' revision: 2 (2026-10-06) is the denser sea drawn to what the water lets anyone see.</summary>
		public const int CurrentVersion = 2;

		/// <summary>
		/// Brings settings written against older defaults up to date, once: a number still at its old default takes the
		/// new one, and one somebody set by hand is left alone.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Version 2: as many fish as a diver sees (Jim, 2026-10-06: "fish are way too rare still").</b> The densities
		/// were per square kilometre of sea, and the sea is seen through some 22 m of water: about 0.002 km² at a time,
		/// so 150 schools to the km² put a fifth of one in view. They are now four to twelve times that. They were
		/// drawn and placed out to 140 m, which nobody under water can see; drawn to 70 m, the denser sea costs about
		/// what the thin one did.
		/// </para>
		/// </remarks>
		public void Upgrade()
		{
			if (Version >= CurrentVersion)
			{
				return;
			}
			if (Version < 2)
			{
				DrawDistance = Mathf.Approximately(DrawDistance, 140f) ? 70f : DrawDistance;
				ShadowDistance = Mathf.Approximately(ShadowDistance, 60f) ? 40f : ShadowDistance;
				foreach (SeaCreatureKind kind in Kinds ?? Array.Empty<SeaCreatureKind>())
				{
					if (kind == null)
					{
						continue;
					}
					foreach ((string name, float before, float after) in Version2Densities)
					{
						if (kind.Name == name && Mathf.Approximately(kind.GroupsPerKm2, before))
						{
							kind.GroupsPerKm2 = after;
						}
					}
				}
			}
			Version = CurrentVersion;
		}

		/// <summary>Version 2's densities, per kind: the old default and the new one.</summary>
		private static readonly (string Name, float Before, float After)[] Version2Densities =
		{
			("Baitfish", 150f, 600f),
			("Reef fish", 80f, 1000f),
			("Jacks", 20f, 100f),
			("Shark", 3f, 12f),
			("Manta", 2f, 8f),
			("Sea turtle", 8f, 30f),
			("Jellyfish", 50f, 200f),
			("Crab", 150f, 1000f),
			("Whale", 0.4f, 1.5f),
		};

		/// <summary>The eight kinds the sea starts with, and a whale.</summary>
		public static SeaCreatureKind[] Defaults()
		{
			return new[]
			{
				new SeaCreatureKind
				{
					Name = "Baitfish", Shape = SeaCreatureShape.Fish, Behaviour = SeaCreatureBehaviour.School,
					Length = new Vector2(0.12f, 0.18f), GroupSize = new Vector2Int(40, 90), GroupsPerKm2 = 600f, Spread = 2.5f,
					Speed = 1.2f, Roam = new Vector2(8f, 18f), StrokeHz = 4f, Stroke = 0.1f, WaveLength = 0.9f,
					Depth = new Vector2(1f, 25f), MinWater = 3f, FloorClearance = new Vector2(0.8f, 3f),
					Back = Hex(0x3a4a5a), Belly = Hex(0xdde2e6), Smoothness = 0.85f,
				},
				new SeaCreatureKind
				{
					Name = "Reef fish", Shape = SeaCreatureShape.ReefFish, Behaviour = SeaCreatureBehaviour.Hover,
					Length = new Vector2(0.14f, 0.3f), GroupSize = new Vector2Int(5, 14), GroupsPerKm2 = 1000f, Spread = 4f,
					Speed = 0.4f, Roam = new Vector2(2f, 5f), StrokeHz = 2.5f, Stroke = 0.08f, WaveLength = 1.1f,
					Depth = new Vector2(1.5f, 30f), MinWater = 1.5f, FloorClearance = new Vector2(0.4f, 2.5f),
					Temperature = new Vector2(0.2f, 1f), Prefers = new[] { "Coral Reef" }, PreferWeight = 5f, ElsewhereWeight = 0.2f,
					Back = new Color(0.82f, 0.82f, 0.82f), Belly = Color.white, Smoothness = 0.6f,
					Palette = new[] { Hex(0xf2d02a), Hex(0x2a7ae0), Hex(0xf07a2a), Hex(0x8a5ad0), Hex(0x3ac0b0), Hex(0xe8e8e8) },
				},
				new SeaCreatureKind
				{
					Name = "Jacks", Shape = SeaCreatureShape.Fish, Behaviour = SeaCreatureBehaviour.School,
					Length = new Vector2(0.6f, 1f), GroupSize = new Vector2Int(10, 24), GroupsPerKm2 = 100f, Spread = 4f,
					Speed = 1.8f, Roam = new Vector2(25f, 50f), StrokeHz = 2.2f, Stroke = 0.07f, WaveLength = 1f,
					Depth = new Vector2(3f, 60f), MinWater = 6f, FloorClearance = new Vector2(1.5f, 6f),
					Back = Hex(0x4a5a6a), Belly = Hex(0xc8d0d0), Smoothness = 0.8f,
				},
				new SeaCreatureKind
				{
					Name = "Shark", Shape = SeaCreatureShape.Shark, Behaviour = SeaCreatureBehaviour.Cruise,
					Length = new Vector2(2.2f, 3.2f), GroupSize = new Vector2Int(1, 1), GroupsPerKm2 = 12f, Spread = 1f,
					Speed = 1.3f, Roam = new Vector2(30f, 70f), StrokeHz = 0.8f, Stroke = 0.06f, WaveLength = 1.1f,
					Depth = new Vector2(3f, 80f), MinWater = 6f, FloorClearance = new Vector2(1.5f, 8f),
					Back = Hex(0x5a6670), Belly = Hex(0xd8d8d0), Smoothness = 0.4f, CastShadows = true,
				},
				new SeaCreatureKind
				{
					Name = "Manta", Shape = SeaCreatureShape.Ray, Behaviour = SeaCreatureBehaviour.Cruise,
					Length = new Vector2(3f, 4.5f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 8f, Spread = 6f,
					Speed = 0.9f, Roam = new Vector2(25f, 50f), StrokeHz = 0.35f, Stroke = 0.22f,
					Depth = new Vector2(3f, 30f), MinWater = 6f, FloorClearance = new Vector2(1.5f, 6f),
					Temperature = new Vector2(0f, 1f),
					Back = Hex(0x2a2e34), Belly = Hex(0xe8e8e4), Smoothness = 0.35f, CastShadows = true,
				},
				new SeaCreatureKind
				{
					Name = "Sea turtle", Shape = SeaCreatureShape.Turtle, Behaviour = SeaCreatureBehaviour.Cruise,
					Length = new Vector2(0.8f, 1.2f), GroupSize = new Vector2Int(1, 1), GroupsPerKm2 = 30f, Spread = 1f,
					Speed = 0.6f, Roam = new Vector2(12f, 30f), StrokeHz = 0.45f, Stroke = 0.18f,
					Depth = new Vector2(1.5f, 20f), MinWater = 2.5f, FloorClearance = new Vector2(0.8f, 4f),
					Temperature = new Vector2(0.1f, 1f), Prefers = new[] { "Coral Reef", "Coastal Water" }, PreferWeight = 2f,
					Back = Hex(0x5a5030), Belly = Hex(0xc8b888), Smoothness = 0.5f, CastShadows = true,
				},
				new SeaCreatureKind
				{
					Name = "Jellyfish", Shape = SeaCreatureShape.Jelly, Behaviour = SeaCreatureBehaviour.Drift,
					Length = new Vector2(0.25f, 0.5f), GroupSize = new Vector2Int(5, 16), GroupsPerKm2 = 200f, Spread = 8f,
					Speed = 0.08f, Roam = new Vector2(2f, 4f), StrokeHz = 0.7f, Stroke = 0.18f,
					Depth = new Vector2(1f, 30f), MinWater = 2.5f, FloorClearance = new Vector2(1f, 4f),
					Back = Hex(0xe0b0d8), Belly = Hex(0xf0e0f0), Smoothness = 0.8f,
					Palette = new[] { Hex(0xe0a0d0), Hex(0xa0c8f0), Hex(0xf0c080), Hex(0xd8e8f0) },
				},
				new SeaCreatureKind
				{
					Name = "Crab", Shape = SeaCreatureShape.Crab, Behaviour = SeaCreatureBehaviour.Crawl,
					Length = new Vector2(0.15f, 0.25f), GroupSize = new Vector2Int(2, 5), GroupsPerKm2 = 1000f, Spread = 4f,
					Speed = 0.12f, Roam = new Vector2(1f, 3f), StrokeHz = 2f, Stroke = 0.08f,
					Depth = new Vector2(0.8f, 30f), MinWater = 0.8f, FloorClearance = new Vector2(0f, 0f),
					Prefers = new[] { "Coastal Water", "Beach", "Coral Reef" }, PreferWeight = 2f, ElsewhereWeight = 0.5f,
					Back = Hex(0xa04a2a), Belly = Hex(0xd88a5a), Smoothness = 0.5f,
				},
				new SeaCreatureKind
				{
					Name = "Whale", Shape = SeaCreatureShape.Whale, Behaviour = SeaCreatureBehaviour.Cruise,
					Length = new Vector2(11f, 15f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 1.5f, Spread = 14f,
					Speed = 1.6f, Roam = new Vector2(80f, 160f), StrokeHz = 0.18f, Stroke = 0.07f, WaveLength = 1.4f,
					Depth = new Vector2(10f, 60f), MinWater = 30f, FloorClearance = new Vector2(6f, 15f),
					Prefers = new[] { "Ocean", "Deep Ocean" }, PreferWeight = 2f, ElsewhereWeight = 0.3f,
					Back = Hex(0x3a4048), Belly = Hex(0xc8c8c8), Smoothness = 0.4f, CastShadows = true,
				},
			};
		}

		/// <summary>An sRGB colour from 0xRRGGBB.</summary>
		private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
	}
}

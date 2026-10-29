using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>What a cave-life spot is: the kind of place in a cave an animal lives on, in or over.</summary>
	/// <remarks>Append only; the ordinal is stored in scene assets.</remarks>
	public enum SceneCaveLifeKind : byte
	{
		/// <summary>A patch of floor flat enough to walk on (<see cref="SceneCaveLifeSpot.Extent"/>.x across): rats, crickets, salamanders.</summary>
		Floor = 0,
		/// <summary>A patch of wall, its normal level or nearly: geckos, spiders and crickets cling to it.</summary>
		Wall = 1,
		/// <summary>A patch of roof high over the floor, its normal pointing down: where a bat colony hangs.</summary>
		Roost = 2,
		/// <summary>
		/// A box of open air found clear of rock all through (<see cref="SceneCaveLifeSpot.Extent"/> its half-extents along
		/// <see cref="SceneCaveLifeSpot.Yaw"/>, up and across): bats and moths fly loops in it.
		/// </summary>
		Flight = 3,
		/// <summary>The still water standing in a cave (a grotto's pool, a sea cave's tide), at its surface: olms swim under it.</summary>
		Pool = 4,
		/// <summary>
		/// A way out to the sky: <see cref="SceneCaveLifeSpot.Position"/> in the mouth's bore, <see cref="SceneCaveLifeSpot.Normal"/>
		/// out of it, and a straight run back in clear of rock (<see cref="SceneCaveLifeSpot.Extent"/>.z): what bats stream out
		/// along at dusk and back in along before dawn.
		/// </summary>
		Mouth = 5,
		/// <summary>Still damp air under a high roof, where glow-worms hang their threads and fungus gnats dance.</summary>
		Glow = 6,
		/// <summary>
		/// A sea hall's smoker (2026-10-26, phase I2n-b, decision 83; its kind in <see cref="SceneCaveLifeFlags"/>):
		/// <see cref="SceneCaveLifeSpot.Position"/> its orifice, <see cref="SceneCaveLifeSpot.Extent"/> x the orifice's radius, y the
		/// chimney's height under it, z the clear water and air over it to the roof; <see cref="SceneCaveLifeSpot.Yaw"/> its own turn
		/// (where its flanges face). What the client's plumes rise from (SceneSeaHallVents); nothing lives on it.
		/// </summary>
		Smoker = 7,
		/// <summary>
		/// A sea hall's dome (2026-10-26, I2n-b): <see cref="SceneCaveLifeSpot.Position"/> its vault's centre (an ellipsoid on a flat
		/// floor), <see cref="SceneCaveLifeSpot.Extent"/> its half-extents along <see cref="SceneCaveLifeSpot.Yaw"/>, up and across: the
		/// roof the client's brinicles hang from and its smoke ceiling spreads under. Its water is the hall's Pool spot.
		/// </summary>
		Hall = 8,
		/// <summary>
		/// A hot brine pool on a sea hall's floor (2026-10-26, I2n-b): <see cref="SceneCaveLifeSpot.Position"/> its mirror's middle (the
		/// brine's surface under the sea water), <see cref="SceneCaveLifeSpot.Extent"/> x its radius, y how far its rim stands over the
		/// mirror. Drawn by the client as a shimmering mirror; nothing lives in it.
		/// </summary>
		Brine = 9,
	}

	/// <summary>The kind of cave a spot is in, which says what can live there (the cut's cave forms, by the same ordinals).</summary>
	/// <remarks>Append only; the ordinal is stored in scene assets.</remarks>
	public enum SceneCaveForm : byte
	{
		/// <summary>A natural cave in the hill's rock: the richest life.</summary>
		Cave = 0,
		/// <summary>A short cave opening onto water, its front flooded.</summary>
		Grotto = 1,
		/// <summary>A cave the sea runs into.</summary>
		SeaCave = 2,
		/// <summary>A cave in ice: nearly lifeless.</summary>
		IceCave = 3,
		/// <summary>A drained lava conduit: dry bare basalt, a few hardy crawlers.</summary>
		LavaTube = 4,
		/// <summary>A worked mine: its drifts and stopes.</summary>
		Mine = 5,
		/// <summary>A sea hall under an ice shell (2026-10-26, I2n-b): its life is its sea's, swimming in its water.</summary>
		SeaHall = 6,
	}

	/// <summary>What else a cave-life spot is (bits).</summary>
	[Flags]
	public enum SceneCaveLifeFlags : byte
	{
		None = 0,
		/// <summary>Wet: by standing water, or under a dripping roof in a limestone cave (salamanders, cave frogs).</summary>
		Damp = 1 << 0,
		/// <summary>By (or in) the sea's water, not fresh: crabs, never olms.</summary>
		Salt = 1 << 1,
		/// <summary>A <see cref="SceneCaveLifeKind.Smoker"/> that is its hall's Great Smoker, its crown out of the water (2026-10-26, I2n-b).</summary>
		Great = 1 << 2,
		/// <summary>A <see cref="SceneCaveLifeKind.Smoker"/> that is a white smoker (anhydrite, white plume, carbon-dioxide beads).</summary>
		White = 1 << 3,
		/// <summary>A <see cref="SceneCaveLifeKind.Smoker"/> that is a beehive diffuser (a shimmer, no column).</summary>
		Beehive = 1 << 4,
	}

	/// <summary>
	/// One place in a carved cave its animals live at, baked by the cut from the cave's own field (the cave dressing): a flat
	/// patch of floor, wall or roof, a box of clear air, a pool, a mouth. The ambient life places its cave creatures on these
	/// and keeps them to them, so it needs no ray against the cave's rock, and the cave's floors under and over each other are
	/// no puzzle to it (the terrain's one height a place is useless underground).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A patch is flat.</b> A floor, wall or roof patch was measured round its centre and kept only as wide as the rock lies
	/// within a few centimetres of its plane, so whatever walks or hangs on it, anywhere within <see cref="Extent"/>.x of
	/// <see cref="Position"/> on the plane through it square to <see cref="Normal"/>, stands on the rock.
	/// </para>
	/// <para>
	/// Plain data: read in builds, by the client only (nothing here is gameplay).
	/// </para>
	/// </remarks>
	[Serializable]
	public struct SceneCaveLifeSpot
	{
		public SceneCaveLifeKind Kind;
		public SceneCaveForm Form;
		public SceneCaveLifeFlags Flags;

		/// <summary>World metres: a patch's centre on the rock; a box's middle; a pool's surface over its deepest; a mouth's bore.</summary>
		public Vector3 Position;

		/// <summary>
		/// Unit: out of the rock into the air at a patch (up off a floor, out of a wall, down from a roof); out of the cave at a
		/// mouth; up for a box, a pool and a glow.
		/// </summary>
		public Vector3 Normal;

		/// <summary>
		/// Metres. A patch: x how far it is flat round its centre, y the air over it (to the roof, or across to the far wall).
		/// A box (and a glow): its half-extents along <see cref="Yaw"/>, up and across. A pool: x its radius, y the water's depth
		/// at its middle. A mouth: x the bore's radius, z how far the run back in from <see cref="Position"/> is clear.
		/// </summary>
		public Vector3 Extent;

		/// <summary>Degrees about +y: a box's heading (the chamber's or the passage's).</summary>
		public float Yaw;

		/// <summary>How far from the daylight it is, 0 in the mouth's light .. 1 past the twilight, in the dark for good.</summary>
		public float Dark;

		/// <summary>The site (<see cref="PointOfInterestRecord.Id"/>) whose cave system it is in.</summary>
		public int SiteId;

		/// <summary>
		/// The cave biome dressing the inside of its cave (2026-10-25, phase I2n: its biome template's id, BiomeRegistry.IDOf), where
		/// the biome it opens in links one (a limestone cave in karst country is the Cave inside); 0 where the cave is its host land's,
		/// read as the biome map's there, as before.
		/// </summary>
		public int Biome;
	}

	/// <summary>
	/// The cave-life spots of every loaded scene (<see cref="ScenePointsOfInterest.CaveLife"/>), keyed by scene, offered by the
	/// scene's client-only site emitters (<see cref="SceneSiteEmitters"/>) while it is loaded: what the ambient life places its
	/// cave creatures from.
	/// </summary>
	/// <remarks>
	/// <b>Keyed by scene,</b> as <see cref="SceneClimbables"/> is, so stacked instances of one scene each offer their own, and
	/// <b>a spot is its index in its scene's list</b>: every client loads the same asset, so a creature hashed from its scene's
	/// name and its spot's index is the same creature for everyone.
	/// </remarks>
	public static class SceneCaveLife
	{
		/// <summary>One scene's spots, as offered.</summary>
		public readonly struct Set
		{
			public readonly Scene Scene;
			public readonly IReadOnlyList<SceneCaveLifeSpot> Spots;

			public Set(Scene scene, IReadOnlyList<SceneCaveLifeSpot> spots)
			{
				Scene = scene;
				Spots = spots;
			}
		}

		private sealed class Entry
		{
			public object Owner;
			public Set Set;
		}

		private static readonly Dictionary<int, Entry> scenes = new Dictionary<int, Entry>();
		private static readonly List<Set> sets = new List<Set>();
		private static bool listed;

		/// <summary>Bumped whenever a scene's spots come or go, so a reader can cache what it filed.</summary>
		public static int Version { get; private set; }

		/// <summary>Offers <paramref name="scene"/>'s spots. A second owner for the same scene replaces the first.</summary>
		public static void Register(Scene scene, object owner, IReadOnlyList<SceneCaveLifeSpot> spots)
		{
			if (spots == null || spots.Count == 0)
			{
				return;
			}
			scenes[scene.handle] = new Entry { Owner = owner, Set = new Set(scene, spots) };
			listed = false;
			Version++;
		}

		/// <summary>Withdraws <paramref name="scene"/>'s spots, when <paramref name="owner"/> is the one that offered them.</summary>
		public static void Unregister(Scene scene, object owner)
		{
			if (scenes.TryGetValue(scene.handle, out Entry entry) && ReferenceEquals(entry.Owner, owner))
			{
				scenes.Remove(scene.handle);
				listed = false;
				Version++;
			}
		}

		/// <summary>Forgets every scene's spots.</summary>
		public static void Clear()
		{
			scenes.Clear();
			listed = false;
			Version++;
		}

		/// <summary>Every loaded scene's spots, ordered by scene name (not by load order, which differs between players).</summary>
		public static IReadOnlyList<Set> Sets
		{
			get
			{
				if (!listed)
				{
					listed = true;
					sets.Clear();
					foreach (Entry entry in scenes.Values)
					{
						sets.Add(entry.Set);
					}
					sets.Sort((a, b) =>
					{
						int byName = string.CompareOrdinal(a.Scene.name, b.Scene.name);
						int ha = a.Scene.handle, hb = b.Scene.handle;
						return byName != 0 ? byName : ha.CompareTo(hb);
					});
				}
				return sets;
			}
		}
	}
}

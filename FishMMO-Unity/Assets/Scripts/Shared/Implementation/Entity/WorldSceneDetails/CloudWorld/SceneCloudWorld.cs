using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene cut on a gas giant (<see cref="Celestial.CloudWorld"/>): islands of rock floating over a cloud deck with
	/// nothing under it. Written by the cut, kept by the server and every client, and found by scene
	/// (<see cref="TryGet"/>) by what has to know: the sky (the deck it draws below the islands), the server (who has fallen
	/// into it), the airships (the docks they sail between).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Heights are y, as everywhere else:</b> scene metres above the body's datum. The deck's top is at
	/// <see cref="DeckTop"/>, the islands stand between <see cref="IslandFloor"/> and <see cref="IslandCeiling"/>, and a
	/// character that sinks past <see cref="DeathAltitude"/> is lost in the cloud: it dies, and respawns as from any death.
	/// </para>
	/// <para>
	/// <b>Keyed by scene,</b> as <see cref="SceneClimbables"/> is: a scene server runs several world scenes at once, each in
	/// its own physics scene, and a character asks with its own.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class SceneCloudWorld : MonoBehaviour
	{
		/// <summary>The object the cut puts this on.</summary>
		public const string ObjectName = "Cloud World";

		/// <summary>What an island is for: how big it was planned, and so what the cut put on it.</summary>
		public enum IslandKind : byte
		{
			Islet = 0,
			Small = 1,
			Medium = 2,
			Large = 3,
			/// <summary>The biggest island, which carries the scene's city.</summary>
			City = 4,
		}

		/// <summary>One floating island: its outline's centre and reach, and the height of its ground.</summary>
		[Serializable]
		public struct Island
		{
			public IslandKind Kind;
			/// <summary>The centre of its outline, at the mean height of its ground.</summary>
			public Vector3 Centre;
			/// <summary>How far its outline reaches from the centre at its widest, metres.</summary>
			public float Radius;
			/// <summary>The lowest point of its underside, y.</summary>
			public float Keel;
		}

		/// <summary>A landing stage at an island's edge, where airships tie up: its end over the void, and the way out.</summary>
		[Serializable]
		public struct Dock
		{
			/// <summary>Index into <see cref="Islands"/>.</summary>
			public int Island;
			/// <summary>The end of the landing stage, at its deck.</summary>
			public Vector3 Position;
			/// <summary>Horizontal, from the island out over the void.</summary>
			public Vector3 Outward;
		}

		[Tooltip("The top of the cloud deck, y: below it is cloud, with no floor.")]
		public float DeckTop;

		[Tooltip("How deep the deck is drawn below its top, metres: past this the cloud is only dark.")]
		public float DeckDepth = 400f;

		[Tooltip("A character below this y has fallen into the cloud: it dies and respawns.")]
		public float DeathAltitude = -60f;

		[Tooltip("The lowest island ground, y.")]
		public float IslandFloor;

		[Tooltip("The highest island ground, y.")]
		public float IslandCeiling;

		public Island[] Islands = Array.Empty<Island>();
		public Dock[] Docks = Array.Empty<Dock>();

		private static readonly Dictionary<int, SceneCloudWorld> scenes = new Dictionary<int, SceneCloudWorld>();

		/// <summary>Bumped whenever a scene's cloud world comes or goes, so a reader can cache what it found.</summary>
		public static int Version { get; private set; }

		/// <summary>True when any loaded scene is a cloud world.</summary>
		public static bool Any => scenes.Count > 0;

		/// <summary>The cloud world <paramref name="scene"/> is, if it is one.</summary>
		public static bool TryGet(Scene scene, out SceneCloudWorld world)
		{
			if (scenes.TryGetValue(scene.handle, out world) && world != null)
			{
				return true;
			}
			world = null;
			return false;
		}

		/// <summary>
		/// The first loaded cloud world, for the client's sky, which draws one world at a time; null when none is loaded.
		/// </summary>
		public static SceneCloudWorld First
		{
			get
			{
				foreach (SceneCloudWorld world in scenes.Values)
				{
					if (world != null)
					{
						return world;
					}
				}
				return null;
			}
		}

		/// <summary>True when a point in <paramref name="scene"/> is below the depth a cloud world loses whoever reaches it.</summary>
		public static bool IsLost(Scene scene, Vector3 position)
			=> TryGet(scene, out SceneCloudWorld world) && position.y < world.DeathAltitude;

		/// <summary>The island whose outline's reach covers a horizontal position nearest its centre; -1 when none does.</summary>
		public int IslandAt(float x, float z)
		{
			int best = -1;
			float nearest = float.PositiveInfinity;
			for (int i = 0; i < Islands.Length; i++)
			{
				float dx = Islands[i].Centre.x - x, dz = Islands[i].Centre.z - z;
				float d = dx * dx + dz * dz;
				if (d <= Islands[i].Radius * Islands[i].Radius && d < nearest)
				{
					nearest = d;
					best = i;
				}
			}
			return best;
		}

		private void OnEnable()
		{
			scenes[gameObject.scene.handle] = this;
			Version++;
		}

		private void OnDisable()
		{
			if (scenes.TryGetValue(gameObject.scene.handle, out SceneCloudWorld world) && world == this)
			{
				scenes.Remove(gameObject.scene.handle);
				Version++;
			}
		}
	}
}

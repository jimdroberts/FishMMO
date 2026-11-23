using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What the core asks of a world scene's entry in the world's design (the world atlas): only what the game itself runs on.
	/// </summary>
	/// <remarks>
	/// Core. Where a scene lies on its planet, its climate, its biomes and
	/// its weather belong to the world modules, which read their own entry; the core needs a scene's capacity and which way
	/// north lies on its map. Without a world module installed no scene has an entry, and every scene keeps its own cap
	/// and +Z as north.
	/// </remarks>
	public interface IWorldSceneEntry
	{
		/// <summary>The client cap this entry sets for its scene; 0 leaves the scene's own.</summary>
		int MaxClients { get; }

		/// <summary>Degrees clockwise from the scene's +Z to its planet's north at a position in the scene; 0 where unknown.</summary>
		float NorthBearingAt(Vector3 position);
	}

	/// <summary>The world scene entries the installed world design offers (<see cref="IWorldSceneEntry"/>), by scene name.</summary>
	public static class WorldSceneEntries
	{
		/// <summary>The lookup the world design installs; null when none is installed.</summary>
		public static Func<string, IWorldSceneEntry> Lookup { get; set; }

		/// <summary>The entry for a scene, or null.</summary>
		public static IWorldSceneEntry Find(string sceneName)
		{
			Func<string, IWorldSceneEntry> lookup = Lookup;
			return lookup != null ? lookup(sceneName) : null;
		}
	}
}

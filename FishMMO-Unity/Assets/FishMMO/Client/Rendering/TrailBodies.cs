using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Bodies that are not characters but tread the ground as one does: the world test bed's walker. Core so the test bed's
	/// scene components name no module (Phase 4 of the core and modules plan, 2026-11-18); the Terrain module's trail map
	/// reads them with the characters.
	/// </summary>
	public static class TrailBodies
	{
		/// <summary>A person's footprint radius, metres.</summary>
		public const float PersonRadius = 0.35f;

		private static readonly Dictionary<Transform, float> bodies = new Dictionary<Transform, float>();

		/// <summary>The bodies and their radii.</summary>
		public static IReadOnlyDictionary<Transform, float> All => bodies;

		/// <summary>Treads <paramref name="body"/> as a character is trodden.</summary>
		public static void Add(Transform body, float radius = PersonRadius)
		{
			if (body != null)
			{
				bodies[body] = Mathf.Max(0.05f, radius);
			}
		}

		public static void Remove(Transform body)
		{
			if (body != null)
			{
				bodies.Remove(body);
			}
		}

		public static void Clear() => bodies.Clear();
	}
}

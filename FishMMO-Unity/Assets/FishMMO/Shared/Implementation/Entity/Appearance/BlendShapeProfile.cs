using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A single named blend shape entry with a 0-100 weight value.
	/// </summary>
	[Serializable]
	public struct BlendShapeEntry
	{
		/// <summary>
		/// Name of the blend shape on the mesh (e.g., "Weight", "MuscleMass").
		/// </summary>
		[Tooltip("Name of the blend shape on the mesh. Must match exactly.")]
		public string Name;

		/// <summary>
		/// Blend shape weight value (0.0 to 100.0).
		/// </summary>
		[Range(0f, 100f)]
		[Tooltip("Blend shape weight (0-100).")]
		public float Value;
	}

	/// <summary>
	/// A collection of blend shape entries representing a character's body shape
	/// or an equipment item's blend shape overrides.
	/// Used to sync body blend shapes to equipment meshes that have matching shape keys.
	/// </summary>
	[Serializable]
	public class BlendShapeProfile
	{
		/// <summary>
		/// Blend shape entries in this profile.
		/// </summary>
		[Tooltip("Blend shape entries for this profile.")]
		public BlendShapeEntry[] Entries = Array.Empty<BlendShapeEntry>();

		/// <summary>
		/// Returns the number of entries in this profile.
		/// </summary>
		public int Count => Entries?.Length ?? 0;

		/// <summary>
		/// Tries to get a blend shape value by name.
		/// </summary>
		/// <param name="name">The blend shape name to look up.</param>
		/// <param name="value">The value if found.</param>
		/// <returns>True if the blend shape exists in this profile.</returns>
		public bool TryGetValue(string name, out float value)
		{
			if (Entries != null)
			{
				for (int i = 0; i < Entries.Length; i++)
				{
					if (Entries[i].Name == name)
					{
						value = Entries[i].Value;
						return true;
					}
				}
			}
			value = 0f;
			return false;
		}
	}
}

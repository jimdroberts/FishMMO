using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// One NPC attribute a difficulty scales, and by how much.
	/// </summary>
	/// <remarks>
	/// Named rather than inferred. There is no built-in notion of which attribute represents an
	/// enemy's damage — that is a decision each build makes when it authors its attribute
	/// templates — so a difficulty says which attributes it changes instead of guessing at a
	/// classification that may not exist.
	/// </remarks>
	[Serializable]
	public class DungeonAttributeScalar
	{
		/// <summary>The attribute to scale.</summary>
		[Tooltip("The NPC attribute to scale inside the instance.")]
		public CharacterAttributeTemplate Template;

		/// <summary>Multiplier applied to it. 1 leaves it alone.</summary>
		[Tooltip("Multiplier applied to that attribute. 1 leaves it unchanged.")]
		[Min(0.01f)]
		public float Multiplier = 1.0f;
	}
}

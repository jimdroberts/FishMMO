using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a single attribute modification applied by a buff, including the value and target attribute template.
	/// </summary>
	[Serializable]
	public class BuffAttributeTemplate
	{
		/// <summary>
		/// The value to add (or subtract) from the target attribute when the buff is applied.
		/// </summary>
		public int Value;

		/// <summary>
		/// The character attribute template that this buff will modify.
		/// </summary>
		[Tooltip("Character Attribute the buff will apply its values to.")]
		public CharacterAttributeTemplate Template;
	}
}
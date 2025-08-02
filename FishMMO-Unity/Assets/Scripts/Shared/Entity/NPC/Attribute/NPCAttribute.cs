using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an attribute for an NPC, with options for scaling, randomization, and value range.
	/// Used to define how an NPC's attribute is calculated and applied from a template.
	/// </summary>
	[Serializable]
	public class NPCAttribute
	{
		/// <summary>
		/// If true, the attribute value is scaled from the standard attribute value; otherwise, it is simply added.
		/// </summary>
		[Tooltip("Determines if the attribute value is scaled from the standard attribute value or simply added on.")]
		public bool IsScalar;

		/// <summary>
		/// If true, the attribute value will be randomly chosen within the min/max range; otherwise, the maximum value is used.
		/// </summary>
		[Tooltip("If true the value will be within the min/max range. Otherwise the maximum value is used.")]
		public bool IsRandom;

		/// <summary>
		/// The minimum value for the attribute (used if IsRandom is true).
		/// </summary>
		public int Min;

		/// <summary>
		/// The maximum value for the attribute.
		/// </summary>
		public int Max;

		/// <summary>
		/// The template that defines the type and base value of this attribute.
		/// </summary>
		public CharacterAttributeTemplate Template;

		/// <summary>
		/// Constructs a new NPCAttribute with the specified scaling, randomization, range, and template.
		/// </summary>
		/// <param name="isScalar">Whether the value is scaled from the template.</param>
		/// <param name="isRandom">Whether the value is randomized between min and max.</param>
		/// <param name="min">Minimum value for the attribute.</param>
		/// <param name="max">Maximum value for the attribute.</param>
		/// <param name="template">The attribute template to use.</param>
		public NPCAttribute(bool isScalar, bool isRandom, int min, int max, CharacterAttributeTemplate template)
		{
			this.IsScalar = isScalar;
			this.IsRandom = isRandom;
			this.Min = min;
			this.Max = max;
			this.Template = template;
		}
	}
}
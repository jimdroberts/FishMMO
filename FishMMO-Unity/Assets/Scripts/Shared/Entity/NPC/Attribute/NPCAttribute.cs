using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[Serializable]
	public class NPCAttribute
	{
		[Tooltip("Determines if the attribute value is scaled from the standard attribute value or simply added on.")]
		public bool IsScalar;
		[Tooltip("If true the value will be within the min/max range. Otherwise the maximum value is used.")]
		public bool IsRandom;
		public int Min;
		public int Max;
		public CharacterAttributeTemplate Template;

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
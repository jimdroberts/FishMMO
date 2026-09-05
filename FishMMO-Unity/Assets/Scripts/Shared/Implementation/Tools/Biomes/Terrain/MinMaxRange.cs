using System;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>A closed float interval, drawn in the inspector as a min-max slider.</summary>
	[Serializable]
	public struct MinMaxRange
	{
		public float min;
		public float max;

		public MinMaxRange(float min, float max)
		{
			this.min = min;
			this.max = max;
		}

		public bool Contains(float value) => value >= min && value <= max;
	}

	/// <summary>Limits for the min-max slider drawn for a <see cref="MinMaxRange"/> field.</summary>
	public class MinMaxRangeAttribute : PropertyAttribute
	{
		public float minLimit;
		public float maxLimit;

		public MinMaxRangeAttribute(float minLimit, float maxLimit)
		{
			this.minLimit = minLimit;
			this.maxLimit = maxLimit;
		}
	}
}

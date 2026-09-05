using System;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>A texture layer that appears on steep faces, weighted by slope angle and optionally by height.</summary>
	[Serializable]
	public class CliffTextureLayer : TerrainTextureLayer
	{
		[Header("Cliff Configuration")]
		[Tooltip("Minimum slope angle in degrees for this cliff texture to appear.")]
		[Range(0f, 90f)] public float minCliffAngle = 45f;

		[Tooltip("Maximum slope angle in degrees for this cliff texture.")]
		[Range(0f, 90f)] public float maxCliffAngle = 90f;

		[Tooltip("How sharply the cliff texture blends based on slope angle.")]
		[Range(1f, 20f)] public float cliffSlopeFalloff = 10f;

		[Tooltip("Additional height constraint for cliff placement.")]
		public bool useCliffHeightConstraint = false;

		[MinMaxRange(0f, 1f)]
		public MinMaxRange cliffHeightRange = new MinMaxRange(0.2f, 1f);

		public CliffTextureLayer()
		{
			useSlopeConstraint = true;
			slopeRange = new MinMaxRange(45f, 90f);
			slopeFalloff = 10f;
			minCliffAngle = 45f;
			maxCliffAngle = 90f;
			cliffSlopeFalloff = 10f;
		}

		/// <summary>Weight of this cliff texture at a slope angle and normalised height, 0-1.</summary>
		public float GetCliffWeight(float slopeAngle, float normalizedHeight)
		{
			float slopeWeight = 0f;
			if (slopeAngle >= minCliffAngle)
			{
				if (slopeAngle <= maxCliffAngle)
				{
					// Guard against a zero range when min == max.
					float cliffRange = maxCliffAngle - minCliffAngle;
					float slopeProgress = cliffRange > Mathf.Epsilon
						? (slopeAngle - minCliffAngle) / cliffRange
						: 1f;
					slopeWeight = Mathf.SmoothStep(0f, 1f, slopeProgress);
				}
				else
				{
					// Above the maximum angle the texture fades out over ten degrees.
					float fadeProgress = Mathf.Clamp01((slopeAngle - maxCliffAngle) / 10f);
					slopeWeight = 1f - fadeProgress;
				}
			}

			if (useCliffHeightConstraint)
			{
				float heightWeight = 1f;
				const float fadeRange = 0.1f;
				if (normalizedHeight < cliffHeightRange.min)
				{
					heightWeight = Mathf.SmoothStep(0f, 1f, (normalizedHeight - (cliffHeightRange.min - fadeRange)) / fadeRange);
				}
				else if (normalizedHeight > cliffHeightRange.max)
				{
					heightWeight = 1f - Mathf.SmoothStep(0f, 1f, (normalizedHeight - cliffHeightRange.max) / fadeRange);
				}
				slopeWeight *= heightWeight;
			}

			return Mathf.Clamp01(slopeWeight);
		}
	}
}

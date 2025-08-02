using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class for configuring fog settings in a region, including mode, color, density, and transition properties.
	/// </summary>
	[Serializable]
	public class FogSettings
	{
		/// <summary>
		/// Whether fog is enabled for the region.
		/// </summary>
		public bool Enabled = false;

		/// <summary>
		/// The rate at which fog settings change (e.g., for smooth transitions between regions).
		/// </summary>
		public float ChangeRate = 0.0f;

		/// <summary>
		/// The mode of the fog (Linear, Exponential, ExponentialSquared).
		/// </summary>
		public FogMode Mode = FogMode.Exponential;

		/// <summary>
		/// The color of the fog.
		/// </summary>
		public Color Color = Color.gray;

		/// <summary>
		/// The density of the fog (used in exponential modes).
		/// </summary>
		public float Density = 0.0f;

		/// <summary>
		/// The starting distance from the camera where fog begins (used in linear mode).
		/// </summary>
		public float StartDistance = 0.0f;

		/// <summary>
		/// The ending distance from the camera where fog reaches full effect (used in linear mode).
		/// </summary>
		public float EndDistance = 0.0f;
	}
}
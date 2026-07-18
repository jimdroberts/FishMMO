using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Common mathematical constants and utilities.
	/// Provides both double-precision and single-precision equivalents
	/// for frequently used values not available in .NET Standard 2.1.
	/// </summary>
	public static class MathHelper
	{
		/// <summary>Half of pi (π / 2), approximately 1.5707963267948966.</summary>
		public const double HalfPI = Math.PI * 0.5;

		/// <summary>Tau (2π), approximately 6.283185307179586. One full rotation in radians.</summary>
		public const double Tau = Math.PI * 2.0;

		/// <summary>Half of pi as a single-precision float.</summary>
		public const float HalfPIF = (float)(Math.PI * 0.5);

		/// <summary>Tau (2π) as a single-precision float.</summary>
		public const float TauF = (float)(Math.PI * 2.0);
	}
}
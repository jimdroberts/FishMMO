namespace FishMMO.Shared
{
	/// <summary>Plot measures that core housing data (structure templates, the scene manifest) shares with the Housing module.</summary>
	public static class PlotRules
	{
		/// <summary>
		/// The smallest a plot's width, depth or height may be, in metres. A plot's measures are floored at it; a structure's
		/// footprint and height likewise. In core so a structure template needs no plot component to size itself (2026-11-21:
		/// PlotFoundation moved into the Housing module).
		/// </summary>
		public const float MinimumExtent = 1f;
	}
}

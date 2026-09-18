namespace FishMMO.Client
{
	/// <summary>What the volumetric clouds cost on one quality tier, as the renderer feature needs it.</summary>
	/// <remarks>
	/// The same system runs on every tier — a browser and a desktop draw the same sky — and only
	/// these numbers change: how much of the screen is marched, how many steps a ray takes, how much
	/// of the fine detail is used, and whether the result is steadied against the last frame.
	/// </remarks>
	public struct CloudTierSettings
	{
		public float Resolution;
		public int Steps;
		public float Detail;
		public bool Temporal;
		public float TemporalBlend;
	}
}

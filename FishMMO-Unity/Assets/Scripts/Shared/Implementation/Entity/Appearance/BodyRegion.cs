namespace FishMMO.Shared
{
	/// <summary>
	/// Body regions that can be individually hidden when equipment is worn.
	/// Maps to the pre-split SkinnedMeshRenderers on the character body model.
	/// </summary>
	public enum BodyRegion : byte
	{
		/// <summary>
		/// Head region (e.g., hidden by helmets).
		/// </summary>
		Head = 0,

		/// <summary>
		/// Torso region (e.g., hidden by chest armor).
		/// </summary>
		Torso,

		/// <summary>
		/// Arms region (e.g., hidden by long-sleeved armor).
		/// </summary>
		Arms,

		/// <summary>
		/// Hands region (e.g., hidden by gloves).
		/// </summary>
		Hands,

		/// <summary>
		/// Legs region (e.g., hidden by pants).
		/// </summary>
		Legs,

		/// <summary>
		/// Feet region (e.g., hidden by boots).
		/// </summary>
		Feet,
	}
}

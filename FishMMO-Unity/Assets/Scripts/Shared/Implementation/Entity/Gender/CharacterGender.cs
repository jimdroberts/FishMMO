namespace FishMMO.Shared
{
	/// <summary>
	/// Gender used for generated scene-object names and optional race model selection.
	/// </summary>
	public enum CharacterGender : byte
	{
		/// <summary>
		/// No gender has been selected or gender is not relevant for this object.
		/// </summary>
		Unspecified = 0,

		/// <summary>
		/// Masculine name/model set.
		/// </summary>
		Male = 1,

		/// <summary>
		/// Feminine name/model set.
		/// </summary>
		Female = 2,
	}
}
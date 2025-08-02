namespace FishMMO.Shared
{
	/// <summary>
	/// Specifies the type of comparison to perform when evaluating an ability's hit count.
	/// </summary>
	public enum HitCountComparisonType
	{
		/// <summary>
		/// The hit count must be greater than the comparison value.
		/// </summary>
		GreaterThan,

		/// <summary>
		/// The hit count must be greater than or equal to the comparison value.
		/// </summary>
		GreaterThanOrEqualTo,

		/// <summary>
		/// The hit count must be less than the comparison value.
		/// </summary>
		LessThan,

		/// <summary>
		/// The hit count must be less than or equal to the comparison value.
		/// </summary>
		LessThanOrEqualTo,

		/// <summary>
		/// The hit count must be equal to the comparison value.
		/// </summary>
		EqualTo,

		/// <summary>
		/// The hit count must not be equal to the comparison value.
		/// </summary>
		NotEqualTo
	}
}
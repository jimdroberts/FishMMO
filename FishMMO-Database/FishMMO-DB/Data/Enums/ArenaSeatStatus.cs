namespace FishMMO.Database.Data.Enums
{
	/// <summary>Whether an arena seat is occupied or open to backfill.</summary>
	public enum ArenaSeatStatus : int
	{
		/// <summary>Held by its character.</summary>
		Seated = 0,
		/// <summary>Its character left or never arrived; a waiter may take it while the match's backfill window is open.</summary>
		Vacated = 1,
	}
}

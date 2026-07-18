namespace FishMMO.Shared
{
	/// <summary>
	/// Marker interface indicating that instances of the implementing type
	/// should be compared by reference equality rather than value equality.
	/// Used by collection types and equality comparers to select the
	/// appropriate comparison strategy at runtime.
	/// </summary>
	public interface IReference
	{
	}
}
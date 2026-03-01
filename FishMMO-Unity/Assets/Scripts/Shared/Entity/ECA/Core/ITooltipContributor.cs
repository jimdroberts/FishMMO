namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for ECA conditions or actions that contribute text to ability tooltips.
	/// Implement this on conditions that should display requirement or cost information in the UI.
	/// </summary>
	public interface ITooltipContributor
	{
		/// <summary>
		/// Returns a formatted tooltip string contribution, or null if this condition has nothing to display.
		/// </summary>
		string GetTooltipContribution();
	}
}
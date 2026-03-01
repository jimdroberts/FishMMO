using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for target selectors that support filtering targets using a list of conditions.
	/// Implement this interface to allow a target selector to require that all specified <see cref="BaseCondition"/>s are met for a target to be considered valid.
	/// </summary>
	public interface IConditionalTargetSelector
	{
		/// <summary>
		/// Gets or sets the list of conditions that must be met for a target to be valid.
		/// All conditions in this list must evaluate to true for the target to be selected.
		/// </summary>
		List<BaseCondition> Conditions { get; set; }
	}
}
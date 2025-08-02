using UnityEngine;
using System.Collections.Generic;
using Cysharp.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for conditions in FishMMO. Provides tooltip formatting and evaluation contract.
	/// </summary>
	public abstract class BaseCondition : CachedScriptableObject<BaseCondition>, ICachedObject, ICondition, ITooltip
	{
		/// <summary>
		/// The icon representing this condition.
		/// </summary>
		[SerializeField]
		private Sprite icon;

		/// <summary>
		/// The description of the condition, used in tooltips and UI.
		/// </summary>
		public string Description;

		/// <summary>
		/// The name of the condition (from the ScriptableObject).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// The icon property for external access.
		/// </summary>
		public Sprite Icon { get { return this.icon; } }

		/// <summary>
		/// Returns the formatted tooltip for this condition.
		/// </summary>
		/// <returns>Tooltip string.</returns>
		public virtual string Tooltip()
		{
			return PrimaryTooltip(null);
		}

		/// <summary>
		/// Returns the formatted tooltip, optionally combining with other tooltips.
		/// </summary>
		/// <param name="combineList">Optional list of tooltips to combine.</param>
		/// <returns>Tooltip string.</returns>
		public virtual string Tooltip(List<ITooltip> combineList)
		{
			return PrimaryTooltip(combineList);
		}

		/// <summary>
		/// Returns the formatted description for this condition.
		/// </summary>
		/// <returns>Formatted description string.</returns>
		public virtual string GetFormattedDescription()
		{
			return Description;
		}

		/// <summary>
		/// Builds the primary tooltip string, including name and description, with rich text formatting.
		/// </summary>
		/// <param name="combineList">Optional list of tooltips to combine (currently unused).</param>
		/// <returns>Formatted tooltip string.</returns>
		private string PrimaryTooltip(List<ITooltip> combineList)
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append(RichText.Format(Name, true, "f5ad6e", "140%"));

				if (!string.IsNullOrWhiteSpace(Description))
				{
					sb.AppendLine();
					sb.Append(RichText.Format(GetFormattedDescription(), true, "a66ef5FF"));
				}
				return sb.ToString();
			}
		}

		/// <summary>
		/// Evaluates the condition. Must be implemented by derived classes.
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Optional event data for the condition.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public abstract bool Evaluate(ICharacter initiator, EventData eventData = null);
	}
}
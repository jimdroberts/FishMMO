using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A saddle (issue #109): worn in the <see cref="ItemSlot.MountSaddle"/> socket, it names the mount the Mount ability puts
	/// its wearer on. Bought from a Stablemaster. Nothing is drawn on the body for it.
	/// </summary>
	/// <remarks>Taking the saddle off (or swapping it) gets its wearer off the mount it named (<see cref="MountController"/>).</remarks>
	[CreateAssetMenu(fileName = "New Saddle", menuName = "FishMMO/Item/Mount Saddle", order = 0)]
	public class SaddleTemplate : EquippableItemTemplate
	{
		[Tooltip("The mount the Mount ability rides with this saddle on.")]
		public MountBuffTemplate Mount;

		/// <inheritdoc />
		public override void BuildTooltip(TooltipContent content, bool describingInstance)
		{
			base.BuildTooltip(content, describingInstance);
			if (Mount == null)
			{
				return;
			}
			content.AddStat("Mount", Mount.name, TooltipPriority.Stats, tone: TooltipTone.Good);
			foreach ((string label, string value) in MountRules.Describe(Mount))
			{
				content.AddStat(label, value, TooltipPriority.Stats + 1, tone: TooltipTone.Good);
			}
		}

#if UNITY_EDITOR
		/// <summary>A saddle goes in the saddle socket, whatever the asset says.</summary>
		private void OnValidate()
		{
			if (Slot != ItemSlot.MountSaddle)
			{
				Slot = ItemSlot.MountSaddle;
				UnityEditor.EditorUtility.SetDirty(this);
			}
		}
#endif
	}
}

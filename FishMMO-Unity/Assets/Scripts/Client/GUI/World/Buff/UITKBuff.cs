using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit container that renders the local player's active (positive) buffs.
	/// </summary>
	public class UITKBuff : UITKBuffContainer
	{
		/// <inheritdoc />
		protected override bool IsDebuff => false;

		/// <inheritdoc />
		protected override string TooltipHint => "\r\n\r\nLeft Mouse Button to remove.";

		/// <inheritdoc />
		protected override void SubscribeAddRemove()
		{
			IBuffController.OnAddBuff += AddBuffGroup;
			IBuffController.OnRemoveBuff += RemoveBuffGroup;
		}

		/// <inheritdoc />
		protected override void UnsubscribeAddRemove()
		{
			IBuffController.OnAddBuff -= AddBuffGroup;
			IBuffController.OnRemoveBuff -= RemoveBuffGroup;
		}
	}
}

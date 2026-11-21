using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit container that renders the local player's active (positive) buffs.
	/// </summary>
	/// <remarks>
	/// Clicking one of these asks the server to remove it — see
	/// <see cref="BaseBuffTemplate.CanBeDismissedByPlayer"/>, which is what decides that every buff
	/// here is dismissable and no debuff on the sibling strip is.
	/// </remarks>
	public class UITKBuff : UITKBuffContainer
	{
		/// <inheritdoc />
		protected override bool IsDebuff => false;

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

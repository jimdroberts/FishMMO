using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit container that renders the local player's active debuffs (negative effects).
	/// </summary>
	public class UITKDebuff : UITKBuffContainer
	{
		/// <inheritdoc />
		protected override bool IsDebuff => true;

		/// <inheritdoc />
		protected override void SubscribeAddRemove()
		{
			IBuffController.OnAddDebuff += AddBuffGroup;
			IBuffController.OnRemoveDebuff += RemoveBuffGroup;
		}

		/// <inheritdoc />
		protected override void UnsubscribeAddRemove()
		{
			IBuffController.OnAddDebuff -= AddBuffGroup;
			IBuffController.OnRemoveDebuff -= RemoveBuffGroup;
		}
	}
}

using System;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UISelector : UIControl
	{
		private Action<int> onAccept;
		private int selectedIndex = -1;
		private List<ITooltip> tooltips;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void Open(List<ITooltip> tooltips, Action<int> onAccept)
		{
			if (Visible || tooltips == null || tooltips.Count < 1)
				return;



			this.onAccept = onAccept;
			Visible = true;
		}

		public void OnClick_Accept()
		{
			if (selectedIndex > -1)
			{
				onAccept?.Invoke(selectedIndex);
			}
			OnClick_Cancel();
		}

		public void OnClick_Cancel()
		{
			selectedIndex = -1;
			onAccept = null;
			Visible = false;
		}
	}
}
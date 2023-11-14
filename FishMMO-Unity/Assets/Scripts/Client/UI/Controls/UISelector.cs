using System;

namespace FishMMO.Client
{
	public class UISelector : UIControl
	{
		private Action<int> OnAccept;
		private int SelectedIndex = -1;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void Open(Action<int> onAccept)
		{
			if (Visible)
				return;

			OnAccept = onAccept;
			Visible = true;
		}

		public void OnClick_Accept()
		{
			if (SelectedIndex > -1)
			{
				OnAccept?.Invoke(SelectedIndex);
			}
			OnClick_Cancel();
		}

		public void OnClick_Cancel()
		{
			SelectedIndex = -1;
			OnAccept = null;
			Visible = false;
		}
	}
}
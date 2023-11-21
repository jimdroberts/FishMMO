using System;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UISelector : UIControl
	{
		private Action<int> onAccept;
		private int selectedIndex = -1;
		private List<ICachedObject> cachedObjects;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void Open(List<ICachedObject> cachedObjects, Action<int> onAccept)
		{
			if (Visible || cachedObjects == null || cachedObjects.Count < 1)
				return;

			this.cachedObjects = cachedObjects;

			this.onAccept = onAccept;
			Visible = true;
		}

		public void OnClick_Accept()
		{
			if (selectedIndex > -1 &&
				cachedObjects != null &&
				cachedObjects.Count < selectedIndex)
			{
				onAccept?.Invoke(cachedObjects[selectedIndex].ID);
			}
			OnClick_Cancel();
		}

		public void OnClick_Cancel()
		{
			cachedObjects = null;
			selectedIndex = -1;
			onAccept = null;
			Visible = false;
		}
	}
}
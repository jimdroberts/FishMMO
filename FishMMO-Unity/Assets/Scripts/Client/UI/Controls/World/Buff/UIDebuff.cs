using UnityEngine;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIDebuff : UICharacterControl
	{
		public RectTransform DebuffParent;
		public UIBuffGroup DebuffButtonPrefab;

		public Dictionary<int, UIBuffGroup> Debuffs = new Dictionary<int, UIBuffGroup>();

		public override void OnStarting()
		{
			IBuffController.OnSubtractTime += BuffController_OnSubtractTime;
			IBuffController.OnAddDebuff += BuffController_OnAddDebuff;
			IBuffController.OnRemoveDebuff += BuffController_OnRemoveDebuff;

			IPlayerCharacter.OnStopLocalClient += (c) => ClearAllDebuffs();
		}

		public override void OnDestroying()
		{
			IBuffController.OnSubtractTime -= BuffController_OnSubtractTime;
			IBuffController.OnAddDebuff -= BuffController_OnAddDebuff;
			IBuffController.OnRemoveDebuff -= BuffController_OnRemoveDebuff;

			IPlayerCharacter.OnStopLocalClient -= (c) => ClearAllDebuffs();

			ClearAllDebuffs();
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
		}

		public override void OnPreUnsetCharacter()
		{
		}

		public override void OnQuitToLogin()
		{
			ClearAllDebuffs();
		}

		private void BuffController_OnSubtractTime(Buff buff)
		{
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (!buff.Template.IsDebuff)
			{
				return;
			}
			if (Debuffs.TryGetValue(buff.Template.ID, out UIBuffGroup buffGroup))
			{
				buffGroup.DurationSlider.value = buff.RemainingTime / buff.Template.Duration;
			}
		}

		private void BuffController_OnAddDebuff(Buff buff)
		{
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (!buff.Template.IsDebuff)
			{
				return;
			}
			if (Debuffs.ContainsKey(buff.Template.ID))
			{
				return;
			}
			UIBuffGroup buffGroup = Instantiate(DebuffButtonPrefab, DebuffParent);
			if (buffGroup.ButtonText != null)
			{
				buffGroup.ButtonText.text = buff.Template.Name;
			}
			if (buffGroup.TooltipButton != null)
			{
				buffGroup.TooltipButton.Initialize(buff.Template.ID, null, null, buff.Template);
			}
			if (buffGroup.Icon != null)
			{
				buffGroup.Icon.sprite = buff.Template.Icon;
			}
			if (Debuffs == null)
			{
				Debuffs = new Dictionary<int, UIBuffGroup>();
			}
			Debuffs.Add(buff.Template.ID, buffGroup);
			buffGroup.gameObject.SetActive(true);
		}

		private void BuffController_OnRemoveDebuff(Buff buff)
		{
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (!buff.Template.IsDebuff)
			{
				return;
			}
			if (Debuffs.TryGetValue(buff.Template.ID, out UIBuffGroup group))
			{
				Destroy(group.gameObject);
				Debuffs.Remove(buff.Template.ID);
			}
		}

		public void ClearAllDebuffs()
		{
			if (Debuffs == null || Debuffs.Count == 0)
			{
				return;
			}
			foreach (UIBuffGroup group in Debuffs.Values)
			{
				Destroy(group.gameObject);
			}
			Debuffs.Clear();
		}
	}
}
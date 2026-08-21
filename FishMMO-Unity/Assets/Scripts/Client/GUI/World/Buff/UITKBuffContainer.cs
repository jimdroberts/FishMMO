using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract UI Toolkit container that renders a horizontal strip of buff or debuff icons,
	/// each with a depleting duration fill. Buff groups are built dynamically as VisualElements,
	/// so no prefab reference is required.
	/// </summary>
	public abstract class UITKBuffContainer : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>Name of the container element that holds the buff/debuff icons.</summary>
		private const string LIST_NAME = "buff-list";

		/// <summary>USS class applied to each generated buff group root.</summary>
		private const string GROUP_CLASS = "buff-group";

		/// <summary>USS class applied to each buff group's icon element.</summary>
		private const string ICON_CLASS = "buff-group__icon";

		/// <summary>USS class applied to each buff group's depleting duration fill.</summary>
		private const string FILL_CLASS = "buff-group__fill";

		/// <summary>USS class applied to each buff group's stack/name label.</summary>
		private const string LABEL_CLASS = "buff-group__label";

		/// <summary>Name of the shared tooltip overlay registered with the UIManager.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		/// <summary>
		/// Visual elements backing a single buff group entry.
		/// </summary>
		private struct GroupView
		{
			/// <summary>Root container for the buff group.</summary>
			public VisualElement Root;
			/// <summary>Depleting duration fill element (height driven from C#).</summary>
			public VisualElement Fill;
			/// <summary>Buff template, cached for tooltip rendering.</summary>
			public BaseBuffTemplate Template;
		}

		/// <summary>True for the debuff container, false for the buff container.</summary>
		protected abstract bool IsDebuff { get; }

		/// <summary>Extra tooltip hint appended for interactive (removable) buffs, if any.</summary>
		protected virtual string TooltipHint => null;

		/// <summary>Subscribes the concrete container to its specific add/remove events.</summary>
		protected abstract void SubscribeAddRemove();

		/// <summary>Unsubscribes the concrete container from its specific add/remove events.</summary>
		protected abstract void UnsubscribeAddRemove();

		/// <summary>Rendered buff groups keyed by template ID.</summary>
		private readonly Dictionary<int, GroupView> groups = new Dictionary<int, GroupView>();
		/// <summary>The container element that holds the buff/debuff icons.</summary>
		private VisualElement list;

		/// <summary>
		/// Queries the list container and subscribes to buff lifecycle events.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root != null)
			{
				list = root.Q(LIST_NAME);
			}

			IBuffController.OnBuffTick += BuffController_OnBuffTick;
			SubscribeAddRemove();

			IPlayerCharacter.OnStopLocalClient += PlayerCharacter_OnStopLocalClient;
		}

		/// <summary>
		/// Unsubscribes from buff lifecycle events and clears all rendered groups.
		/// </summary>
		public override void OnDestroying()
		{
			IBuffController.OnBuffTick -= BuffController_OnBuffTick;
			UnsubscribeAddRemove();

			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;

			ClearAll();
		}

		/// <summary>
		/// Clears all rendered groups when quitting to the login screen.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClearAll();
		}

		/// <summary>
		/// Clears all rendered groups when the local client stops.
		/// </summary>
		/// <param name="character">The local player character.</param>
		private void PlayerCharacter_OnStopLocalClient(IPlayerCharacter character)
		{
			ClearAll();
		}

		/// <summary>
		/// Updates the depleting duration fill for the supplied buff each tick.
		/// </summary>
		/// <param name="buff">The buff that ticked.</param>
		/// <param name="currentTick">Current network tick, used to compute remaining duration.</param>
		private void BuffController_OnBuffTick(Buff buff, uint currentTick)
		{
			if (buff == null || buff.Template == null || buff.Template.IsDebuff != IsDebuff)
			{
				return;
			}

			if (!groups.TryGetValue(buff.Template.ID, out GroupView view) || view.Fill == null)
			{
				return;
			}

			float fraction = buff.Template.Duration > 0.0f
				? buff.RemainingSeconds(currentTick) / buff.Template.Duration
				: 1.0f;

			view.Fill.style.height = Length.Percent(Mathf.Clamp01(fraction) * 100.0f);
		}

		/// <summary>
		/// Builds and registers a buff group for the supplied buff if not already present.
		/// </summary>
		/// <param name="buff">The buff to add.</param>
		protected void AddBuffGroup(Buff buff)
		{
			if (buff == null || buff.Template == null || buff.Template.IsDebuff != IsDebuff)
			{
				return;
			}

			if (list == null || groups.ContainsKey(buff.Template.ID))
			{
				return;
			}

			GroupView view = CreateGroup(buff.Template);
			list.Add(view.Root);
			groups.Add(buff.Template.ID, view);
		}

		/// <summary>
		/// Removes and disposes the buff group for the supplied buff.
		/// </summary>
		/// <param name="buff">The buff to remove.</param>
		protected void RemoveBuffGroup(Buff buff)
		{
			if (buff == null || buff.Template == null || buff.Template.IsDebuff != IsDebuff)
			{
				return;
			}

			if (groups.TryGetValue(buff.Template.ID, out GroupView view))
			{
				view.Root?.RemoveFromHierarchy();
				groups.Remove(buff.Template.ID);
			}
		}

		/// <summary>
		/// Removes all rendered buff groups from the UI.
		/// </summary>
		protected void ClearAll()
		{
			if (groups.Count == 0)
			{
				return;
			}

			foreach (GroupView view in groups.Values)
			{
				view.Root?.RemoveFromHierarchy();
			}
			groups.Clear();
		}

		/// <summary>
		/// Creates the visual elements for a single buff group.
		/// </summary>
		/// <param name="template">The buff template to render.</param>
		/// <returns>The populated <see cref="GroupView"/>.</returns>
		private GroupView CreateGroup(BaseBuffTemplate template)
		{
			VisualElement groupRoot = new VisualElement();
			groupRoot.AddToClassList(GROUP_CLASS);

			VisualElement fill = new VisualElement();
			fill.AddToClassList(FILL_CLASS);
			groupRoot.Add(fill);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(ICON_CLASS);
			if (template.Icon != null)
			{
				icon.style.backgroundImage = new StyleBackground(template.Icon);
			}
			groupRoot.Add(icon);

			Label label = new Label(template.Name);
			label.AddToClassList(LABEL_CLASS);
			groupRoot.Add(label);

			GroupView view;
			view.Root = groupRoot;
			view.Fill = fill;
			view.Template = template;

			groupRoot.RegisterCallback<PointerEnterEvent>(evt => OnGroupPointerEnter(template));
			groupRoot.RegisterCallback<PointerLeaveEvent>(evt => OnGroupPointerLeave());

			return view;
		}

		/// <summary>
		/// Shows the buff tooltip when the pointer enters a buff group.
		/// </summary>
		/// <param name="template">The buff template to describe.</param>
		private void OnGroupPointerEnter(BaseBuffTemplate template)
		{
			if (template == null)
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				string text = string.IsNullOrEmpty(TooltipHint)
					? template.Tooltip()
					: template.Tooltip() + TooltipHint;
				tooltip.Open(text);
			}
		}

		/// <summary>
		/// Hides the buff tooltip when the pointer leaves a buff group.
		/// </summary>
		private void OnGroupPointerLeave()
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Hide();
			}
		}
	}
}

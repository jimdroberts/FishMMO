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
	/// <remarks>
	/// <para>
	/// <b>Owner scoping.</b> The five buff lifecycle events on <see cref="IBuffController"/> are
	/// STATIC — one invocation list for the whole process — and used to carry only the
	/// <see cref="Buff"/>, which has no owner. Every buff applied to every NPC, pet, summon and
	/// other player in view was therefore delivered here and rendered onto the LOCAL player's own
	/// strips. It was easy to miss because groups are keyed by template ID, so N mobs carrying the
	/// same debuff collapsed into one icon that looked plausible; a debuff landing on a mob really
	/// did appear on the player, and expired off the player when it expired on the mob.
	/// </para>
	/// <para>
	/// The events now carry the owning controller and every handler here compares it against this
	/// panel's character before doing anything. A static event cannot be scoped at subscription
	/// time, so the filter has to live in the callback.
	/// </para>
	/// <para>
	/// <b>Model / view split.</b> <see cref="entries"/> is plain data owned by the character;
	/// <see cref="groups"/> holds the elements of ONE visual tree. <c>UIDocument</c> re-clones the
	/// UXML on every enable, so a dictionary of elements cached across a hide/show points into a
	/// discarded tree and the strip comes back permanently empty.
	/// </para>
	/// </remarks>
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
		/// What is being displayed for one buff. Plain data — survives a tree rebuild.
		/// </summary>
		private struct BuffEntry
		{
			/// <summary>The buff template being rendered.</summary>
			public BaseBuffTemplate Template;
			/// <summary>Remaining duration fraction (0-1).</summary>
			public float Fraction;
			/// <summary>Stack count above the base application.</summary>
			public int Stacks;
		}

		/// <summary>
		/// Visual elements backing a single buff group entry.
		/// </summary>
		private struct GroupView
		{
			/// <summary>Root container for the buff group.</summary>
			public VisualElement Root;
			/// <summary>Depleting duration fill element (height driven from C#).</summary>
			public VisualElement Fill;
			/// <summary>Stack-count label.</summary>
			public Label Label;
			/// <summary>The fraction currently written into <see cref="Fill"/>.</summary>
			public float AppliedFraction;
			/// <summary>The stack count currently written into <see cref="Label"/>.</summary>
			public int AppliedStacks;
		}

		/// <summary>True for the debuff container, false for the buff container.</summary>
		protected abstract bool IsDebuff { get; }

		/// <summary>Extra tooltip hint appended for interactive (removable) buffs, if any.</summary>
		protected virtual string TooltipHint => null;

		/// <summary>Subscribes the concrete container to its specific add/remove events.</summary>
		protected abstract void SubscribeAddRemove();

		/// <summary>Unsubscribes the concrete container from its specific add/remove events.</summary>
		protected abstract void UnsubscribeAddRemove();

		/// <summary>The buffs this strip is showing, keyed by template ID. Model.</summary>
		private readonly Dictionary<int, BuffEntry> entries = new Dictionary<int, BuffEntry>();
		/// <summary>Rendered buff groups keyed by template ID. View — belongs to one tree.</summary>
		private readonly Dictionary<int, GroupView> groups = new Dictionary<int, GroupView>();
		/// <summary>The container element that holds the buff/debuff icons.</summary>
		private VisualElement list;

		/// <summary>
		/// Queries the list container and subscribes to buff lifecycle events.
		/// </summary>
		/// <remarks>
		/// Re-runs on every tree rebuild, so the element dictionary is dropped first (those
		/// elements belong to the discarded tree) and every static subscription is removed before
		/// it is added. A bare <c>+=</c> from a hook that can re-run leaks handlers without bound.
		/// </remarks>
		public override void OnStarting()
		{
			groups.Clear();
			list = null;

			VisualElement root = Root;
			if (root != null)
			{
				list = root.Q(LIST_NAME);
			}

			IBuffController.OnBuffTick -= BuffController_OnBuffTick;
			IBuffController.OnBuffTick += BuffController_OnBuffTick;
			UnsubscribeAddRemove();
			SubscribeAddRemove();

			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;
			IPlayerCharacter.OnStopLocalClient += PlayerCharacter_OnStopLocalClient;
		}

		/// <summary>
		/// Rebuilds the strip from the model after the visual tree was replaced.
		/// </summary>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			RebuildView();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			RebuildView();
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

			base.OnDestroying();
		}

		/// <summary>
		/// Seeds the strip from the character's current buffs when a character is applied.
		/// </summary>
		/// <remarks>
		/// Without this the strip only ever learned about buffs applied AFTER it was bound, so a
		/// buff that survived a relog or a scene change (they are persisted, and restored from the
		/// spawn payload) was active on the character and invisible on the bar.
		/// </remarks>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			entries.Clear();

			if (Character != null &&
				Character.TryGet(out IBuffController buffController) &&
				buffController.Buffs != null)
			{
				uint currentTick = buffController.GetCurrentDomainTick();
				foreach (KeyValuePair<int, Buff> pair in buffController.Buffs)
				{
					StoreEntry(pair.Value, currentTick);
				}
			}

			RebuildView();
		}

		/// <summary>
		/// Clears the strip when the character is removed, so one character's buffs cannot appear
		/// on the next one's bar.
		/// </summary>
		public override void OnPostUnsetCharacter()
		{
			ClearAll();
		}

		/// <summary>
		/// Clears all rendered groups when quitting to the login screen.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClearAll();

			base.OnQuitToLogin();
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
		/// Returns true when the supplied controller belongs to this panel's character.
		/// </summary>
		/// <param name="buffController">The controller that raised the event.</param>
		private bool IsOwnController(IBuffController buffController)
		{
			return buffController != null &&
				Character != null &&
				ReferenceEquals(buffController.Character, Character);
		}

		/// <summary>
		/// Returns true when the buff belongs on this strip (right owner, right polarity).
		/// </summary>
		/// <param name="buffController">The controller that raised the event.</param>
		/// <param name="buff">The buff in question.</param>
		private bool Accepts(IBuffController buffController, Buff buff)
		{
			return buff != null &&
				buff.Template != null &&
				buff.Template.IsDebuff == IsDebuff &&
				IsOwnController(buffController);
		}

		/// <summary>
		/// Updates the depleting duration fill for the supplied buff each tick.
		/// </summary>
		/// <param name="buffController">The controller the buff belongs to.</param>
		/// <param name="buff">The buff that ticked.</param>
		/// <param name="currentTick">Current network tick, used to compute remaining duration.</param>
		private void BuffController_OnBuffTick(IBuffController buffController, Buff buff, uint currentTick)
		{
			if (!Accepts(buffController, buff))
			{
				return;
			}

			StoreEntry(buff, currentTick);
			ApplyEntry(buff.Template.ID);
		}

		/// <summary>
		/// Records a buff's current display state in the model.
		/// </summary>
		/// <param name="buff">The buff to record.</param>
		/// <param name="currentTick">The tick to compute remaining duration against.</param>
		private void StoreEntry(Buff buff, uint currentTick)
		{
			if (buff?.Template == null || buff.Template.IsDebuff != IsDebuff)
			{
				return;
			}

			float fraction = buff.Template.Duration > 0.0f
				? buff.RemainingSeconds(currentTick) / buff.Template.Duration
				: 1.0f;

			entries[buff.Template.ID] = new BuffEntry()
			{
				Template = buff.Template,
				Fraction = Mathf.Clamp01(fraction),
				Stacks = buff.Stacks,
			};
		}

		/// <summary>
		/// Builds and registers a buff group for the supplied buff if not already present.
		/// </summary>
		/// <param name="buffController">The controller the buff belongs to.</param>
		/// <param name="buff">The buff to add.</param>
		protected void AddBuffGroup(IBuffController buffController, Buff buff)
		{
			if (!Accepts(buffController, buff))
			{
				return;
			}

			StoreEntry(buff, buffController.GetCurrentDomainTick());
			ApplyEntry(buff.Template.ID);
		}

		/// <summary>
		/// Removes and disposes the buff group for the supplied buff.
		/// </summary>
		/// <param name="buffController">The controller the buff belongs to.</param>
		/// <param name="buff">The buff to remove.</param>
		protected void RemoveBuffGroup(IBuffController buffController, Buff buff)
		{
			if (!Accepts(buffController, buff))
			{
				return;
			}

			int templateID = buff.Template.ID;
			entries.Remove(templateID);

			if (groups.TryGetValue(templateID, out GroupView view))
			{
				view.Root?.RemoveFromHierarchy();
				groups.Remove(templateID);
			}
		}

		/// <summary>
		/// Removes all rendered buff groups and forgets the model.
		/// </summary>
		protected void ClearAll()
		{
			entries.Clear();

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
		/// Recreates every rendered group from the model.
		/// </summary>
		private void RebuildView()
		{
			if (list == null)
			{
				return;
			}

			foreach (GroupView view in groups.Values)
			{
				view.Root?.RemoveFromHierarchy();
			}
			groups.Clear();

			foreach (int templateID in entries.Keys)
			{
				ApplyEntry(templateID);
			}
		}

		/// <summary>
		/// Creates or updates the rendered group for one model entry.
		/// </summary>
		/// <param name="templateID">The buff template ID.</param>
		private void ApplyEntry(int templateID)
		{
			if (list == null || !entries.TryGetValue(templateID, out BuffEntry entry) || entry.Template == null)
			{
				return;
			}

			if (!groups.TryGetValue(templateID, out GroupView view))
			{
				view = CreateGroup(entry.Template);
				list.Add(view.Root);
			}

			// Quantised: the icon is 40px tall, so sub-percent changes are not visible and writing
			// them every tick would repaint the element for nothing.
			if (Mathf.Abs(view.AppliedFraction - entry.Fraction) >= 0.005f)
			{
				view.AppliedFraction = entry.Fraction;
				if (view.Fill != null)
				{
					view.Fill.style.height = Length.Percent(entry.Fraction * 100.0f);
				}
			}

			if (view.AppliedStacks != entry.Stacks)
			{
				view.AppliedStacks = entry.Stacks;
				if (view.Label != null)
				{
					// Stacks counts applications ABOVE the base one, so a single application is
					// not annotated at all — a bare "1" on every icon is noise.
					view.Label.text = entry.Stacks > 0 ? (entry.Stacks + 1).ToString() : string.Empty;
				}
			}

			groups[templateID] = view;
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

			Label label = new Label(string.Empty);
			label.AddToClassList(LABEL_CLASS);
			label.pickingMode = PickingMode.Ignore;
			groupRoot.Add(label);

			GroupView view;
			view.Root = groupRoot;
			view.Fill = fill;
			view.Label = label;
			view.AppliedFraction = -1.0f;
			view.AppliedStacks = -1;

			groupRoot.RegisterCallback<PointerEnterEvent>(evt => OnGroupPointerEnter(template, groupRoot));
			groupRoot.RegisterCallback<PointerLeaveEvent>(evt => OnGroupPointerLeave(groupRoot));

			return view;
		}

		/// <summary>
		/// Shows the buff tooltip when the pointer enters a buff group.
		/// </summary>
		/// <param name="template">The buff template to describe.</param>
		/// <param name="owner">The hovered element, used to auto-close the tooltip.</param>
		private void OnGroupPointerEnter(BaseBuffTemplate template, VisualElement owner)
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
				tooltip.Open(text, owner);
			}
		}

		/// <summary>
		/// Hides the buff tooltip when the pointer leaves a buff group.
		/// </summary>
		/// <param name="owner">The element the pointer left.</param>
		private void OnGroupPointerLeave(VisualElement owner)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.HideFor(owner);
			}
		}
	}
}

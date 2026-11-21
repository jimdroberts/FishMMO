using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Object;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Transporting;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit pet control panel. Shows each pet's name and health, one row per pet, and issues
	/// follow/stay/summon/release commands, which go to every pet.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Clicking a pet's row pins that pet to the target frame (<see cref="ITargetController.TryPinTarget"/>), and the
	/// pinned pet's row is marked.
	/// </para>
	/// <para>
	/// A pet's health is a live value, so this panel subscribes to each pet's health attribute
	/// rather than sampling it once at summon time. It previously read the attribute exactly twice
	/// — on summon and on character set — and then never again, so the bar showed the pet's health
	/// at the moment it appeared for the pet's whole life.
	/// </para>
	/// <para>
	/// The fill fraction was also inverted (<c>FinalValue / CurrentValue</c>), which reads as a
	/// full bar at any health at all and an over-full one below half, and the name block set the
	/// GUILD label active inside a check on the NAME label.
	/// </para>
	/// </remarks>
	public class UITKPetControl : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Window;

		/// <summary>Name of the panel title label element.</summary>
		private const string NAME_LABEL_NAME = "pet-name";

		/// <summary>Name of the container the per-pet rows are built in.</summary>
		private const string LIST_NAME = "pet-list";

		/// <summary>Name prefix of a pet's row; the pet's slot follows.</summary>
		public const string ROW_PREFIX = "pet-row-";

		/// <summary>Class on the row of the pet pinned to the target frame.</summary>
		private const string ROW_PINNED_CLASS = "pet-row--pinned";

		/// <summary>Name of the pet status badge element.</summary>
		private const string STATUS_LABEL_NAME = "pet-status";

		/// <summary>Name of the follow command button.</summary>
		private const string FOLLOW_BUTTON_NAME = "pet-follow";

		/// <summary>Name of the stay command button.</summary>
		private const string STAY_BUTTON_NAME = "pet-stay";

		/// <summary>Name of the summon command button.</summary>
		private const string SUMMON_BUTTON_NAME = "pet-summon";

		/// <summary>Name of the release command button.</summary>
		private const string RELEASE_BUTTON_NAME = "pet-release";

		/// <summary>Name of the attack command button.</summary>
		private const string ATTACK_BUTTON_NAME = "pet-attack";

		/// <summary>Name of the button that opens and closes the attack priority rows.</summary>
		private const string PRIORITY_BUTTON_NAME = "pet-priority";

		/// <summary>Name of the container holding the attack priority rows.</summary>
		private const string PRIORITY_PANEL_NAME = "pet-priority-panel";

		/// <summary>Name prefix of the label naming the step in a priority row; the slot index follows.</summary>
		private const string PRIORITY_LABEL_PREFIX = "pet-priority-label-";

		/// <summary>Name prefix of the button that moves a priority row up; the slot index follows.</summary>
		private const string PRIORITY_UP_PREFIX = "pet-priority-up-";

		/// <summary>Name prefix of the button that moves a priority row down; the slot index follows.</summary>
		private const string PRIORITY_DOWN_PREFIX = "pet-priority-down-";

		/// <summary>Settings key the attack priority is kept under between sessions.</summary>
		private const string PRIORITY_SETTING_KEY = "PetAttackPriority";

		/// <summary>The priority rows' container.</summary>
		private VisualElement priorityPanel;

		/// <summary>One label per priority slot, first slot first.</summary>
		private readonly Label[] priorityLabels = new Label[PetAttackPriority.StepCount];

		/// <summary>One move-up button per priority slot, first slot first.</summary>
		private readonly Button[] priorityUpButtons = new Button[PetAttackPriority.StepCount];

		/// <summary>One move-down button per priority slot, first slot first.</summary>
		private readonly Button[] priorityDownButtons = new Button[PetAttackPriority.StepCount];

		/// <summary>The order in force, packed; see <see cref="PetAttackPriority"/>.</summary>
		private int attackPriority = PetAttackPriority.Default;

		/// <summary>Scratch for unpacking <see cref="attackPriority"/>.</summary>
		private readonly PetAttackTarget[] priorityOrder = new PetAttackTarget[PetAttackPriority.StepCount];

		/// <summary>Name of the passive stance button.</summary>
		private const string STANCE_PASSIVE_NAME = "pet-stance-passive";

		/// <summary>Name of the defensive stance button.</summary>
		private const string STANCE_DEFENSIVE_NAME = "pet-stance-defensive";

		/// <summary>Name of the aggressive stance button.</summary>
		private const string STANCE_AGGRESSIVE_NAME = "pet-stance-aggressive";

		/// <summary>Class applied to the stance button matching the pet's current stance.</summary>
		private const string STANCE_ACTIVE_CLASS = "pet-stance--active";

		/// <summary>Cached reference to the panel title label element.</summary>
		private Label nameLabel;
		/// <summary>Cached reference to the container of per-pet rows.</summary>
		private VisualElement petList;
		/// <summary>Cached reference to the pet status badge element.</summary>
		private Label statusLabel;

		/// <summary>Cached reference to the passive stance button.</summary>
		private Button passiveButton;
		/// <summary>Cached reference to the defensive stance button.</summary>
		private Button defensiveButton;
		/// <summary>Cached reference to the aggressive stance button.</summary>
		private Button aggressiveButton;

		/// <summary>
		/// The stance currently shown. Model, not view — the panel rebuilds its tree on show, so
		/// the highlighted button has to be reapplied from here rather than left on the elements.
		/// </summary>
		private PetStance petStance = PetStance.Defensive;

		/// <summary>The movement order currently shown.</summary>
		private PetMovementOrder petMovementOrder = PetMovementOrder.Follow;

		/// <summary>
		/// One pet the panel shows: the health subscription taken on it and its row in the current tree.
		/// </summary>
		/// <remarks>
		/// Model, not view: the subscription is held so it can be released against the SAME pet it was taken on (reading
		/// the controller again at unsubscribe time can hand back a different pet, or none, which leaves the old attribute
		/// holding a handler that writes into this panel forever), and the health is kept so a rebuilt tree is redrawn.
		/// </remarks>
		private sealed class BoundPet
		{
			public Pet Pet;
			public string Name;
			public CharacterResourceAttribute Health;
			public System.Action<CharacterAttribute> OnHealth;
			public float HealthFraction;
			public string HealthText = string.Empty;
			public VisualElement Row;
			public VisualElement Fill;
			public Label Text;
			/// <summary>The whole-unit health and maximum last drawn (see OnTick); MinValue until first drawn.</summary>
			public int DrawnCurrent = int.MinValue;
			public int DrawnMax = int.MinValue;
		}

		/// <summary>The pets shown, in slot order.</summary>
		private readonly List<BoundPet> boundPets = new List<BoundPet>();

		/// <summary>The container the rows were last built in; a rebuilt tree has a new one.</summary>
		private VisualElement rowsBuiltIn;

		/// <summary>The target controller whose pin events this panel listens to, released against the same one.</summary>
		private ITargetController pinSource;

		/// <summary>The first pet's name, which speaks for the pack when an order applies to all of them.</summary>
		private string petName = "Pet";

		/// <summary>
		/// Queries elements and wires up the command buttons.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			nameLabel = root.Q<Label>(NAME_LABEL_NAME);
			petList = root.Q(LIST_NAME);
			statusLabel = root.Q<Label>(STATUS_LABEL_NAME);

			Button follow = root.Q<Button>(FOLLOW_BUTTON_NAME);
			if (follow != null)
			{
				follow.clicked += OnFollowPet;
			}

			Button stay = root.Q<Button>(STAY_BUTTON_NAME);
			if (stay != null)
			{
				stay.clicked += OnStayPet;
			}

			Button summon = root.Q<Button>(SUMMON_BUTTON_NAME);
			if (summon != null)
			{
				summon.clicked += OnSummonPet;
			}

			Button release = root.Q<Button>(RELEASE_BUTTON_NAME);
			if (release != null)
			{
				release.clicked += OnReleasePet;
			}

			Button attack = root.Q<Button>(ATTACK_BUTTON_NAME);
			if (attack != null)
			{
				attack.clicked += OnAttackWithPet;
			}

			Button priority = root.Q<Button>(PRIORITY_BUTTON_NAME);
			if (priority != null)
			{
				priority.clicked += OnTogglePriorityPanel;
			}
			priorityPanel = root.Q(PRIORITY_PANEL_NAME);
			if (priorityPanel != null)
			{
				priorityPanel.style.display = DisplayStyle.None;
			}
			for (int i = 0; i < PetAttackPriority.StepCount; ++i)
			{
				int slot = i;
				priorityLabels[i] = root.Q<Label>(PRIORITY_LABEL_PREFIX + i);
				priorityUpButtons[i] = root.Q<Button>(PRIORITY_UP_PREFIX + i);
				if (priorityUpButtons[i] != null)
				{
					priorityUpButtons[i].clicked += () => OnPriorityMoveUp(slot);
				}
				priorityDownButtons[i] = root.Q<Button>(PRIORITY_DOWN_PREFIX + i);
				if (priorityDownButtons[i] != null)
				{
					priorityDownButtons[i].clicked += () => OnPriorityMoveDown(slot);
				}
			}
			attackPriority = LoadStoredPriority();

			passiveButton = root.Q<Button>(STANCE_PASSIVE_NAME);
			if (passiveButton != null)
			{
				passiveButton.clicked += OnStancePassive;
			}

			defensiveButton = root.Q<Button>(STANCE_DEFENSIVE_NAME);
			if (defensiveButton != null)
			{
				defensiveButton.clicked += OnStanceDefensive;
			}

			aggressiveButton = root.Q<Button>(STANCE_AGGRESSIVE_NAME);
			if (aggressiveButton != null)
			{
				aggressiveButton.clicked += OnStanceAggressive;
			}

			/* Static events, and OnStarting re-runs on every tree rebuild. Removing first makes
			 * the pair idempotent; a bare += would stack a handler per rebuild. */
			IPetController.OnPetSummoned -= PetController_OnPetSummoned;
			IPetController.OnPetSummoned += PetController_OnPetSummoned;
			IPetController.OnPetDestroyed -= PetController_OnPetDestroyed;
			IPetController.OnPetDestroyed += PetController_OnPetDestroyed;
			IPetController.OnPetOrdersChanged -= PetController_OnPetOrdersChanged;
			IPetController.OnPetOrdersChanged += PetController_OnPetOrdersChanged;
			PetController.OnClientPetAction -= PetController_OnClientPetAction;
			PetController.OnClientPetAction += PetController_OnClientPetAction;
		}

		/// <summary>
		/// Releases the static pet subscriptions and the pet health subscription.
		/// </summary>
		public override void OnDestroying()
		{
			IPetController.OnPetSummoned -= PetController_OnPetSummoned;
			IPetController.OnPetDestroyed -= PetController_OnPetDestroyed;
			IPetController.OnPetOrdersChanged -= PetController_OnPetOrdersChanged;
			PetController.OnClientPetAction -= PetController_OnClientPetAction;

			UnbindAllPets();
			UnbindPinSource();

			base.OnDestroying();
		}

		/// <summary>
		/// Binds the panel to whatever pet the character already has.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out ITargetController targetController))
			{
				pinSource = targetController;
				pinSource.OnPinTarget += TargetController_OnPinChanged;
				pinSource.OnUnpinTarget += TargetController_OnPinChanged;
			}
			RebindPets();
		}

		/// <summary>
		/// Releases the outgoing character's pet binding before a new character is applied.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			UnbindAllPets();
			UnbindPinSource();
		}

		/// <summary>
		/// Releases the pet binding and resets the panel.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			UnbindAllPets();
			UnbindPinSource();

			petName = "Pet";
			petStance = PetStance.Defensive;
			petMovementOrder = PetMovementOrder.Follow;
			ApplyPetState();

			Hide();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			ApplyPetState();
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			ApplyPetState();
		}

		/// <summary>
		/// Writes the tracked pet state into the current visual tree.
		/// </summary>
		/// <remarks>
		/// Called from both <see cref="OnAfterShow"/> and <see cref="OnAfterStarting"/>, the latter
		/// for a tree that was built or replaced. Applying the same state from both is idempotent.
		/// </remarks>
		private void ApplyPetState()
		{
			if (nameLabel != null)
			{
				nameLabel.text = boundPets.Count > 1 ? $"Pets ({boundPets.Count})" : (boundPets.Count == 1 ? boundPets[0].Name : "Pet");
			}
			BuildRows();
			for (int i = 0; i < boundPets.Count; ++i)
			{
				ApplyRowHealth(boundPets[i]);
			}
			ApplyPinnedRow();
			if (statusLabel != null)
			{
				statusLabel.text = boundPets.Count > 0 ? DescribeOrders() : string.Empty;
			}

			ApplyStanceHighlight();
			ApplyPriorityRows();
		}

		/// <summary>
		/// Builds one row per pet in the current tree, in slot order, unless the rows already there are these pets'.
		/// </summary>
		private void BuildRows()
		{
			if (petList == null)
			{
				return;
			}
			bool current = rowsBuiltIn == petList && petList.childCount == boundPets.Count;
			for (int i = 0; current && i < boundPets.Count; ++i)
			{
				current = boundPets[i].Row != null && boundPets[i].Row.parent == petList && petList[i] == boundPets[i].Row;
			}
			if (current)
			{
				return;
			}

			petList.Clear();
			rowsBuiltIn = petList;
			for (int i = 0; i < boundPets.Count; ++i)
			{
				BoundPet bound = boundPets[i];
				VisualElement row = new VisualElement();
				row.name = ROW_PREFIX + bound.Pet.Slot;
				row.AddToClassList("pet-row");
				row.tooltip = "Pin " + bound.Name + " to the target frame";

				Label name = new Label(bound.Name);
				name.pickingMode = PickingMode.Ignore;
				name.AddToClassList("fish-label");
				name.AddToClassList("pet-row__name");
				row.Add(name);

				VisualElement bar = new VisualElement();
				bar.pickingMode = PickingMode.Ignore;
				bar.AddToClassList("fish-bar");
				bar.AddToClassList("pet-health");
				VisualElement fill = new VisualElement();
				fill.pickingMode = PickingMode.Ignore;
				fill.AddToClassList("fish-bar__fill");
				fill.AddToClassList("fish-bar__fill--hp");
				fill.AddToClassList("pet-health-fill");
				Label text = new Label();
				text.pickingMode = PickingMode.Ignore;
				text.AddToClassList("fish-bar__label");
				text.AddToClassList("pet-health-text");
				bar.Add(fill);
				bar.Add(text);
				row.Add(bar);

				Pet pet = bound.Pet;
				row.RegisterCallback<ClickEvent>(_ => OnPetRowClicked(pet));

				bound.Row = row;
				bound.Fill = fill;
				bound.Text = text;
				petList.Add(row);
			}
		}

		/// <summary>
		/// Each frame while shown: every row against the pet itself, the way the target frame reads its pinned target.
		/// </summary>
		/// <remarks>
		/// The rows follow health events, and the events are right; but a row bound to an object that is no longer the pet
		/// hears nothing, and the panel sat at full health while the target frame, reading the live pet, showed 65%
		/// (2026-11-18). So a row whose pet is no longer the listed, live object is rebound, and a row whose drawn numbers
		/// differ from its health is redrawn: two integer comparisons per pet per frame until something moves.
		/// </remarks>
		protected override void OnTick()
		{
			if (!Visible || boundPets.Count == 0 || Character == null || !Character.TryGet(out IPetController petController))
			{
				return;
			}
			for (int i = 0; i < boundPets.Count; ++i)
			{
				BoundPet bound = boundPets[i];
				if (bound.Pet == null || !IsListed(petController.Pets, bound.Pet))
				{
					RebindPets();
					return;
				}
				CharacterResourceAttribute health = bound.Health;
				if (health == null)
				{
					continue;
				}
				int current = Mathf.RoundToInt(health.CurrentValue);
				int max = health.FinalValue;
				if (current != bound.DrawnCurrent || max != bound.DrawnMax)
				{
					ApplyHealth(bound, health);
					ApplyRowHealth(bound);
				}
			}
		}

		/// <summary>Whether <paramref name="pet"/> is in the list, by reference; a loop, as this runs every frame.</summary>
		private static bool IsListed(IReadOnlyList<Pet> pets, Pet pet)
		{
			for (int i = 0; i < pets.Count; ++i)
			{
				if (ReferenceEquals(pets[i], pet))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Writes one pet's health into its row.</summary>
		private static void ApplyRowHealth(BoundPet bound)
		{
			if (bound.Fill != null)
			{
				bound.Fill.style.width = Length.Percent(Mathf.Clamp01(bound.HealthFraction) * 100.0f);
			}
			if (bound.Text != null)
			{
				bound.Text.text = bound.HealthText;
			}
		}

		/// <summary>Marks the row of the pet pinned to the target frame, and only that one.</summary>
		private void ApplyPinnedRow()
		{
			Transform pinned = pinSource != null ? pinSource.PinnedTarget : null;
			for (int i = 0; i < boundPets.Count; ++i)
			{
				BoundPet bound = boundPets[i];
				bound.Row?.EnableInClassList(ROW_PINNED_CLASS, pinned != null && bound.Pet != null && bound.Pet.Transform == pinned);
			}
		}

		/// <summary>Clicking a pet's row pins that pet as the target.</summary>
		public void OnPetRowClicked(Pet pet)
		{
			if (pet == null || Character == null || !Character.TryGet(out ITargetController targetController))
			{
				return;
			}
			targetController.TryPinTarget(pet.Transform);
			ApplyPinnedRow();
		}

		/// <summary>A pin was taken or released: re-mark the rows.</summary>
		private void TargetController_OnPinChanged(Transform target)
		{
			ApplyPinnedRow();
		}

		/// <summary>Stops listening to the pin events of the outgoing character.</summary>
		private void UnbindPinSource()
		{
			if (pinSource == null)
			{
				return;
			}
			pinSource.OnPinTarget -= TargetController_OnPinChanged;
			pinSource.OnUnpinTarget -= TargetController_OnPinChanged;
			pinSource = null;
		}

		/// <summary>The stance's colour class, on the stance buttons (fixed) and on the status badge (following the stance).</summary>
		private static string StanceClass(PetStance stance)
		{
			switch (stance)
			{
				case PetStance.Aggressive: return "pet-stance--aggressive";
				case PetStance.Passive: return "pet-stance--passive";
				default: return "pet-stance--defensive";
			}
		}

		/// <summary>
		/// Renders the attack priority rows from the order in force, first slot on top.
		/// </summary>
		/// <remarks>
		/// Every row can be moved. The first row used to carry only a move-up button, permanently
		/// greyed out because there is nowhere above it to go — a row with no working control. Each
		/// row now has a move-down as well, and the one direction a row cannot go (up on the first,
		/// down on the last) is hidden rather than shown dead; hidden by visibility, so the arrows
		/// stay lined up in one column.
		/// </remarks>
		private void ApplyPriorityRows()
		{
			if (!PetAttackPriority.TryDecode(attackPriority, priorityOrder))
			{
				attackPriority = PetAttackPriority.Default;
				PetAttackPriority.TryDecode(attackPriority, priorityOrder);
			}
			for (int i = 0; i < PetAttackPriority.StepCount; ++i)
			{
				if (priorityLabels[i] != null)
				{
					priorityLabels[i].text = (i + 1) + ". " + PetAttackPriority.Describe(priorityOrder[i]);
				}
				SetMoveButton(priorityUpButtons[i], i > 0);
				SetMoveButton(priorityDownButtons[i], i < PetAttackPriority.StepCount - 1);
			}
		}

		/// <summary>
		/// Short badge text describing what the pet has been told to do.
		/// </summary>
		/// <returns>A label such as "Defensive · Stay".</returns>
		private string DescribeOrders()
		{
			return petMovementOrder == PetMovementOrder.Stay
				? petStance + " \u00B7 Stay"
				: petStance.ToString();
		}

		/// <summary>
		/// Marks the button matching the current stance and clears the other two.
		/// </summary>
		private void ApplyStanceHighlight()
		{
			SetStanceActive(passiveButton, petStance == PetStance.Passive);
			SetStanceActive(defensiveButton, petStance == PetStance.Defensive);
			SetStanceActive(aggressiveButton, petStance == PetStance.Aggressive);
			// The badge in the header takes the stance's colour too (the word stays: colour is never the only cue).
			if (statusLabel != null)
			{
				foreach (PetStance stance in new[] { PetStance.Passive, PetStance.Defensive, PetStance.Aggressive })
				{
					statusLabel.EnableInClassList(StanceClass(stance), boundPets.Count > 0 && petStance == stance);
				}
			}
		}

		/// <summary>
		/// Adds or removes the active-stance class on a button.
		/// </summary>
		/// <param name="button">The stance button, which may be null before the tree is built.</param>
		/// <param name="active">Whether this is the current stance.</param>
		private static void SetStanceActive(Button button, bool active)
		{
			if (button == null)
			{
				return;
			}
			button.EnableInClassList(STANCE_ACTIVE_CLASS, active);
		}

		/// <summary>
		/// Refreshes the panel when the server confirms a stance or movement order change.
		/// </summary>
		/// <param name="pet">The first of the pets whose orders changed.</param>
		public void PetController_OnPetOrdersChanged(Pet pet)
		{
			/* Static event: it fires for every pet on this client. Only react to ours. */
			if (Character == null || !Character.TryGet(out IPetController petController))
			{
				return;
			}
			if (pet != null && !IsOurs(petController, pet))
			{
				return;
			}

			petStance = petController.Stance;
			petMovementOrder = petController.MovementOrder;
			SayConfirmedOrders();
			/* The server's confirmation is the order in force; it wins over the local copy and
			 * is what gets remembered, so the panel and the settings never disagree with it. */
			attackPriority = PetAttackPriority.Normalize(petController.AttackPriority);
			StorePriority(attackPriority);
			ApplyPetState();
		}

		/// <summary>A stance the owner asked for and the server has not confirmed yet; null for none.</summary>
		private PetStance? pendingStance;

		/// <summary>A movement order the owner asked for and the server has not confirmed yet; null for none.</summary>
		private PetMovementOrder? pendingMovementOrder;

		/// <summary>
		/// The pet says, in the owner's chat only, the order the server just confirmed. Only orders this owner gave: a summon
		/// or a reconnect confirms orders too, and the pet does not announce those.
		/// </summary>
		private void SayConfirmedOrders()
		{
			if (pendingStance.HasValue && pendingStance.Value == petStance)
			{
				pendingStance = null;
				switch (petStance)
				{
					case PetStance.Aggressive: Say("I will attack anything that comes near, master."); break;
					case PetStance.Defensive: Say("I will defend you, master."); break;
					case PetStance.Passive: Say("I will not fight unless you command it, master."); break;
				}
			}
			if (pendingMovementOrder.HasValue && pendingMovementOrder.Value == petMovementOrder)
			{
				pendingMovementOrder = null;
				Say(petMovementOrder == PetMovementOrder.Stay ? "I will hold here, master." : "Following you, master.");
			}
		}

		/// <summary>
		/// What the pet has just started doing, from the server (<see cref="PetActionBroadcast"/>): which target an attack order
		/// chose, or a fight it joined on its own.
		/// </summary>
		public void PetController_OnClientPetAction(Pet pet, PetAction action, long targetID)
		{
			BoundPet bound = Find(pet);
			if (bound == null)
			{
				return;
			}
			string target = TargetName(targetID);
			switch (action)
			{
				case PetAction.Attacking: Say(bound.Name, $"Attacking {target}, master."); break;
				case PetAction.Defending: Say(bound.Name, $"Defending against {target}, master!"); break;
				case PetAction.Engaging: Say(bound.Name, $"Engaging {target}!"); break;
				case PetAction.NoTarget: Say(bound.Name, "I see nothing I may attack, master."); break;
			}
		}

		/// <summary>The name this client shows for a character, as the target frame does.</summary>
		private static string TargetName(long characterID)
		{
			if (characterID != 0 && BaseCharacter.ClientCharacters.TryGetValue(characterID, out ICharacter character) && character != null)
			{
				if (character is IPlayerCharacter player && !string.IsNullOrEmpty(player.CharacterName))
				{
					return player.CharacterName;
				}
				if (character.GameObject != null)
				{
					return character.GameObject.name.Replace("(Clone)", string.Empty);
				}
			}
			return "the target";
		}

		/// <summary>The first pet says <paramref name="text"/> for the pack: an order every pet obeys is confirmed once.</summary>
		private void Say(string text)
		{
			Say(petName, text);
		}

		/// <summary>The pet named <paramref name="speaker"/> says <paramref name="text"/> in this client's chat, as a local Say line.</summary>
		private static void Say(string speaker, string text)
		{
			ChatWindow.ShowLine(ChatChannel.Say, speaker, text);
		}

		/// <summary>The name this client shows for a pet.</summary>
		private static string PetDisplayName(Pet pet)
		{
			return pet != null && pet.GameObject != null ? pet.GameObject.name.Replace("(Clone)", string.Empty) : "Pet";
		}

		/// <summary>True when <paramref name="pet"/> is one of <paramref name="petController"/>'s pets.</summary>
		private static bool IsOurs(IPetController petController, Pet pet)
		{
			for (int i = 0; i < petController.Pets.Count; ++i)
			{
				if (ReferenceEquals(petController.Pets[i], pet))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The shown entry for <paramref name="pet"/>, or null.</summary>
		private BoundPet Find(Pet pet)
		{
			for (int i = 0; pet != null && i < boundPets.Count; ++i)
			{
				if (ReferenceEquals(boundPets[i].Pet, pet))
				{
					return boundPets[i];
				}
			}
			return null;
		}

		/// <summary>
		/// Shows exactly the pets the character has out: binds the new ones (health subscription, nameplate), releases the
		/// ones that left, orders the rows by slot, and shows or hides the panel.
		/// </summary>
		private void RebindPets()
		{
			List<Pet> pets = new List<Pet>();
			if (Character != null && Character.TryGet(out IPetController petController))
			{
				for (int i = 0; i < petController.Pets.Count; ++i)
				{
					if (petController.Pets[i] != null)
					{
						pets.Add(petController.Pets[i]);
					}
				}
			}

			for (int i = boundPets.Count - 1; i >= 0; --i)
			{
				if (!pets.Contains(boundPets[i].Pet))
				{
					Unbind(boundPets[i]);
					boundPets.RemoveAt(i);
				}
			}

			bool added = false;
			for (int i = 0; i < pets.Count; ++i)
			{
				if (Find(pets[i]) == null)
				{
					boundPets.Add(Bind(pets[i]));
					added = true;
				}
			}
			boundPets.Sort((a, b) => a.Pet.Slot.CompareTo(b.Pet.Slot));

			if (boundPets.Count == 0)
			{
				petName = "Pet";
				petStance = PetStance.Defensive;
				petMovementOrder = PetMovementOrder.Follow;
				ApplyPetState();
				Hide();
				return;
			}

			BoundPet first = boundPets[0];
			petName = first.Name;
			petStance = first.Pet.Stance;
			petMovementOrder = first.Pet.MovementOrder;
			if (added)
			{
				ReplayStoredPriority(first.Pet);
			}

			// Show() writes the state above through OnAfterShow; an already-visible panel is
			// written directly.
			if (!Visible)
			{
				Show();
			}
			else
			{
				ApplyPetState();
			}
		}

		/// <summary>Takes a pet's health subscription and shows its nameplate.</summary>
		private BoundPet Bind(Pet pet)
		{
			BoundPet bound = new BoundPet() { Pet = pet, Name = PetDisplayName(pet) };

			if (pet.CharacterNameplate != null)
			{
				pet.CharacterNameplate.Visible = true;
			}

			if (pet.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				bound.Health = health;
				bound.OnHealth = attribute => PetHealth_OnAttributeUpdated(bound, attribute);
				health.OnAttributeUpdated += bound.OnHealth;
				ApplyHealth(bound, health);
			}
			return bound;
		}

		/// <summary>Releases a pet's health subscription, on the attribute it was taken on.</summary>
		private static void Unbind(BoundPet bound)
		{
			if (bound.Health != null && bound.OnHealth != null)
			{
				bound.Health.OnAttributeUpdated -= bound.OnHealth;
			}
			bound.Health = null;
			bound.OnHealth = null;
			bound.Row?.RemoveFromHierarchy();
		}

		/// <summary>Releases every pet's health subscription.</summary>
		private void UnbindAllPets()
		{
			for (int i = 0; i < boundPets.Count; ++i)
			{
				Unbind(boundPets[i]);
			}
			boundPets.Clear();
		}

		/// <summary>
		/// Refreshes a pet's health bar whenever its health attribute changes.
		/// </summary>
		private void PetHealth_OnAttributeUpdated(BoundPet bound, CharacterAttribute attribute)
		{
			if (attribute is CharacterResourceAttribute resource)
			{
				ApplyHealth(bound, resource);

				// Every health tick; a hidden panel is written when it opens, by OnAfterShow.
				if (Visible)
				{
					ApplyRowHealth(bound);
				}
			}
		}

		/// <summary>
		/// Recomputes a pet's health fraction and label from its resource attribute.
		/// </summary>
		private static void ApplyHealth(BoundPet bound, CharacterResourceAttribute health)
		{
			// CurrentValue / FinalValue. The original was FinalValue / CurrentValue, which is the
			// reciprocal — a pet at 1/100 health showed a bar clamped to full.
			bound.HealthFraction = health.FinalValueAsFloat > 0.0f
				? health.CurrentValue / health.FinalValueAsFloat
				: 0.0f;
			bound.HealthText = Mathf.RoundToInt(health.CurrentValue) + "/" + health.FinalValue;
			bound.DrawnCurrent = Mathf.RoundToInt(health.CurrentValue);
			bound.DrawnMax = health.FinalValue;
		}

		/// <summary>
		/// Adds a summoned pet's row and makes the panel visible.
		/// </summary>
		/// <param name="pet">The summoned pet.</param>
		public void PetController_OnPetSummoned(Pet pet)
		{
			/* Static event: it fires for EVERY pet summoned anywhere on this client, including
			 * other players'. Only bind the ones belonging to this panel's character. */
			if (pet != null &&
				Character != null &&
				Character.TryGet(out IPetController petController) &&
				!IsOurs(petController, pet))
			{
				return;
			}

			RebindPets();
		}

		/// <summary>
		/// Drops a pet's row when it leaves; hides the panel when none is left.
		/// </summary>
		/// <param name="pet">The pet that left, or null when every pet left.</param>
		public void PetController_OnPetDestroyed(Pet pet)
		{
			RebindPets();
		}

		/// <summary>
		/// Returns whether the character currently has a pet.
		/// </summary>
		/// <returns>True if a pet exists.</returns>
		private bool HasPet()
		{
			return Character != null &&
				Character.TryGet(out IPetController petController) &&
				petController.Pets.Count > 0;
		}

		/// <summary>
		/// Sends a follow command to every pet.
		/// </summary>
		public void OnFollowPet()
		{
			if (!HasPet())
			{
				return;
			}
			pendingMovementOrder = PetMovementOrder.Follow;
			Client.Broadcast(new PetFollowBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Sends a stay command to every pet.
		/// </summary>
		public void OnStayPet()
		{
			if (!HasPet())
			{
				return;
			}
			pendingMovementOrder = PetMovementOrder.Stay;
			Client.Broadcast(new PetStayBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Calls every pet to the player's side.
		/// </summary>
		public void OnSummonPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetSummonBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Orders every pet to attack the player's current target.
		/// </summary>
		/// <remarks>
		/// Names the target frame — the pinned target when one is held, else whatever is hovered —
		/// so the pet goes at what the player is looking at in the HUD rather than at whatever the
		/// camera centre happens to trace. The server verifies the claim before acting on it; see
		/// <see cref="PetAttackBroadcast"/>.
		/// </remarks>
		public void OnAttackWithPet()
		{
			if (!HasPet())
			{
				return;
			}
			int pinned = 0;
			int hovered = 0;
			if (Character != null && Character.TryGet(out ITargetController targetController))
			{
				pinned = ResolveCharacterObjectId(targetController.PinnedTarget);
				hovered = ResolveCharacterObjectId(targetController.Current.Target);
			}
			Client.Broadcast(new PetAttackBroadcast()
			{
				PinnedTargetObjectID = pinned,
				HoveredTargetObjectID = hovered,
			}, Channel.Reliable);
		}

		/// <summary>
		/// The NetworkObject id of a targeted character, or 0 when the transform is nothing, not
		/// spawned, or not a character. A signpost is worth hovering but never worth sending a
		/// pet at.
		/// </summary>
		private static int ResolveCharacterObjectId(Transform target)
		{
			if (target == null ||
				!target.TryGetComponent(out NetworkObject targetObject) ||
				!targetObject.IsSpawned ||
				target.GetComponent<ICharacter>() == null)
			{
				return 0;
			}
			return targetObject.ObjectId;
		}

		/// <summary>Shows or hides the attack priority rows.</summary>
		public void OnTogglePriorityPanel()
		{
			if (priorityPanel == null)
			{
				return;
			}
			bool open = priorityPanel.style.display == DisplayStyle.None;
			priorityPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
			if (open)
			{
				ApplyPriorityRows();
			}
		}

		/// <summary>
		/// Moves the step in <paramref name="slot"/> one place earlier, remembers the new order
		/// and asks the server to adopt it.
		/// </summary>
		/// <remarks>
		/// Applied locally at once so the rows respond to the click; the server's confirmation
		/// then arrives through <see cref="PetController_OnPetOrdersChanged"/> and, being the
		/// order in force, overwrites whatever is shown. The local copy is stored regardless of
		/// whether a pet is out, because the preference is the player's and the pet is only its
		/// current audience — it is replayed on the next summon.
		/// </remarks>
		public void OnPriorityMoveUp(int slot)
		{
			attackPriority = PetAttackPriority.MoveUp(attackPriority, slot);
			StorePriority(attackPriority);
			ApplyPriorityRows();
			RequestPriority(attackPriority);
		}

		/// <summary>
		/// Moves the step in <paramref name="slot"/> one place later; see <see cref="OnPriorityMoveUp"/>.
		/// </summary>
		public void OnPriorityMoveDown(int slot)
		{
			attackPriority = PetAttackPriority.MoveDown(attackPriority, slot);
			StorePriority(attackPriority);
			ApplyPriorityRows();
			RequestPriority(attackPriority);
		}

		/// <summary>Shows a move button that can act, and hides one that cannot.</summary>
		private static void SetMoveButton(Button button, bool usable)
		{
			if (button == null)
			{
				return;
			}
			button.SetEnabled(usable);
			button.style.visibility = usable ? Visibility.Visible : Visibility.Hidden;
		}

		/// <summary>Asks the server to adopt <paramref name="priority"/> for the current pet.</summary>
		private void RequestPriority(int priority)
		{
			if (Character == null || !Character.TryGet(out IPetController _))
			{
				return;
			}
			Client.Broadcast(new PetAttackPriorityBroadcast() { Priority = priority }, Channel.Reliable);
		}

		/// <summary>
		/// Pushes the remembered order to the server for a newly bound pet whose server-side
		/// order differs — the server holds it per session and the settings file across them.
		/// </summary>
		private void ReplayStoredPriority(Pet pet)
		{
			if (pet == null || Character == null || !Character.TryGet(out IPetController petController) ||
				!IsOurs(petController, pet))
			{
				return;
			}
			int stored = LoadStoredPriority();
			attackPriority = stored;
			if (PetAttackPriority.Normalize(petController.AttackPriority) != stored)
			{
				RequestPriority(stored);
			}
		}

		/// <summary>The order remembered in the client settings, or the default.</summary>
		private static int LoadStoredPriority()
		{
			if (Configuration.GlobalSettings == null)
			{
				return PetAttackPriority.Default;
			}
			Configuration.GlobalSettings.TryGetInt(PRIORITY_SETTING_KEY, out int stored, PetAttackPriority.Default);
			return PetAttackPriority.Normalize(stored);
		}

		/// <summary>Remembers <paramref name="priority"/> in the client settings.</summary>
		private static void StorePriority(int priority)
		{
			if (Configuration.GlobalSettings == null)
			{
				return;
			}
			Configuration.GlobalSettings.TryGetInt(PRIORITY_SETTING_KEY, out int stored, -1);
			if (stored == priority)
			{
				return;
			}
			Configuration.GlobalSettings.Set(PRIORITY_SETTING_KEY, priority.ToString());
			Configuration.GlobalSettings.Save();
		}

		/// <summary>Requests the passive stance.</summary>
		public void OnStancePassive()
		{
			RequestStance(PetStance.Passive);
		}

		/// <summary>Requests the defensive stance.</summary>
		public void OnStanceDefensive()
		{
			RequestStance(PetStance.Defensive);
		}

		/// <summary>Requests the aggressive stance.</summary>
		public void OnStanceAggressive()
		{
			RequestStance(PetStance.Aggressive);
		}

		/// <summary>
		/// Asks the server to change the pet's stance.
		/// </summary>
		/// <remarks>
		/// The panel does not paint the new stance here. It waits for the server's confirming
		/// <see cref="PetStanceBroadcast"/>, so the highlighted button always reflects what the
		/// pet is really doing rather than what was last clicked.
		/// </remarks>
		/// <param name="stance">The requested stance.</param>
		private void RequestStance(PetStance stance)
		{
			if (!HasPet())
			{
				return;
			}
			pendingStance = stance;
			Client.Broadcast(new PetStanceBroadcast() { Stance = stance }, Channel.Reliable);
		}

		/// <summary>
		/// Releases every pet.
		/// </summary>
		public void OnReleasePet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetReleaseBroadcast(), Channel.Reliable);
		}
	}
}

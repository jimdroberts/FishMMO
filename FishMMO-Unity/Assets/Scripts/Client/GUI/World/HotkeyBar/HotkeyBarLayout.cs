using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Where a hotkey slot sits among the bars, and which bar a key press belongs to.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One flat index.</b> The character's hotkey list, the wire and the database all address a
	/// slot by one number and know nothing of bars; a bar is <see cref="SlotsPerBar"/> consecutive
	/// slots (see <see cref="Constants.Configuration.HotkeySlotsPerBar"/> for why that makes the
	/// width, unlike the count, unsafe to change once characters have bindings). Only the client's
	/// drawing and its key routing ever think in bars, and both go through here.
	/// </para>
	/// <para>
	/// <b>Keys are positions, bars are modifiers.</b> The first bar's keys — left and right mouse,
	/// then 1 to 0 — name a POSITION on a bar. Which bar a press lands on is decided by the bar
	/// modifiers held at the moment of the press: the second bar's (Ctrl by default), the third's
	/// (Alt), and so on; with none held, the press lands on the first bar, or on the page a paged
	/// hotbar is resting on. So exactly one slot answers any press — there is no binding for
	/// "Ctrl+1" that could fire alongside the one for "1" — and rebinding a position key moves it on
	/// every bar at once. The rule is <see cref="ResolvePressBar"/> and its press/release pairing is
	/// <see cref="HotkeyPressRouter"/>; both are pure so they can be tested without an input device.
	/// </para>
	/// </remarks>
	public static class HotkeyBarLayout
	{
		/// <summary>Number of bars the game gives every character.</summary>
		public static int BarCount => Constants.Configuration.HotkeyBarCount < 1 ? 1 : Constants.Configuration.HotkeyBarCount;

		/// <summary>Number of slots on each bar.</summary>
		public static int SlotsPerBar => Constants.Configuration.HotkeySlotsPerBar;

		/// <summary>The flat slot index of a position on a bar.</summary>
		/// <param name="bar">Zero-based bar index.</param>
		/// <param name="position">Zero-based position on the bar.</param>
		public static int SlotIndex(int bar, int position) => bar * SlotsPerBar + position;

		/// <summary>The zero-based bar a flat slot index belongs to.</summary>
		public static int BarOf(int slotIndex) => slotIndex / SlotsPerBar;

		/// <summary>The zero-based position on its bar of a flat slot index.</summary>
		public static int PositionOf(int slotIndex) => slotIndex % SlotsPerBar;

		/// <summary>
		/// The bar a position key pressed now should fire on.
		/// </summary>
		/// <param name="heldModifierBar">
		/// The first bar (in bar order, from the second) whose modifier is held, or -1 for none.
		/// Bar order is the tie-break when a player holds two: the lower bar wins, deterministically,
		/// rather than whichever modifier happened to be pressed last.
		/// </param>
		/// <param name="paged">True when the hotbar shows one bar at a time.</param>
		/// <param name="restingPage">The bar a paged hotbar shows while no modifier is held.</param>
		/// <returns>The zero-based bar the press belongs to.</returns>
		/// <remarks>
		/// A held modifier always wins, in either layout: Ctrl+1 is the second bar's first key whether
		/// or not that bar is the page on screen, so a player's hands do not have to know the layout.
		/// Without one, a stacked hotbar's unmodified keys are the first bar's — every bar is on
		/// screen and the first is the one they belong to — while a paged hotbar's are the resting
		/// page's, because that is the bar the player is looking at.
		/// </remarks>
		public static int ResolvePressBar(int heldModifierBar, bool paged, int restingPage)
		{
			return ResolvePressBar(heldModifierBar, paged, restingPage, BarCount);
		}

		/// <summary>
		/// <see cref="ResolvePressBar(int, bool, int)"/> for an explicit bar count, so the rule can be
		/// tested for several bars in a game that ships one.
		/// </summary>
		public static int ResolvePressBar(int heldModifierBar, bool paged, int restingPage, int barCount)
		{
			if (heldModifierBar > 0 && heldModifierBar < barCount)
			{
				return heldModifierBar;
			}
			if (paged && restingPage > 0 && restingPage < barCount)
			{
				return restingPage;
			}
			return 0;
		}

		/// <summary>
		/// The bar a paged hotbar should show now: the held modifier's, else the resting page.
		/// </summary>
		/// <remarks>
		/// The same answer <see cref="ResolvePressBar"/> gives a paged hotbar, and deliberately so:
		/// the bar on screen is always the bar the next unmodified or modified press will fire.
		/// </remarks>
		public static int ResolveShownPage(int heldModifierBar, int restingPage)
		{
			return ResolveShownPage(heldModifierBar, restingPage, BarCount);
		}

		/// <summary><see cref="ResolveShownPage(int, int)"/> for an explicit bar count.</summary>
		public static int ResolveShownPage(int heldModifierBar, int restingPage, int barCount)
		{
			return ResolvePressBar(heldModifierBar, paged: true, restingPage, barCount);
		}

		/// <summary>The next resting page when paging by <paramref name="delta"/>, wrapping at either end.</summary>
		public static int StepPage(int restingPage, int delta)
		{
			return StepPage(restingPage, delta, BarCount);
		}

		/// <summary><see cref="StepPage(int, int)"/> for an explicit bar count.</summary>
		public static int StepPage(int restingPage, int delta, int barCount)
		{
			if (barCount < 1)
			{
				return 0;
			}
			int page = (restingPage + delta) % barCount;
			return page < 0 ? page + barCount : page;
		}
	}

	/// <summary>
	/// Pairs every position key's press with its release on the SAME bar.
	/// </summary>
	/// <remarks>
	/// The bar is decided once, at the press, and remembered until the key comes up. Without that,
	/// letting go of Ctrl before letting go of 1 would release the first bar's slot — which never
	/// started anything — and leave the second bar's charged ability held until its hold cap
	/// cancelled it; and pressing Ctrl while 1 is already down would move a press that is still in
	/// progress onto a different ability.
	/// </remarks>
	public sealed class HotkeyPressRouter
	{
		/// <summary>The bar that owns each position's press in progress, or -1.</summary>
		private readonly int[] owner;

		/// <summary>Creates a router for a bar width.</summary>
		/// <param name="positions">Positions on each bar.</param>
		public HotkeyPressRouter(int positions)
		{
			owner = new int[positions < 0 ? 0 : positions];
			Reset();
		}

		/// <summary>Forgets every press in progress, without releasing anything.</summary>
		public void Reset()
		{
			for (int i = 0; i < owner.Length; ++i)
			{
				owner[i] = -1;
			}
		}

		/// <summary>
		/// Advances one position key by one frame.
		/// </summary>
		/// <param name="position">The position whose key was sampled.</param>
		/// <param name="pressed">Whether its key is down this frame.</param>
		/// <param name="targetBar">The bar a press starting now would land on.</param>
		/// <param name="pressedBar">The bar that must activate this frame, or -1.</param>
		/// <param name="releasedBar">The bar that must release this frame, or -1.</param>
		public void Step(int position, bool pressed, int targetBar, out int pressedBar, out int releasedBar)
		{
			pressedBar = -1;
			releasedBar = -1;

			if (position < 0 || position >= owner.Length)
			{
				return;
			}

			int current = owner[position];
			if (pressed && current < 0)
			{
				owner[position] = targetBar;
				pressedBar = targetBar;
			}
			else if (!pressed && current >= 0)
			{
				owner[position] = -1;
				releasedBar = current;
			}
		}
	}
}

using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Transporting.WebTransport;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// A small heads-up overlay of what this client sends and receives, layer by layer: the game's
	/// own messages, the QUIC/UDP payload the transport puts on the network, and the estimated
	/// bytes on the wire once the IP and UDP headers are added. Toggled from the options panel.
	/// </summary>
	/// <remarks>
	/// <para><b>Where the numbers come from.</b> Everything is read through
	/// <see cref="TransportTraffic.Capture"/>, the transport's own accounting, and never through
	/// FishNet's statistics manager (off in release builds, internal, and double-counting in
	/// development ones). Every figure carries the transport's <see cref="TrafficMeasure"/>, and
	/// every row shows it as a badge whose tooltip says why: a layer this process cannot see is
	/// labelled an estimate, and a value it cannot know is a dash. The rules between the counters
	/// and the text are in <see cref="NetworkStatsPresentation"/>; the smoothing and the graph's
	/// memory are in <see cref="NetworkStatsSampler"/>. Both are pure, and both are tested.</para>
	///
	/// <para><b>Cost.</b> Nothing happens while the overlay is hidden: <see cref="OnTick"/> runs
	/// for hidden panels too and returns at once. While shown it captures one snapshot a second
	/// (allocation-free) and rewrites its labels only when a new sample has arrived, at most
	/// <see cref="MaxRefreshesPerSecond"/> times a second — nothing it shows changes faster than
	/// the sample. Capturing is also what keeps the transport refreshing the connection's
	/// round-trip figures, which it stops doing a few seconds after the last capture, so a hidden
	/// overlay costs the connection nothing either.</para>
	///
	/// <para><b>Why it lives in ClientPreboot.</b> That scene persists for the whole session, so
	/// the overlay works on the login, server-select and character-select screens as well as in
	/// the world — and those hops are exactly where handshakes show up in the numbers. The
	/// process-lifetime totals survive every hop.</para>
	///
	/// <para><b>Visibility has one owner</b>, <see cref="ClientNetworkStatsSettings.Enabled"/>,
	/// applied in <see cref="ApplySettings"/>. Quit-to-login re-applies it rather than following
	/// the generic close-or-show rule, since the player's choice is what decides.</para>
	/// </remarks>
	public class UITKNetworkStats : UITKControl
	{
		/// <summary>Name this panel registers under with <see cref="UIManager"/>: its GameObject name.</summary>
		public const string PanelName = "UINetworkStats";

		/// <summary>Seconds between snapshots.</summary>
		public const double SampleIntervalSeconds = 1.0;

		/// <summary>Ceiling on label rewrites.</summary>
		public const double MaxRefreshesPerSecond = 4.0;

		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>
		/// Draggable by its header, like the resource bars: it is a readout the player places, and
		/// no default corner suits every HUD arrangement and every login screen.
		/// </summary>
		protected override bool CanDrag => true;

		private const string TOOLTIP_NAME = "UITooltip";

		private const string BACKEND_NAME = "netstats-backend";
		private const string HEADLINE_NAME = "netstats-headline";
		private const string HEADLINE_DOWN_NAME = "netstats-headline-down";
		private const string HEADLINE_UP_NAME = "netstats-headline-up";
		private const string HEADLINE_BADGE_NAME = "netstats-headline-badge";
		private const string GRAPH_NAME = "netstats-graph";
		private const string GRAPH_SCALE_NAME = "netstats-graph-scale";

		/// <summary>Element-name stem of each row, indexed by <see cref="NetworkStatsRow"/>.</summary>
		public static readonly string[] RowStems =
		{
			"netstats-app",
			"netstats-quic",
			"netstats-wire",
			"netstats-dgram",
			"netstats-overhead",
			"netstats-link",
		};

		private const string SeriesDownClass = "netstats-series--down";
		private const string SeriesUpClass = "netstats-series--up";
		private const string SeriesClass = "netstats-series";

		/// <summary>The labels of one row.</summary>
		private struct RowView
		{
			public VisualElement Root;
			public Label Down;
			public Label Up;
			public Label DownTotal;
			public Label UpTotal;
			public Label Badge;
		}

		/// <summary>An <see cref="ITooltip"/> over one row, built from the panel's current readout.</summary>
		/// <remarks>
		/// A source rather than a one-off content, so the open tooltip can be re-described on every
		/// refresh through <see cref="UITKTooltip.RefreshFor"/>, which only ever touches the tooltip
		/// if it still belongs to that row.
		/// </remarks>
		private sealed class RowTooltip : ITooltip
		{
			private readonly UITKNetworkStats panel;
			private readonly NetworkStatsRow row;
			private readonly bool graph;

			public RowTooltip(UITKNetworkStats panel, NetworkStatsRow row, bool graph)
			{
				this.panel = panel;
				this.row = row;
				this.graph = graph;
			}

			public Sprite Icon => null;

			public string Name => graph ? "Traffic, last minute" : NetworkStatsPresentation.RowTitle(row);

			public void BuildTooltip(TooltipContent content)
			{
				NetworkStatsReadout readout = panel.readout;
				if (graph)
				{
					NetworkStatsPresentation.BuildGraphTooltip(content, panel.sampler.HistoryPeak(),
						NetworkStatsPresentation.RowMeasure(NetworkStatsRow.Wire, in readout));
					return;
				}
				NetworkStatsPresentation.BuildTooltip(content, row, in readout);
			}
		}

		private readonly NetworkStatsSampler sampler = new NetworkStatsSampler();
		private NetworkStatsReadout readout = NetworkStatsReadout.Empty;

		private readonly RowView[] rows = new RowView[6];
		private Label backendLabel;
		private VisualElement headline;
		private Label headlineDown;
		private Label headlineUp;
		private Label headlineBadge;
		private VisualElement graph;
		private Label graphScaleLabel;
		private NetworkStatsGraphSeries downSeries;
		private NetworkStatsGraphSeries upSeries;

		private RowTooltip[] rowTooltips;
		private RowTooltip headlineTooltip;
		private RowTooltip graphTooltip;

		/// <summary>The element whose tooltip is open, so a refresh can keep it current.</summary>
		private VisualElement hoveredOwner;
		/// <summary>The source of that tooltip.</summary>
		private RowTooltip hoveredSource;

		/// <summary>Stopwatch time the next snapshot is due.</summary>
		private double nextSampleAt;
		/// <summary>Stopwatch time before which the labels are not rewritten again.</summary>
		private double nextRefreshAt;
		/// <summary>True when a sample has arrived that the labels do not show yet.</summary>
		private bool refreshOwed;

		/// <summary>The smoothing window and graph memory. Exposed for tests and the render harness.</summary>
		public NetworkStatsSampler Sampler => sampler;

		/// <summary>What the labels currently show.</summary>
		public NetworkStatsReadout CurrentReadout => readout;

		/// <summary>
		/// Resolves the rows, builds the graph lines, wires the tooltips, subscribes to the
		/// settings and applies them. Runs once per visual tree.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			backendLabel = Root.Q<Label>(BACKEND_NAME);
			headline = Root.Q<VisualElement>(HEADLINE_NAME);
			headlineDown = Root.Q<Label>(HEADLINE_DOWN_NAME);
			headlineUp = Root.Q<Label>(HEADLINE_UP_NAME);
			headlineBadge = Root.Q<Label>(HEADLINE_BADGE_NAME);
			graph = Root.Q<VisualElement>(GRAPH_NAME);
			graphScaleLabel = Root.Q<Label>(GRAPH_SCALE_NAME);

			if (rowTooltips == null)
			{
				rowTooltips = new RowTooltip[RowStems.Length];
				for (int i = 0; i < RowStems.Length; ++i)
				{
					rowTooltips[i] = new RowTooltip(this, (NetworkStatsRow)i, false);
				}
				headlineTooltip = new RowTooltip(this, NetworkStatsRow.Wire, false);
				graphTooltip = new RowTooltip(this, NetworkStatsRow.Wire, true);
			}

			for (int i = 0; i < RowStems.Length; ++i)
			{
				string stem = RowStems[i];
				rows[i] = new RowView
				{
					Root = Root.Q<VisualElement>(stem + "-row"),
					Down = Root.Q<Label>(stem + "-down"),
					Up = Root.Q<Label>(stem + "-up"),
					DownTotal = Root.Q<Label>(stem + "-down-total"),
					UpTotal = Root.Q<Label>(stem + "-up-total"),
					Badge = Root.Q<Label>(stem + "-badge"),
				};
				AttachTooltip(rows[i].Root, rowTooltips[i]);
			}
			AttachTooltip(headline, headlineTooltip);
			AttachTooltip(graph, graphTooltip);

			/* The lines are code-built elements rather than UXML so the markup needs no custom
			 * element factory; inserted under the scale label so the label draws on top. The tree
			 * is new whenever this runs, so there is never a previous pair to remove. */
			if (graph != null)
			{
				downSeries = new NetworkStatsGraphSeries(upload: false);
				downSeries.AddToClassList(SeriesClass);
				downSeries.AddToClassList(SeriesDownClass);
				upSeries = new NetworkStatsGraphSeries(upload: true);
				upSeries.AddToClassList(SeriesClass);
				upSeries.AddToClassList(SeriesUpClass);
				graph.Insert(0, upSeries);
				graph.Insert(0, downSeries);
			}

			/* Static event, and OnStarting re-runs whenever the tree is rebuilt: remove first so
			 * the pair stays idempotent (see UITKCrosshair.OnStarting). */
			ClientNetworkStatsSettings.OnChanged -= ApplySettings;
			ClientNetworkStatsSettings.OnChanged += ApplySettings;

			Refresh();
			ApplySettings();
		}

		/// <summary>Unsubscribes from the settings.</summary>
		public override void OnDestroying()
		{
			ClientNetworkStatsSettings.OnChanged -= ApplySettings;
		}

		/// <summary>
		/// Starts a fresh window and graph on every open, and samples on the next frame.
		/// </summary>
		/// <remarks>
		/// Nothing was sampled while the overlay was hidden, so keeping the old window would
		/// average across the hidden spell and join its ends in the graph as adjacent seconds.
		/// </remarks>
		protected override void OnAfterShow()
		{
			base.OnAfterShow();
			sampler.Clear();
			nextSampleAt = 0.0;
			nextRefreshAt = 0.0;
			Refresh();
		}

		/// <summary>Re-applies the player's choice after the generic quit-to-login rule has run.</summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();
			ApplySettings();
		}

		/// <summary>
		/// Samples once a second and refreshes the labels when a new sample is in, only while shown.
		/// </summary>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			double now = TransportTraffic.NowSeconds;
			if (now >= nextSampleAt)
			{
				TransportTraffic.Capture(out TransportTrafficSnapshot snapshot);
				Ingest(in snapshot);

				/* On a one-second grid, so a slow frame does not push every later sample back; a
				 * stall longer than a second resumes from now rather than sampling several times
				 * to catch up. */
				nextSampleAt += SampleIntervalSeconds;
				if (nextSampleAt <= now)
				{
					nextSampleAt = now + SampleIntervalSeconds;
				}
			}

			if (refreshOwed && now >= nextRefreshAt)
			{
				Refresh();
				nextRefreshAt = now + 1.0 / MaxRefreshesPerSecond;
			}
		}

		/// <summary>
		/// Feeds one snapshot into the window and the graph. <see cref="OnTick"/> calls this once a
		/// second; tests and the render harness call it with fabricated snapshots.
		/// </summary>
		public void Ingest(in TransportTrafficSnapshot snapshot)
		{
			if (sampler.Add(in snapshot))
			{
				refreshOwed = true;
			}
		}

		/// <summary>Writes the current readout into every label, badge and graph line.</summary>
		public void Refresh()
		{
			refreshOwed = false;
			readout = sampler.Readout();

			SetText(backendLabel, NetworkStatsPresentation.BackendCaption(in readout));

			NetworkStatsPresentation.Headline(in readout, out string down, out string up);
			SetText(headlineDown, down);
			SetText(headlineUp, up);
			SetBadge(headlineBadge,
				NetworkStatsPresentation.RowMeasure(NetworkStatsRow.Wire, in readout), !readout.HasSnapshot);

			for (int i = 0; i < rows.Length; ++i)
			{
				NetworkStatsFigures figures = NetworkStatsPresentation.Describe((NetworkStatsRow)i, in readout);
				ref RowView view = ref rows[i];
				SetText(view.Down, figures.Down);
				SetText(view.Up, figures.Up);
				SetText(view.DownTotal, figures.DownTotal);
				SetText(view.UpTotal, figures.UpTotal);
				SetBadge(view.Badge, figures.Measure, figures.Pending);
			}

			double scale = NetworkStatsPresentation.GraphScale(sampler.HistoryPeak());
			SetText(graphScaleLabel, NetworkStatsPresentation.ByteRate(scale));
			downSeries?.Bind(sampler, scale);
			upSeries?.Bind(sampler, scale);

			/* A tooltip left open over a row keeps describing it as the numbers move. RefreshFor
			 * does nothing unless the tooltip still belongs to this owner. */
			if (hoveredOwner != null && hoveredSource != null && UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.RefreshFor(hoveredOwner, hoveredSource);
			}
		}

		/// <summary>
		/// Shows or hides the overlay and its graph according to the player's settings.
		/// </summary>
		private void ApplySettings()
		{
			if (graph != null)
			{
				/* display, not visibility: the graph's height leaves the panel with it, so turning
				 * the graph off makes the overlay smaller rather than leaving a hole. */
				graph.style.display = ClientNetworkStatsSettings.ShowGraph ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (ClientNetworkStatsSettings.Enabled)
			{
				Show();
			}
			else
			{
				Hide();
			}
		}

		private void AttachTooltip(VisualElement owner, RowTooltip source)
		{
			if (owner == null)
			{
				return;
			}
			owner.RegisterCallback<PointerEnterEvent>(evt => OnOwnerPointerEnter(owner, source));
			owner.RegisterCallback<PointerLeaveEvent>(evt => OnOwnerPointerLeave(owner));
		}

		private void OnOwnerPointerEnter(VisualElement owner, RowTooltip source)
		{
			if (!UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				return;
			}
			hoveredOwner = owner;
			hoveredSource = source;
			tooltip.Open(source, owner);
		}

		private void OnOwnerPointerLeave(VisualElement owner)
		{
			if (ReferenceEquals(hoveredOwner, owner))
			{
				hoveredOwner = null;
				hoveredSource = null;
			}
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.HideFor(owner);
			}
		}

		/// <summary>Writes a label only when its text changes, so an unchanged figure is not re-meshed.</summary>
		private static void SetText(Label label, string text)
		{
			if (label == null)
			{
				return;
			}
			text ??= string.Empty;
			if (label.text != text)
			{
				label.text = text;
			}
		}

		/// <summary>Puts one badge's word and colour class on it, clearing the others.</summary>
		private static void SetBadge(Label badge, TrafficMeasure measure, bool pending)
		{
			if (badge == null)
			{
				return;
			}
			SetText(badge, NetworkStatsPresentation.BadgeText(measure, pending));
			string wanted = NetworkStatsPresentation.BadgeClass(measure, pending);
			for (int i = 0; i < NetworkStatsPresentation.BadgeClasses.Length; ++i)
			{
				string c = NetworkStatsPresentation.BadgeClasses[i];
				badge.EnableInClassList(c, c == wanted);
			}
		}
	}
}

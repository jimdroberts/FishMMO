using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// One line of the network statistics graph: the last minute of on-the-wire traffic in one
	/// direction, drawn with UI Toolkit's vector painter.
	/// </summary>
	/// <remarks>
	/// <para><b>The colour comes from the stylesheet.</b> The line is stroked in this element's own
	/// resolved <c>color</c>, which <c>UINetworkStats.uss</c> sets from a theme token per
	/// direction, so no colour is written in code and the legend beside the headline and the line
	/// cannot drift apart.</para>
	/// <para><b>Both lines share one scale</b>, set by the panel: two lines drawn to their own
	/// peaks would make a trickle of uploads look as large as the downloads.</para>
	/// <para><b>An unknown second is a gap.</b> The pen lifts over a NaN point and puts down again
	/// at the next known one (see <see cref="NetworkStatsPresentation.GraphY"/>), so a second the
	/// transport could not measure never reads as a second with no traffic.</para>
	/// </remarks>
	public sealed class NetworkStatsGraphSeries : VisualElement
	{
		/// <summary>Stroke width, in panel points.</summary>
		private const float LineWidth = 1.25f;

		private NetworkStatsSampler sampler;
		private readonly bool upload;
		private double scale = 1.0;

		/// <summary>Creates a line for one direction.</summary>
		/// <param name="upload">True for sent traffic, false for received.</param>
		public NetworkStatsGraphSeries(bool upload)
		{
			this.upload = upload;
			pickingMode = PickingMode.Ignore;
			generateVisualContent += Draw;
		}

		/// <summary>Points the line at the data and the shared scale, and schedules a redraw.</summary>
		public void Bind(NetworkStatsSampler sampler, double scale)
		{
			this.sampler = sampler;
			this.scale = scale;
			MarkDirtyRepaint();
		}

		private void Draw(MeshGenerationContext context)
		{
			NetworkStatsSampler data = sampler;
			if (data == null || data.HistoryCount < 2)
			{
				return;
			}

			Rect rect = contentRect;
			if (!(rect.width > 1.0f) || !(rect.height > 1.0f))
			{
				return;
			}

			/* The x axis is fixed at a full minute rather than stretched to the points held, so a
			 * line that has only just started grows in from the right as time passes instead of
			 * being redrawn at a new spacing every second. */
			int capacity = NetworkStatsSampler.HistoryCapacity;
			float step = rect.width / (capacity - 1);
			float x0 = rect.xMax - (data.HistoryCount - 1) * step;

			Painter2D painter = context.painter2D;
			painter.strokeColor = resolvedStyle.color;
			painter.lineWidth = LineWidth;
			painter.lineJoin = LineJoin.Round;
			painter.lineCap = LineCap.Round;

			bool penDown = false;
			bool pathOpen = false;
			for (int i = 0; i < data.HistoryCount; ++i)
			{
				data.GetHistory(i, out double down, out double up);
				float y = NetworkStatsPresentation.GraphY(upload ? up : down, scale, rect.height);
				if (float.IsNaN(y))
				{
					penDown = false;
					continue;
				}

				Vector2 point = new Vector2(x0 + i * step, rect.yMin + y);
				if (!pathOpen)
				{
					painter.BeginPath();
					pathOpen = true;
				}
				if (penDown)
				{
					painter.LineTo(point);
				}
				else
				{
					painter.MoveTo(point);
					penDown = true;
				}
			}

			if (pathOpen)
			{
				painter.Stroke();
			}
		}
	}
}

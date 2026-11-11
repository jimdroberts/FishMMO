using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// A compass for the world map: a dial whose needle points at the planet's north as the map draws it, with an upright
	/// "N" at the needle's tip.
	/// </summary>
	/// <remarks>
	/// The needle is painted (north half in --map-north, south half faint) and turned by its rotation, so it points
	/// exactly; the letter is placed at the tip rather than turned with it, so it reads at any angle. Styled by
	/// <c>.map-compass</c> in UIMap.uss. Draws over the map and takes no clicks.
	/// </remarks>
	public sealed class MapCompass : VisualElement
	{
		public const string ClassName = "map-compass";
		public const string NeedleClassName = ClassName + "__needle";
		public const string LetterClassName = ClassName + "__letter";

		/// <summary>How far in from the dial's edge the letter's centre sits, in points.</summary>
		private const float LetterInset = 7.0f;

		private readonly VisualElement needle;
		private readonly Label letter;
		private float northDegrees;

		public MapCompass()
		{
			pickingMode = PickingMode.Ignore;
			AddToClassList(ClassName);

			needle = new VisualElement { pickingMode = PickingMode.Ignore };
			needle.AddToClassList(NeedleClassName);
			needle.generateVisualContent += DrawNeedle;
			Add(needle);

			letter = new Label("N") { pickingMode = PickingMode.Ignore };
			letter.AddToClassList(LetterClassName);
			Add(letter);

			// The letter's place depends on the dial's size, which is only known once laid out.
			RegisterCallback<GeometryChangedEvent>(_ => PlaceLetter());
		}

		/// <summary>Points the compass: north's direction in the view, degrees clockwise from the view's top.</summary>
		public void SetNorth(float degrees)
		{
			if (Mathf.Approximately(degrees, northDegrees) && needle.style.rotate.keyword != StyleKeyword.Null)
			{
				return;
			}
			northDegrees = degrees;
			needle.style.rotate = new StyleRotate(new Rotate(degrees));
			PlaceLetter();
		}

		private void PlaceLetter()
		{
			float radius = Mathf.Min(layout.width, layout.height) * 0.5f - LetterInset;
			if (float.IsNaN(radius) || radius <= 0.0f)
			{
				return;
			}
			float radians = northDegrees * Mathf.Deg2Rad;
			letter.style.left = layout.width * 0.5f + Mathf.Sin(radians) * radius;
			letter.style.top = layout.height * 0.5f - Mathf.Cos(radians) * radius;
		}

		/// <summary>A slim diamond: the north half in the element's colour, the south half the same at a third.</summary>
		private static void DrawNeedle(MeshGenerationContext context)
		{
			VisualElement element = context.visualElement;
			Rect rect = element.contentRect;
			if (rect.width <= 0.0f || rect.height <= 0.0f)
			{
				return;
			}
			float middleX = rect.width * 0.5f;
			float middleY = rect.height * 0.5f;
			Color north = element.resolvedStyle.color;
			Painter2D painter = context.painter2D;

			painter.fillColor = north;
			painter.BeginPath();
			painter.MoveTo(new Vector2(middleX, 0.0f));
			painter.LineTo(new Vector2(rect.width, middleY));
			painter.LineTo(new Vector2(0.0f, middleY));
			painter.ClosePath();
			painter.Fill();

			painter.fillColor = new Color(north.r, north.g, north.b, north.a * 0.35f);
			painter.BeginPath();
			painter.MoveTo(new Vector2(0.0f, middleY));
			painter.LineTo(new Vector2(rect.width, middleY));
			painter.LineTo(new Vector2(middleX, rect.height));
			painter.ClosePath();
			painter.Fill();
		}
	}
}

using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The landmarks on a settlement's streets (2026-10-19): a city's cathedral, 22 m across and 39 m deep, found no frontage deep
	/// enough behind a radial city's square in small and medium cities, and so was never laid there; a landmark deeper than any
	/// frontage leaves it is now tried turned a quarter, its long side to the street, when it found no place square to one.
	/// </summary>
	[TestFixture]
	public class LandmarkFrontageTests
	{
		[SetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		/// <summary>The request PropsFeature builds for a template at a size and seed (its landmarks flagged), and the slot of each item.</summary>
		private static PointOfInterestLayoutRequest Request(PointOfInterestTemplateSpec spec, int size, int seed, DeterministicRNG random)
		{
			PointOfInterestKindRule rule = PointOfInterestRules.Default().RuleFor(spec.Kind);
			float min = rule != null ? rule.FootprintMin : 10f, max = rule != null ? rule.FootprintMax : 10f;
			float radius = Mathf.Lerp(min, max, size / 2f);
			var request = new PointOfInterestLayoutRequest
			{
				Layout = spec.Layout, Radius = radius, Gap = 1f, SpanHalf = radius, ShoreAhead = radius * PointOfInterestLayouts.Inset,
				Kind = spec.Kind, Seed = seed,
			};
			for (int s = 0; s < spec.Pieces.Count; s++)
			{
				PointOfInterestPieceSlot slot = spec.Pieces[s];
				string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
				PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role);
				int count = PropsFeature.CountFor(slot, size, true, random);
				PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, 0));
				bool station = role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower;
				request.Slots.Add(new PointOfInterestLayoutSlot
				{
					Role = role, Footprint = metrics.Footprint, Module = metrics.Module, Count = count,
					Walk = role == PointOfInterestSlotRole.Perimeter ? PropsFeature.PerimeterWalk(slot.Tag, style) : 0f,
					Join = station ? PropsFeature.StationJoin(slot.Tag, style, 0) : 0f,
					Landmark = PointOfInterestTemplateSpecs.IsLandmark(slot.Tag, spec.Kind),
				});
				if (PointOfInterestLayouts.Generates(role))
				{
					continue;
				}
				for (int k = 0; k < count; k++)
				{
					int pieceSeed = random.Next();
					PointOfInterestPieceMetrics piece = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, pieceSeed, 0f));
					request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = piece.Footprint });
				}
			}
			return request;
		}

		[Test]
		public void EveryCityAndCapitalLaysItsCathedral_AtEverySize()
		{
			var problems = new List<string>();
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				int slot = spec.Pieces.FindIndex(p => p.Tag == PointOfInterestTemplateSpecs.Cathedral && p.MinCount > 0);
				if (slot < 0)
				{
					continue;
				}
				foreach (int size in spec.Sizes())
				{
					const int seeds = 12;
					int laid = 0;
					for (int seed = 1; seed <= seeds; seed++)
					{
						var random = new DeterministicRNG(7000 + seed * 31 + size);
						PointOfInterestLayoutRequest request = Request(spec, size, seed, random);
						List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, random);
						laid += pieces.Exists(p => p.Slot == slot && p.Item >= 0) ? 1 : 0;
					}
					Console.WriteLine($"{spec.Name} size {size}: cathedral laid in {laid}/{seeds}");
					// Nearly always: a seed whose streets fall too short for it (a hill town's terraces) may leave it out.
					if (laid < seeds * 0.8f)
					{
						problems.Add($"{spec.Name} at size {size}: the cathedral was laid in {laid} of {seeds} seeds");
					}
				}
			}
			Assert.That(problems, Is.Empty, string.Join("\n", problems));
		}
	}
}

using System;
using System.Reflection;
using FishMMO.Shared;
using FishNet.Serializing;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that a shot lands under the crosshair whatever the camera's offset from the eye: the aim converges on the
	/// crosshair's point (<see cref="AimConvergence"/>), the camera's heading travels beside it for movement
	/// (<see cref="CharacterReplicateData.HeadingOffset"/>), and a projectile leaves the hand at the cast's hit point
	/// (<see cref="AbilityObject.SpawnerAimDirection"/>).
	/// </summary>
	/// <remarks>
	/// The geometry is the over-the-shoulder camera's: the eye at the character's head, the camera 2.5 m behind it, half
	/// a metre to the right and a little lower. Before convergence the aim was the camera's forward fired from the eye,
	/// a line parallel to the crosshair's that missed it by the camera's offset at every range.
	/// </remarks>
	[TestFixture]
	public class AimConvergenceTests
	{
		private static readonly Vector3 Eye = new Vector3(0f, 1.65f, 0f);
		private static readonly Vector3 ShoulderCamera = new Vector3(0.55f, 1.55f, -2.5f);
		private static readonly Vector3 Forward = Vector3.forward;

		/// <summary>How far the camera sits behind the eye along its own forward: where the trace begins.</summary>
		private const float CameraBehindEye = 2.5f;

		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			MethodInfo register = typeof(CharacterReplicateDataDeltaSerializer).GetMethod("RegisterSerializers",
				BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			LogAssert.IsNotNull(register, "CharacterReplicateDataDeltaSerializer must expose RegisterSerializers.");
			register.Invoke(null, null);
		}

		/// <summary>Where a ray from <paramref name="origin"/> along <paramref name="direction"/> crosses the plane z = <paramref name="z"/>.</summary>
		private static Vector3 AtDepth(Vector3 origin, Vector3 direction, float z)
		{
			float t = (z - origin.z) / direction.z;
			return origin + direction * t;
		}

		[TestCase(4f)]
		[TestCase(10f)]
		[TestCase(40f)]
		public void TheConvergedAim_LandsOnTheCrosshairPoint_AtAnyRange(float wallDepth)
		{
			Vector3 crosshairPoint = AtDepth(ShoulderCamera, Forward, wallDepth);
			float hitBeyondStart = wallDepth - ShoulderCamera.z - CameraBehindEye;

			Vector3 apex = AimConvergence.Apex(ShoulderCamera, Forward, Eye, hitBeyondStart);
			Vector3 aim = AimConvergence.Direction(Eye, apex, Forward);

			Vector3 landed = AtDepth(Eye, aim, wallDepth);
			LogAssert.IsTrue((landed - crosshairPoint).magnitude < 1e-4f,
				$"A shot from the eye along the converged aim must land on the crosshair's point at {wallDepth} m; " +
				$"it landed {(landed - crosshairPoint).magnitude} m away.");

			// The old aim (the camera's forward, fired from the eye) is the parallel line that never meets it.
			Vector3 parallel = AtDepth(Eye, Forward, wallDepth);
			LogAssert.IsTrue((parallel - crosshairPoint).magnitude > 0.5f,
				"The camera's forward fired from the eye misses the crosshair by the camera's offset: this is the defect.");
		}

		[Test]
		public void NothingUnderTheCrosshair_AimsAtTheFarPoint()
		{
			Vector3 apex = AimConvergence.Apex(ShoulderCamera, Forward, Eye, float.PositiveInfinity);
			LogAssert.AreEqual(CameraBehindEye + AimConvergence.FarDistance, apex.z - ShoulderCamera.z,
				"With nothing hit the apex is the far distance beyond the eye's level.");
		}

		[Test]
		public void SomethingAtTheElbow_IsHeldToTheMinimumConvergence()
		{
			Vector3 apex = AimConvergence.Apex(ShoulderCamera, Forward, Eye, 0.05f);
			LogAssert.AreEqual(CameraBehindEye + AimConvergence.MinConvergenceDistance, apex.z - ShoulderCamera.z,
				"A crosshair resting on something right beside the character must not swing the aim sideways.");

			Vector3 aim = AimConvergence.Direction(Eye, apex, Forward);
			float sideways = Vector3.Angle(Forward, aim);
			LogAssert.IsTrue(sideways < 20f, $"At the nearest apex the aim may turn at most ~15 degrees; it turned {sideways}.");
		}

		[Test]
		public void TheTraceStartsLevelWithTheEye_OrAtTheCameraInFirstPerson()
		{
			LogAssert.AreEqual(CameraBehindEye, AimConvergence.RayStart(ShoulderCamera, Forward, Eye),
				"Nothing between the camera and the character may be the apex.");
			LogAssert.AreEqual(0f, AimConvergence.RayStart(Eye, Forward, Eye),
				"In first person the camera is at the eye and the trace starts there.");
		}

		/// <summary>Where the shoulder camera's character is drawn: its follow point, on the shoulder camera's left.</summary>
		private static readonly Vector3 Drawn = new Vector3(0f, 1.35f, 0f);

		[TestCase(0.5f)]
		[TestCase(10f)]
		[TestCase(float.PositiveInfinity)]
		public void InThirdPerson_TheBodyFacesTheCamerasHeading_AsBefore(float hitDistance)
		{
			Vector3 centredCamera = new Vector3(0f, 1.55f, -6f);
			Vector3 point = AimConvergence.BodyPoint(centredCamera, Forward, Eye, hitDistance, Drawn);
			Vector3 heading = AimConvergence.BodyHeading(Drawn, point, Forward);
			LogAssert.IsTrue(Vector3.Angle(heading, Forward) < 1e-3f,
				"A character drawn on the crosshair's line already faces the crosshair: nothing may change in third person.");
		}

		[Test]
		public void OverTheShoulder_TheBodyFacesTheCrosshairsPoint()
		{
			const float WallDepth = 40f;
			Vector3 crosshairPoint = AtDepth(ShoulderCamera, Forward, WallDepth);
			Vector3 point = AimConvergence.BodyPoint(ShoulderCamera, Forward, Eye, WallDepth, Drawn);
			Vector3 heading = AimConvergence.BodyHeading(Drawn, point, Forward);

			Vector3 along = AtDepth(new Vector3(Drawn.x, 0f, Drawn.z), heading, WallDepth);
			LogAssert.IsTrue(Mathf.Abs(along.x - crosshairPoint.x) < 1e-3f,
				"Walking the body's heading must arrive at the crosshair's point: camera, character and shot form one V.");
		}

		[Test]
		public void OverTheShoulder_SomethingClose_TurnsTheBodyOnlyAFewDegrees()
		{
			Vector3 point = AimConvergence.BodyPoint(ShoulderCamera, Forward, Eye, 0.5f, Drawn);
			Vector3 heading = AimConvergence.BodyHeading(Drawn, point, Forward);
			float turn = Vector3.Angle(heading, Forward);
			LogAssert.IsTrue(turn > 4f && turn < 6f,
				$"Held to {AimConvergence.BodyMinConvergenceDistance} m the shoulder's half metre turns the body ~5 degrees; it turned {turn}.");
		}

		[Test]
		public void TheHeadingOffset_IsTheYawFromTheAimToTheCamera()
		{
			Vector3 aim = Quaternion.Euler(0f, -10f, 0f) * Forward;
			float offset = AimConvergence.HeadingOffsetDegrees(aim, Forward);
			LogAssert.IsTrue(Mathf.Abs(offset - 10f) < 1e-3f, $"Expected +10 degrees from the aim to the camera, got {offset}.");

			LogAssert.AreEqual(0f, AimConvergence.HeadingOffsetDegrees(Vector3.up, Forward),
				"Looking straight up there is no heading to offset.");
		}

		[Test]
		public void TheHeadingOffset_IsBoundedOnTheWire()
		{
			LogAssert.AreEqual((short)40, AimConvergence.EncodeHeadingOffset(10f), "Quarter degrees.");
			LogAssert.AreEqual((short)0, AimConvergence.EncodeHeadingOffset(float.NaN), "NaN is no offset.");
			LogAssert.AreEqual(170f, AimConvergence.DecodeHeadingOffset(AimConvergence.EncodeHeadingOffset(170f)),
				"The whole circle fits: an aim whose heading points backwards still gives the body its own.");
			LogAssert.AreEqual(AimConvergence.MaxHeadingOffsetDegrees,
				AimConvergence.DecodeHeadingOffset(AimConvergence.EncodeHeadingOffset(400f)), "Encoding holds to the bound.");
			LogAssert.AreEqual(-AimConvergence.MaxHeadingOffsetDegrees, AimConvergence.DecodeHeadingOffset(short.MinValue),
				"A value from a modified client is held to the bound when decoded.");
		}

		[Test]
		public void TheMovementRotation_HasTheCamerasHeadingAndTheAimsPitch()
		{
			Quaternion aimRotation = Quaternion.LookRotation(Quaternion.Euler(-20f, -10f, 0f) * Forward);
			Quaternion movement = AimConvergence.MovementRotation(aimRotation, AimConvergence.EncodeHeadingOffset(10f));

			Vector3 heading = Vector3.ProjectOnPlane(movement * Vector3.forward, Vector3.up).normalized;
			LogAssert.IsTrue(Vector3.Angle(heading, Forward) < 0.01f, "Movement must take the camera's heading.");

			float aimPitch = Vector3.Angle(aimRotation * Vector3.forward, Vector3.up);
			float movementPitch = Vector3.Angle(movement * Vector3.forward, Vector3.up);
			LogAssert.IsTrue(Mathf.Abs(aimPitch - movementPitch) < 0.01f, "Turning about world up must leave the pitch alone.");

			LogAssert.AreEqual(aimRotation, AimConvergence.MovementRotation(aimRotation, 0), "No offset is exactly the aim.");
		}

		[Test]
		public void TheHeadingOffset_RoundTripsWithTheAim_EvenWhenOnlyTheOffsetChanges()
		{
			CharacterReplicateData prev = default;
			prev.AimDirection = AimDirectionCompression.Quantize(Forward);
			prev.HeadingOffset = 250;

			CharacterReplicateData next = prev;
			next.HeadingOffset = -731;

			Writer writer = new Writer();
			LogAssert.IsTrue(writer.WriteDelta(prev, next, DeltaSerializerOption.Unset),
				"A change to the offset alone must still be sent.");
			const int Sentinel = 0x51DE;
			writer.WriteInt32(Sentinel);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterReplicateData result = reader.ReadDelta(prev);
			LogAssert.AreEqual(next.HeadingOffset, result.HeadingOffset, "The offset must survive the delta.");
			LogAssert.AreEqual(AimDirectionCompression.Encode(next.AimDirection), AimDirectionCompression.Encode(result.AimDirection),
				"The aim must be unchanged by a forced, zero difference.");
			LogAssert.AreEqual(Sentinel, reader.ReadInt32(), "The reader must land exactly where the writer finished.");

			Writer full = new Writer();
			full.WriteCharacterReplicateData(next);
			full.WriteInt32(Sentinel);
			Reader fullReader = new Reader(full.GetArraySegment(), null);
			LogAssert.AreEqual(next.HeadingOffset, fullReader.ReadCharacterReplicateData().HeadingOffset,
				"The offset must survive the full serializer.");
			LogAssert.AreEqual(Sentinel, fullReader.ReadInt32(), "The full reader must land exactly where the writer finished.");
		}

		[Test]
		public void AnUnchangedInput_StillSendsNothing()
		{
			CharacterReplicateData input = default;
			input.AimDirection = AimDirectionCompression.Quantize(Forward);
			input.HeadingOffset = 123;

			Writer writer = new Writer();
			LogAssert.IsFalse(writer.WriteDelta(input, input, DeltaSerializerOption.Unset),
				"An identical input must still collapse to nothing on the wire.");
		}

		[Test]
		public void AHeldInput_IsNotARepeat_IfOnlyTheHeadingOffsetChanged()
		{
			CharacterReplicateData a = default;
			CharacterReplicateData b = default;
			b.HeadingOffset = 1;
			LogAssert.IsFalse(CharacterPredictionController.SameHeldInput(a, b),
				"The server runs a held input in place of the owner's; one that steers differently is not the same input.");
		}

		[TestCase(2f)]
		[TestCase(5f)]
		[TestCase(12.3f)]
		[TestCase(40f)]
		[TestCase(249f)]
		public void TheConvergenceDistance_FitsAByte_ToWithinAPercentOrSo(float metres)
		{
			byte code = AimConvergence.EncodeDistance(metres);
			LogAssert.IsTrue(code != 0, "Zero means 'not given' and is never produced for a real distance.");
			float decoded = AimConvergence.DecodeDistance(code);
			LogAssert.IsTrue(Mathf.Abs(decoded - metres) / metres < 0.0126f,
				$"{metres} m came back as {decoded} m: more than the 1.25% a log step allows.");
		}

		[Test]
		public void NoConvergenceDistance_IsZero_AndDecodesToNothing()
		{
			LogAssert.AreEqual(0f, AimConvergence.DecodeDistance(0), "The code 0 is 'not given'.");
			LogAssert.AreEqual((byte)1, AimConvergence.EncodeDistance(0.01f), "Nearer than the scale starts is its first step.");
			LogAssert.AreEqual((byte)255, AimConvergence.EncodeDistance(100000f), "Farther than the scale ends is its last.");
		}

		/// <summary>The distance from <paramref name="point"/> to the line through <paramref name="origin"/> along <paramref name="direction"/>.</summary>
		private static float DistanceToLine(Vector3 point, Vector3 origin, Vector3 direction)
		{
			Vector3 to = point - origin;
			return (to - direction * Vector3.Dot(to, direction)).magnitude;
		}

		/// <summary>What the owner sends and both peers rebuild, for a camera whose crosshair rests on a wall <paramref name="wallDepth"/> away.</summary>
		private static void RebuildLaunchLine(Vector3 camera, float wallDepth, out Vector3 origin, out Vector3 direction)
		{
			Vector3 crosshairPoint = AtDepth(camera, Forward, wallDepth);
			Vector3 launchPoint = camera + Forward * AimConvergence.RayStart(camera, Forward, Eye);
			Vector3 aim = AimDirectionCompression.Quantize((crosshairPoint - Eye).normalized);
			AimConvergence.AimLaunch launch = AimConvergence.EncodeLaunch(Eye, launchPoint, aim, (crosshairPoint - Eye).magnitude);
			AimConvergence.LaunchLine(Eye, aim, launch, out origin, out direction);
		}

		[TestCase(4f)]
		[TestCase(10f)]
		[TestCase(40f)]
		public void TheLaunchLine_IsTheCrosshairsLine_AtEveryRange_OverTheShoulder(float wallDepth)
		{
			RebuildLaunchLine(ShoulderCamera, wallDepth, out Vector3 origin, out Vector3 direction);

			// A shot led ahead of a moving target passes SHORT of what the crosshair rests on: it has to be on the line there too.
			foreach (float fraction in new[] { 0.25f, 0.5f, 0.75f, 1f })
			{
				Vector3 onCrosshairLine = AtDepth(ShoulderCamera, Forward, wallDepth * fraction);
				float off = DistanceToLine(onCrosshairLine, origin, direction);
				LogAssert.IsTrue(off < 0.03f,
					$"{fraction:P0} of the way to a wall {wallDepth} m off, the projectile's line is {off} m from the crosshair's; it must follow it.");
			}

			// What it replaces, for the record: from a hand 0.65 m under the eye turned toward the same point, halfway there.
			Vector3 hand = new Vector3(0f, 1f, 0.5f);
			Vector3 crosshairPoint = AtDepth(ShoulderCamera, Forward, wallDepth);
			Vector3 fromHand = AtDepth(hand, (crosshairPoint - hand).normalized, wallDepth * 0.5f);
			LogAssert.IsTrue((fromHand - AtDepth(ShoulderCamera, Forward, wallDepth * 0.5f)).magnitude > 0.25f,
				"A projectile flown from the hand at the crosshair's point is well off the crosshair's line short of it: the defect.");
		}

		[Test]
		public void TheLaunchLine_IsTheCrosshairsLine_InThirdPersonToo()
		{
			Vector3 centredCamera = new Vector3(0f, 1.55f, -6f);
			RebuildLaunchLine(centredCamera, 20f, out Vector3 origin, out Vector3 direction);
			foreach (float depth in new[] { 5f, 10f, 20f })
			{
				LogAssert.IsTrue(DistanceToLine(AtDepth(centredCamera, Forward, depth), origin, direction) < 0.03f,
					$"At {depth} m the projectile's line must be on the crosshair's.");
			}
		}

		[Test]
		public void WithoutALaunchLine_AProjectileFliesFromTheEyeAlongTheAim()
		{
			// An NPC, which has no camera, and a tick the owner did not expect to spawn on.
			Vector3 aim = (new Vector3(1f, -0.2f, 3f)).normalized;
			AimConvergence.LaunchLine(Eye, aim, default, out Vector3 origin, out Vector3 direction);
			LogAssert.AreEqual(Eye, origin, "No launch line: from the eye.");
			LogAssert.IsTrue(Vector3.Angle(direction, aim) < 1e-3f, "Along the aim, so it passes through the point aimed at.");
		}

		[Test]
		public void TheLaunchLinesStart_IsHeldWithinAMetreOfTheEye_WhateverIsSent()
		{
			AimConvergence.LaunchLine(Eye, Forward, new AimConvergence.AimLaunch(AimConvergence.EncodeDistance(20f), 127, 127),
				out Vector3 origin, out _);
			LogAssert.IsTrue((origin - Eye).magnitude <= AimConvergence.MaxOriginOffset + 1e-4f,
				$"A launch line may start at most {AimConvergence.MaxOriginOffset} m from the eye; this one started {(origin - Eye).magnitude} m away.");
		}

		[Test]
		public void APointTooNearToAimThrough_LeavesTheLineOnTheAim()
		{
			// The offset is half a metre to the side and the point aimed at only half a metre ahead: a line through both would fly sideways.
			AimConvergence.LaunchLine(Eye, Forward, new AimConvergence.AimLaunch(AimConvergence.EncodeDistance(0.5f), 50, 0),
				out _, out Vector3 direction);
			LogAssert.IsTrue(Vector3.Angle(direction, Forward) < 1e-3f, "Too near to aim through: the aim itself is used.");
		}

		[Test]
		public void TheRareFields_AreOneShot_AndGoneOnTheNextInput()
		{
			CharacterReplicateData idle = default;
			idle.AimDirection = AimDirectionCompression.Quantize(Forward);

			CharacterReplicateData claim = idle;
			claim.ConvergenceDistance = AimConvergence.EncodeDistance(12f);
			claim.AimOriginRight = 55;
			claim.AimOriginUp = -10;
			claim.HitClaimVictim = 4321;
			claim.HitClaimSpawnAge = 37;
			claim.HitClaimObject = 2;

			Writer writer = new Writer();
			LogAssert.IsTrue(writer.WriteDelta(idle, claim, DeltaSerializerOption.Unset), "An input with a claim must be sent.");
			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterReplicateData read = reader.ReadDelta(idle);
			LogAssert.AreEqual(claim.ConvergenceDistance, read.ConvergenceDistance, "The distance must arrive.");
			LogAssert.AreEqual(claim.AimOriginRight, read.AimOriginRight, "The launch line's start, right.");
			LogAssert.AreEqual(claim.AimOriginUp, read.AimOriginUp, "The launch line's start, up.");
			LogAssert.AreEqual(claim.HitClaimVictim, read.HitClaimVictim, "The victim must arrive.");
			LogAssert.AreEqual(claim.HitClaimSpawnAge, read.HitClaimSpawnAge, "The cast's age must arrive.");
			LogAssert.AreEqual(claim.HitClaimObject, read.HitClaimObject, "The object's index must arrive.");
			LogAssert.AreEqual(0, reader.Remaining, "Nothing may be left unread.");

			// The next input carries none. That alone has to reach the reader, or it would run the claim a second time.
			Writer next = new Writer();
			LogAssert.IsTrue(next.WriteDelta(claim, idle, DeltaSerializerOption.Unset),
				"An input that drops a one-shot field must be sent even when nothing else changed.");
			Reader nextReader = new Reader(next.GetArraySegment(), null);
			CharacterReplicateData cleared = nextReader.ReadDelta(claim);
			LogAssert.AreEqual(0, cleared.HitClaimVictim, "A claim must not survive into the next input.");
			LogAssert.AreEqual((byte)0, cleared.ConvergenceDistance, "Nor the distance.");
			LogAssert.AreEqual((sbyte)0, cleared.AimOriginRight, "Nor the launch line's start.");
			LogAssert.AreEqual((sbyte)0, cleared.AimOriginUp, "Nor the launch line's start.");
			LogAssert.AreEqual(1, next.GetArraySegment().Count, "Clearing them costs the flags byte and nothing else.");
		}

		[Test]
		public void TheFullForm_CarriesEveryField_AndARareByteWhenThereIsNothingRare()
		{
			CharacterReplicateData value = default;
			value.AimDirection = AimDirectionCompression.Quantize(Forward);
			value.MoveAxisForward = MoveAxisCompression.Quantize(1f);
			value.HeadingOffset = -17;

			Writer plain = new Writer();
			plain.WriteCharacterReplicateData(value);
			int plainLength = plain.GetArraySegment().Count;

			value.EquipmentRequest = 3;
			value.EquipmentIndex = 12;
			value.ConvergenceDistance = 200;
			value.AimOriginRight = -42;
			value.AimOriginUp = 17;
			value.HitClaimVictim = 77;
			value.HitClaimSpawnAge = 300;
			value.HitClaimObject = 1;

			Writer full = new Writer();
			full.WriteCharacterReplicateData(value);
			Reader reader = new Reader(full.GetArraySegment(), null);
			CharacterReplicateData read = reader.ReadCharacterReplicateData();
			LogAssert.AreEqual(value.HeadingOffset, read.HeadingOffset, "Heading offset.");
			LogAssert.AreEqual(value.EquipmentRequest, read.EquipmentRequest, "Equipment request.");
			LogAssert.AreEqual(value.EquipmentIndex, read.EquipmentIndex, "Equipment index.");
			LogAssert.AreEqual(value.ConvergenceDistance, read.ConvergenceDistance, "Convergence distance.");
			LogAssert.AreEqual(value.AimOriginRight, read.AimOriginRight, "Launch line start, right.");
			LogAssert.AreEqual(value.AimOriginUp, read.AimOriginUp, "Launch line start, up.");
			LogAssert.AreEqual(value.HitClaimVictim, read.HitClaimVictim, "Claim victim.");
			LogAssert.AreEqual(value.HitClaimSpawnAge, read.HitClaimSpawnAge, "Claim age.");
			LogAssert.AreEqual(value.HitClaimObject, read.HitClaimObject, "Claim object.");
			LogAssert.AreEqual(0, reader.Remaining, "The full reader must land exactly where the writer finished.");
			LogAssert.IsTrue(full.GetArraySegment().Count > plainLength, "The rare fields cost bytes only when they are there.");
		}

		[Test]
		public void TheHeadingOffset_HasItsOwnBit_SoASteadyOneCostsNothingWhileTheAimTurns()
		{
			CharacterReplicateData prev = default;
			prev.AimDirection = AimDirectionCompression.Quantize(Forward);
			prev.HeadingOffset = 9;
			CharacterReplicateData turned = prev;
			turned.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, 1f, 0f) * Forward);

			Writer withSteadyOffset = new Writer();
			withSteadyOffset.WriteDelta(prev, turned, DeltaSerializerOption.Unset);

			CharacterReplicateData both = turned;
			both.HeadingOffset = 10;
			Writer withChangedOffset = new Writer();
			withChangedOffset.WriteDelta(prev, both, DeltaSerializerOption.Unset);

			LogAssert.AreEqual(withSteadyOffset.GetArraySegment().Count + 1, withChangedOffset.GetArraySegment().Count,
				"A turning aim with an unchanged heading offset must not pay for the offset.");

			Reader reader = new Reader(withChangedOffset.GetArraySegment(), null);
			CharacterReplicateData read = reader.ReadDelta(prev);
			LogAssert.AreEqual((short)10, read.HeadingOffset, "The changed offset must arrive.");
			LogAssert.AreEqual(AimDirectionCompression.Encode(both.AimDirection), AimDirectionCompression.Encode(read.AimDirection), "With the aim.");
		}

		[Test]
		public void AnInputThatAsksForSomething_IsNeverHeldAsARepeat()
		{
			CharacterReplicateData input = default;
			LogAssert.IsTrue(CharacterPredictionController.IsInertInput(input), "An empty input is inert.");

			CharacterReplicateData withDistance = input;
			withDistance.ConvergenceDistance = 50;
			LogAssert.IsFalse(CharacterPredictionController.IsInertInput(withDistance), "A convergence distance is a request.");
			LogAssert.IsFalse(CharacterPredictionController.SameHeldInput(input, withDistance), "And changes the input.");

			CharacterReplicateData withClaim = input;
			withClaim.HitClaimVictim = 5;
			LogAssert.IsFalse(CharacterPredictionController.IsInertInput(withClaim), "A hit claim is a request.");
			LogAssert.IsFalse(CharacterPredictionController.SameHeldInput(input, withClaim), "And changes the input.");
		}
	}

	/// <summary>
	/// Proofs for the server's check of a hit the owner claims: granted when the object's sweep passed where the victim
	/// could have been drawn, refused when it did not.
	/// </summary>
	[TestFixture]
	public class HitClaimGeometryTests
	{
		private const float TickDelta = 1f / 30f;
		private const float Radius = 0.4f;
		private const float Height = 1.8f;
		private const float FireballReach = 0.25f;

		private static float GapTo(Vector3 victimFeet)
		{
			// A fireball's step: 0.4 m along +z at chest height.
			return HitClaimGeometry.Gap(new Vector3(0f, 1.2f, 9.8f), new Vector3(0f, 1.2f, 10.2f), FireballReach,
				victimFeet, Vector3.up, Radius, Height, out _, out _);
		}

		[Test]
		public void ASweepThroughTheVictim_HasNoGap()
		{
			LogAssert.AreEqual(0f, GapTo(new Vector3(0f, 0f, 10f)), "A sweep through the body touches it.");
			LogAssert.AreEqual(0f, GapTo(new Vector3(0.6f, 0f, 10f)), "As does one whose shape reaches its side.");
		}

		[Test]
		public void TheGap_IsTheClearDistanceBetweenShapeAndBody()
		{
			float gap = GapTo(new Vector3(1f, 0f, 10f));
			LogAssert.IsTrue(Mathf.Abs(gap - (1f - Radius - FireballReach)) < 1e-4f, $"Expected 0.35 m clear, got {gap}.");
		}

		[Test]
		public void AStandingVictim_GetsAlmostNoTolerance_AndARunningOneAFewTicksOfItsRun()
		{
			LogAssert.AreEqual(HitClaimGeometry.BaseTolerance, HitClaimGeometry.Tolerance(0f, TickDelta),
				"Nothing about a still character can have been drawn elsewhere.");
			float running = HitClaimGeometry.Tolerance(6f, TickDelta);
			LogAssert.IsTrue(Mathf.Abs(running - (0.1f + 6f * TickDelta * 2.5f)) < 1e-5f, $"Expected 0.6 m at a run, got {running}.");
			LogAssert.AreEqual(HitClaimGeometry.MaximumTolerance, HitClaimGeometry.Tolerance(1000f, TickDelta), "And never more than the cap.");
		}

		[Test]
		public void AClaim_IsGrantedInsideTheTolerance_AndRefusedOutsideIt()
		{
			// The owner drew a running victim half a step from where the history has it: the kind of difference a claim is for.
			float tolerance = HitClaimGeometry.Tolerance(6f, TickDelta);
			LogAssert.IsTrue(GapTo(new Vector3(1.1f, 0f, 10f)) <= tolerance, "0.45 m clear of a running victim is within what it could have been drawn at.");
			LogAssert.IsTrue(GapTo(new Vector3(2.5f, 0f, 10f)) > tolerance, "1.85 m clear is not: the claim is refused.");
			LogAssert.IsTrue(GapTo(new Vector3(1.1f, 0f, 10f)) > HitClaimGeometry.Tolerance(0f, TickDelta),
				"And the same miss on a STANDING victim is refused: it was never drawn there.");
		}

		[Test]
		public void SegmentDistance_HandlesPointsParallelsAndCrossings()
		{
			float crossing = HitClaimGeometry.SegmentDistance(new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
				new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f), out _, out _);
			LogAssert.IsTrue(crossing < 1e-5f, "Crossing segments touch.");

			float parallel = HitClaimGeometry.SegmentDistance(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 2f),
				new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 2f), out _, out _);
			LogAssert.IsTrue(Mathf.Abs(parallel - 1f) < 1e-5f, "Parallel segments are their separation apart.");

			// A stationary object (a punch) is a point; a victim's axis may be one too.
			float point = HitClaimGeometry.SegmentDistance(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0f),
				new Vector3(3f, 0f, 0f), new Vector3(3f, 2f, 0f), out _, out _);
			LogAssert.IsTrue(Mathf.Abs(point - 3f) < 1e-5f, "A point against a segment is the distance to its nearest point.");
		}
	}

	/// <summary>
	/// Proofs for what a client draws of an ability object: the same closed form the simulation steps through, at a
	/// fractional tick; the owner's blend from the drawn hand; and an observed copy's age on the playback clock.
	/// </summary>
	[TestFixture]
	public class AbilityPresentationTests
	{
		private const float TickDelta = 1f / 30f;
		private static readonly Vector3 Start = new Vector3(4f, 1.3f, -2f);

		[Test]
		public void TheDrawnPath_PassesThroughEverySimulatedPosition_AndGlidesBetweenThem()
		{
			Quaternion heading = Quaternion.Euler(0f, 35f, 0f);
			const float Speed = 12f;

			Vector3 atTick3 = AbilityObject.PresentedPosition(Start, heading, Vector3.forward, Speed, 3d, TickDelta);
			Vector3 atTick4 = AbilityObject.PresentedPosition(Start, heading, Vector3.forward, Speed, 4d, TickDelta);
			Vector3 simulatedAt3 = Start + heading * Vector3.forward * (Speed * (3 * TickDelta));
			LogAssert.IsTrue((atTick3 - simulatedAt3).magnitude < 1e-5f,
				"At a whole tick the visuals are exactly where the simulation is: the same closed form.");

			Vector3 halfway = AbilityObject.PresentedPosition(Start, heading, Vector3.forward, Speed, 3.5d, TickDelta);
			LogAssert.IsTrue((halfway - (atTick3 + atTick4) * 0.5f).magnitude < 1e-5f,
				"Half a tick on, they are halfway to the next simulated position, not still on the last.");
			LogAssert.IsTrue(Mathf.Abs((atTick4 - atTick3).magnitude - Speed * TickDelta) < 1e-5f, "A tick's travel is speed times the tick.");
		}

		[Test]
		public void TheHandBlend_StartsWhole_EndsAtNothing_AndNeverTurnsBack()
		{
			LogAssert.AreEqual(1f, AbilityObject.HandBlendWeight(0f), "The visuals start at the drawn hand.");
			LogAssert.AreEqual(0f, AbilityObject.HandBlendWeight(AbilityObject.HandBlendSeconds), "And are on the real path when the blend ends.");
			LogAssert.AreEqual(0f, AbilityObject.HandBlendWeight(10f), "And stay there.");

			float previous = 1f;
			for (int i = 1; i <= 20; ++i)
			{
				float weight = AbilityObject.HandBlendWeight(AbilityObject.HandBlendSeconds * i / 20f);
				LogAssert.IsTrue(weight <= previous + 1e-6f, "The blend only ever moves toward the real path.");
				previous = weight;
			}
		}

		[Test]
		public void AnObservedCopy_IsAsOldAsThePlaybackClockSays()
		{
			const uint SpawnedOn = 1000u;

			// The message arrives while the clock is still short of the spawn tick: the caster has not been DRAWN casting yet.
			double before = AbilityObject.ObservedElapsed(997.4d, SpawnedOn, -1d);
			LogAssert.IsTrue(before < 0d, "Before the clock reaches the spawn tick the copy has not begun, and is not drawn.");

			double started = AbilityObject.ObservedElapsed(1000.25d, SpawnedOn, before);
			LogAssert.IsTrue(System.Math.Abs(started - 0.25d) < 1e-9, "A quarter of a tick past the spawn tick it is a quarter of a tick old.");

			double later = AbilityObject.ObservedElapsed(1006.5d, SpawnedOn, started);
			LogAssert.IsTrue(System.Math.Abs(later - 6.5d) < 1e-9, "Its age is the clock less the tick the server spawned it on, whenever the message came.");
		}

		[Test]
		public void AnObservedCopy_Waits_WhenTheClockStepsBack()
		{
			// A late packet grows the playback delay at once: the clock steps back, and every drawn character stalls.
			double held = AbilityObject.ObservedElapsed(1004.0d, 1000u, 6.5d);
			LogAssert.AreEqual(6.5d, held, "A projectile must wait for the clock, never fly backward.");
		}
	}

	/// <summary>
	/// Proofs that a projectile starts where the SIMULATION has the hand on every peer, not where the owner draws it.
	/// </summary>
	[TestFixture]
	public class SimulatedSpawnerTests
	{
		private GameObject root;

		[TearDown]
		public void TearDown()
		{
			UnityEngine.Object.DestroyImmediate(root);
		}

		[Test]
		public void AHandOnASmoothedBody_IsReadWhereTheRootIs()
		{
			root = new GameObject("SimulatedSpawnerRoot");
			root.transform.SetPositionAndRotation(new Vector3(100f, 0f, 50f), Quaternion.Euler(0f, 30f, 0f));
			Transform graphical = new GameObject("Smoothing").transform;
			graphical.SetParent(root.transform, false);
			Transform hand = new GameObject("AbilitySpawnPoint").transform;
			hand.SetParent(graphical, false);
			hand.localPosition = new Vector3(0f, 0.8f, 0.5f);

			Vector3 expected = hand.position;
			Quaternion expectedRotation = hand.rotation;

			// The owner's smoother holds the drawn body two ticks behind the root and still turning.
			graphical.SetPositionAndRotation(root.transform.position - root.transform.forward * 0.4f, Quaternion.Euler(0f, 22f, 0f));

			AbilityObject.SimulatedSpawnerPose(graphical, hand, out Vector3 position, out Quaternion rotation);
			LogAssert.IsTrue((position - expected).magnitude < 1e-4f,
				$"The spawn must be the simulated hand; it was {(position - expected).magnitude} m from it.");
			LogAssert.IsTrue(Quaternion.Angle(rotation, expectedRotation) < 1e-3f, "And face the way the simulated hand faces.");
		}

		[Test]
		public void AtRest_TheHandIsUnchanged()
		{
			root = new GameObject("SimulatedSpawnerRoot");
			Transform graphical = new GameObject("Smoothing").transform;
			graphical.SetParent(root.transform, false);
			Transform hand = new GameObject("AbilitySpawnPoint").transform;
			hand.SetParent(graphical, false);
			hand.localPosition = new Vector3(0.2f, 1f, 0.3f);

			AbilityObject.SimulatedSpawnerPose(graphical, hand, out Vector3 position, out _);
			LogAssert.IsTrue((position - hand.position).magnitude < 1e-5f, "The server's body is at rest: nothing may move.");
		}

		/// <summary>
		/// The conversion puts the graphical object back at an IDENTITY local pose. Every prefab with a graphical object
		/// has to author it that way, or the server and the owner would both spawn from a hand the body never had.
		/// </summary>
		[Test]
		public void EveryGraphicalObject_RestsAtIdentityOnItsParent()
		{
			int checkedPrefabs = 0;
			foreach (string path in System.IO.Directory.GetFiles("Assets/Prefabs", "*.prefab", System.IO.SearchOption.AllDirectories))
			{
				string text = System.IO.File.ReadAllText(path);
				System.Text.RegularExpressions.Match graphical = System.Text.RegularExpressions.Regex.Match(text, @"_graphicalObject: \{fileID: (\d+)");
				if (!graphical.Success || graphical.Groups[1].Value == "0")
				{
					continue;
				}
				System.Text.RegularExpressions.Match block = System.Text.RegularExpressions.Regex.Match(text,
					@"--- !u!4 &" + graphical.Groups[1].Value + @"\r?\n(.*?)(?=\r?\n--- )", System.Text.RegularExpressions.RegexOptions.Singleline);
				LogAssert.IsTrue(block.Success, $"{path}: the graphical object's transform was not found.");
				string body = block.Groups[1].Value;
				LogAssert.IsTrue(body.Contains("m_LocalPosition: {x: 0, y: 0, z: 0}"), $"{path}: the graphical object must rest at the origin.");
				LogAssert.IsTrue(body.Contains("m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}"), $"{path}: the graphical object must rest unrotated.");
				checkedPrefabs++;
			}
			LogAssert.IsTrue(checkedPrefabs > 0, "No prefab with a graphical object was found; the scan is wrong.");
		}
	}

	/// <summary>
	/// Proofs for the camera styles: each keeps its own zoom, first person sits at the eye, and the shoulder is kept out
	/// of walls.
	/// </summary>
	[TestFixture]
	public class CameraStyleTests
	{
		private GameObject cameraObject;
		private GameObject feet;
		private GameObject followPoint;
		private KCCCamera camera;

		[SetUp]
		public void SetUp()
		{
			feet = new GameObject("CameraStyleTestFeet");
			feet.transform.position = new Vector3(7000f, 400f, 7000f);
			followPoint = new GameObject("CameraStyleTestFollowPoint");
			followPoint.transform.SetParent(feet.transform, false);
			followPoint.transform.localPosition = new Vector3(0f, 1.35f, 0f);

			cameraObject = new GameObject("CameraStyleTestCamera");
			camera = cameraObject.AddComponent<KCCCamera>();
			// EditMode does not call Awake on an added component (see CameraRestTests).
			typeof(KCCCamera).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(camera, null);
			camera.ObstructionLayers = 0;
			camera.SetFollowTransform(followPoint.transform);
		}

		[TearDown]
		public void TearDown()
		{
			UnityEngine.Object.DestroyImmediate(cameraObject);
			UnityEngine.Object.DestroyImmediate(feet);
		}

		[Test]
		public void EachStyle_KeepsItsOwnZoom()
		{
			camera.TargetDistance = 8f;
			camera.SetStyle(KCCCameraStyle.OverShoulder);
			LogAssert.AreEqual(camera.ShoulderDefaultDistance, camera.TargetDistance, "The shoulder starts at its own distance.");

			camera.TargetDistance = 3f;
			camera.SetStyle(KCCCameraStyle.FirstPerson);
			LogAssert.AreEqual(0f, camera.TargetDistance, "First person has no boom.");

			camera.SetStyle(KCCCameraStyle.ThirdPerson);
			LogAssert.AreEqual(8f, camera.TargetDistance, "Third person comes back to the zoom it was left at.");
			camera.SetStyle(KCCCameraStyle.OverShoulder);
			LogAssert.AreEqual(3f, camera.TargetDistance, "And so does the shoulder.");
		}

		[Test]
		public void FirstPerson_SitsAtTheEye()
		{
			camera.FallbackEyeHeight = 1.6f;
			camera.SetStyle(KCCCameraStyle.FirstPerson);
			camera.UpdateWithInput(0.05f, 0f, Vector3.zero);

			Vector3 eye = feet.transform.position + Vector3.up * 1.6f;
			LogAssert.IsTrue((camera.transform.position - eye).magnitude < 1e-4f,
				$"The first-person view must be at the eye; it is {(camera.transform.position - eye).magnitude} m away.");
		}

		[Test]
		public void OverShoulder_SitsBehindAndToTheRight()
		{
			camera.SetStyle(KCCCameraStyle.OverShoulder);
			for (int i = 0; i < 20; i++)
			{
				camera.UpdateWithInput(0.05f, 0f, Vector3.zero);
			}

			Vector3 local = camera.transform.InverseTransformPoint(followPoint.transform.position);
			LogAssert.IsTrue(local.x < -0.5f, $"The character must be left of the view's centre; local x {local.x}.");
			LogAssert.IsTrue(local.z > 2f, $"The character must be ahead of the view; local z {local.z}.");
		}
	}
}

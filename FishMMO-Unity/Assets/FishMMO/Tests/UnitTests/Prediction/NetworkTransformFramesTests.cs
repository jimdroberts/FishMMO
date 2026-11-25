using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Serializing;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The frame codec behind NetworkTransformFrames: one tick's NetworkTransform updates for one observer in one message,
	/// updates sorted by object, each preceded by the object id's distance from the previous one and a bit for "the object's
	/// first NetworkTransform" (component index 255 here); any other component index follows as a byte. No per-update
	/// length: the reader stands in for the NetworkTransform, which reads exactly its own bytes. Reached by reflection, as
	/// FishNet keeps it internal.
	/// </summary>
	public class NetworkTransformFramesTests
	{
		private static readonly Type Frames = Type.GetType("FishNet.Component.Transforming.NetworkTransformFrames, FishNet.Runtime");

		private static List<byte[]> Encode(List<(int ObjectId, byte ComponentIndex, byte[] Payload)> updates)
		{
			MethodInfo encode = null;
			foreach (MethodInfo m in Frames.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
			{
				if (m.Name == "EncodeForTests" && m.GetParameters()[0].ParameterType == typeof(IList<(int, byte, byte[])>))
				{
					encode = m;
				}
			}
			return (List<byte[]>)encode.Invoke(null, new object[] { updates });
		}

		private static List<byte[]> EncodeSlotted(List<(int ObjectId, byte ComponentIndex, int Slot, byte[] Payload)> updates)
		{
			MethodInfo encode = null;
			foreach (MethodInfo m in Frames.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
			{
				if (m.Name == "EncodeForTests" && m.GetParameters()[0].ParameterType == typeof(IList<(int, byte, int, byte[])>))
				{
					encode = m;
				}
			}
			return (List<byte[]>)encode.Invoke(null, new object[] { updates });
		}

		/// <summary>Parses one frame message; <paramref name="known"/> is the payload length of each object the reader knows.</summary>
		private static List<(int ObjectId, byte ComponentIndex, byte[] Payload)> Parse(byte[] message, Dictionary<(int, byte), int> known, out int trailingReadPosition)
			=> Parse(message, known, null, out trailingReadPosition);

		/// <summary>As above; <paramref name="slots"/> maps the slots the reader was given to their object ids.</summary>
		private static List<(int ObjectId, byte ComponentIndex, byte[] Payload)> Parse(byte[] message, Dictionary<(int, byte), int> known, Dictionary<int, int> slots, out int trailingReadPosition)
		{
			var read = new List<(int, byte, byte[])>();
			Func<int, byte, ArraySegment<byte>, int> testRead = (objectId, componentIndex, rest) =>
			{
				if (!known.TryGetValue((objectId, componentIndex), out int length))
				{
					return 0;
				}
				byte[] payload = new byte[length];
				Buffer.BlockCopy(rest.Array, rest.Offset, payload, 0, length);
				read.Add((objectId, componentIndex, payload));
				return length;
			};
			// A byte after the frame: the parser must stop exactly at the frame's end, never read into the next message.
			byte[] buffer = new byte[message.Length + 1];
			Buffer.BlockCopy(message, 0, buffer, 0, message.Length);
			buffer[message.Length] = 0xEE;
			PooledReader reader = ReaderPool.Retrieve(new ArraySegment<byte>(buffer), null);
			reader.Skip(2); // PacketId.TransformFrame
			MethodInfo parse = null;
			foreach (MethodInfo m in Frames.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
			{
				if (m.Name == "Parse" && m.GetParameters().Length == 4)
				{
					parse = m;
				}
			}
			Func<int, int> slotObject = slot => slots != null && slots.TryGetValue(slot, out int id) ? id : -1;
			parse.Invoke(null, new object[] { reader, null, testRead, slotObject });
			trailingReadPosition = reader.Position;
			reader.Store();
			return read;
		}

		private static byte[] Payload(int seed, int length)
		{
			byte[] bytes = new byte[length];
			for (int i = 0; i < length; i++)
			{
				bytes[i] = (byte)(seed * 31 + i * 7);
			}
			return bytes;
		}

		[Test]
		public void FrameRoundTrips_SortedByObject_WithEveryPayloadByte()
		{
			var updates = new List<(int, byte, byte[])>
			{
				(5000, 3, Payload(1, 5)),
				(12, 3, Payload(2, 4)),
				(13, 3, Payload(3, 7)),
				(13, 4, Payload(4, 2)),
				(4000, 255, Payload(5, 6)),
			};
			List<byte[]> messages = Encode(updates);
			Assert.AreEqual(1, messages.Count, "Small updates fit one frame.");

			var known = new Dictionary<(int, byte), int>();
			foreach ((int id, byte component, byte[] payload) in updates)
			{
				known[(id, component)] = payload.Length;
			}
			List<(int ObjectId, byte ComponentIndex, byte[] Payload)> read = Parse(messages[0], known, out int end);

			Assert.AreEqual(messages[0].Length, end, "The parser stops exactly at the frame's end.");
			int[] order = { 12, 13, 13, 4000, 5000 };
			Assert.AreEqual(order.Length, read.Count);
			for (int i = 0; i < order.Length; i++)
			{
				Assert.AreEqual(order[i], read[i].ObjectId, "Updates arrive sorted by object.");
			}
			foreach ((int id, byte component, byte[] payload) in updates)
			{
				CollectionAssert.AreEqual(payload, read.Find(r => r.ObjectId == id && r.ComponentIndex == component).Payload);
			}
		}

		[Test]
		public void AnUpdateCostsItsPayloadPlusAboutOneByte()
		{
			// 30 objects' first NetworkTransforms, 5 byte updates, ids scattered as FishNet reuses them (gaps of up to 5,000).
			var updates = new List<(int, byte, byte[])>();
			for (int i = 0; i < 30; i++)
			{
				updates.Add((100 + i * i * 6, 255, Payload(i, 5)));
			}
			byte[] frame = Encode(updates)[0];
			// Packet id 2 + frame length 2, then at most 2 header bytes per update (an id gap below 8,192), no component byte.
			Assert.LessOrEqual(frame.Length, 4 + 30 * (5 + 2), $"30 updates of 5 bytes took {frame.Length} bytes.");
			Assert.Less(frame.Length, 30 * (5 + 3), "Less than the 3 bytes of link and length each update cost as an RPC.");
		}

		[Test]
		public void LargeTicks_SplitIntoFramesThatEachStandAlone()
		{
			var updates = new List<(int, byte, byte[])>();
			var known = new Dictionary<(int, byte), int>();
			for (int i = 0; i < 400; i++)
			{
				updates.Add((i * 50, 1, Payload(i, 6)));
				known[(i * 50, 1)] = 6;
			}
			List<byte[]> messages = Encode(updates);
			Assert.Greater(messages.Count, 1, "2,800 bytes of updates need several frames.");
			int total = 0;
			foreach (byte[] message in messages)
			{
				Assert.LessOrEqual(message.Length, 900 + 8, "A frame never needs splitting by the transport.");
				total += Parse(message, known, out int end).Count;
				Assert.AreEqual(message.Length, end);
			}
			Assert.AreEqual(400, total, "Every update arrives in exactly one frame.");
		}

		[Test]
		public void AnUnknownObject_DropsTheRestOfItsFrameOnly()
		{
			var updates = new List<(int, byte, byte[])>
			{
				(10, 0, Payload(1, 4)),
				(20, 0, Payload(2, 4)),
				(30, 0, Payload(3, 4)),
			};
			byte[] frame = Encode(updates)[0];
			var known = new Dictionary<(int, byte), int> { [(10, 0)] = 4, [(30, 0)] = 4 };

			List<(int ObjectId, byte ComponentIndex, byte[] Payload)> read = Parse(frame, known, out int end);

			Assert.AreEqual(1, read.Count, "Updates before the unknown object are read.");
			Assert.AreEqual(10, read[0].ObjectId);
			Assert.AreEqual(frame.Length, end, "The reader still ends at the frame's end, so the next message is read normally.");
		}

		/// <summary>
		/// Slotted updates come first, sorted by slot, each named by its slot's distance from the previous one: one byte for
		/// a crowd an observer numbers 0, 1, 2... The rest follow by object id as before, and both read back exactly.
		/// </summary>
		[Test]
		public void SlottedUpdates_ComeFirst_AndCostAboutOneByteEach()
		{
			var updates = new List<(int, byte, int, byte[])>
			{
				(30000, 255, 7, Payload(1, 5)),
				(812, 255, 2, Payload(2, 4)),
				(15000, 3, -1, Payload(3, 6)),
				(20000, 255, 0, Payload(4, 5)),
				(9000, 255, -1, Payload(5, 3)),
			};
			byte[] frame = EncodeSlotted(updates)[0];
			var known = new Dictionary<(int, byte), int>();
			foreach ((int id, byte component, int slot, byte[] payload) in updates)
			{
				known[(id, component)] = payload.Length;
			}
			var slots = new Dictionary<int, int> { [7] = 30000, [2] = 812, [0] = 20000 };
			List<(int ObjectId, byte ComponentIndex, byte[] Payload)> read = Parse(frame, known, slots, out int end);

			Assert.AreEqual(frame.Length, end, "The parser stops exactly at the frame's end.");
			int[] order = { 20000, 812, 30000, 9000, 15000 };
			Assert.AreEqual(order.Length, read.Count, "Every update is read.");
			for (int i = 0; i < order.Length; i++)
			{
				Assert.AreEqual(order[i], read[i].ObjectId, "Slotted by slot (0, 2, 7), then the rest by object id.");
			}
			foreach ((int id, byte component, int slot, byte[] payload) in updates)
			{
				CollectionAssert.AreEqual(payload, read.Find(r => r.ObjectId == id && r.ComponentIndex == component).Payload);
			}

			// 30 slotted updates of 5 bytes: one header byte each.
			var crowd = new List<(int, byte, int, byte[])>();
			for (int i = 0; i < 30; i++)
			{
				crowd.Add((100 + i * i * 600, 255, i * 3, Payload(i, 5)));
			}
			byte[] crowdFrame = EncodeSlotted(crowd)[0];
			// Packet id 2 + frame length 2 + slotted count 1, then 1 header byte per update.
			Assert.LessOrEqual(crowdFrame.Length, 5 + 30 * (5 + 1), $"30 slotted updates of 5 bytes took {crowdFrame.Length} bytes.");
		}

		/// <summary>A slot the reader does not know (its object not spawned here yet, or already gone) drops the rest of the frame only.</summary>
		[Test]
		public void AnUnknownSlot_DropsTheRestOfItsFrameOnly()
		{
			var updates = new List<(int, byte, int, byte[])>
			{
				(10, 255, 0, Payload(1, 4)),
				(20, 255, 1, Payload(2, 4)),
				(30, 255, -1, Payload(3, 4)),
			};
			byte[] frame = EncodeSlotted(updates)[0];
			var known = new Dictionary<(int, byte), int> { [(10, 255)] = 4, [(20, 255)] = 4, [(30, 255)] = 4 };
			List<(int ObjectId, byte ComponentIndex, byte[] Payload)> read = Parse(frame, known, new Dictionary<int, int> { [0] = 10 }, out int end);

			Assert.AreEqual(1, read.Count, "The update before the unknown slot is read; nothing after it is guessed at.");
			Assert.AreEqual(10, read[0].ObjectId);
			Assert.AreEqual(frame.Length, end, "The reader still ends at the frame's end.");
		}

		/// <summary>
		/// Each observer numbers the objects it sees from 0, lowest free first, the same object always the same number, and a
		/// freed number is not handed out again until the quarantine has passed, so an update in flight for the object that
		/// left can never be read as the one that replaced it.
		/// </summary>
		[Test]
		public void Slots_AreLowestFirst_StablePerObject_AndQuarantinedWhenFreed()
		{
			MethodInfo assign = Frames.GetMethod("AssignSlot", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo release = Frames.GetMethod("ReleaseSlot", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo clear = Frames.GetMethod("ClearSlots", BindingFlags.Static | BindingFlags.NonPublic);
			clear.Invoke(null, null);
			try
			{
				var viewer = new FishNet.Connection.NetworkConnection { ClientId = 71 };
				var other = new FishNet.Connection.NetworkConnection { ClientId = 72 };
				int Assign(FishNet.Connection.NetworkConnection c, int id) => (int)assign.Invoke(null, new object[] { c, id });

				Assert.AreEqual(0, Assign(viewer, 5000), "The first object an observer sees is 0...");
				Assert.AreEqual(1, Assign(viewer, 17), "...the next 1, whatever their object ids.");
				Assert.AreEqual(0, Assign(viewer, 5000), "The same object keeps its number.");
				Assert.AreEqual(0, Assign(other, 17), "Each observer numbers its own view.");

				release.Invoke(null, new object[] { viewer, 5000 });
				Assert.AreEqual(2, Assign(viewer, 9999), "A freed number is quarantined: the next object gets a new one.");
				Assert.AreEqual(3, Assign(viewer, 5000), "The object that left gets a fresh number when it returns.");
			}
			finally
			{
				clear.Invoke(null, null);
			}
		}
	}
}

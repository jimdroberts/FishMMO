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
			MethodInfo encode = Frames.GetMethod("EncodeForTests", BindingFlags.Static | BindingFlags.NonPublic);
			return (List<byte[]>)encode.Invoke(null, new object[] { updates });
		}

		/// <summary>Parses one frame message; <paramref name="known"/> is the payload length of each object the reader knows.</summary>
		private static List<(int ObjectId, byte ComponentIndex, byte[] Payload)> Parse(byte[] message, Dictionary<(int, byte), int> known, out int trailingReadPosition)
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
				if (m.Name == "Parse" && m.GetParameters().Length == 3)
				{
					parse = m;
				}
			}
			parse.Invoke(null, new object[] { reader, null, testRead });
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
	}
}

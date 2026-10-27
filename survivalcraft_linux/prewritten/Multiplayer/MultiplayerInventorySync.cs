using System;
using System.Collections.Generic;
using Engine;
using Engine.Serialization;
using GameEntitySystem;

namespace Game;

/// <summary>One block-entity inventory as sent over the wire.</summary>
public sealed class BlockInventoryRecord
{
	public int NetId;

	public int ActiveSlot = -1;

	public int[] Values = Array.Empty<int>();

	public int[] Counts = Array.Empty<int>();
}

/// <summary>
/// Mirrors the contents of block-entity inventories (chests, furnaces,
/// dispensers, ...) in both directions.
///
/// The client renders those inventories itself, so a player can open a chest
/// and move items around on the replica. Polling the local inventory state and
/// forwarding the differences keeps the host (and therefore every other
/// client) in sync. Player inventories are handled separately by
/// <see cref="MultiplayerMessage.PlayerState"/>.
/// </summary>
public static class MultiplayerInventorySync
{
	private sealed class Snapshot
	{
		public int ActiveSlot;

		public int[] Values;

		public int[] Counts;
	}

	private static readonly Dictionary<int, Snapshot> s_known = new Dictionary<int, Snapshot>();

	public static void Reset()
	{
		s_known.Clear();
	}

	/// <summary>Collects inventories that changed since the last call.</summary>
	public static void Collect(Project project, List<BlockInventoryRecord> records)
	{
		records.Clear();
		if (project == null)
		{
			return;
		}
		HashSet<int> present = new HashSet<int>();
		foreach (Entity entity in project.Entities)
		{
			if (entity.FindComponent<ComponentPlayer>(throwOnError: false) != null)
			{
				continue;
			}
			ComponentInventoryBase inventory = entity.FindComponent<ComponentInventoryBase>(throwOnError: false);
			if (inventory == null)
			{
				continue;
			}
			int netId = EntityNetRegistry.GetOrCreateId(entity);
			if (netId == 0)
			{
				continue;
			}
			present.Add(netId);
			int count = inventory.SlotsCount;
			int[] values = new int[count];
			int[] counts = new int[count];
			for (int i = 0; i < count; i++)
			{
				values[i] = inventory.GetSlotValue(i);
				counts[i] = inventory.GetSlotCount(i);
			}
			int activeSlot = inventory.ActiveSlotIndex;
			if (s_known.TryGetValue(netId, out Snapshot snapshot) && snapshot.ActiveSlot == activeSlot
				&& Equal(snapshot.Values, values) && Equal(snapshot.Counts, counts))
			{
				continue;
			}
			s_known[netId] = new Snapshot
			{
				ActiveSlot = activeSlot,
				Values = values,
				Counts = counts
			};
			records.Add(new BlockInventoryRecord
			{
				NetId = netId,
				ActiveSlot = activeSlot,
				Values = values,
				Counts = counts
			});
		}
		// Drop cached snapshots of entities that no longer exist; otherwise
		// every chest that was ever loaded stays referenced for the session.
		if (s_known.Count > present.Count)
		{
			List<int> stale = new List<int>();
			foreach (int key in s_known.Keys)
			{
				if (!present.Contains(key))
				{
					stale.Add(key);
				}
			}
			foreach (int item in stale)
			{
				s_known.Remove(item);
			}
		}
	}

	public static void Write(EngineBinaryWriter writer, List<BlockInventoryRecord> records)
	{
		writer.Write(records.Count);
		foreach (BlockInventoryRecord record in records)
		{
			writer.Write(record.NetId);
			writer.Write(record.ActiveSlot);
			writer.Write(record.Values.Length);
			for (int i = 0; i < record.Values.Length; i++)
			{
				writer.Write(record.Values[i]);
				writer.Write(record.Counts[i]);
			}
		}
	}

	public static void ReadAndApply(Project project, EngineBinaryReader reader)
	{
		List<BlockInventoryRecord> list = new List<BlockInventoryRecord>();
		Read(reader, list);
		Apply(project, list);
	}

	/// <summary>Parses a <see cref="MultiplayerMessage.BlockInventory"/> payload.</summary>
	public static void Read(EngineBinaryReader reader, List<BlockInventoryRecord> records)
	{
		records.Clear();
		int num = reader.ReadInt32();
		for (int i = 0; i < num; i++)
		{
			BlockInventoryRecord blockInventoryRecord = new BlockInventoryRecord();
			blockInventoryRecord.NetId = reader.ReadInt32();
			blockInventoryRecord.ActiveSlot = reader.ReadInt32();
			int num2 = reader.ReadInt32();
			blockInventoryRecord.Values = new int[num2];
			blockInventoryRecord.Counts = new int[num2];
			for (int j = 0; j < num2; j++)
			{
				blockInventoryRecord.Values[j] = reader.ReadInt32();
				blockInventoryRecord.Counts[j] = reader.ReadInt32();
			}
			records.Add(blockInventoryRecord);
		}
	}

	public static void Apply(Project project, List<BlockInventoryRecord> records)
	{
		if (project == null)
		{
			return;
		}
		foreach (BlockInventoryRecord record in records)
		{
			Entity entity = EntityNetRegistry.Find(record.NetId);
			if (entity == null)
			{
				continue;
			}
			ComponentInventoryBase inventory = entity.FindComponent<ComponentInventoryBase>(throwOnError: false);
			if (inventory == null)
			{
				continue;
			}
			int num = MathUtils.Min(record.Values.Length, inventory.SlotsCount);
			for (int i = 0; i < num; i++)
			{
				inventory.SetSlotNet(i, record.Values[i], record.Counts[i]);
			}
			if (record.ActiveSlot >= -1 && record.ActiveSlot < inventory.SlotsCount)
			{
				inventory.ActiveSlotIndex = record.ActiveSlot;
			}
			// Remember the applied state so it is not echoed straight back.
			s_known[record.NetId] = new Snapshot
			{
				ActiveSlot = record.ActiveSlot,
				Values = record.Values,
				Counts = record.Counts
			};
		}
	}

	private static bool Equal(int[] a, int[] b)
	{
		if (a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i])
			{
				return false;
			}
		}
		return true;
	}
}

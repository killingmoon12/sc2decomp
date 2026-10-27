using System;
using System.Collections.Generic;
using Engine;
using Engine.Serialization;

namespace Game;

/// <summary>
/// Replicates <see cref="SubsystemPickables"/> (dropped item stacks). Pickables
/// are not entities in Survivalcraft, so they need their own message instead of
/// riding on the normal entity replication.
/// </summary>
public static class MultiplayerPickables
{
	/// <summary>Largest accepted pickable list; guards against corrupt frames.</summary>
	public const int MaxPickables = 8192;

	private static int s_nextId;

	public static void Reset()
	{
		s_nextId = 0;
	}

	private static int EnsureNetId(Pickable pickable)
	{
		if (pickable.NetId == 0)
		{
			pickable.NetId = ++s_nextId;
		}
		return pickable.NetId;
	}

	/// <summary>
	/// Writes the pickables near <paramref name="center"/>. The host must call
	/// this from the game thread.
	/// </summary>
	public static void Write(EngineBinaryWriter writer, SubsystemPickables pickables, Vector3 center, float maxDistanceSquared)
	{
		List<Pickable> list = new List<Pickable>();
		foreach (Pickable pickable in pickables.Pickables)
		{
			if (pickable.ToRemove)
			{
				continue;
			}
			if (Vector3.DistanceSquared(pickable.Position, center) > maxDistanceSquared)
			{
				continue;
			}
			EnsureNetId(pickable);
			list.Add(pickable);
		}
		writer.Write(list.Count);
		foreach (Pickable item in list)
		{
			writer.Write(item.NetId);
			writer.Write(item.Value);
			writer.Write(item.Count);
			writer.Write(item.Position);
			writer.Write(item.CreationTime);
			writer.Write(item.StuckMatrix.HasValue);
			if (item.StuckMatrix.HasValue)
			{
				writer.Write(item.StuckMatrix.Value);
			}
		}
	}

	public static List<Pickable> Read(EngineBinaryReader reader)
	{
		int num = reader.ReadInt32();
		if (num < 0 || num > MaxPickables)
		{
			throw new InvalidOperationException("Invalid pickable count " + num + ".");
		}
		List<Pickable> list = new List<Pickable>(num);
		for (int i = 0; i < num; i++)
		{
			Pickable pickable = new Pickable();
			pickable.NetId = reader.ReadInt32();
			pickable.Value = reader.ReadInt32();
			pickable.Count = reader.ReadInt32();
			pickable.Position = reader.ReadVector3();
			pickable.CreationTime = reader.ReadDouble();
			if (reader.ReadBoolean())
			{
				pickable.StuckMatrix = reader.ReadMatrix();
			}
			list.Add(pickable);
		}
		return list;
	}

	public static void ReadAndApply(EngineBinaryReader reader, SubsystemPickables pickables)
	{
		pickables.ApplyNetPickables(Read(reader));
	}
}

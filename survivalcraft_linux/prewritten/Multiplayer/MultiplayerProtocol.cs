using System;
using Engine;
using Engine.Serialization;

namespace Game;

/// <summary>
/// Message types exchanged between the host and its clients.
/// Frames are length prefixed and carry a single message type byte.
/// </summary>
public enum MultiplayerMessage : byte
{
	Hello = 1,
	Welcome = 2,
	Snapshot = 3,
	Ready = 4,
	Input = 5,
	CellChange = 6,
	EntityUpdate = 7,
	EntityAdd = 8,
	EntityRemove = 9,
	PlayerState = 10,
	TimeSync = 11,
	ChatMessage = 12,
	PlayerJoined = 13,
	PlayerLeft = 14,
	Disconnect = 15,
	Ping = 16,
	Pong = 17,
	BlockInventory = 18,
	PlayerInventory = 19,
	MiningState = 20,
	Pickables = 21
}

public static class MultiplayerProtocol
{
	/// <summary>Bumped whenever the wire format changes incompatibly.</summary>
	public const int Version = 2;

	/// <summary>TCP port the host listens on by default.</summary>
	public const int DefaultPort = 27020;

	/// <summary>UDP port used for LAN discovery queries.</summary>
	public const int DiscoveryPort = 27021;

	public const string DiscoveryRequest = "SC2MP_DISCOVER_V1";

	public const string DiscoveryReplyPrefix = "SC2MP_HOST_V1|";

	/// <summary>Maximum accepted frame size (256 MiB). Guards against garbage input.</summary>
	public const int MaxFrameSize = 256 * 1024 * 1024;

	public static string MakeDiscoveryReply(string worldName, string hostName, int tcpPort, int players, int maxPlayers, int version)
	{
		return DiscoveryReplyPrefix + Sanitize(worldName) + "|" + Sanitize(hostName) + "|" + tcpPort + "|" + players + "|" + maxPlayers + "|" + version;
	}

	public static bool TryParseDiscoveryReply(string text, out MultiplayerGameInfo info)
	{
		info = null;
		if (text == null || !text.StartsWith(DiscoveryReplyPrefix, StringComparison.Ordinal))
		{
			return false;
		}
		string[] array = text.Substring(DiscoveryReplyPrefix.Length).Split('|');
		if (array.Length < 6
			|| !int.TryParse(array[2], out int tcpPort)
			|| !int.TryParse(array[3], out int players)
			|| !int.TryParse(array[4], out int maxPlayers)
			|| !int.TryParse(array[5], out int version))
		{
			return false;
		}
		info = new MultiplayerGameInfo
		{
			WorldName = array[0],
			HostName = array[1],
			Port = tcpPort,
			Players = players,
			MaxPlayers = maxPlayers,
			ProtocolVersion = version
		};
		return true;
	}

	private static string Sanitize(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "?";
		}
		return value.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
	}
}

/// <summary>Description of a discovered game on the local network.</summary>
public class MultiplayerGameInfo
{
	public string Address = string.Empty;

	public string WorldName = string.Empty;

	public string HostName = string.Empty;

	public int Port;

	public int Players;

	public int MaxPlayers;

	public int ProtocolVersion;

	public double LastSeenTime;

	public string DisplayName => WorldName + "  (" + Players + "/" + MaxPlayers + ")  " + Address;
}

/// <summary>Helpers shared by the host and the client when (de)serializing messages.</summary>
public static class MultiplayerSerialization
{
	public static void WriteRay(this EngineBinaryWriter writer, Ray3? ray)
	{
		writer.Write(ray.HasValue);
		if (ray.HasValue)
		{
			writer.Write(ray.Value.Position);
			writer.Write(ray.Value.Direction);
		}
	}

	public static Ray3? ReadRay(this EngineBinaryReader reader)
	{
		if (!reader.ReadBoolean())
		{
			return null;
		}
		Vector3 position = reader.ReadVector3();
		Vector3 direction = reader.ReadVector3();
		return new Ray3(position, direction);
	}

	public static void WriteQuaternion(this EngineBinaryWriter writer, Quaternion value)
	{
		writer.Write(value.X);
		writer.Write(value.Y);
		writer.Write(value.Z);
		writer.Write(value.W);
	}

	public static Quaternion ReadQuaternion(this EngineBinaryReader reader)
	{
		return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
	}

	// Bit layout of the compact action mask used on the wire.
	private const uint ActionJump = 1u;
	private const uint ActionToggleCreativeFly = 2u;
	private const uint ActionToggleCrouch = 4u;
	private const uint ActionToggleMount = 8u;
	private const uint ActionEditItem = 16u;
	private const uint ActionDrop = 32u;
	private const uint ActionToggleInventory = 64u;
	private const uint ActionToggleClothing = 128u;

	public static void WritePlayerInput(this EngineBinaryWriter writer, PlayerInput input)
	{
		writer.Write(input.Look);
		writer.Write(input.Move);
		writer.Write(input.CrouchMove);
		writer.Write(input.ScrollInventory);
		writer.Write(input.SelectInventorySlot.HasValue);
		if (input.SelectInventorySlot.HasValue)
		{
			writer.Write(input.SelectInventorySlot.Value);
		}
		uint num = 0u;
		if (input.Jump)
		{
			num |= ActionJump;
		}
		if (input.ToggleCreativeFly)
		{
			num |= ActionToggleCreativeFly;
		}
		if (input.ToggleCrouch)
		{
			num |= ActionToggleCrouch;
		}
		if (input.ToggleMount)
		{
			num |= ActionToggleMount;
		}
		if (input.EditItem)
		{
			num |= ActionEditItem;
		}
		if (input.Drop)
		{
			num |= ActionDrop;
		}
		if (input.ToggleInventory)
		{
			num |= ActionToggleInventory;
		}
		if (input.ToggleClothing)
		{
			num |= ActionToggleClothing;
		}
		writer.Write(num);
		writer.WriteRay(input.Dig);
		writer.WriteRay(input.Hit);
		writer.WriteRay(input.Aim);
		writer.WriteRay(input.Interact);
		writer.WriteRay(input.PickBlockType);
	}

	public static PlayerInput ReadPlayerInput(this EngineBinaryReader reader)
	{
		PlayerInput result = default(PlayerInput);
		result.Look = reader.ReadVector2();
		result.Move = reader.ReadVector3();
		result.CrouchMove = reader.ReadVector3();
		result.ScrollInventory = reader.ReadInt32();
		if (reader.ReadBoolean())
		{
			result.SelectInventorySlot = reader.ReadInt32();
		}
		uint num = reader.ReadUInt32();
		result.Jump = (num & ActionJump) != 0;
		result.ToggleCreativeFly = (num & ActionToggleCreativeFly) != 0;
		result.ToggleCrouch = (num & ActionToggleCrouch) != 0;
		result.ToggleMount = (num & ActionToggleMount) != 0;
		result.EditItem = (num & ActionEditItem) != 0;
		result.Drop = (num & ActionDrop) != 0;
		result.ToggleInventory = (num & ActionToggleInventory) != 0;
		result.ToggleClothing = (num & ActionToggleClothing) != 0;
		result.Dig = reader.ReadRay();
		result.Hit = reader.ReadRay();
		result.Aim = reader.ReadRay();
		result.Interact = reader.ReadRay();
		result.PickBlockType = reader.ReadRay();
		result.CameraLook = result.Look;
		result.CameraMove = result.Move;
		result.CameraCrouchMove = result.CrouchMove;
		return result;
	}

	/// <summary>
	/// Folds a freshly received input into one that has not been consumed by the
	/// host simulation yet. Continuous values come from the newest sample while
	/// one-shot actions are never lost.
	/// </summary>
	public static PlayerInput MergePlayerInput(PlayerInput previous, PlayerInput next)
	{
		next.Jump |= previous.Jump;
		next.ToggleCreativeFly |= previous.ToggleCreativeFly;
		next.ToggleCrouch |= previous.ToggleCrouch;
		next.ToggleMount |= previous.ToggleMount;
		next.EditItem |= previous.EditItem;
		next.Drop |= previous.Drop;
		next.ToggleInventory |= previous.ToggleInventory;
		next.ToggleClothing |= previous.ToggleClothing;
		if (!next.Hit.HasValue)
		{
			next.Hit = previous.Hit;
		}
		if (!next.Interact.HasValue)
		{
			next.Interact = previous.Interact;
		}
		if (!next.PickBlockType.HasValue)
		{
			next.PickBlockType = previous.PickBlockType;
		}
		if (next.ScrollInventory == 0)
		{
			next.ScrollInventory = previous.ScrollInventory;
		}
		if (!next.SelectInventorySlot.HasValue)
		{
			next.SelectInventorySlot = previous.SelectInventorySlot;
		}
		return next;
	}
}

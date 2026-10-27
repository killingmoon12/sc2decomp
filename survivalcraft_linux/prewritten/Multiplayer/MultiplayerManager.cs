using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Engine;
using GameEntitySystem;

namespace Game;

public enum MultiplayerRole
{
	Off,
	Host,
	Client
}

/// <summary>
/// Entry point and shared state for multiplayer (LAN and internet).
///
/// The host owns the simulation: it runs a normal single-player world and
/// streams the results to connected clients. A client is a thin replica: it
/// loads a snapshot of the host world and then applies the deltas it receives.
/// It never simulates the world itself; only the mouse look of the local player
/// is predicted locally so the camera stays responsive.
/// </summary>
public static class MultiplayerManager
{
	public const double InputSendInterval = 1.0 / 30.0;

	public const double EntitySyncInterval = 1.0 / 20.0;

	public const double PlayerStateInterval = 0.2;

	public const double TimeSyncInterval = 2.0;

	public const double InventorySyncInterval = 1.0;

	/// <summary>How often the host publishes the dig/animation state of players.</summary>
	public const double MiningStateInterval = 1.0 / 30.0;

	/// <summary>How often the host publishes dropped item stacks.</summary>
	public const double PickableSyncInterval = 0.1;

	/// <summary>
	/// A ready client that stops sending input for this long is considered gone.
	/// Clients send input every frame (30 Hz), so this only catches crashes and
	/// killed processes.
	/// </summary>
	public const double ClientInputTimeout = 10.0;

	public const int MaxPlayers = 8;

	/// <summary>
	/// File written into worlds imported by a client. Such worlds are only a
	/// cache of the host world; leftover copies from a crash are removed the
	/// next time a world list is shown.
	/// </summary>
	public const string ClientCacheMarkerFileName = "MultiplayerCache.txt";

	public const float PickableSyncDistanceSquared = 96f * 96f;

	private static MultiplayerServer s_server;

	private static MultiplayerClient s_client;

	private static bool s_includeRemotePlayersInSave;

	private static bool s_applyingRemoteCell;

	private static double s_lastStatusUpdateTime;

	private static string s_statusText = string.Empty;

	private static string s_hostName = "Player";

	private static Task<MultiplayerJoinResult> s_joinTask;

	private static string s_clientWorldDirectory;

	private static string s_worldToDelete;

	private static MultiplayerJoinResult s_joinResult;

	public static MultiplayerRole Role { get; private set; }

	public static bool IsHost => Role == MultiplayerRole.Host;

	public static bool IsClient => Role == MultiplayerRole.Client;

	public static bool IsClientReplica => Role == MultiplayerRole.Client;

	public static bool IsActive => Role != MultiplayerRole.Off;

	public static bool IsJoiningProject { get; private set; }

	public static int LocalPlayerIndex { get; private set; } = -1;

	public static MultiplayerServer Server => s_server;

	public static MultiplayerClient Client => s_client;

	public static string StatusText => s_statusText;

	/// <summary>
	/// While true, <see cref="SubsystemPlayers"/> writes remote players into the
	/// save file. Enabled only while a world snapshot is being produced for a
	/// joining client; normal saves must never bake remote avatars into the host
	/// world.
	/// </summary>
	public static bool IncludeRemotePlayersInSave => s_includeRemotePlayersInSave;

	public static string PlayerName
	{
		get
		{
			return s_hostName;
		}
		set
		{
			if (!string.IsNullOrEmpty(value))
			{
				s_hostName = value;
			}
		}
	}

	public static int ConnectedPlayerCount
	{
		get
		{
			if (IsHost && s_server != null)
			{
				return s_server.ConnectedPlayerCount;
			}
			if (IsClient && s_client != null)
			{
				return s_client.PlayerCount;
			}
			return 0;
		}
	}

	public static string DescribeStatus()
	{
		if (IsHost && s_server != null)
		{
			return "Hosting on port " + s_server.Port + " (" + s_server.ConnectedPlayerCount + "/" + MaxPlayers + " players)";
		}
		if (IsClient && s_client != null)
		{
			return "Connected to " + s_client.HostDescription + " as player " + (LocalPlayerIndex + 1);
		}
		return "Multiplayer is off.";
	}

	/// <summary>Host-facing explanation of how other players can connect.</summary>
	public static string DescribeHostingDetails()
	{
		if (!IsHost || s_server == null)
		{
			return DescribeStatus();
		}
		int port = s_server.Port;
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("Hosting on TCP port ").Append(port).Append(" (").Append(s_server.ConnectedPlayerCount).Append('/').Append(MaxPlayers).Append(" players).\n\n");
		stringBuilder.Append("Same network (LAN): connect to one of these addresses:\n");
		foreach (string localAddress in MultiplayerDiscovery.GetLocalAddresses())
		{
			stringBuilder.Append("  ").Append(localAddress).Append(':').Append(port).Append('\n');
		}
		stringBuilder.Append("\nInternet: forward TCP port ").Append(port)
			.Append(" in your router (and allow it in your firewall), then give other players your public IP address with port ")
			.Append(port).Append('.');
		stringBuilder.Append("\n\nDiscovery only works on the local network; on the internet use Direct Connect.");
		return stringBuilder.ToString();
	}

	public static string StartHosting()
	{
		if (IsActive)
		{
			return "Multiplayer is already running.";
		}
		if (GameManager.Project == null)
		{
			return "Load a world before opening it to the LAN.";
		}
		EntityNetRegistry.Reset();
		MultiplayerInventorySync.Reset();
		EntityNetRegistry.Enabled = true;
		// Remote avatars belong to a live session, not to the world. Keep them
		// out of normal saves (they are pulled in again for snapshots).
		EntityNetRegistry.SaveFilter = delegate(Entity entity)
		{
			return s_includeRemotePlayersInSave || !IsRemotePlayerEntity(entity);
		};
		try
		{
			s_server = new MultiplayerServer(GameManager.Project, MultiplayerProtocol.DefaultPort, s_hostName);
			s_server.Start();
			Role = MultiplayerRole.Host;
			MultiplayerDiscovery.StartAnnouncer(s_server);
			s_statusText = DescribeStatus();
			Log.Information("[Multiplayer] Hosting on port {0}", s_server.Port);
			return null;
		}
		catch (Exception ex)
		{
			EntityNetRegistry.Reset();
			s_server?.Dispose();
			s_server = null;
			Role = MultiplayerRole.Off;
			Log.Error("[Multiplayer] Could not host: " + ex);
			return "Could not open the world to the LAN: " + ex.Message;
		}
	}

	public static void StopHosting()
	{
		if (!IsHost)
		{
			return;
		}
		MultiplayerDiscovery.StopAnnouncer();
		try
		{
			s_server?.Stop();
		}
		catch (Exception ex)
		{
			Log.Error("[Multiplayer] Error while stopping host: " + ex);
		}
		s_server?.Dispose();
		s_server = null;
		Role = MultiplayerRole.Off;
		EntityNetRegistry.Reset();
		s_statusText = DescribeStatus();
		Log.Information("[Multiplayer] Stopped hosting.");
	}

	/// <summary>
	/// Parses a direct-connect target. Accepts "host", "host:port",
	/// "1.2.3.4:port" and "[ipv6]:port"; a port typed in the separate field is
	/// used when the address does not contain one.
	/// </summary>
	public static bool TryParseAddress(string address, string portText, out string host, out int port, out string error)
	{
		host = string.Empty;
		port = MultiplayerProtocol.DefaultPort;
		error = null;
		if (string.IsNullOrWhiteSpace(address))
		{
			error = "Type the address of the host.";
			return false;
		}
		string text = address.Trim();
		bool flag = false;
		if (text.StartsWith("["))
		{
			int num = text.IndexOf(']');
			if (num < 0)
			{
				error = "Invalid IPv6 address.";
				return false;
			}
			host = text.Substring(1, num - 1);
			string text2 = text.Substring(num + 1);
			if (text2.Length > 0)
			{
				if (!text2.StartsWith(":") || !int.TryParse(text2.Substring(1), out port))
				{
					error = "Invalid port after the IPv6 address.";
					return false;
				}
				flag = true;
			}
		}
		else
		{
			int num2 = text.LastIndexOf(':');
			if (num2 > 0 && text.IndexOf(':') == num2)
			{
				string text3 = text.Substring(num2 + 1);
				if (int.TryParse(text3, out int num3))
				{
					host = text.Substring(0, num2);
					port = num3;
					flag = true;
				}
				else
				{
					host = text;
				}
			}
			else
			{
				host = text;
			}
		}
		if (!flag && !string.IsNullOrWhiteSpace(portText))
		{
			if (!int.TryParse(portText.Trim(), out port))
			{
				error = "The port must be a number.";
				return false;
			}
		}
		if (string.IsNullOrEmpty(host))
		{
			error = "Type the address of the host.";
			return false;
		}
		if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
		{
			error = "\"" + host + "\" is not a valid host name or IP address.";
			return false;
		}
		if (port < 1 || port > 65535)
		{
			error = "The port must be between 1 and 65535.";
			return false;
		}
		return true;
	}

	public static void StartJoin(string address, int port, string playerName)
	{
		if (IsActive)
		{
			s_joinResult = MultiplayerJoinResult.Failed("Leave the current multiplayer game first.");
			return;
		}
		if (s_joinTask != null && !s_joinTask.IsCompleted)
		{
			return;
		}
		PlayerName = playerName;
		s_joinResult = null;
		// Clear anything left over from an earlier session before the
		// downloaded world registers its own network ids.
		EntityNetRegistry.Reset();
		MultiplayerInventorySync.Reset();
		s_joinTask = Task.Run(delegate
		{
			try
			{
				return MultiplayerClient.ConnectAndDownload(address, port, playerName);
			}
			catch (Exception ex)
			{
				Log.Error("[Multiplayer] Join failed: " + ex);
				return MultiplayerJoinResult.Failed(ex.Message);
			}
		});
	}

	/// <summary>
	/// Called from the join screen. Returns null while the download is still in
	/// progress, otherwise the finished result. A successful result means the
	/// world has been imported and the screen should switch to GameLoading.
	/// </summary>
	public static MultiplayerJoinResult PollJoin()
	{
		if (s_joinResult != null)
		{
			return s_joinResult;
		}
		if (s_joinTask == null || !s_joinTask.IsCompleted)
		{
			return null;
		}
		s_joinResult = s_joinTask.Result;
		s_joinTask = null;
		if (!s_joinResult.Success)
		{
			return s_joinResult;
		}
		IsJoiningProject = true;
		LocalPlayerIndex = s_joinResult.PlayerIndex;
		s_statusText = "Downloaded world, loading...";
		return s_joinResult;
	}

	public static bool IsJoining => s_joinTask != null && !s_joinTask.IsCompleted;

	public static void CancelJoin()
	{
		s_joinResult = null;
		s_joinTask = null;
		IsJoiningProject = false;
	}

	/// <summary>
	/// Hooked at the end of <see cref="GameManager.LoadProject"/>. When a join is
	/// pending this is the moment the freshly downloaded world is available, so
	/// the live session can be attached to it.
	/// </summary>
	public static void OnProjectLoaded()
	{
		if (!IsJoiningProject)
		{
			return;
		}
		if (GameManager.Project == null || s_joinResult == null)
		{
			IsJoiningProject = false;
			return;
		}
		IsJoiningProject = false;
		try
		{
			s_client = new MultiplayerClient(GameManager.Project, s_joinResult, s_hostName);
			Role = MultiplayerRole.Client;
			s_clientWorldDirectory = s_joinResult.WorldDirectory;
			s_client.Start();
			s_statusText = DescribeStatus();
			Log.Information("[Multiplayer] Joined game at {0}", s_client.HostDescription);
		}
		catch (Exception ex)
		{
			Role = MultiplayerRole.Off;
			Log.Error("[Multiplayer] Could not attach client session: " + ex);
			s_statusText = "Could not join: " + ex.Message;
		}
	}

	public static void OnProjectDisposing()
	{
		if (IsHost)
		{
			StopHosting();
		}
		else if (IsClient)
		{
			s_client?.Stop("Left the world.");
			s_client = null;
			Role = MultiplayerRole.Off;
			LocalPlayerIndex = -1;
			s_statusText = DescribeStatus();
			// The imported world was only ever a cache of the host world;
			// delete it so repeated joins do not fill the world list.
			if (!string.IsNullOrEmpty(s_clientWorldDirectory))
			{
				s_worldToDelete = s_clientWorldDirectory;
				s_clientWorldDirectory = null;
			}
		}
		MultiplayerInventorySync.Reset();
		IsJoiningProject = false;
		s_joinTask = null;
		s_joinResult = null;
		s_applyingRemoteCell = false;
	}

	/// <summary>Marks an imported world as a disposable client-side cache.</summary>
	public static void MarkClientWorldCache(string directoryName)
	{
		if (string.IsNullOrEmpty(directoryName))
		{
			return;
		}
		try
		{
			Storage.WriteAllText(Storage.CombinePaths(directoryName, ClientCacheMarkerFileName), "Survivalcraft 2 multiplayer client cache\n");
		}
		catch (Exception ex)
		{
			Log.Warning("[Multiplayer] Could not mark client world cache: " + ex.Message);
		}
	}

	public static bool IsClientWorldCache(string directoryName)
	{
		if (string.IsNullOrEmpty(directoryName))
		{
			return false;
		}
		try
		{
			return Storage.FileExists(Storage.CombinePaths(directoryName, ClientCacheMarkerFileName));
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>
	/// Removes worlds that were imported by a client and left behind by a crash
	/// or an abrupt exit. Safe to call whenever no session is running; it is
	/// invoked before the world list is built so the list never shows duplicates.
	/// </summary>
	public static void SweepStaleClientWorlds()
	{
		if (IsActive || IsJoining)
		{
			return;
		}
		IEnumerable<string> enumerable;
		try
		{
			enumerable = Storage.ListDirectoryNames("data:/Worlds");
		}
		catch (Exception)
		{
			return;
		}
		bool flag = false;
		foreach (string item in enumerable)
		{
			string text = Storage.CombinePaths("data:/Worlds", item);
			if (!IsClientWorldCache(text))
			{
				continue;
			}
			if (text == s_clientWorldDirectory)
			{
				continue;
			}
			try
			{
				WorldsManager.DeleteWorld(text);
				flag = true;
				Log.Information("[Multiplayer] Removed stale client world cache " + text);
			}
			catch (Exception ex)
			{
				Log.Warning("[Multiplayer] Could not remove stale client world cache " + text + ": " + ex.Message);
			}
			if (text == s_worldToDelete)
			{
				s_worldToDelete = null;
			}
		}
		if (flag)
		{
			try
			{
				WorldsManager.UpdateWorldsList();
			}
			catch (Exception ex2)
			{
				Log.Warning("[Multiplayer] Could not refresh world list: " + ex2.Message);
			}
		}
	}

	public static void Update()
	{
		if (IsHost)
		{
			s_server?.Update();
		}
		else if (IsClient)
		{
			s_client?.Update();
		}
		if (s_worldToDelete != null && !IsActive)
		{
			string directoryName = s_worldToDelete;
			s_worldToDelete = null;
			try
			{
				WorldsManager.DeleteWorld(directoryName);
				WorldsManager.UpdateWorldsList();
				Log.Information("[Multiplayer] Deleted cached client world " + directoryName);
			}
			catch (Exception ex3)
			{
				Log.Warning("[Multiplayer] Could not delete cached client world: " + ex3.Message);
			}
		}
		double realTime = Time.RealTime;
		if (realTime - s_lastStatusUpdateTime > 0.5)
		{
			s_lastStatusUpdateTime = realTime;
			s_statusText = DescribeStatus();
		}
	}

	/// <summary>
	/// Called from <see cref="SubsystemUpdate"/> for every updateable. On a client
	/// all world simulation is suppressed: entity state comes from the host.
	///
	/// Instead of enumerating every simulation system (which would silently
	/// miss new ones) we keep an allowlist of the systems that only produce
	/// visuals, audio or UI, and skip everything else.
	/// </summary>
	public static bool SkipUpdate(IUpdateable updateable)
	{
		if (!IsClientReplica || updateable == null)
		{
			return false;
		}
		if (updateable is Subsystem)
		{
			return !(updateable is SubsystemTerrain
				|| updateable is SubsystemGameInfo
				|| updateable is SubsystemGameWidgets
				|| updateable is SubsystemPlayers
				|| updateable is SubsystemAudio
				|| updateable is SubsystemSky
				|| updateable is SubsystemSeasons
				|| updateable is SubsystemAnimatedTextures
				|| updateable is SubsystemAmbientSounds
				|| updateable is SubsystemParticles);
		}
		// Local player input/UI must keep running so the client can send its
		// input and draw its HUD.
		if (updateable is ComponentInput || updateable is ComponentGui
			|| updateable is ComponentScreenOverlays || updateable is ComponentAimingSights
			|| updateable is ComponentBlockHighlight || updateable is ComponentCreatureModel
			|| updateable is ComponentFirstPersonModel || updateable is ComponentVrHandsModel)
		{
			return false;
		}
		// Everything else (physics, AI, damage, inventories, needs, animation
		// state of non-visual components) is authored by the host.
		return true;
	}

	/// <summary>True when the entity is the avatar of a player driven over the network.</summary>
	public static bool IsRemotePlayerEntity(Entity entity)
	{
		ComponentPlayer componentPlayer = entity?.FindComponent<ComponentPlayer>(throwOnError: false);
		return componentPlayer != null && componentPlayer.PlayerData != null && componentPlayer.PlayerData.IsRemote;
	}

	/// <summary>True when the given player is simulated by the host instead of locally.</summary>
	public static bool IsRemotePlayer(PlayerData playerData)
	{
		return playerData != null && (playerData.IsRemote || (IsClientReplica && playerData.PlayerIndex != LocalPlayerIndex));
	}

	public static bool IsLocalClientPlayer(PlayerData playerData)
	{
		return IsClientReplica && playerData != null && playerData.PlayerIndex == LocalPlayerIndex;
	}

	/// <summary>Applies the host-authoritative look state of a remote creature.</summary>
	public static void SetRemoteLook(ComponentLocomotion locomotion, Vector2 lookAngles)
	{
		if (locomotion != null)
		{
			locomotion.SetLookAngles(lookAngles);
		}
	}

	/// <summary>
	/// Client-only mouse look prediction. The host remains authoritative for the
	/// player position, but rotation is handled locally so the camera does not
	/// lag behind the mouse.
	/// </summary>
	public static void ApplyLocalLook(ComponentPlayer componentPlayer, Vector2 look, float dt)
	{
		if (!IsClientReplica || componentPlayer == null)
		{
			return;
		}
		ComponentLocomotion componentLocomotion = componentPlayer.ComponentLocomotion;
		ComponentBody componentBody = componentPlayer.ComponentBody;
		float num = SettingsManager.FlipVerticalAxis ? (-1f) : 1f;
		float yaw = MultiplayerEntities.GetYaw(componentBody.Rotation);
		yaw -= componentLocomotion.TurnSpeed * look.X * dt;
		componentBody.Rotation = MultiplayerEntities.RotationFromYaw(yaw);
		componentLocomotion.AddLookAngles(new Vector2(componentLocomotion.LookSpeed * look.X * dt, componentLocomotion.LookSpeed * look.Y * num * dt));
	}

	public static void SubmitLocalInput(PlayerInput input)
	{
		s_client?.SubmitInput(input);
	}

	/// <summary>Hook from <see cref="SubsystemTerrain.ChangeCell"/>.</summary>
	public static void OnCellChanged(int x, int y, int z, int value)
	{
		if (s_applyingRemoteCell)
		{
			return;
		}
		if (IsHost)
		{
			s_server?.OnLocalCellChanged(x, y, z, value);
		}
		else if (IsClient)
		{
			s_client?.OnLocalCellChanged(x, y, z, value);
		}
	}

	public static void ApplyRemoteCell(SubsystemTerrain terrain, int x, int y, int z, int value)
	{
		if (terrain == null)
		{
			return;
		}
		int cellValue = terrain.Terrain.GetCellValue(x, y, z);
		s_applyingRemoteCell = true;
		try
		{
			terrain.ChangeCell(x, y, z, value);
		}
		finally
		{
			s_applyingRemoteCell = false;
		}
		// Breaking a block on the host destroys it and creates debris there.
		// The client only receives the new cell value, so it spawns the same
		// debris locally to keep the break readable.
		if (Terrain.ExtractContents(value) == 0 && cellValue != 0)
		{
			try
			{
				SubsystemParticles subsystem = terrain.Project.FindSubsystem<SubsystemParticles>(throwOnError: false);
				if (subsystem != null)
				{
					Block block = BlocksManager.Blocks[Terrain.ExtractContents(cellValue)];
					subsystem.AddParticleSystem(block.CreateDebrisParticleSystem(terrain, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), cellValue, 1f));
				}
			}
			catch (Exception ex)
			{
				Log.Warning("[Multiplayer] Could not create break particles: " + ex.Message);
			}
		}
	}

	public static void BeginSnapshotSave()
	{
		s_includeRemotePlayersInSave = true;
	}

	public static void EndSnapshotSave()
	{
		s_includeRemotePlayersInSave = false;
	}

	public static void ShowDisconnectedMessage(string reason)
	{
		Log.Warning("[Multiplayer] Disconnected: " + reason);
		if (GameManager.Project == null)
		{
			return;
		}
		DialogsManager.ShowDialog(null, new MessageDialog("Disconnected", reason, "OK", null, delegate
		{
			GameManager.DisposeProject();
			ScreensManager.SwitchScreen("MainMenu");
		}));
	}

	internal static void SetStatus(string text)
	{
		s_statusText = text;
	}
}

/// <summary>Result of the join handshake / snapshot download.</summary>
public class MultiplayerJoinResult
{
	public bool Success;

	public string Error = string.Empty;

	public string WorldDirectory = string.Empty;

	public WorldInfo WorldInfo;

	public int PlayerIndex = -1;

	public NetChannel Channel;

	public string HostDescription = string.Empty;

	public static MultiplayerJoinResult Failed(string error)
	{
		return new MultiplayerJoinResult
		{
			Success = false,
			Error = error
		};
	}
}

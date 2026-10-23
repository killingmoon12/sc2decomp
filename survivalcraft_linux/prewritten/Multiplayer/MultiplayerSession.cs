#region // =====================================================================
// Multiplayer session for Survivalcraft 2 (Linux port)
// Modelled on Vintage Story: host-authoritative, UDP game state + TCP reliable
// control, LAN discovery, client connection states (Connecting -> Connected -> Playing).
//
// Game-facing API (used by patches/ UI hooks):
//   StartHost(port) / StartClient(host, port) / Stop()
//   IsActive, Mode, Status, DefaultPort, DiscoveryPort, IsJoinedWorld
//   DiscoverLanHostsAsync(), SendChat(), NotifyBlockChange(...), Tick(dt)
//   Events: PlayerJoined, PlayerLeft, StatusChanged, ChatReceived
// =====================================================================
#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;
using ZipArchive = System.IO.Compression.ZipArchive;

namespace Multiplayer
{
	public enum MultiplayerSessionMode
	{
		None,
		Host,
		Client,
	}

	public sealed class LanHostInfo
	{
		public string Name { get; set; } = string.Empty;
		public string Address { get; set; } = string.Empty;
		public int Port { get; set; }
		public override string ToString() => $"{Name} ({Address}:{Port})";
	}

	/// <summary>One connected peer (host side tracks many; client side tracks one).</summary>
	internal sealed class Connection
	{
		public int ClientId;
		public MultiplayerSessionMode Role; // Host or Client
		public TcpControl Tcp;
		public UdpTransport Udp;
		public IPEndPoint RemoteEndPoint;
		public string Name = string.Empty;
		public bool Ready;
		public Vector3 Position;
		public Quaternion Rotation;
		public DateTime LastPacket;
		public ConcurrentQueue<NetPacket> OutgoingUdp = new ConcurrentQueue<NetPacket>();
	}

	public static class MultiplayerSession
	{
		public const int DefaultPort = 55551;
		public const int DiscoveryPort = 55552;
		public const int MaxClients = 8;

		public static MultiplayerSessionMode Mode { get; private set; } = MultiplayerSessionMode.None;
		public static string Status { get; private set; } = "Not connected.";
		public static bool IsActive => Mode != MultiplayerSessionMode.None;
		public static bool IsJoinedWorld { get; private set; }

		public static event Action<int, string> PlayerJoined;
		public static event Action<int> PlayerLeft;
		public static event Action<string> StatusChanged;
		public static event Action<string> ChatReceived;

		private static NetHost s_host;
		private static NetClient s_client;

		/// <summary>Remote players currently in the world, keyed by their network client id.</summary>
		private static readonly Dictionary<int, PlayerData> m_remotePlayers = new Dictionary<int, PlayerData>();

		private static void SetStatus(string status)
		{
			Status = status;
			StatusChanged?.Invoke(status);
			Log.Information("[Multiplayer] " + status);
		}

		// ------------------------------------------------------------------
		// Public API
		// ------------------------------------------------------------------

		public static void StartHost(int port)
		{
			Stop();
			if (port < 1 || port > 65535) { SetStatus("Invalid port."); return; }
			try
			{
				s_host = new NetHost(port);
				bool ok = s_host.Start();
				if (!ok) { s_host = null; return; }
				Mode = MultiplayerSessionMode.Host;
				SetStatus($"Hosting on LAN port {port}.");
			}
			catch (Exception ex)
			{
				s_host = null;
				SetStatus("Cannot host: " + ex.Message);
			}
		}

		public static void StartClient(string host, int port)
		{
			Stop();
			if (string.IsNullOrWhiteSpace(host)) { SetStatus("Enter a host address."); return; }
			if (port < 1 || port > 65535) { SetStatus("Invalid port."); return; }
			try
			{
				s_client = new NetClient(host, port);
				bool ok = s_client.Connect();
				if (!ok) { s_client = null; return; }
				Mode = MultiplayerSessionMode.Client;
				SetStatus($"Connecting to {host}:{port}...");
			}
			catch (Exception ex)
			{
				s_client = null;
				SetStatus("Cannot connect: " + ex.Message);
			}
		}

		public static void Stop()
		{
			if (s_host != null) { try { s_host.Dispose(); } catch { } s_host = null; }
			if (s_client != null) { try { s_client.Dispose(); } catch { } s_client = null; }
			Mode = MultiplayerSessionMode.None;
			IsJoinedWorld = false;
			SetStatus("Not connected.");
		}

		public static Task<List<LanHostInfo>> DiscoverLanHostsAsync(int timeoutMilliseconds = 1500)
		{
			return NetDiscovery.DiscoverAsync(timeoutMilliseconds);
		}

		public static void SendChat(string message)
		{
			if (string.IsNullOrEmpty(message)) return;
			if (Mode == MultiplayerSessionMode.Host && s_host != null)
			{
				s_host.BroadcastChat(0, message); // 0 = host
			}
			else if (Mode == MultiplayerSessionMode.Client && s_client != null)
			{
				s_client.SendChat(message);
			}
		}

		/// <summary>Client requests a block change (dig/place). Host authoritative.</summary>
		public static void NotifyBlockChange(int x, int y, int z, int newValue)
		{
			if (Mode != MultiplayerSessionMode.Client || s_client == null) return;
			s_client.SendBlockChangeRequest(x, y, z, newValue);
		}

		/// <summary>Call once per frame from the game's update loop.</summary>
		public static void Tick(float dt)
		{
			s_host?.Tick(dt);
			s_client?.Tick(dt);
		}

		// ------------------------------------------------------------------
		// Game-state access (used by patches to apply remote state)
		// ------------------------------------------------------------------

		internal static void OnRemotePlayerState(int clientId, Vector3 position, Quaternion rotation)
		{
			ApplyRemotePlayerBody(clientId, position, rotation);
		}

		internal static void OnRemoteBlockChanged(int x, int y, int z, int newValue)
		{
			ApplyRemoteBlockChange(x, y, z, newValue);
		}

		internal static void OnRemotePlayerJoined(int clientId, string name)
		{
			PlayerJoined?.Invoke(clientId, name);
		}

		internal static void OnRemotePlayerLeft(int clientId)
		{
			PlayerLeft?.Invoke(clientId);
		}

		internal static void OnRemoteChat(int clientId, string name, string message)
		{
			ChatReceived?.Invoke($"[{name}] {message}");
		}

		internal static bool TryGetRemoteState(int clientId, out Vector3 position, out Quaternion rotation)
		{
			position = Vector3.Zero;
			rotation = Quaternion.Identity;
			var conn = s_host?.GetConnection(clientId) ?? s_client?.GetConnection();
			if (conn == null) return false;
			position = conn.Position;
			rotation = conn.Rotation;
			return true;
		}

		// ------------------------------------------------------------------
        // Local world application helpers (called on the host/client as appropriate)
        // ------------------------------------------------------------------

		private static void ApplyRemotePlayerBody(int clientId, Vector3 position, Quaternion rotation)
		{
			try
			{
				if (!m_remotePlayers.TryGetValue(clientId, out var playerData)) return;
				var body = playerData.ComponentPlayer?.ComponentBody;
				if (body == null) return;
				body.Position = position;
				body.Rotation = rotation;
			}
			catch (Exception ex) { Log.Warning("[Multiplayer] apply remote player failed: " + ex.Message); }
		}

		/// <summary>Spawns the in-world entity for a remote player (host and client alike).</summary>
		internal static void SpawnRemotePlayer(int clientId, string name, Vector3 spawnPosition)
		{
			try
			{
				if (m_remotePlayers.ContainsKey(clientId)) return;
				var project = GameManager.Project;
				if (project == null) return;
				var players = project.FindSubsystem<SubsystemPlayers>(false);
				if (players == null) return;
				var playerData = new PlayerData(project)
				{
					IsRemote = true,
					Name = string.IsNullOrEmpty(name) ? $"Player{clientId}" : name,
					PlayerClass = PlayerClass.Male,
					InputDevice = WidgetInputDevice.None,
					SpawnPosition = spawnPosition,
				};
				players.AddPlayerData(playerData);
				// Ensure unique PlayerIndex by using a high value that won't conflict with local players (0-7)
				// Must be set AFTER AddPlayerData which assigns m_nextPlayerIndex++
				playerData.PlayerIndex = 100 + clientId;
				m_remotePlayers[clientId] = playerData;
				var overrides = new ValuesDictionary
				{
					{ "Player", new ValuesDictionary { { "PlayerIndex", playerData.PlayerIndex } } },
					{ "Intro", new ValuesDictionary { { "PlayIntro", false } } },
				};
				string templateName = playerData.GetEntityTemplateName();
				var template = DatabaseManager.FindEntityValuesDictionary(templateName, throwIfNotFound: false);
				if (template == null)
				{
					Log.Error("[Multiplayer] Entity template '{0}' not found!", templateName);
					return;
				}

				// Apply PlayerIndex override to template BEFORE entity creation
				// ComponentPlayer.Load reads PlayerIndex from template during entity creation
				var templateCopy = new ValuesDictionary();
				foreach (var kvp in template)
				{
					if (kvp.Value is ValuesDictionary componentDict)
					{
						// Copy component dictionary and its DatabaseObject
						var componentCopy = new ValuesDictionary();
						foreach (var ck in componentDict)
						{
							componentCopy.SetValue(ck.Key, ck.Value);
						}
						componentCopy.DatabaseObject = componentDict.DatabaseObject;
						templateCopy.SetValue(kvp.Key, componentCopy);
					}
					else
					{
						templateCopy.SetValue(kvp.Key, kvp.Value);
					}
				}
				// IMPORTANT: Copy the DatabaseObject from the original template
				templateCopy.DatabaseObject = template.DatabaseObject;
				
				// Set the correct PlayerIndex in the template copy
				var playerComponent = templateCopy.Values.OfType<ValuesDictionary>().FirstOrDefault(v => v.GetValue<string>("Class", "").Contains("ComponentPlayer", StringComparison.OrdinalIgnoreCase));
				if (playerComponent != null)
				{
					playerComponent.SetValue("PlayerIndex", playerData.PlayerIndex);
				}

				// Use original template (with ComponentGui) - the patch in ComponentGui.Load()
				// handles remote players by skipping GUI initialization
				var entity = project.CreateEntity(templateCopy);
				if (entity != null)
				{
					// Apply remaining overrides after entity creation
					foreach (var kvp in overrides)
					{
						if (kvp.Key != "Player") // PlayerIndex already set in template
						{
							entity.ValuesDictionary.SetValue(kvp.Key, kvp.Value);
						}
					}
					var body = entity.FindComponent<ComponentBody>(throwOnError: true);
					body.Position = spawnPosition;
					body.Rotation = Quaternion.Identity;
					project.AddEntity(entity);
				}
				Log.Information("[Multiplayer] spawned remote player {0} (net {1}) with PlayerIndex {2}", playerData.Name, clientId, playerData.PlayerIndex);
			}
			catch (Exception ex) { Log.Error("[Multiplayer] spawn remote player failed: {0}", ex.ToString()); }
		}

		internal static void RemoveRemotePlayer(int clientId)
		{
			try
			{
				if (!m_remotePlayers.TryGetValue(clientId, out var playerData)) return;
				m_remotePlayers.Remove(clientId);
				var project = GameManager.Project;
				var players = project?.FindSubsystem<SubsystemPlayers>(false);
				if (players != null && players.PlayersData.Contains(playerData))
				{
					players.RemovePlayerData(playerData);
				}
			}
			catch (Exception ex) { Log.Warning("[Multiplayer] remove remote player failed: " + ex.Message); }
		}

		private static void ApplyRemoteBlockChange(int x, int y, int z, int newValue)
		{
			try
			{
				var project = GameManager.Project;
				if (project == null) return;
				var terrain = project.FindSubsystem<SubsystemTerrain>(false);
				if (terrain == null || terrain.Terrain == null) return;
				terrain.Terrain.SetCellValueFast(x, y, z, newValue);
				terrain.TerrainUpdater?.RequestSynchronousUpdate();
			}
			catch (Exception ex) { Log.Warning("[Multiplayer] apply remote block change failed: " + ex.Message); }
		}

		// ------------------------------------------------------------------
        // Host/Client session implementations
        // ------------------------------------------------------------------

		private abstract class NetSession : IDisposable
		{
			protected ConcurrentQueue<NetPacket> Incoming = new ConcurrentQueue<NetPacket>();
			protected abstract void DisposeCore();
			public void Dispose() { DisposeCore(); }
		}

		private sealed class NetHost : NetSession, IDisposable
		{
			private readonly int m_port;
			private TcpListenerTransport m_tcpListener;
			private UdpTransport m_udp;
			private readonly Dictionary<int, Connection> m_connections = new Dictionary<int, Connection>();
			private int m_nextClientId = 1;
			private readonly object m_lock = new object();
			private double m_nextStateBroadcast;
			private double m_nextPing;

			public NetHost(int port) { m_port = port; }

			public bool Start()
			{
				try
				{
					m_udp = new UdpTransport(m_port);
					m_udp.StartReceive(OnUdpPacket);
					m_tcpListener = new TcpListenerTransport(m_port);
					m_tcpListener.StartAccept(OnTcpAccept);
					Log.Information("[Multiplayer] host listening TCP+UDP port {0}", m_port);
					_ = NetDiscovery.StartResponderAsync(m_port);
					return true;
				}
				catch (Exception ex)
				{
					Log.Warning("[Multiplayer] host start failed: " + ex.Message);
					return false;
				}
			}

			private bool OnTcpAccept(TcpClient client)
			{
				lock (m_lock)
				{
					if (m_connections.Count >= MaxClients) return false;
				}
				var tcp = new TcpControl(client);
				var conn = new Connection { Tcp = tcp, Role = MultiplayerSessionMode.Host };
				tcp.StartReceive(OnTcpMessage, () => RemoveConnection(conn));
				lock (m_lock) { m_connections[conn.ClientId = m_nextClientId++] = conn; }
				_ = Task.Run(() => SendUdpOutgoing(conn));
				Log.Information("[Multiplayer] client {0} connected from {1}", conn.ClientId, conn.Tcp.RemoteEndPoint);
				return true;
			}

			private void OnTcpMessage(NetPacket pkt)
			{
				Log.Information("[Multiplayer] Server received TCP message from {0}: Type={1}, PayloadLen={2}", pkt.RemoteEndPoint, pkt.Type, pkt.Payload?.Length ?? 0);
				Connection conn = FindConnectionByTcp(pkt);
				if (conn == null)
				{
					Log.Warning("[Multiplayer] No connection found for {0}", pkt.RemoteEndPoint);
					return;
				}
				conn.LastPacket = DateTime.UtcNow;
				switch (pkt.Type)
				{
					case NetMessageType.Hello:
						HandleHello(conn, pkt.Payload);
						break;
					case NetMessageType.Disconnect:
						RemoveConnection(conn);
						break;
					case NetMessageType.Chat:
						HandleChat(conn, pkt.Payload);
						break;
					case NetMessageType.BlockChanged:
						HandleBlockChangeRequest(conn, pkt.Payload);
						break;
				}
			}

			private void OnUdpPacket(NetPacket pkt)
			{
				Connection conn = FindConnectionByEndpoint(pkt.RemoteEndPoint);
				if (conn == null) return;
				conn.LastPacket = DateTime.UtcNow;
				if (pkt.Type == NetMessageType.PlayerState)
					HandlePlayerState(conn, pkt.Payload);
			}

			private Connection FindConnectionByTcp(NetPacket pkt)
			{
				lock (m_lock)
				{
					foreach (var c in m_connections.Values)
						if (c.Tcp.RemoteEndPoint.Equals(pkt.RemoteEndPoint)) return c;
				}
				return null;
			}

			private Connection FindConnectionByEndpoint(IPEndPoint ep)
			{
				lock (m_lock)
				{
					foreach (var c in m_connections.Values)
						if (c.RemoteEndPoint != null && c.RemoteEndPoint.Equals(ep)) return c;
				}
				return null;
			}

			private void HandleHello(Connection conn, byte[] payload)
			{
				try
				{
					using var ms = new MemoryStream(payload);
					using var r = new BinaryReader(ms);
					string name = r.ReadString();
					int clientUdpPort = r.ReadInt32();
					if (string.IsNullOrEmpty(name)) name = "Player";
					conn.Name = name.Length > 32 ? name.Substring(0, 32) : name;
					conn.Ready = true;
					// Use client's IP from TCP connection, but client's UDP port
					conn.RemoteEndPoint = new IPEndPoint(conn.Tcp.RemoteEndPoint.Address, clientUdpPort);
					SendHelloAck(conn, clientUdpPort);
					SpawnRemotePlayer(conn.ClientId, conn.Name, Vector3.Zero);
					SendWorldSnapshot(conn);
					BroadcastPlayerJoined(conn);
					SendPlayerListTo(conn);
					SetStatus($"Hosting on LAN port {m_port}. Clients: {ReadyClientCount()}.");
					MultiplayerSession.OnRemotePlayerJoined(conn.ClientId, conn.Name);
					Log.Information("[Multiplayer] {0} ({1}) joined, UDP port {2}", conn.Name, conn.ClientId, clientUdpPort);
				}
				catch (Exception ex)
				{
					Log.Warning("[Multiplayer] HandleHello failed: " + ex.Message);
					conn.Tcp?.Send(NetMessageType.JoinRejected, Encoding.UTF8.GetBytes("Server error: " + ex.Message));
					RemoveConnection(conn);
				}
			}

			private void SendHelloAck(Connection conn, int clientUdpPort)
			{
				using var ms = new MemoryStream();
				using var w = new BinaryWriter(ms);
				w.Write(conn.ClientId);
				w.Write(DefaultPort); // server's UDP port for client->server traffic
				conn.Tcp.Send(NetMessageType.HelloAck, ms.ToArray());
			}

			private void SendPlayerListTo(Connection toConn)
			{
				lock (m_lock)
				{
					foreach (var c in m_connections.Values)
					{
						if (c.ClientId == toConn.ClientId) continue;
						using var ms = new MemoryStream();
						using var w = new BinaryWriter(ms);
						w.Write(c.ClientId);
						w.Write(c.Name ?? string.Empty);
						toConn.Tcp.Send(NetMessageType.PlayerJoined, ms.ToArray());
					}
				}
			}

			private void BroadcastPlayerJoined(Connection conn)
			{
				using var ms = new MemoryStream();
				using var w = new BinaryWriter(ms);
				w.Write(conn.ClientId);
				w.Write(conn.Name ?? string.Empty);
				lock (m_lock)
				{
					foreach (var c in m_connections.Values)
						if (c.Ready && c.ClientId != conn.ClientId) EnqueueUdp(c, NetMessageType.PlayerJoined, ms.ToArray());
				}
			}

			private void BroadcastPlayerLeft(Connection conn)
			{
				using var ms = new MemoryStream();
				using var w = new BinaryWriter(ms);
				w.Write(conn.ClientId);
				lock (m_lock)
				{
					foreach (var c in m_connections.Values)
						if (c.Ready) EnqueueUdp(c, NetMessageType.PlayerLeft, ms.ToArray());
				}
			}

			private void HandleChat(Connection conn, byte[] payload)
			{
				string message = Encoding.UTF8.GetString(payload ?? Array.Empty<byte>());
				BroadcastChat(conn.ClientId, $"{conn.Name}: {message}");
			}

			public void BroadcastChat(int senderClientId, string message)
			{
				using var ms = new MemoryStream();
				using var w = new BinaryWriter(ms);
				w.Write(senderClientId);
				w.Write(message ?? string.Empty);
				var payload = ms.ToArray();
				lock (m_lock)
				{
					foreach (var c in m_connections.Values)
						if (c.Ready) c.Tcp.Send(NetMessageType.Chat, payload);
				}
				MultiplayerSession.OnRemoteChat(senderClientId, GetClientName(senderClientId), message);
			}

			private void HandleBlockChangeRequest(Connection conn, byte[] payload)
			{
				using var ms = new MemoryStream(payload);
				using var r = new BinaryReader(ms);
				int x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32(), value = r.ReadInt32();
				// Host authoritative: apply locally and broadcast.
				ApplyRemoteBlockChange(x, y, z, value);
				using var outMs = new MemoryStream();
				using var outW = new BinaryWriter(outMs);
				outW.Write(x); outW.Write(y); outW.Write(z); outW.Write(value);
				var outPayload = outMs.ToArray();
				lock (m_lock)
				{
					foreach (var c in m_connections.Values)
						if (c.Ready && c.ClientId != conn.ClientId) c.Tcp.Send(NetMessageType.BlockChanged, outPayload);
				}
			}

			private void HandlePlayerState(Connection conn, byte[] payload)
			{
				using var ms = new MemoryStream(payload);
				using var r = new BinaryReader(ms);
				float px = r.ReadSingle(), py = r.ReadSingle(), pz = r.ReadSingle();
				float rx = r.ReadSingle(), ry = r.ReadSingle(), rz = r.ReadSingle(), rw = r.ReadSingle();
				conn.Position = new Vector3(px, py, pz);
				conn.Rotation = new Quaternion(rx, ry, rz, rw);
				// Apply to host's local/remote player body immediately.
				ApplyRemotePlayerBody(conn.ClientId, conn.Position, conn.Rotation);
			}

			private string GetClientName(int clientId)
			{
				lock (m_lock)
				{
					if (m_connections.TryGetValue(clientId, out var c)) return c.Name;
				}
				return "Host";
			}

			private int ReadyClientCount()
			{
				lock (m_lock) { return m_connections.Values.Count(c => c.Ready); }
			}

			private void RemoveConnection(Connection conn)
			{
				lock (m_lock) { m_connections.Remove(conn.ClientId); }
				BroadcastPlayerLeft(conn);
				SetStatus($"Hosting on LAN port {m_port}. Clients: {ReadyClientCount()}.");
				MultiplayerSession.OnRemotePlayerLeft(conn.ClientId);
				try { conn.Tcp?.Close(); } catch { }
				try { conn.Udp?.Close(); } catch { }
				Log.Information("[Multiplayer] client {0} disconnected", conn.ClientId);
			}

			private void EnqueueUdp(Connection conn, NetMessageType type, byte[] payload)
			{
				conn.OutgoingUdp.Enqueue(new NetPacket { Channel = NetChannel.UdpGame, Type = type, Payload = payload });
			}

			private async Task SendUdpOutgoing(Connection conn)
			{
				try
				{
					while (true)
					{
						if (!conn.OutgoingUdp.TryDequeue(out var pkt)) { await Task.Delay(5); continue; }
						IPEndPoint target = conn.RemoteEndPoint;
						if (target == null) continue;
						m_udp?.Send(target, pkt.Payload);
					}
				}
				catch (Exception) { }
			}

			public void Tick(float dt)
			{
				// Process incoming UDP player states already handled inline; broadcast state to clients.
				double now = Time.RealTime;
				if (now >= m_nextStateBroadcast)
				{
					m_nextStateBroadcast = now + 0.05; // 20 Hz
					BroadcastPlayerStates();
				}
				if (now >= m_nextPing)
				{
					m_nextPing = now + 1.0;
					BroadcastPing();
				}
				// Drop silent clients
				var stale = new List<Connection>();
				lock (m_lock) { foreach (var c in m_connections.Values) if ((DateTime.UtcNow - c.LastPacket).TotalSeconds > 10.0) stale.Add(c); }
				foreach (var c in stale) RemoveConnection(c);
			}

			private void BroadcastPlayerStates()
			{
				lock (m_lock)
				{
					if (m_connections.Count == 0) return;
					using var ms = new MemoryStream();
					using var w = new BinaryWriter(ms);
					w.Write(m_connections.Count);
					foreach (var c in m_connections.Values)
					{
						if (!c.Ready) continue;
						w.Write(c.ClientId);
						w.Write(c.Position.X); w.Write(c.Position.Y); w.Write(c.Position.Z);
						w.Write(c.Rotation.X); w.Write(c.Rotation.Y); w.Write(c.Rotation.Z); w.Write(c.Rotation.W);
					}
					var payload = ms.ToArray();
					foreach (var c in m_connections.Values)
						if (c.Ready) EnqueueUdp(c, NetMessageType.PlayerState, payload);
				}
			}

			private void BroadcastPing()
			{
				var payload = BitConverter.GetBytes((long)(Time.RealTime * 1000.0));
				lock (m_lock)
				{
					foreach (var c in m_connections.Values)
						if (c.Ready) EnqueueUdp(c, NetMessageType.Ping, payload);
				}
			}

			private void SendWorldSnapshot(Connection conn)
			{
				_ = Task.Run(async () =>
				{
					try
					{
						var worldInfo = GameManager.WorldInfo;
						if (worldInfo == null) return;
						string dir = worldInfo.DirectoryName;
						if (!Storage.DirectoryExists(dir)) return;
						byte[] zip = ZipDirectory(dir);
						if (zip == null || zip.Length == 0) return;
						conn.Tcp.Send(NetMessageType.WorldSnapshotStart, BitConverter.GetBytes(zip.Length));
						const int chunk = 65500;
						for (int off = 0; off < zip.Length; off += chunk)
						{
							int count = Math.Min(chunk, zip.Length - off);
							var c = new byte[count];
							Buffer.BlockCopy(zip, off, c, 0, count);
							conn.Tcp.Send(NetMessageType.WorldSnapshotChunk, c);
						}
						conn.Tcp.Send(NetMessageType.WorldSnapshotEnd, null);
						Log.Information("[Multiplayer] sent world snapshot ({0} bytes) to {1}", zip.Length, conn.ClientId);
					}
					catch (Exception ex) { Log.Warning("[Multiplayer] snapshot send failed: " + ex.Message); }
				});
			}

			private static byte[] ZipDirectory(string directoryName)
			{
				using var ms = new MemoryStream();
				using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
				{
					AddDirectoryToZip(zip, directoryName, string.Empty);
				}
				return ms.ToArray();
			}

			private static void AddDirectoryToZip(ZipArchive zip, string directoryName, string basePath)
			{
				if (!Storage.DirectoryExists(directoryName)) return;
				foreach (var file in Storage.ListFileNames(directoryName))
				{
					string path = Storage.CombinePaths(directoryName, file);
					using var stream = Storage.OpenFile(path, OpenFileMode.Read);
					var entry = zip.CreateEntry(Path.Combine(basePath, file).Replace('\\', '/'));
					using var outStream = entry.Open();
					stream.CopyTo(outStream);
				}
				foreach (var sub in Storage.ListDirectoryNames(directoryName))
				{
					string path = Storage.CombinePaths(directoryName, sub);
					AddDirectoryToZip(zip, path, Path.Combine(basePath, sub).Replace('\\', '/'));
				}
			}

			public Connection GetConnection(int clientId)
			{
				lock (m_lock)
				{
					return m_connections.TryGetValue(clientId, out var c) ? c : null;
				}
			}

			protected override void DisposeCore()
			{
				lock (m_lock)
				{
					foreach (var c in m_connections.Values.ToArray()) RemoveConnection(c);
				}
				m_tcpListener?.Close();
				m_udp?.Close();
			}
		}

		private sealed class NetClient : NetSession, IDisposable
		{
			private readonly string m_host;
			private readonly int m_port;
			private TcpControl m_tcp;
			private UdpTransport m_udp;
			private IPEndPoint m_serverEndPoint;
			private int m_clientId = 0;
			private bool m_connected;
			private double m_nextStateSend;
			private readonly object m_lock = new object();

			public NetClient(string host, int port) { m_host = host; m_port = port; }

			public bool Connect()
			{
				try
				{
					var client = new TcpClient();
					client.Connect(m_host, m_port);
					m_tcp = new TcpControl(client);
					m_tcp.StartReceive(OnTcpMessage, StopSession);
					// UDP for game state (bind any port, send to server)
					m_udp = new UdpTransport(0);
					m_udp.StartReceive(OnUdpPacket);
					m_serverEndPoint = new IPEndPoint(ResolveHost(m_host), m_port);
					SendHello(Environment.UserName, m_udp.LocalPort);
					m_connected = true;
					Log.Information("[Multiplayer] client connected to {0}:{1}", m_host, m_port);
					return true;
				}
				catch (Exception ex)
				{
					Log.Warning("[Multiplayer] client connect failed: " + ex.Message);
					return false;
				}
			}

			private IPAddress ResolveHost(string host)
			{
				if (IPAddress.TryParse(host, out var addr)) return addr;
				var entries = Dns.GetHostAddresses(host);
				return entries.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? IPAddress.Loopback;
			}

			private void OnTcpMessage(NetPacket pkt)
			{
				Log.Information("[Multiplayer] Client received TCP message: Type={0}, PayloadLen={1}", pkt.Type, pkt.Payload?.Length ?? 0);
				switch (pkt.Type)
				{
					case NetMessageType.HelloAck: HandleHelloAck(pkt.Payload); break;
					case NetMessageType.JoinRejected: SetStatus("Rejected: " + Encoding.UTF8.GetString(pkt.Payload ?? Array.Empty<byte>())); StopSession(); break;
					case NetMessageType.WorldSnapshotStart: BeginSnapshot(BitConverter.ToInt64(pkt.Payload, 0)); break;
					case NetMessageType.WorldSnapshotChunk: AppendSnapshotChunk(pkt.Payload); break;
					case NetMessageType.WorldSnapshotEnd: FinishSnapshot(); break;
					case NetMessageType.PlayerJoined: HandleRemotePlayerJoined(pkt.Payload); break;
					case NetMessageType.PlayerLeft: HandleRemotePlayerLeft(pkt.Payload); break;
					case NetMessageType.BlockChanged: HandleRemoteBlockChanged(pkt.Payload); break;
					case NetMessageType.Chat: HandleChat(pkt.Payload); break;
					case NetMessageType.Disconnect: StopSession(); break;
				}
			}

			private void OnUdpPacket(NetPacket pkt)
			{
				if (pkt.Type == NetMessageType.PlayerState) HandlePlayerStateBroadcast(pkt.Payload);
				else if (pkt.Type == NetMessageType.Ping) { m_udp?.Send(m_serverEndPoint, new byte[] { (byte)NetMessageType.Pong }); }
			}

			private void HandleHelloAck(byte[] payload)
			{
				try
				{
					using var ms = new MemoryStream(payload);
					using var r = new BinaryReader(ms);
					m_clientId = r.ReadInt32();
					int udpPort = r.ReadInt32();
					m_serverEndPoint = new IPEndPoint(m_serverEndPoint.Address, udpPort);
					SetStatus("Connected to host.");
					Log.Information("[Multiplayer] HelloAck received: clientId={0}, serverUdpPort={1}", m_clientId, udpPort);
				}
				catch (Exception ex)
				{
					Log.Warning("[Multiplayer] HandleHelloAck failed: " + ex.Message);
					StopSession();
				}
			}

			private void SendHello(string name, int udpPort)
			{
				using var ms = new MemoryStream();
				using var w = new BinaryWriter(ms);
				w.Write(name ?? string.Empty);
				w.Write(udpPort);
				m_tcp?.Send(NetMessageType.Hello, ms.ToArray());
			}

			private void SetStatus(string status)
			{
				MultiplayerSession.SetStatus(status);
			}

			private void StopSession()
			{
				Mode = MultiplayerSessionMode.None;
				IsJoinedWorld = false;
				MultiplayerSession.SetStatus("Disconnected from host.");
				s_client = null;
			}

			private void BeginSnapshot(long totalBytes) { /* chunks accumulate */ }
			private readonly List<byte> m_snapshotChunks = new List<byte>();
			private void AppendSnapshotChunk(byte[] payload)
			{
				if (payload == null || payload.Length == 0) return;
				// Server sends raw chunk data without length prefix
				m_snapshotChunks.AddRange(payload);
			}
			private void FinishSnapshot()
			{
				_ = Task.Run(() =>
				{
					try
					{
						byte[] zip = m_snapshotChunks.ToArray();
						m_snapshotChunks.Clear();
						ImportSnapshot(zip);
					}
					catch (Exception ex) { Log.Warning("[Multiplayer] snapshot import failed: " + ex.Message); }
				});
			}

			private void ImportSnapshot(byte[] zip)
			{
				string dir = Storage.CombinePaths("data:", "Multiplayer", "JoinedWorld");
				Log.Information("[Multiplayer] Importing world snapshot to {0}", dir);
				try
				{
					// Ensure parent directory exists
					string parentDir = Path.GetDirectoryName(dir);
					if (!string.IsNullOrEmpty(parentDir) && !Storage.DirectoryExists(parentDir))
					{
						Storage.CreateDirectory(parentDir);
					}
					// Only delete if directory exists
					if (Storage.DirectoryExists(dir))
					{
						Storage.DeleteDirectoryRecursive(dir);
					}
					Storage.CreateDirectory(dir);
					using var ms = new MemoryStream(zip);
					using var zipArchive = new ZipArchive(ms, ZipArchiveMode.Read);
					foreach (var entry in zipArchive.Entries)
					{
						if (string.IsNullOrEmpty(entry.Name)) continue;
						string target = Storage.CombinePaths(dir, entry.FullName);
						string dirName = Path.GetDirectoryName(target);
						if (!string.IsNullOrEmpty(dirName)) Storage.CreateDirectory(dirName);
						using var outStream = Storage.OpenFile(target, OpenFileMode.Create);
						using var inStream = entry.Open();
						inStream.CopyTo(outStream);
					}
				}
				catch (Exception ex)
				{
					Log.Error("[Multiplayer] Failed to import snapshot: {0}", ex.ToString());
					throw;
				}
				var worldInfo = WorldsManager.GetWorldInfo(dir);
				if (worldInfo == null) { SetStatus("Received world is invalid."); return; }
				MultiplayerSession.IsJoinedWorld = true;
				Log.Information("[Multiplayer] World snapshot imported successfully, switching to GameLoading");
				Dispatcher.Dispatch(() =>
				{
					try
					{
						DialogsManager.HideAllDialogs();
						ScreensManager.SwitchScreen("GameLoading", worldInfo, null);
					}
					catch (Exception ex) { Log.Warning("[Multiplayer] switch to GameLoading failed: " + ex.Message); }
				});
			}

			private void HandleRemotePlayerJoined(byte[] payload)
			{
				using var ms = new MemoryStream(payload);
				using var r = new BinaryReader(ms);
				int id = r.ReadInt32();
				string name = r.ReadString();
				MultiplayerSession.OnRemotePlayerJoined(id, name);
				// Spawn the avatar once the client's world is loaded; before that the
				// project is not available yet, so the spawn is skipped here.
				if (MultiplayerSession.IsJoinedWorld) SpawnRemotePlayer(id, name, Vector3.Zero);
			}

			private void HandleRemotePlayerLeft(byte[] payload)
			{
				using var ms = new MemoryStream(payload);
				using var r = new BinaryReader(ms);
				int id = r.ReadInt32();
				MultiplayerSession.OnRemotePlayerLeft(id);
			}

			private void HandleRemoteBlockChanged(byte[] payload)
			{
				using var ms = new MemoryStream(payload);
				using var r = new BinaryReader(ms);
				int x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32(), value = r.ReadInt32();
				MultiplayerSession.OnRemoteBlockChanged(x, y, z, value);
			}

			private void HandleChat(byte[] payload)
			{
				using var ms = new MemoryStream(payload);
				using var r = new BinaryReader(ms);
				int id = r.ReadInt32();
				string msg = r.ReadString();
				string name = id == 0 ? "Host" : "Player" + id;
				MultiplayerSession.OnRemoteChat(id, name, msg);
			}

			private void HandlePlayerStateBroadcast(byte[] payload)
			{
				using var ms = new MemoryStream(payload);
				using var r = new BinaryReader(ms);
				int count = r.ReadInt32();
				for (int i = 0; i < count; i++)
				{
					int id = r.ReadInt32();
					Vector3 pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
					Quaternion rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
					if (id == m_clientId)
					{
						// authoritative correction for local player
						ApplyLocalBody(pos, rot);
					}
					else
					{
						MultiplayerSession.OnRemotePlayerState(id, pos, rot);
					}
				}
			}

			private void ApplyLocalBody(Vector3 position, Quaternion rotation)
			{
				try
				{
					var project = GameManager.Project;
					if (project == null) return;
					var players = project.FindSubsystem<SubsystemPlayers>(false);
					if (players == null) return;
					var local = players.PlayersData.FirstOrDefault(p => !p.IsRemote && p.ComponentPlayer != null);
					if (local == null || local.ComponentPlayer == null) return;
					var body = local.ComponentPlayer.ComponentBody;
					if (body == null) return;
					body.Position = position;
					body.Rotation = rotation;
				}
				catch (Exception ex) { Log.Warning("[Multiplayer] apply local body failed: " + ex.Message); }
			}

			public void SendChat(string message)
			{
				m_tcp?.Send(NetMessageType.Chat, Encoding.UTF8.GetBytes(message ?? string.Empty));
			}

			public void SendBlockChangeRequest(int x, int y, int z, int value)
			{
				using var ms = new MemoryStream();
				using var w = new BinaryWriter(ms);
				w.Write(x); w.Write(y); w.Write(z); w.Write(value);
				m_tcp?.Send(NetMessageType.BlockChanged, ms.ToArray());
			}

			public void SendPlayerState(Vector3 position, Quaternion rotation)
			{
				if (m_udp == null || m_serverEndPoint == null) return;
				using var ms = new MemoryStream();
				using var w = new BinaryWriter(ms);
				w.Write(position.X); w.Write(position.Y); w.Write(position.Z);
				w.Write(rotation.X); w.Write(rotation.Y); w.Write(rotation.Z); w.Write(rotation.W);
				m_udp.Send(m_serverEndPoint, ms.ToArray());
			}

			public void Tick(float dt)
			{
				if (!m_connected || m_tcp == null) return;
				double now = Time.RealTime;
				if (now >= m_nextStateSend && GameManager.Project != null)
				{
					m_nextStateSend = now + 0.05; // 20 Hz
					var players = GameManager.Project.FindSubsystem<SubsystemPlayers>(false);
					var local = players?.PlayersData.FirstOrDefault(p => !p.IsRemote && p.ComponentPlayer != null);
					if (local != null && local.ComponentPlayer != null && local.ComponentPlayer.ComponentBody != null)
					{
						var body = local.ComponentPlayer.ComponentBody;
						SendPlayerState(body.Position, body.Rotation);
					}
				}
			}

			public Connection GetConnection() => null;

			protected override void DisposeCore()
			{
				try { m_tcp?.Close(); } catch { }
				try { m_udp?.Close(); } catch { }
			}
		}
	}

	// ------------------------------------------------------------------
    // LAN discovery (UDP broadcast)
    // ------------------------------------------------------------------

	public static class NetDiscovery
	{
		private static readonly byte[] DiscoveryRequest = Encoding.UTF8.GetBytes("SC2MP!");
		public static async Task<List<LanHostInfo>> DiscoverAsync(int timeoutMilliseconds = 1500)
		{
			var hosts = new List<LanHostInfo>();
			var seen = new HashSet<string>();
			try
			{
				using var udp = new System.Net.Sockets.UdpClient(AddressFamily.InterNetwork);
				udp.EnableBroadcast = true;
				udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
				var targets = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, MultiplayerSession.DiscoveryPort), new IPEndPoint(IPAddress.Loopback, MultiplayerSession.DiscoveryPort) };
				foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
				{
					if (iface.OperationalStatus != OperationalStatus.Up || iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
					foreach (var ua in iface.GetIPProperties().UnicastAddresses)
					{
						if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
						var bytes = ua.Address.GetAddressBytes();
						var mask = ua.IPv4Mask.GetAddressBytes();
						var bcast = new byte[4];
						for (int i = 0; i < 4; i++) bcast[i] = (byte)(bytes[i] | (byte)~mask[i]);
						targets.Add(new IPEndPoint(new IPAddress(bcast), MultiplayerSession.DiscoveryPort));
					}
				}
				foreach (var t in targets)
				{
					try { await udp.SendAsync(DiscoveryRequest, DiscoveryRequest.Length, t); } catch { }
				}
				using var cts = new CancellationTokenSource(timeoutMilliseconds);
				while (!cts.Token.IsCancellationRequested)
				{
					try
					{
						var result = await udp.ReceiveAsync(cts.Token);
						var text = Encoding.UTF8.GetString(result.Buffer);
						var parts = text.Split('|');
						if (parts.Length < 3 || parts[0] != "SC2MP!") continue;
						int port;
						if (!int.TryParse(parts[1], out port) || port < 1 || port > 65535) continue;
						string key = result.RemoteEndPoint.Address.ToString() + ":" + port;
						if (!seen.Add(key)) continue;
						hosts.Add(new LanHostInfo { Name = parts[2], Address = result.RemoteEndPoint.Address.ToString(), Port = port });
					}
					catch (OperationCanceledException) { break; }
					catch { break; }
				}
			}
			catch { }
			return hosts;
		}

		public static async Task StartResponderAsync(int hostPort)
		{
			try
			{
				using var udp = new System.Net.Sockets.UdpClient(AddressFamily.InterNetwork);
				udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				udp.Client.Bind(new IPEndPoint(IPAddress.Any, MultiplayerSession.DiscoveryPort));
				udp.EnableBroadcast = true;
				while (true)
				{
					var result = await udp.ReceiveAsync();
					var text = Encoding.UTF8.GetString(result.Buffer);
					if (text != "SC2MP!") continue;
					try
					{
						string worldName = GameManager.WorldInfo?.WorldSettings?.Name ?? "Survivalcraft";
						worldName = worldName.Replace('|', ' ');
						var reply = Encoding.UTF8.GetBytes($"SC2MP!|{hostPort}|{worldName}");
						await udp.SendAsync(reply, reply.Length, result.RemoteEndPoint);
					}
					catch { }
				}
			}
			catch { }
		}
	}
}

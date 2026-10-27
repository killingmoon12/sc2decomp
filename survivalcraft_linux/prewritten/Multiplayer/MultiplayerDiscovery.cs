using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Engine;

namespace Game;

/// <summary>
/// Minimal LAN discovery: the client broadcasts a query, every host on the local
/// network answers with a one-line description of its world.
/// </summary>
public static class MultiplayerDiscovery
{
	private static UdpClient s_announcer;

	private static Thread s_announcerThread;

	private static volatile bool s_announcerRunning;

	private static MultiplayerServer s_server;

	private static readonly object s_gamesLock = new object();

	private static readonly Dictionary<string, MultiplayerGameInfo> s_games = new Dictionary<string, MultiplayerGameInfo>();

	private static Thread s_probeThread;

	private static volatile bool s_probeRunning;

	private static readonly List<IPAddress> s_broadcastAddresses = new List<IPAddress>();

	public const double GameTimeout = 5.0;

	public static void StartAnnouncer(MultiplayerServer server)
	{
		if (s_announcerRunning)
		{
			return;
		}
		s_server = server;
		try
		{
			s_announcer = new UdpClient();
			s_announcer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);
			s_announcer.Client.Bind(new IPEndPoint(IPAddress.Any, MultiplayerProtocol.DiscoveryPort));
			s_announcerRunning = true;
			s_announcerThread = new Thread(AnnouncerLoop)
			{
				IsBackground = true,
				Name = "Multiplayer Discovery Announcer"
			};
			s_announcerThread.Start();
			Log.Information("[Multiplayer] LAN discovery listening on UDP {0}", MultiplayerProtocol.DiscoveryPort);
		}
		catch (Exception ex)
		{
			Log.Warning("[Multiplayer] Could not start LAN discovery: " + ex.Message);
			s_announcerRunning = false;
			s_announcer?.Close();
			s_announcer = null;
		}
	}

	public static void StopAnnouncer()
	{
		s_announcerRunning = false;
		try
		{
			s_announcer?.Close();
		}
		catch (Exception)
		{
		}
		s_announcer = null;
		s_server = null;
	}

	private static void AnnouncerLoop()
	{
		IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
		while (s_announcerRunning)
		{
			try
			{
				byte[] array = s_announcer.Receive(ref remoteEP);
				if (array == null || array.Length == 0)
				{
					continue;
				}
				string text = Encoding.UTF8.GetString(array);
				if (text != MultiplayerProtocol.DiscoveryRequest)
				{
					continue;
				}
				MultiplayerServer multiplayerServer = s_server;
				if (multiplayerServer == null || !multiplayerServer.IsRunning)
				{
					continue;
				}
				string s = MultiplayerProtocol.MakeDiscoveryReply(multiplayerServer.WorldName, multiplayerServer.HostName, multiplayerServer.Port, multiplayerServer.ConnectedPlayerCount + 1, MultiplayerManager.MaxPlayers, MultiplayerProtocol.Version);
				byte[] bytes = Encoding.UTF8.GetBytes(s);
				s_announcer.Send(bytes, bytes.Length, remoteEP);
			}
			catch (SocketException)
			{
				if (!s_announcerRunning)
				{
					break;
				}
				Thread.Sleep(50);
			}
			catch (ObjectDisposedException)
			{
				break;
			}
			catch (Exception ex)
			{
				Log.Warning("[Multiplayer] Discovery announcer error: " + ex.Message);
				Thread.Sleep(100);
			}
		}
	}

	public static IReadOnlyList<MultiplayerGameInfo> GetGames()
	{
		double realTime = Time.RealTime;
		List<MultiplayerGameInfo> list = new List<MultiplayerGameInfo>();
		lock (s_gamesLock)
		{
			List<string> list2 = new List<string>();
			foreach (KeyValuePair<string, MultiplayerGameInfo> game in s_games)
			{
				if (realTime - game.Value.LastSeenTime > GameTimeout)
				{
					list2.Add(game.Key);
				}
				else
				{
					list.Add(game.Value);
				}
			}
			foreach (string item in list2)
			{
				s_games.Remove(item);
			}
		}
		list.Sort((MultiplayerGameInfo a, MultiplayerGameInfo b) => string.Compare(a.WorldName, b.WorldName, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	public static void StartProbe()
	{
		if (s_probeThread != null && s_probeThread.IsAlive)
		{
			return;
		}
		s_probeRunning = true;
		s_probeThread = new Thread(ProbeLoop)
		{
			IsBackground = true,
			Name = "Multiplayer Discovery Probe"
		};
		s_probeThread.Start();
	}

	public static void StopProbe()
	{
		s_probeRunning = false;
	}

	private static void ProbeLoop()
	{
		try
		{
			using UdpClient udpClient = new UdpClient();
			udpClient.EnableBroadcast = true;
			udpClient.Client.ReceiveTimeout = 150;
			byte[] bytes = Encoding.UTF8.GetBytes(MultiplayerProtocol.DiscoveryRequest);
			foreach (IPAddress address in GetBroadcastAddresses())
			{
				try
				{
					udpClient.Send(bytes, bytes.Length, new IPEndPoint(address, MultiplayerProtocol.DiscoveryPort));
				}
				catch (Exception)
				{
				}
			}
			double realTime = Time.RealTime;
			while (s_probeRunning && Time.RealTime - realTime < 0.6)
			{
				try
				{
					IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
					byte[] array = udpClient.Receive(ref remoteEP);
					string text = Encoding.UTF8.GetString(array);
					if (MultiplayerProtocol.TryParseDiscoveryReply(text, out MultiplayerGameInfo info))
					{
						info.Address = remoteEP.Address.ToString();
						info.LastSeenTime = Time.RealTime;
						lock (s_gamesLock)
						{
							s_games[info.Address + ":" + info.Port] = info;
						}
					}
				}
				catch (SocketException)
				{
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[Multiplayer] Discovery probe error: " + ex.Message);
		}
		finally
		{
			s_probeRunning = false;
		}
	}

	private static List<IPAddress> GetBroadcastAddresses()
	{
		List<IPAddress> list = new List<IPAddress>();
		try
		{
			foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
				{
					continue;
				}
				foreach (UnicastIPAddressInformation unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
				{
					if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork && unicastAddress.IPv4Mask != null)
					{
						byte[] addressBytes = unicastAddress.Address.GetAddressBytes();
						byte[] addressBytes2 = unicastAddress.IPv4Mask.GetAddressBytes();
						for (int i = 0; i < 4; i++)
						{
							addressBytes[i] = (byte)(addressBytes[i] | ~addressBytes2[i]);
						}
						list.Add(new IPAddress(addressBytes));
					}
				}
			}
		}
		catch (Exception)
		{
		}
		list.Add(IPAddress.Broadcast);
		list.Add(IPAddress.Loopback);
		return list;
	}
}

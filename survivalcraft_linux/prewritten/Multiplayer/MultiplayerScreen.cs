using System;
using System.Collections.Generic;
using Engine;
using Engine.Media;

namespace Game;

/// <summary>
/// Multiplayer join screen. Hosts on the local network are discovered
/// automatically; internet games are joined by typing the host's address and
/// port (the host must forward the TCP port on its router).
/// </summary>
public class MultiplayerScreen : Screen
{
	private readonly ListPanelWidget m_gamesList;

	private readonly TextBoxWidget m_nameBox;

	private readonly TextBoxWidget m_addressBox;

	private readonly TextBoxWidget m_portBox;

	private readonly LabelWidget m_statusLabel;

	private readonly ButtonWidget m_joinButton;

	private readonly ButtonWidget m_connectButton;

	private readonly ButtonWidget m_refreshButton;

	private readonly ButtonWidget m_backButton;

	private static string s_lastAddress = string.Empty;

	private static string s_lastPort = MultiplayerProtocol.DefaultPort.ToString();

	private List<MultiplayerGameInfo> m_games = new List<MultiplayerGameInfo>();

	private double m_lastRefreshTime;

	private bool m_switchedToGame;

	private bool m_joinFailedShown;

	public MultiplayerScreen()
	{
		Children.Add(new RectangleWidget
		{
			Size = new Vector2(float.PositiveInfinity),
			FillColor = new Color(0, 0, 0, 210)
		});
		CanvasWidget canvasWidget = new CanvasWidget
		{
			Size = new Vector2(820f, 600f),
			HorizontalAlignment = WidgetAlignment.Center,
			VerticalAlignment = WidgetAlignment.Center
		};
		Children.Add(canvasWidget);
		canvasWidget.Children.Add(new BevelledRectangleWidget
		{
			Size = new Vector2(820f, 600f),
			CenterColor = new Color(32, 40, 32, 255),
			BevelColor = new Color(64, 78, 64, 255),
			AmbientLight = 1f
		});
		StackPanelWidget stackPanelWidget = new StackPanelWidget
		{
			Direction = LayoutDirection.Vertical,
			HorizontalAlignment = WidgetAlignment.Center,
			Margin = new Vector2(20f, 12f)
		};
		canvasWidget.Children.Add(stackPanelWidget);
		BitmapFont font = ContentManager.Get<BitmapFont>("Fonts/Pericles24");
		BitmapFont font2 = ContentManager.Get<BitmapFont>("Fonts/Pericles18");
		stackPanelWidget.Children.Add(new LabelWidget
		{
			Text = "Join Multiplayer Game",
			Font = font,
			HorizontalAlignment = WidgetAlignment.Center,
			Color = Color.White,
			Margin = new Vector2(0f, 4f)
		});
		stackPanelWidget.Children.Add(new LabelWidget
		{
			Text = "Pick a game on your network, or connect to an address on the internet.",
			Font = font2,
			HorizontalAlignment = WidgetAlignment.Center,
			Color = new Color(200, 200, 200),
			Margin = new Vector2(0f, 2f)
		});
		CanvasWidget canvasWidget2 = new CanvasWidget
		{
			Size = new Vector2(760f, 230f),
			Margin = new Vector2(0f, 8f)
		};
		stackPanelWidget.Children.Add(canvasWidget2);
		canvasWidget2.Children.Add(new BevelledRectangleWidget
		{
			Size = new Vector2(760f, 230f),
			CenterColor = new Color(16, 20, 16, 255),
			BevelColor = new Color(50, 60, 50, 255),
			AmbientLight = 1f
		});
		m_gamesList = new ListPanelWidget
		{
			Direction = LayoutDirection.Vertical,
			Margin = new Vector2(4f, 4f),
			ItemSize = 46f,
			SelectionColor = new Color(10, 70, 0, 120)
		};
		m_gamesList.ItemWidgetFactory = delegate(object item)
		{
			MultiplayerGameInfo multiplayerGameInfo = (MultiplayerGameInfo)item;
			return new LabelWidget
			{
				Text = multiplayerGameInfo.DisplayName,
				Font = font2,
				VerticalAlignment = WidgetAlignment.Center,
				HorizontalAlignment = WidgetAlignment.Near,
				Margin = new Vector2(12f, 0f),
				Color = Color.White
			};
		};
		canvasWidget2.Children.Add(m_gamesList);
		StackPanelWidget stackPanelWidget2 = new StackPanelWidget
		{
			Direction = LayoutDirection.Horizontal,
			HorizontalAlignment = WidgetAlignment.Center,
			Margin = new Vector2(0f, 6f)
		};
		stackPanelWidget.Children.Add(stackPanelWidget2);
		stackPanelWidget2.Children.Add(new LabelWidget
		{
			Text = "Your name:",
			Font = font2,
			VerticalAlignment = WidgetAlignment.Center,
			Color = Color.White
		});
		CanvasWidget canvasWidget3 = new CanvasWidget
		{
			Size = new Vector2(300f, 50f),
			Margin = new Vector2(10f, 0f)
		};
		stackPanelWidget2.Children.Add(canvasWidget3);
		canvasWidget3.Children.Add(new BevelledRectangleWidget
		{
			Size = new Vector2(300f, 50f),
			CenterColor = new Color(12, 16, 12, 255),
			BevelColor = new Color(50, 60, 50, 255),
			AmbientLight = 1f
		});
		m_nameBox = new TextBoxWidget
		{
			Title = "Your name",
			Size = new Vector2(280f, 50f),
			VerticalAlignment = WidgetAlignment.Center,
			HorizontalAlignment = WidgetAlignment.Center,
			Font = font2,
			MaximumLength = 14
		};
		m_nameBox.Text = MakeDefaultName();
		canvasWidget3.Children.Add(m_nameBox);
		StackPanelWidget stackPanelWidget4 = new StackPanelWidget
		{
			Direction = LayoutDirection.Horizontal,
			HorizontalAlignment = WidgetAlignment.Center,
			Margin = new Vector2(0f, 6f)
		};
		stackPanelWidget.Children.Add(stackPanelWidget4);
		stackPanelWidget4.Children.Add(new LabelWidget
		{
			Text = "Address:",
			Font = font2,
			VerticalAlignment = WidgetAlignment.Center,
			Color = Color.White
		});
		CanvasWidget canvasWidget4 = new CanvasWidget
		{
			Size = new Vector2(320f, 50f),
			Margin = new Vector2(8f, 0f)
		};
		stackPanelWidget4.Children.Add(canvasWidget4);
		canvasWidget4.Children.Add(new BevelledRectangleWidget
		{
			Size = new Vector2(320f, 50f),
			CenterColor = new Color(12, 16, 12, 255),
			BevelColor = new Color(50, 60, 50, 255),
			AmbientLight = 1f
		});
		m_addressBox = new TextBoxWidget
		{
			Title = "Host address (name or IP)",
			Size = new Vector2(300f, 50f),
			VerticalAlignment = WidgetAlignment.Center,
			HorizontalAlignment = WidgetAlignment.Center,
			Font = font2,
			MaximumLength = 128
		};
		m_addressBox.Text = s_lastAddress;
		canvasWidget4.Children.Add(m_addressBox);
		stackPanelWidget4.Children.Add(new LabelWidget
		{
			Text = "Port:",
			Font = font2,
			VerticalAlignment = WidgetAlignment.Center,
			Color = Color.White,
			Margin = new Vector2(8f, 0f)
		});
		CanvasWidget canvasWidget5 = new CanvasWidget
		{
			Size = new Vector2(110f, 50f),
			Margin = new Vector2(6f, 0f)
		};
		stackPanelWidget4.Children.Add(canvasWidget5);
		canvasWidget5.Children.Add(new BevelledRectangleWidget
		{
			Size = new Vector2(110f, 50f),
			CenterColor = new Color(12, 16, 12, 255),
			BevelColor = new Color(50, 60, 50, 255),
			AmbientLight = 1f
		});
		m_portBox = new TextBoxWidget
		{
			Title = "Port",
			Size = new Vector2(96f, 50f),
			VerticalAlignment = WidgetAlignment.Center,
			HorizontalAlignment = WidgetAlignment.Center,
			Font = font2,
			MaximumLength = 5
		};
		m_portBox.Text = s_lastPort;
		canvasWidget5.Children.Add(m_portBox);
		m_connectButton = CreateButton("Connect", 150f);
		m_connectButton.Margin = new Vector2(10f, 0f);
		stackPanelWidget4.Children.Add(m_connectButton);
		m_statusLabel = new LabelWidget
		{
			Text = string.Empty,
			Font = font2,
			HorizontalAlignment = WidgetAlignment.Center,
			Color = new Color(230, 220, 160),
			Margin = new Vector2(0f, 4f)
		};
		stackPanelWidget.Children.Add(m_statusLabel);
		StackPanelWidget stackPanelWidget3 = new StackPanelWidget
		{
			Direction = LayoutDirection.Horizontal,
			HorizontalAlignment = WidgetAlignment.Center,
			Margin = new Vector2(0f, 6f)
		};
		stackPanelWidget.Children.Add(stackPanelWidget3);
		m_refreshButton = CreateButton("Refresh", 150f);
		m_joinButton = CreateButton("Join Selected", 190f);
		m_backButton = CreateButton("Back", 150f);
		stackPanelWidget3.Children.Add(m_refreshButton);
		stackPanelWidget3.Children.Add(new CanvasWidget
		{
			Size = new Vector2(12f, 0f)
		});
		stackPanelWidget3.Children.Add(m_joinButton);
		stackPanelWidget3.Children.Add(new CanvasWidget
		{
			Size = new Vector2(12f, 0f)
		});
		stackPanelWidget3.Children.Add(m_backButton);
	}

	private static BevelledButtonWidget CreateButton(string text, float width)
	{
		return new BevelledButtonWidget
		{
			Text = text,
			Size = new Vector2(width, 52f)
		};
	}

	private static string MakeDefaultName()
	{
		string text = MultiplayerManager.PlayerName;
		if (!PlayerData.VerifyName(text))
		{
			return "Player";
		}
		return text;
	}

	public override void Enter(object[] parameters)
	{
		m_switchedToGame = false;
		m_joinFailedShown = false;
		MultiplayerManager.CancelJoin();
		// Remove leftover client caches from a previous crash before the list
		// is built, otherwise every crashed join would show up as a world.
		MultiplayerManager.SweepStaleClientWorlds();
		m_addressBox.Text = s_lastAddress;
		m_portBox.Text = s_lastPort;
		RefreshGames(force: true);
	}

	public override void Leave()
	{
		MultiplayerDiscovery.StopProbe();
		// If the player backed out while a download was still running, make
		// sure the socket and the imported cache world are released. A screen
		// switch caused by a successful download must keep its result.
		if (!m_switchedToGame)
		{
			MultiplayerManager.CancelJoin();
		}
	}

	public override void Update()
	{
		if (m_switchedToGame)
		{
			return;
		}
		MultiplayerJoinResult multiplayerJoinResult = MultiplayerManager.PollJoin();
		if (multiplayerJoinResult != null)
		{
			if (multiplayerJoinResult.Success)
			{
				m_switchedToGame = true;
				m_statusLabel.Text = "World downloaded, loading...";
				ScreensManager.SwitchScreen("GameLoading", multiplayerJoinResult.WorldInfo, null);
				return;
			}
			if (!m_joinFailedShown)
			{
				m_joinFailedShown = true;
				m_statusLabel.Text = multiplayerJoinResult.Error;
				DialogsManager.ShowDialog(null, new MessageDialog("Could not join", multiplayerJoinResult.Error, "OK", null, null));
			}
			MultiplayerManager.CancelJoin();
		}
		if (MultiplayerManager.IsJoining)
		{
			m_statusLabel.Text = "Downloading world from host...";
		}
		if (m_refreshButton.IsClicked)
		{
			RefreshGames(force: true);
		}
		if (m_backButton.IsClicked)
		{
			MultiplayerManager.CancelJoin();
			ScreensManager.SwitchScreen("Play");
		}
		if (m_joinButton.IsClicked)
		{
			JoinSelectedGame();
		}
		if (m_connectButton.IsClicked)
		{
			ConnectDirect();
		}
		if (Time.RealTime - m_lastRefreshTime > 1.5)
		{
			RefreshGames(force: false);
		}
	}

	private string GetPlayerName()
	{
		string text = m_nameBox.Text;
		if (!PlayerData.VerifyName(text))
		{
			text = "Player";
			m_nameBox.Text = text;
		}
		return text;
	}

	private void JoinSelectedGame()
	{
		if (MultiplayerManager.IsJoining)
		{
			return;
		}
		MultiplayerGameInfo multiplayerGameInfo = m_gamesList.SelectedItem as MultiplayerGameInfo;
		if (multiplayerGameInfo == null)
		{
			m_statusLabel.Text = "Select a game first, or use the address field.";
			return;
		}
		s_lastAddress = multiplayerGameInfo.Address;
		s_lastPort = multiplayerGameInfo.Port.ToString();
		m_addressBox.Text = s_lastAddress;
		m_portBox.Text = s_lastPort;
		m_joinFailedShown = false;
		m_statusLabel.Text = "Connecting to " + multiplayerGameInfo.Address + "...";
		MultiplayerManager.StartJoin(multiplayerGameInfo.Address, multiplayerGameInfo.Port, GetPlayerName());
	}

	private void ConnectDirect()
	{
		if (MultiplayerManager.IsJoining)
		{
			return;
		}
		if (!MultiplayerManager.TryParseAddress(m_addressBox.Text, m_portBox.Text, out string text, out int num, out string error))
		{
			m_statusLabel.Text = error;
			DialogsManager.ShowDialog(null, new MessageDialog("Invalid address", error, "OK", null, null));
			return;
		}
		s_lastAddress = text;
		s_lastPort = num.ToString();
		m_addressBox.Text = s_lastAddress;
		m_portBox.Text = s_lastPort;
		m_joinFailedShown = false;
		m_statusLabel.Text = "Connecting to " + text + ":" + num + "...";
		MultiplayerManager.StartJoin(text, num, GetPlayerName());
	}

	private void RefreshGames(bool force)
	{
		if (!force && MultiplayerManager.IsJoining)
		{
			return;
		}
		m_lastRefreshTime = Time.RealTime;
		MultiplayerDiscovery.StartProbe();
		IReadOnlyList<MultiplayerGameInfo> games = MultiplayerDiscovery.GetGames();
		if (!force && games.Count == m_games.Count)
		{
			bool flag = true;
			for (int i = 0; i < games.Count; i++)
			{
				if (games[i] != m_games[i])
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				return;
			}
		}
		object selectedItem = m_gamesList.SelectedItem;
		m_games = new List<MultiplayerGameInfo>(games);
		m_gamesList.ClearItems();
		foreach (MultiplayerGameInfo game in m_games)
		{
			m_gamesList.AddItem(game);
		}
		if (m_games.Count == 0 && !MultiplayerManager.IsJoining)
		{
			m_statusLabel.Text = "No games on the local network. Use the address field for internet play.";
		}
		else if (selectedItem != null && m_gamesList.SelectedItem == null)
		{
			foreach (MultiplayerGameInfo game2 in m_games)
			{
				if (game2.Equals(selectedItem))
				{
					m_gamesList.SelectedItem = game2;
					break;
				}
			}
		}
	}
}

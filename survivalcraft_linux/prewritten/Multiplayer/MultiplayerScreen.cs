using System;
using System.Collections.Generic;
using Engine;
using Engine.Media;

namespace Game;

/// <summary>
/// "Join LAN game" screen. Lists hosts discovered on the local network and
/// downloads the selected world before handing it to the normal loading screen.
/// </summary>
public class MultiplayerScreen : Screen
{
	private readonly ListPanelWidget m_gamesList;

	private readonly TextBoxWidget m_nameBox;

	private readonly LabelWidget m_statusLabel;

	private readonly ButtonWidget m_joinButton;

	private readonly ButtonWidget m_refreshButton;

	private readonly ButtonWidget m_backButton;

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
			Size = new Vector2(780f, 560f),
			HorizontalAlignment = WidgetAlignment.Center,
			VerticalAlignment = WidgetAlignment.Center
		};
		Children.Add(canvasWidget);
		canvasWidget.Children.Add(new BevelledRectangleWidget
		{
			Size = new Vector2(780f, 560f),
			CenterColor = new Color(32, 40, 32, 255),
			BevelColor = new Color(64, 78, 64, 255),
			AmbientLight = 1f
		});
		StackPanelWidget stackPanelWidget = new StackPanelWidget
		{
			Direction = LayoutDirection.Vertical,
			HorizontalAlignment = WidgetAlignment.Center,
			Margin = new Vector2(20f, 14f)
		};
		canvasWidget.Children.Add(stackPanelWidget);
		BitmapFont font = ContentManager.Get<BitmapFont>("Fonts/Pericles24");
		BitmapFont font2 = ContentManager.Get<BitmapFont>("Fonts/Pericles18");
		stackPanelWidget.Children.Add(new LabelWidget
		{
			Text = "Join LAN Game",
			Font = font,
			HorizontalAlignment = WidgetAlignment.Center,
			Color = Color.White,
			Margin = new Vector2(0f, 4f)
		});
		stackPanelWidget.Children.Add(new LabelWidget
		{
			Text = "Select a world hosted on your local network.",
			Font = font2,
			HorizontalAlignment = WidgetAlignment.Center,
			Color = new Color(200, 200, 200),
			Margin = new Vector2(0f, 2f)
		});
		CanvasWidget canvasWidget2 = new CanvasWidget
		{
			Size = new Vector2(720f, 250f),
			Margin = new Vector2(0f, 10f)
		};
		stackPanelWidget.Children.Add(canvasWidget2);
		canvasWidget2.Children.Add(new BevelledRectangleWidget
		{
			Size = new Vector2(720f, 250f),
			CenterColor = new Color(16, 20, 16, 255),
			BevelColor = new Color(50, 60, 50, 255),
			AmbientLight = 1f
		});
		m_gamesList = new ListPanelWidget
		{
			Direction = LayoutDirection.Vertical,
			Margin = new Vector2(4f, 4f),
			ItemSize = 52f,
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
			Margin = new Vector2(0f, 10f)
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
			Margin = new Vector2(0f, 8f)
		};
		stackPanelWidget.Children.Add(stackPanelWidget3);
		m_refreshButton = CreateButton("Refresh", 150f);
		m_joinButton = CreateButton("Join", 150f);
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
			Size = new Vector2(width, 56f)
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
		RefreshGames(force: true);
	}

	public override void Leave()
	{
		MultiplayerDiscovery.StopProbe();
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
		if (Time.RealTime - m_lastRefreshTime > 1.5)
		{
			RefreshGames(force: false);
		}
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
			m_statusLabel.Text = "Select a game first.";
			return;
		}
		string text = m_nameBox.Text;
		if (!PlayerData.VerifyName(text))
		{
			text = "Player";
			m_nameBox.Text = text;
		}
		m_joinFailedShown = false;
		m_statusLabel.Text = "Connecting to " + multiplayerGameInfo.Address + "...";
		MultiplayerManager.StartJoin(multiplayerGameInfo.Address, multiplayerGameInfo.Port, text);
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
			m_statusLabel.Text = "Searching the local network...";
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

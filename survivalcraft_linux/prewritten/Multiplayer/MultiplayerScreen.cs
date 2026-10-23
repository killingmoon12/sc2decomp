#region // =====================================================================
// MultiplayerScreen - client join UI (reused from opencode/survivalcraft multiplayer branch)
// Hooked into PlayScreen via a minimal patch; netcode lives in MultiplayerSession.
// =====================================================================
#endregion

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using Engine;
using Multiplayer;

namespace Game
{
	public class MultiplayerScreen : Screen
	{
		private TextBoxWidget m_ipTextBox;
		private TextBoxWidget m_portTextBox;
		private LabelWidget m_portTipLabel;
		private ButtonWidget m_connectButton;
		private ButtonWidget m_scanButton;
		private bool m_scanning;
		private LabelWidget m_statusLabel;

		public MultiplayerScreen()
		{
			XElement node = XElement.Parse("""
				<Screen xmlns="runtime-namespace:Game">
					<PanoramaWidget />
					<StackPanelWidget Direction="Horizontal">
						<CanvasWidget Style="{Widgets/TopBarContents}">
							<LabelWidget Name="TopBar.Label" Text="Multiplayer" />
						</CanvasWidget>
						<StackPanelWidget Direction="Vertical" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="10, 5">
							<StackPanelWidget Direction="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center">
								<LabelWidget Size="140, 32" TextAnchor="Right, VerticalCenter" VerticalAlignment="Center" Font="{Fonts/Pericles18}" Text="Server IP:" Margin="10, 0" />
								<CanvasWidget VerticalAlignment="Center" Size="400, 50" Margin="9, 5">
									<BevelledRectangleWidget Style="{Styles/TextBoxArea}" />
									<TextBoxWidget Name="Ip" Title="Server IP Address" Size="380, 50" VerticalAlignment="Center" Font="{Fonts/Pericles24}" MaximumLength="64" Margin="10, 0" />
								</CanvasWidget>
							</StackPanelWidget>
							<StackPanelWidget Direction="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center">
								<LabelWidget Size="140, 32" TextAnchor="Right, VerticalCenter" VerticalAlignment="Center" Font="{Fonts/Pericles18}" Text="Server Port:" Margin="10, 0" />
								<CanvasWidget VerticalAlignment="Center" Size="400, 50" Margin="9, 5">
									<BevelledRectangleWidget Style="{Styles/TextBoxArea}" />
									<TextBoxWidget Name="Port" Title="Server Port" Description="Leave blank to use 55551." Size="380, 50" VerticalAlignment="Center" Font="{Fonts/Pericles24}" MaximumLength="5" Margin="10, 0" />
									<LabelWidget Name="PortTip" HorizontalAlignment="Center" VerticalAlignment="Center" Font="{Fonts/Pericles18}" Text="Leave blank for 55551" Color="96, 96, 96" />
								</CanvasWidget>
							</StackPanelWidget>
							<CanvasWidget Size="0, Infinity" />
							<CanvasWidget HorizontalAlignment="Center" Margin="0, 2">
								<BevelledButtonWidget Name="Connect" Style="{Styles/ButtonStyle_310x60}" Text="Connect" VerticalAlignment="Center" HorizontalAlignment="Center">
									<BevelledRectangleWidget Name="BevelledButton.Rectangle" CenterColor="50, 150, 35" BevelColor="50, 150, 35" />
								</BevelledButtonWidget>
							</CanvasWidget>
							<CanvasWidget HorizontalAlignment="Center" Margin="0, 2">
								<BevelledButtonWidget Name="Scan" Style="{Styles/ButtonStyle_310x60}" Text="Find LAN Games" VerticalAlignment="Center" HorizontalAlignment="Center" />
							</CanvasWidget>
							<LabelWidget Name="Status" HorizontalAlignment="Center" VerticalAlignment="Center" Font="{Fonts/Pericles18}" Color="96, 96, 96" Text="Not connected." Margin="0, 5" />
						</StackPanelWidget>
					</StackPanelWidget>
				</Screen>
				""");
			LoadContents(this, node);
			m_ipTextBox = Children.Find<TextBoxWidget>("Ip");
			m_portTextBox = Children.Find<TextBoxWidget>("Port");
			m_portTipLabel = Children.Find<LabelWidget>("PortTip");
			m_connectButton = Children.Find<ButtonWidget>("Connect");
			m_scanButton = Children.Find<ButtonWidget>("Scan");
			m_statusLabel = Children.Find<LabelWidget>("Status");
		}

		public override void Update()
		{
			m_portTipLabel.IsVisible = m_portTextBox.Text.Length == 0 && !m_portTextBox.HasFocus;
			bool active = MultiplayerSession.IsActive;
			m_connectButton.IsEnabled = !active;
			m_scanButton.IsEnabled = !active && !m_scanning;
			if (!active && !m_scanning && m_scanButton.IsClicked)
			{
				m_scanning = true;
				m_scanButton.Text = "Searching...";
				_ = ScanLanAsync();
			}
			if (!active && m_connectButton.IsClicked)
			{
				string host = m_ipTextBox.Text.Trim();
				if (host.Length == 0) host = "127.0.0.1";
				MultiplayerSession.StartClient(host, GetPort());
			}
			m_statusLabel.Text = MultiplayerSession.Status;
			if (base.Input.Back || base.Input.Cancel || Children.Find<ButtonWidget>("TopBar.Back").IsClicked)
			{
				MultiplayerSession.Stop();
				ScreensManager.SwitchScreen("Play");
			}
		}

		private async Task ScanLanAsync()
		{
			List<LanHostInfo> hosts = await MultiplayerSession.DiscoverLanHostsAsync();
			Dispatcher.Dispatch(delegate
			{
				m_scanning = false;
				m_scanButton.Text = "Find LAN Games";
				if (hosts.Count == 0)
				{
					DialogsManager.ShowDialog(null, new MessageDialog("LAN Games", "No games found on the local network.", "OK", null, null));
				}
				else if (hosts.Count == 1)
				{
					UseLanHost(hosts[0]);
				}
				else
				{
					Func<object, string> itemToString = (object item) => ((LanHostInfo)item).ToString();
					DialogsManager.ShowDialog(null, new ListSelectionDialog("LAN Games", hosts, 52f, itemToString, delegate(object item)
					{
						UseLanHost((LanHostInfo)item);
					}));
				}
			});
		}

		private void UseLanHost(LanHostInfo host)
		{
			m_ipTextBox.Text = host.Address;
			m_portTextBox.Text = host.Port.ToString();
			m_statusLabel.Text = $"Found {host.Name} at {host.Address}:{host.Port}.";
		}

		private int GetPort()
		{
			if (string.IsNullOrWhiteSpace(m_portTextBox.Text)) return MultiplayerSession.DefaultPort;
			if (int.TryParse(m_portTextBox.Text.Trim(), out int port)) return port;
			return 0;
		}
	}
}

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace NXInput_Server
{
    public partial class Form1 : Form
    {
        private static string L(string zh,string en) =>
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? zh : en;

        private static ViGEmClient vigemClient;
        public static IXbox360Controller controller;
        private static Thread networkThread;
        private static UdpClient udpSocket;
        private static UdpClient discoverySocket;
        private static Thread discoveryThread;
        private static volatile bool running = false;
        private static bool switchConnected = false;
        private static IPEndPoint clientEndPoint;

        private bool _serverRunning;

        private sealed class AdapterAddress
        {
            public string Id { get; }
            public string Name { get; }
            public IPAddress Address { get; }
            public IPAddress Mask { get; }
            public string Gateways { get; }

            public AdapterAddress(string id, string name, IPAddress address, IPAddress mask, string gateways)
            {
                Id = id;
                Name = name;
                Address = address;
                Mask = mask;
                Gateways = gateways;
            }

            public override string ToString() => Name + " — " + Address;
        }


        public Form1()
        {
            InitializeComponent();
            var credit = new LinkLabel { Text = "GitHub @Yukinsnow", AutoSize = true,
                Location = new System.Drawing.Point(15, 145) };
            credit.LinkClicked += (sender,args) => System.Diagnostics.Process.Start("https://github.com/Yukinsnow");
            Controls.Add(credit);
            FormClosing += (sender, args) => StopServer();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            RefreshNetworkAdapters();
        }

        private void RefreshNetworkAdapters()
        {
            if (_serverRunning) return;
            var previous = adapterComboBox.SelectedItem as AdapterAddress;
            adapterComboBox.Items.Clear();
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                    .Where(a => a.OperationalStatus == OperationalStatus.Up && a.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .OrderBy(a => a.Name))
                {
                    var properties = adapter.GetIPProperties();
                    string gateways = string.Join(", ", properties.GatewayAddresses
                        .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                        .Select(g => g.Address.ToString()));
                    foreach (var address in properties.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)))
                    {
                        adapterComboBox.Items.Add(new AdapterAddress(adapter.Id, adapter.Name,
                            address.Address, address.IPv4Mask, gateways));
                    }
                }
                if (previous != null)
                {
                    for (int index = 0; index < adapterComboBox.Items.Count; index++)
                    {
                        var candidate = (AdapterAddress)adapterComboBox.Items[index];
                        if (candidate.Id == previous.Id && candidate.Address.Equals(previous.Address))
                        {
                            adapterComboBox.SelectedIndex = index;
                            break;
                        }
                    }
                }
                // Multiple interfaces require an explicit choice; do not guess a route.
                if (adapterComboBox.SelectedIndex < 0 && adapterComboBox.Items.Count == 1)
                    adapterComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log(L("读取网卡失败：","Could not read adapters: ") + ex.Message);
            }
            UpdateSelectedAdapter();
        }

        private void UpdateSelectedAdapter()
        {
            var adapter = adapterComboBox.SelectedItem as AdapterAddress;
            label2.Text = adapter == null
                ? (adapterComboBox.Items.Count == 0 ? L("没有可用的 IPv4 网卡。连接网络后点击刷新。","No active IPv4 adapter. Connect to a network and refresh.") : L("请选择 Switch 可以访问的网卡，然后启动服务。","Select an adapter the Switch can reach, then start the server."))
                : L("Switch 中填写此 IP：","Enter this IP on the Switch: ") + adapter.Address + L("    UDP：8192 / 自动发现：8193\n","    UDP: 8192 / Discovery: 8193\n")
                    + L("子网掩码：","Subnet mask: ") + adapter.Mask + L("    网关：","    Gateway: ") + (string.IsNullOrEmpty(adapter.Gateways) ? L("无","None") : adapter.Gateways)
                    + L("\n请与 Switch 网络设置对照；网关不同不一定不通，跨子网需要路由可达。","\nCompare with the Switch network. Different subnets require a reachable route.");
            button1.Enabled = _serverRunning || adapter != null;
        }

        private UdpClient CreateSelectedListener()
        {
            var adapter = adapterComboBox.SelectedItem as AdapterAddress;
            if (adapter == null) throw new InvalidOperationException(L("请先选择网卡和 IPv4 地址。","Select an adapter and IPv4 address first."));
            var listener = new UdpClient(new IPEndPoint(adapter.Address, 8192));
            listener.Client.ReceiveTimeout = 500;
            return listener;
        }

        private static bool SameSubnet(IPAddress peer, AdapterAddress adapter)
        {
            if (adapter.Mask == null || peer.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] a = peer.GetAddressBytes(), b = adapter.Address.GetAddressBytes(), mask = adapter.Mask.GetAddressBytes();
            if (mask.All(value => value == 0)) return false;
            for (int i = 0; i < 4; i++)
                if ((a[i] & mask[i]) != (b[i] & mask[i])) return false;
            return true;
        }

        private void DiscoverServers(AdapterAddress selectedAdapter)
        {
            byte[] reply = Encoding.ASCII.GetBytes("xbox\0");
            while (running)
            {
                try
                {
                    var peer = new IPEndPoint(IPAddress.Any, 0);
                    byte[] request = discoverySocket.Receive(ref peer);
                    if (Encoding.ASCII.GetString(request).TrimEnd('\0') != "xbox_switch") continue;
                    if (!SameSubnet(peer.Address, selectedAdapter)) continue;
                    // Reply from the chosen address/8192; client then handshakes there.
                    udpSocket.Send(reply, reply.Length, peer);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.TimedOut) break;
                }
                catch (ObjectDisposedException) { break; }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (!_serverRunning)
                StartServer();
            else
                StopServer();
        }



        private void StartServer()
        {
            if (_serverRunning) return;

            try
            {
                button1.Enabled = false;
                // Bind synchronously: report a stale address/occupied port before starting.
                udpSocket = CreateSelectedListener();
                discoverySocket = new UdpClient(new IPEndPoint(IPAddress.Any, 8193));
                discoverySocket.Client.ReceiveTimeout = 500;

                vigemClient = new ViGEmClient();
                controller = vigemClient.CreateXbox360Controller();
                controller.Connect();
                Log(L("虚拟手柄已创建。", "Virtual controller created."));

                running = true;
                switchConnected = false;
                adapterComboBox.Enabled = false;
                refreshButton.Enabled = false;
                _serverRunning = true;
                Log(L("监听：","Listening on: ") + udpSocket.Client.LocalEndPoint);
                Log(L("自动发现端口：UDP 8193（仅响应所选网卡子网）。","Discovery: UDP 8193 (responds only within the selected adapter subnet)."));
                var selectedAdapter = (AdapterAddress)adapterComboBox.SelectedItem;
                discoveryThread = new Thread(() => DiscoverServers(selectedAdapter)) { IsBackground = true };
                discoveryThread.Start();

                networkThread = new Thread(networking) { IsBackground = true };
                networkThread.Start();

                button1.Text = L("停止服务","Stop server");
            }
            catch (Exception ex)
            {
                Log(L("启动服务失败：", "Error starting server: ") + ex.Message);
                StopServer();
            }
            finally
            {
                UpdateSelectedAdapter();
            }
        }

        private void StopServer()
        {
            try
            {
                running = false;
                switchConnected = false;

                udpSocket?.Close();
                discoverySocket?.Close();
                networkThread?.Join(1000);
                discoveryThread?.Join(1000);
                networkThread = null;
                discoveryThread = null;
                udpSocket = null;
                discoverySocket = null;

                controller?.Disconnect();
                vigemClient?.Dispose();
                controller = null;
                vigemClient = null;

                Log(L("服务已停止。", "Server stopped."));
            }
            catch (Exception ex)
            {
                Log(L("停止服务失败：", "Error stopping server: ") + ex.Message);
            }
            finally
            {
                _serverRunning = false;
                button1.Text = L("启动服务","Start server");
                adapterComboBox.Enabled = true;
                refreshButton.Enabled = true;
                UpdateSelectedAdapter();
            }
        }

        private void networking()
        {
            byte[] data = new byte[1024];

            try
            {
                var sender = new IPEndPoint(IPAddress.Any, 0);

                Log(L("等待 Switch 连接…", "Waiting for Switch discovery..."));

                while (!switchConnected && running)
                {
                    try
                    {
                        data = udpSocket.Receive(ref sender);
                        string msg = Encoding.ASCII.GetString(data).Trim();

                        if (msg.Contains("xbox_switch"))
                        {
                            udpSocket.Send(Encoding.ASCII.GetBytes("xbox\0"), Encoding.ASCII.GetBytes("xbox\0").Length, sender);
                            clientEndPoint = (IPEndPoint)sender;
                            switchConnected = true;
                            Log(L("已连接：", "Connected to: ") + clientEndPoint.Address);
                            break;
                        }
                    }
                    catch {}
                }

                if (!switchConnected)
                {
                    Log(L("Switch 连接未完成。", "Switch discovery failed"));
                    return;
                }

                Log(L("正在接收输入…", "Receiving input..."));

                while (running)
                {
                    try
                    {
                        data = udpSocket.Receive(ref sender);

                        if (sender.Address.Equals(clientEndPoint.Address) &&
                            Encoding.ASCII.GetString(data).TrimEnd('\0') == "xbox_switch")
                        {
                            ReleaseInputs();
                            clientEndPoint = new IPEndPoint(sender.Address, sender.Port);
                            byte[] reply = Encoding.ASCII.GetBytes("xbox\0");
                            udpSocket.Send(reply, reply.Length, sender);
                            Log(L("Switch 已重新连接：","Switch reconnected: ") + sender);
                            continue;
                        }
                        if (!sender.Equals(clientEndPoint)) continue;
                        if (data.Length < 2) continue;
                        if ((data[0] == 0x12 || data[0] == 0x13) && data.Length != 5) continue;
                        if (data[0] != 0x12 && data[0] != 0x13 && data.Length != 2) continue;
                        if (data[0] == 0 || data[0] == 0x0B || data[0] > 0x13) continue;

                        byte code = data[0];
                        byte v1 = data[1];

                        if (code <= 0x0B)
                        {
                            var btn = MapCodeToXboxButton(code);
                            controller.SetButtonState(btn, v1 != 0);
                        }
                        else if (code <= 0x0F)
                        {
                            var btn = MapCodeToXboxButton(code);
                            controller.SetButtonState(btn, v1 != 0);
                        }
                        else if (code == 0x10) // ZL
                        {
                            controller.SetSliderValue(Xbox360Slider.LeftTrigger, v1 == 0 ? (byte)0x00 : (byte)0xFF);
                        }
                        else if (code == 0x11) // ZR
                        {
                            controller.SetSliderValue(Xbox360Slider.RightTrigger, v1 == 0 ? (byte)0x00 : (byte)0xFF);
                        }
                        else if (code == 0x12) // Left stick
                        {
                            short lx = (short)((data[1] << 8) | data[2]);
                            short ly = (short)((data[3] << 8) | data[4]);
                            controller.SetAxisValue(Xbox360Axis.LeftThumbX, lx);
                            controller.SetAxisValue(Xbox360Axis.LeftThumbY, ly);
                        }
                        else if (code == 0x13) // Right stick
                        {
                            short rx = (short)((data[1] << 8) | data[2]);
                            short ry = (short)((data[3] << 8) | data[4]);
                            controller.SetAxisValue(Xbox360Axis.RightThumbX, rx);
                            controller.SetAxisValue(Xbox360Axis.RightThumbY, ry);
                        }
                    }
                    catch (SocketException ex)
                    {
                        if (ex.SocketErrorCode == SocketError.TimedOut)
                        {
                            ReleaseInputs();
                            continue;
                        }
                        break;
                    }
                    catch { /* ignore bad packets */ }
                }
            }
            catch (Exception ex)
            {
                Log(L("网络错误：", "Networking error: ") + ex.Message);
            }
        }




        private static void ReleaseInputs()
        {
            for (byte code = 1; code <= 0x0F; code++)
            {
                var button = MapCodeToXboxButton(code);
                if (button != null) controller.SetButtonState(button, false);
            }
            controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
            controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);
            controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
            controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
            controller.SetAxisValue(Xbox360Axis.RightThumbX, 0);
            controller.SetAxisValue(Xbox360Axis.RightThumbY, 0);
        }

        private static Xbox360Button MapCodeToXboxButton(byte code)
        {
            switch (code)
            {
                case 0x01: return Xbox360Button.Up;
                case 0x02: return Xbox360Button.Down;
                case 0x03: return Xbox360Button.Left;
                case 0x04: return Xbox360Button.Right;
                case 0x05: return Xbox360Button.Back;
                case 0x06: return Xbox360Button.Start;
                case 0x07: return Xbox360Button.LeftThumb;
                case 0x08: return Xbox360Button.RightThumb;
                case 0x09: return Xbox360Button.LeftShoulder;
                case 0x0A: return Xbox360Button.RightShoulder;
                case 0x0C: return Xbox360Button.B;
                case 0x0D: return Xbox360Button.A;
                case 0x0E: return Xbox360Button.Y;
                case 0x0F: return Xbox360Button.X;
                default: return null;
            };
        }

        private void SetButtonState(Xbox360Button btn, bool pressed)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetButtonState(btn, pressed)));
                return;
            }
            controller.SetButtonState(btn, pressed);
        }

        private void displayButtons()
        {
            while (running)
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(displayButtons));
                    Thread.Sleep(50);
                    continue;
                }
                Log("Controller active...");
                Thread.Sleep(100);
            }
        }

        private void Log(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Log(text)));
                return;
            }
            richTextBox1.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
            richTextBox1.ScrollToCaret();
        }

    }
}

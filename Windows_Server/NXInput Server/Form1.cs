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
        private ReceiverPreferences preferences = new ReceiverPreferences();
        private bool preferencesReady;
        private readonly string preferencesPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteLink", "settings.xml");
        private readonly CheckBox launchAtLogin = new CheckBox();
        private readonly CheckBox autoReceive = new CheckBox();
        private readonly CheckBox autoUpdate = new CheckBox();
        private readonly Button checkUpdate = new Button();
        private bool updateBusy, exitingForUpdate;
        private readonly System.Windows.Forms.Timer startupRetry = new System.Windows.Forms.Timer();
        private int retryCount;
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

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
            launchAtLogin.Text = L("登录 Windows 后自动启动 Lite Link", "Launch Lite Link when I sign in to Windows");
            launchAtLogin.Location = new System.Drawing.Point(15, 180); launchAtLogin.AutoSize = true;
            autoReceive.Text = L("打开应用后自动接收（使用记忆的网卡）", "Start receiving when the app opens (remembered adapter)");
            autoReceive.Location = new System.Drawing.Point(15, 212); autoReceive.AutoSize = true;
            Controls.Add(launchAtLogin); Controls.Add(autoReceive);
            autoUpdate.Text = L("打开时自动检查并安装正式版更新（空闲时重启）", "Install stable updates on launch (restarts when idle)");
            autoUpdate.Location = new System.Drawing.Point(15,244); autoUpdate.AutoSize = true;
            checkUpdate.Location = new System.Drawing.Point(623,240); checkUpdate.Size = new System.Drawing.Size(112,28);
            checkUpdate.Text = L("检查更新", "Check updates");
            checkUpdate.Click += async (sender,args) => await CheckForUpdates(false);
            autoUpdate.CheckedChanged += (sender,args) => { if (preferencesReady) SavePreferences(); };
            Controls.Add(autoUpdate); Controls.Add(checkUpdate);
            launchAtLogin.CheckedChanged += (sender,args) => {
                if (!preferencesReady) return;
                try {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) {
                        if (launchAtLogin.Checked) key.SetValue("LiteLink", ReceiverPreferences.StartupCommand(Application.ExecutablePath));
                        else key.DeleteValue("LiteLink", false);
                    }
                } catch (Exception ex) { Log(L("自启动设置失败：", "Startup setting failed: ") + ex.Message);
                    preferencesReady = false; launchAtLogin.Checked = !launchAtLogin.Checked; preferencesReady = true; }
            };
            autoReceive.CheckedChanged += (sender,args) => {
                if (!preferencesReady) return;
                preferences.AutoReceive = autoReceive.Checked; SavePreferences();
                if (!autoReceive.Checked) startupRetry.Stop();
            };
            startupRetry.Interval = 2000;
            startupRetry.Tick += (sender,args) => {
                if (updateBusy) return;
                RefreshNetworkAdapters();
                if (adapterComboBox.SelectedItem != null) { startupRetry.Stop(); StartServer(); }
                else if (++retryCount >= 30) { startupRetry.Stop(); Log(L("记忆的网卡未上线，请手动选择。", "Remembered adapter unavailable; select it manually.")); }
            };
            FormClosing += (sender, args) => { startupRetry.Stop(); SavePreferences(); StopServer(); };
            Disposed += (sender,args) => startupRetry.Dispose();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            preferences = ReceiverPreferences.Load(preferencesPath);
            autoReceive.Checked = preferences.AutoReceive;
            autoUpdate.Checked = preferences.AutoUpdate;
            try { using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                launchAtLogin.Checked = string.Equals(key?.GetValue("LiteLink") as string,
                    ReceiverPreferences.StartupCommand(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) { Log(ex.Message); }
            RefreshNetworkAdapters();
            preferencesReady = true;
            if (!string.IsNullOrEmpty(preferences.LastSwitchIp)) Log(L("上次连接的 Switch：", "Last Switch: ") + preferences.LastSwitchIp);
            BeginInvoke(new Action(async () => {
                bool failedUpdate = Environment.GetCommandLineArgs().Contains("--update-failed");
                if (failedUpdate) Log(L("上次更新失败，本次不自动重试。日志：%LOCALAPPDATA%/LiteLink/Updates", "Previous update failed; automatic retry skipped. Logs: %LOCALAPPDATA%/LiteLink/Updates"));
                if (preferences.AutoUpdate && !failedUpdate) await CheckForUpdates(true);
                if (IsDisposed || exitingForUpdate) return;
                if (preferences.AutoReceive) {
                    if (adapterComboBox.SelectedItem != null) StartServer();
                    else { retryCount = 0; startupRetry.Start(); Log(L("等待记忆的网卡上线…", "Waiting for the remembered adapter...")); }
                }
            }));
        }

        private void SavePreferences()
        {
            if (!preferencesReady) return;
            var selected = adapterComboBox.SelectedItem as AdapterAddress;
            if (selected != null) { preferences.AdapterId = selected.Id; preferences.Address = selected.Address.ToString(); }
            preferences.AutoReceive = autoReceive.Checked;
            preferences.AutoUpdate = autoUpdate.Checked;
            try { preferences.Save(preferencesPath); }
            catch (Exception ex) { Log(L("设置保存失败：", "Could not save preferences: ") + ex.Message); }
        }

        private void RememberSwitch(System.Net.IPAddress address)
        {
            if (!preferencesReady || IsDisposed) return;
            BeginInvoke(new Action(() => { preferences.LastSwitchIp = address.ToString(); SavePreferences(); }));
        }

        private async System.Threading.Tasks.Task CheckForUpdates(bool automatic)
        {
            if (updateBusy) return;
            updateBusy = true; checkUpdate.Enabled = false; UpdateSelectedAdapter();
            try {
                Log(L("正在检查 GitHub 正式版…", "Checking the latest stable GitHub release..."));
                var release = await ReleaseUpdater.Latest();
                if (IsDisposed) return;
                if (automatic && !autoUpdate.Checked) return;
                if (ReleaseUpdater.NormalizeVersion(release.tag_name) <= GetType().Assembly.GetName().Version) {
                    Log(L("当前已是最新版本。", "Already up to date.")); return;
                }
                if (_serverRunning) { Log(L("发现新版，请停止接收后再次检查并安装：", "Update found. Stop receiving, then check again to install: ") + release.tag_name); return; }
                if (!automatic && MessageBox.Show(this,L("下载并校验更新后将重启应用。是否继续？", "Download, verify and restart the app to update?"),
                    "Lite Link " + release.tag_name,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                Log(L("正在下载并校验更新…", "Downloading and verifying the update..."));
                string staged = await ReleaseUpdater.Stage(release);
                if (IsDisposed) return;
                if (automatic && !autoUpdate.Checked) { Log(L("已关闭自动更新，未安装下载内容。", "Automatic updates disabled; downloaded files were not installed.")); return; }
                SavePreferences();
                ReleaseUpdater.LaunchInstaller(staged,Application.ExecutablePath);
                exitingForUpdate = true; Application.Exit();
            } catch (Exception ex) { if (!IsDisposed) Log(L("更新失败，当前版本未替换：", "Update failed; current files were not replaced: ") + ex.Message); }
            finally { if (!IsDisposed) { updateBusy = false; checkUpdate.Enabled = true; UpdateSelectedAdapter(); } }
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
                string rememberedId = previous?.Id ?? preferences.AdapterId;
                string rememberedIp = previous?.Address.ToString() ?? preferences.Address;
                if (adapterComboBox.SelectedIndex < 0 && !string.IsNullOrEmpty(rememberedId)) {
                    var choices = adapterComboBox.Items.Cast<AdapterAddress>().Where(a => a.Id == rememberedId).ToArray();
                    var choice = choices.FirstOrDefault(a => a.Address.ToString() == rememberedIp)
                        ?? (choices.Length == 1 ? choices[0] : null);
                    if (choice != null) adapterComboBox.SelectedItem = choice;
                }
                // Multiple interfaces require an explicit choice; do not guess a route.
                if (adapterComboBox.SelectedIndex < 0 && adapterComboBox.Items.Count == 1 && string.IsNullOrEmpty(rememberedId))
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
            button1.Enabled = _serverRunning || (!updateBusy && adapter != null);
            if (adapter != null) SavePreferences();
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
            if (_serverRunning || updateBusy) return;
            startupRetry.Stop();
            SavePreferences();

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
                            RememberSwitch(clientEndPoint.Address);
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

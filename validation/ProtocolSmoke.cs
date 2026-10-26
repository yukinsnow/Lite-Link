using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Nefarius.ViGEm.Client.Targets;
using NXInput_Server;

sealed class Recorder : RealProxy
{
    public readonly List<string> Calls = new List<string>();
    public Recorder() : base(typeof(IXbox360Controller)) { }
    public override IMessage Invoke(IMessage message)
    {
        var call = (IMethodCallMessage)message;
        lock (Calls) Calls.Add(call.MethodName + ":" + string.Join(",", call.Args));
        return new ReturnMessage(null, null, 0, call.LogicalCallContext, call);
    }
    public bool Has(string text) { lock (Calls) return Calls.Exists(c => c == text); }
    public void Clear() { lock (Calls) Calls.Clear(); }
    public int Count { get { lock (Calls) return Calls.Count; } }
}

class ProtocolSmoke
{
    static readonly BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(5); }
    }
    static void Assert(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
    [STAThread]
    static int Main()
    {
        var recorder = new Recorder();
        var form = new Form1();
        var handle = form.Handle;
        var combo = (ComboBox)typeof(Form1).GetField("adapterComboBox", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
        var refresh = typeof(Form1).GetMethod("RefreshNetworkAdapters", BindingFlags.NonPublic | BindingFlags.Instance);
        refresh.Invoke(form, null);
        Assert(combo.Items.Count > 0, "enumerates active IPv4 interfaces");
        if (combo.Items.Count > 1) Assert(combo.SelectedIndex == -1, "multiple interfaces require explicit selection");
        combo.SelectedIndex = 0;
        string original = combo.SelectedItem.ToString();
        refresh.Invoke(form, null);
        Assert(combo.SelectedItem.ToString() == original, "refresh preserves selected interface and address");
        var adapterType = typeof(Form1).GetNestedType("AdapterAddress", BindingFlags.NonPublic);
        combo.Items.Clear();
        foreach (var address in new[] { "127.0.0.1", "127.0.0.2" })
            combo.Items.Add(Activator.CreateInstance(adapterType, new object[] { address, "Test interface", IPAddress.Parse(address), IPAddress.Parse("255.255.255.0"), "" }));
        combo.SelectedIndex = 1;
        var listener = (UdpClient)typeof(Form1).GetMethod("CreateSelectedListener", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
        Assert(((IPEndPoint)listener.Client.LocalEndPoint).Address.Equals(IPAddress.Parse("127.0.0.2")), "binds to explicitly selected second address");
        typeof(Form1).GetField("udpSocket", Hidden).SetValue(null, listener);
        Form1.controller = (IXbox360Controller)recorder.GetTransparentProxy();
        typeof(Form1).GetField("running", Hidden).SetValue(null, true);
        var discoverySocket = new UdpClient(new IPEndPoint(IPAddress.Any, 8193));
        discoverySocket.Client.ReceiveTimeout = 100;
        typeof(Form1).GetField("discoverySocket", Hidden).SetValue(null, discoverySocket);
        var discoveryThread = new Thread(() => typeof(Form1).GetMethod("DiscoverServers", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new[] { combo.SelectedItem }));
        discoveryThread.IsBackground = true;
        discoveryThread.Start();
        var thread = new Thread(() => typeof(Form1).GetMethod("networking", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null));
        thread.IsBackground = true;
        thread.Start();
        try
        {
            Pump(100);
            using (var probe = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0)))
            {
                probe.Client.ReceiveTimeout = 1000;
                byte[] query = Encoding.ASCII.GetBytes("xbox_switch\0");
                probe.Send(query, query.Length, new IPEndPoint(IPAddress.Loopback, 8193));
                var found = new IPEndPoint(IPAddress.Any, 0);
                Assert(Encoding.ASCII.GetString(probe.Receive(ref found)) == "xbox\0" && found.Address.Equals(IPAddress.Parse("127.0.0.2")) && found.Port == 8192,
                    "discovery advertises selected adapter and input port");
                Assert(!(bool)typeof(Form1).GetField("switchConnected", Hidden).GetValue(null), "discovery does not claim controller session");
            }
            using (var otherSubnet = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.1.1"), 0)))
            {
                otherSubnet.Client.ReceiveTimeout = 150;
                byte[] query = Encoding.ASCII.GetBytes("xbox_switch\0");
                otherSubnet.Send(query, query.Length, new IPEndPoint(IPAddress.Loopback, 8193));
                bool ignored = false;
                var found = new IPEndPoint(IPAddress.Any, 0);
                try { otherSubnet.Receive(ref found); }
                catch (SocketException ex) { ignored = ex.SocketErrorCode == SocketError.TimedOut; }
                Assert(ignored, "discovery ignores clients outside selected subnet");
            }
            using (var client = new UdpClient())
            {
                client.Connect(IPAddress.Parse("127.0.0.2"), 8192);
                client.Client.ReceiveTimeout = 2000;
                var discovery = Encoding.ASCII.GetBytes("xbox_switch\0");
                client.Send(discovery, discovery.Length);
                var remote = new IPEndPoint(IPAddress.Any, 0);
                Assert(Encoding.ASCII.GetString(client.Receive(ref remote)) == "xbox\0", "UDP discovery handshake");
                client.Send(new byte[] { 0x0D, 1 }, 2);
                client.Send(new byte[] { 0x12, 0x7F, 0xFF, 0x80, 0x00 }, 5);
                Pump(100);
                Assert(recorder.Has("SetButtonState:A,True"), "Switch B maps to Xbox A");
                Assert(recorder.Has("SetAxisValue:LeftThumbX,32767") && recorder.Has("SetAxisValue:LeftThumbY,-32768"), "signed big-endian stick decoding");
                recorder.Clear();
                client.Send(new byte[] { 0x12, 1 }, 2);
                using (var stranger = new UdpClient())
                {
                    stranger.Send(new byte[] { 0x0D, 1 }, 2, new IPEndPoint(IPAddress.Parse("127.0.0.2"), 8192));
                }
                Pump(100);
                Assert(recorder.Count == 0, "malformed packet and foreign endpoint ignored");
                Pump(550);
                Assert(recorder.Has("SetButtonState:A,False") && recorder.Has("SetAxisValue:LeftThumbX,0"), "timeout releases buttons and centers sticks");
                recorder.Clear();
                client.Send(new byte[] { 0x0D, 1 }, 2);
                Pump(100);
                Assert(recorder.Has("SetButtonState:A,True"), "input resumes after timeout");
                using (var replacement = new UdpClient())
                {
                    replacement.Connect(IPAddress.Parse("127.0.0.2"), 8192);
                    replacement.Client.ReceiveTimeout = 1000;
                    recorder.Clear();
                    replacement.Send(discovery, discovery.Length);
                    Assert(Encoding.ASCII.GetString(replacement.Receive(ref remote)) == "xbox\0", "same Switch reconnects using a new UDP port");
                    Pump(50);
                    Assert(recorder.Has("SetButtonState:A,False"), "reconnection releases previous input");
                    recorder.Clear();
                    client.Send(new byte[] { 0x0D, 1 }, 2);
                    Pump(50);
                    Assert(recorder.Count == 0, "old endpoint is ignored after reconnect");
                    replacement.Send(new byte[] { 0x0D, 1 }, 2);
                    Pump(50);
                    Assert(recorder.Has("SetButtonState:A,True"), "replacement endpoint controls virtual pad");
                }
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            typeof(Form1).GetField("running", Hidden).SetValue(null, false);
            var socket = (UdpClient)typeof(Form1).GetField("udpSocket", Hidden).GetValue(null);
            if (socket != null) socket.Close();
            discoverySocket.Close();
            thread.Join(1000);
            discoveryThread.Join(1000);
            form.Dispose();
        }
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NXInput_Server;
class SettingsUpdateSmoke
{
    static void Check(bool value,string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
    static int Main(string[] args)
    {
        string root=Path.Combine(Path.GetTempPath(),"LiteLink-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path=Path.Combine(root,"settings.xml");
        var saved=new ReceiverPreferences { AdapterId="adapter-guid", Address="192.168.5.20", LastSwitchIp="192.168.5.50", AutoReceive=true, AutoUpdate=true };
        saved.Save(path); saved.Save(path);
        var loaded=ReceiverPreferences.Load(path);
        Check(loaded.AdapterId==saved.AdapterId && loaded.Address==saved.Address && loaded.LastSwitchIp==saved.LastSwitchIp && loaded.AutoReceive && loaded.AutoUpdate,"Windows preferences survive repeated saves");
        Check(!ReceiverPreferences.Load(Path.Combine(root,"missing")).AutoReceive,"missing settings do not auto-start");
        File.WriteAllText(path,"broken xml");Check(!ReceiverPreferences.Load(path).AutoUpdate,"corrupt settings do not auto-update");
        Check(ReceiverPreferences.StartupCommand(@"C:\Test Folder\NXInput Server.exe")=="\"C:\\Test Folder\\NXInput Server.exe\" --startup","startup command quotes spaces");
        Check(ReleaseUpdater.NormalizeVersion("v0.4.0")>ReleaseUpdater.NormalizeVersion("0.3.0"),"update version comparison");
        Check(ReleaseUpdater.NormalizeVersion("v0.3.0")==new Version(0,3,0,0),"same version does not re-update");
        byte[] data=Encoding.ASCII.GetBytes("abc");
        string digest="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        ReleaseUpdater.Verify(data,ReleaseUpdater.ExpectedHash(digest+"  asset.zip\n","asset.zip"));
        bool rejected=false;try {ReleaseUpdater.Verify(data,new string('0',64));} catch(InvalidDataException) {rejected=true;}
        Check(rejected,"checksum mismatch rejected");
        using (var memory=new MemoryStream()) {
            using(var archive=new ZipArchive(memory,ZipArchiveMode.Create,true)) {
                foreach(string name in ReleaseUpdater.PayloadNames) using(var writer=new StreamWriter(archive.CreateEntry("Windows/"+name).Open())) writer.Write("payload");
                using(var writer=new StreamWriter(archive.CreateEntry("../escape.txt").Open())) writer.Write("bad");
            }
            string output=Path.Combine(root,"payload"); ReleaseUpdater.ExtractPayload(memory.ToArray(),output);
            Check(Directory.GetFiles(output).Length==3 && !File.Exists(Path.Combine(root,"escape.txt")),"only exact payload files extracted; path traversal ignored");
        }
        if(args.Contains("--network")) {
            try {
                var release=ReleaseUpdater.Latest().GetAwaiter().GetResult();
                Check(!release.draft && !release.prerelease,"live GitHub stable-release check");
            } catch(Exception ex) { Console.Error.WriteLine(ex.GetType().FullName);Console.Error.WriteLine(ex.Message);Console.Error.WriteLine(ex.InnerException?.Message);Console.Error.WriteLine(System.Net.ServicePointManager.SecurityProtocol);return 1; }
        }
        return 0;
    }
}

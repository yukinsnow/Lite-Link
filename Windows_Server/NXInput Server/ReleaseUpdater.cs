using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NXInput_Server
{
    public static class ReleaseUpdater
    {
        public const string Repository = "https://github.com/yukinsnow/Lite-Link";
        public static readonly string[] PayloadNames = { "NXInput Server.exe", "NXInput Server.exe.config", "Nefarius.ViGEm.Client.dll" };
        public sealed class Asset { public string name { get; set; } public string browser_download_url { get; set; } }
        public sealed class Release { public string tag_name { get; set; } public bool draft { get; set; } public bool prerelease { get; set; } public Asset[] assets { get; set; } }
        public static Version NormalizeVersion(string value)
        {
            Version version;
            if (!Version.TryParse((value ?? "").TrimStart('v', 'V'), out version)) throw new InvalidDataException("Invalid release version.");
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }
        public static string ExpectedHash(string manifest, string name)
        {
            foreach (string line in manifest.Split('\n')) {
                string trimmed = line.Trim();
                if (trimmed.Length < 66) continue;
                string hash = trimmed.Substring(0,64);
                if (trimmed.Substring(64).Trim().TrimStart('*') == name && hash.All(c => Uri.IsHexDigit(c))) return hash;
            }
            throw new InvalidDataException("Checksum not found for this asset.");
        }
        public static void Verify(byte[] data, string expected)
        {
            using (var hash = SHA256.Create()) {
                string actual = BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "");
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 checksum mismatch.");
            }
        }
        private static bool AllowedAsset(string url)
        {
            Uri parsed;
            return Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed.Scheme == "https" && parsed.Host == "github.com"
                && parsed.AbsolutePath.StartsWith("/yukinsnow/Lite-Link/releases/download/", StringComparison.OrdinalIgnoreCase);
        }
        private static async Task<byte[]> Fetch(string url, int limit)
        {
            // .NET Framework hosts can otherwise inherit the obsolete TLS 1.0 default.
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            using (var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(90)))
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) }) {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Lite-Link/0.3.0");
                for (int hop = 0; hop < 6; hop++) {
                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)) {
                        int code = (int)response.StatusCode;
                        if (code >= 300 && code <= 399 && response.Headers.Location != null) {
                            var next = new Uri(new Uri(url), response.Headers.Location);
                            if (next.Scheme != "https") throw new InvalidDataException("Insecure update redirect rejected.");
                            url = next.AbsoluteUri; continue;
                        }
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update exceeds the size limit.");
                        using (var input = await response.Content.ReadAsStreamAsync())
                        using (var output = new MemoryStream()) {
                            byte[] buffer = new byte[65536]; int count;
                            while ((count = await input.ReadAsync(buffer,0,buffer.Length,timeout.Token)) > 0) {
                                if (output.Length + count > limit) throw new InvalidDataException("Update exceeds the size limit.");
                                output.Write(buffer,0,count);
                            }
                            return output.ToArray();
                        }
                    }
                }
            }
            throw new InvalidDataException("Too many update redirects.");
        }
        public static async Task<Release> Latest()
        {
            byte[] json = await Fetch("https://api.github.com/repos/yukinsnow/Lite-Link/releases/latest", 1024*1024);
            var release = new JavaScriptSerializer().Deserialize<Release>(Encoding.UTF8.GetString(json));
            if (release == null || release.draft || release.prerelease) throw new InvalidDataException("No stable release available.");
            NormalizeVersion(release.tag_name); return release;
        }
        public static void ExtractPayload(byte[] zip, string directory)
        {
            Directory.CreateDirectory(directory);
            using (var memory = new MemoryStream(zip))
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Read)) {
                foreach (string name in PayloadNames) {
                    var matches = archive.Entries.Where(e => e.FullName == "Windows/" + name).ToArray();
                    if (matches.Length != 1 || matches[0].Length == 0 || matches[0].Length > 32*1024*1024) throw new InvalidDataException("Unexpected update payload.");
                    // Exact names only: never extract arbitrary archive paths or scripts.
                    using (var input = matches[0].Open())
                    using (var output = File.Create(Path.Combine(directory, name))) {
                        byte[] buffer=new byte[65536];int count;long total=0;
                        while((count=input.Read(buffer,0,buffer.Length))>0) {
                            total+=count;if(total>32*1024*1024) throw new InvalidDataException("Expanded update payload too large.");
                            output.Write(buffer,0,count);
                        }
                        if(total!=matches[0].Length) throw new InvalidDataException("Update payload size mismatch.");
                    }
                }
            }
        }
        public static async Task<string> Stage(Release release)
        {
            string name = "Lite-Link-" + release.tag_name + "-Windows.zip";
            var package = release.assets?.SingleOrDefault(a => a.name == name);
            var manifest = release.assets?.SingleOrDefault(a => a.name == "SHA256SUMS.txt");
            if (package == null || manifest == null || !AllowedAsset(package.browser_download_url) || !AllowedAsset(manifest.browser_download_url))
                throw new InvalidDataException("This release does not contain a supported update package and checksum.");
            string hashes = Encoding.UTF8.GetString(await Fetch(manifest.browser_download_url, 128*1024));
            byte[] zip = await Fetch(package.browser_download_url,64*1024*1024);
            Verify(zip,ExpectedHash(hashes,name));
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteLink", "Updates", Guid.NewGuid().ToString("N"));
            ExtractPayload(zip,Path.Combine(directory,"payload")); return directory;
        }
        public static string Quote(string value)
        {
            var text = new StringBuilder("\""); int slash = 0;
            foreach (char c in value) {
                if (c == '\\') { slash++; continue; }
                if (c == '"') text.Append('\\',slash*2+1).Append(c);
                else text.Append('\\',slash).Append(c);
                slash=0;
            }
            return text.Append('\\',slash*2).Append('"').ToString();
        }
        public static void LaunchInstaller(string stage, string executable)
        {
            if(Path.GetFileName(executable)!="NXInput Server.exe") throw new InvalidOperationException("Restore the original executable filename before updating.");
            string helper = Path.Combine(stage,"LiteLinkUpdate.exe");
            File.Copy(executable,helper);
            if (File.Exists(executable+".config")) File.Copy(executable+".config",helper+".config");
            string dependency=Path.Combine(Path.GetDirectoryName(executable),"Nefarius.ViGEm.Client.dll");
            if (File.Exists(dependency)) File.Copy(dependency,Path.Combine(stage,"Nefarius.ViGEm.Client.dll"));
            string args="--apply-update " + Quote(stage) + " " + Quote(executable) + " " + Process.GetCurrentProcess().Id;
            Process.Start(new ProcessStartInfo(helper,args) { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden });
        }
        public static int ApplyUpdate(string[] args)
        {
            if (args.Length!=4) return 2;
            string stage=Path.GetFullPath(args[1]), executable=Path.GetFullPath(args[2]);
            string allowed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LiteLink","Updates")+Path.DirectorySeparatorChar;
            if (!stage.StartsWith(allowed,StringComparison.OrdinalIgnoreCase) || Path.GetFileName(executable)!="NXInput Server.exe") return 2;
            string destination=Path.GetDirectoryName(executable), backup=Path.Combine(stage,"backup");
            try {
                int pid; if (!int.TryParse(args[3],out pid)) return 2;
                try { using (var previous=Process.GetProcessById(pid)) if (!previous.WaitForExit(60000)) return 3; }
                catch (ArgumentException) { }
                Directory.CreateDirectory(backup);
                foreach (string name in PayloadNames) {
                    string target=Path.Combine(destination,name);
                    if (File.Exists(target)) File.Copy(target,Path.Combine(backup,name));
                }
                try {
                    foreach (string name in PayloadNames) File.Copy(Path.Combine(stage,"payload",name),Path.Combine(destination,name),true);
                    var updated = Process.Start(new ProcessStartInfo(executable) { WorkingDirectory=destination,UseShellExecute=true });
                    if(updated!=null && updated.WaitForExit(3000) && updated.ExitCode!=0) throw new IOException("Updated application failed to launch.");
                } catch {
                    foreach (string name in PayloadNames) if (File.Exists(Path.Combine(backup,name))) File.Copy(Path.Combine(backup,name),Path.Combine(destination,name),true);
                    throw;
                }
                return 0;
            } catch (Exception ex) {
                File.WriteAllText(Path.Combine(stage,"update-error.txt"),ex.Message);
                try { Process.Start(executable,"--update-failed"); } catch { }
                return 1;
            }
        }
    }
}

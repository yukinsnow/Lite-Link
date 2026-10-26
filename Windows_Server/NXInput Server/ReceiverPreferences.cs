using System;
using System.IO;
using System.Xml.Linq;

namespace NXInput_Server
{
    public sealed class ReceiverPreferences
    {
        public string AdapterId = "", Address = "", LastSwitchIp = "";
        public bool AutoReceive, AutoUpdate;
        public static ReceiverPreferences Load(string path)
        {
            var settings = new ReceiverPreferences();
            try
            {
                var root = XDocument.Load(path).Root;
                if (root == null || root.Name != "LiteLink") return settings;
                settings.AdapterId = (string)root.Element("AdapterId") ?? "";
                settings.Address = (string)root.Element("Address") ?? "";
                settings.LastSwitchIp = (string)root.Element("LastSwitchIp") ?? "";
                settings.AutoReceive = (bool?)root.Element("AutoReceive") ?? false;
                settings.AutoUpdate = (bool?)root.Element("AutoUpdate") ?? false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException || ex is FormatException) { return new ReceiverPreferences(); }
            return settings;
        }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            new XDocument(new XElement("LiteLink", new XElement("AdapterId", AdapterId),
                new XElement("Address", Address), new XElement("LastSwitchIp", LastSwitchIp),
                new XElement("AutoReceive", AutoReceive), new XElement("AutoUpdate", AutoUpdate))).Save(temporary);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        public static string StartupCommand(string executable) => "\"" + executable + "\" --startup";
    }
}

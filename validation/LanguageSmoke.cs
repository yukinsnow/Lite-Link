using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using NXInput_Server;
class LanguageSmoke
{
    [STAThread] static int Main()
    {
        foreach (var culture in new[] { "en-US", "zh-CN" })
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            using (var form = new Form1())
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var label = (Label)typeof(Form1).GetField("label1", flags).GetValue(form);
                bool matches = culture == "zh-CN" ? label.Text.StartsWith("选择") : label.Text.StartsWith("Select");
                if (!matches) throw new Exception("Wrong interface language: " + culture);
                if (!form.Controls.OfType<LinkLabel>().Any(link => link.Text == "GitHub @Yukinsnow"))
                    throw new Exception("Credit link missing");
                Console.WriteLine("PASS: " + culture + " automatic UI language and GitHub credit");
            }
        }
        return 0;
    }
}

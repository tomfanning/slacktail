using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Tailf;

namespace slacktail
{
    class Program
    {
        static string url;

        static void Main(string[] args)
        {
            SetProcessName();

            string conf = "/etc/slack.conf";

            if (!File.Exists(conf))
            {
                Console.WriteLine($"Missing {conf} - create a file there with just your slack webhook URL in it. Get one from https://my.slack.com/services/new/incoming-webhook/");
                Environment.Exit(-1);
            }

            url = File.ReadAllText(conf).Trim();
            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://hooks.slack.com/services/"))
            {
                Console.WriteLine($"{conf} contains invalid Slack webhook URL, get one from https://my.slack.com/services/new/incoming-webhook/");
                Environment.Exit(-1);
            }

            bool displayed = false;
            while (!File.Exists(args[0]))
            {
                if (!displayed)
                {
                    Console.WriteLine($"Waiting for {conf} to exist...");
                    displayed = true;
                }

                Thread.Sleep(1000);
            }

            var tail = new Tail(args[0], 1);
            tail.Changed += new EventHandler<Tail.TailEventArgs>(FileChanged);
            tail.Run();
            Thread.CurrentThread.Join();
        }

        [DllImport("libc")] // Linux
        private static extern int prctl(int option, byte[] arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5);

        static void SetProcessName()
        {
            const int PR_SET_NAME = 15;

            string name = Assembly.GetExecutingAssembly().GetName().Name;

            int result;

            try
            {
                result = prctl(PR_SET_NAME, Encoding.ASCII.GetBytes(name + "\0"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
            catch (DllNotFoundException)
            {
                return;
            }

            if (result != 0)
            {
                throw new ApplicationException("Error setting process name");
            }
        }

        static void FileChanged(object sender, Tail.TailEventArgs e)
        {
            if (e == null)
                return;

            if (string.IsNullOrWhiteSpace(e.Line))
                return;

            var wc = new WebClient();

            string line = e.Line.Trim();

            var oldCol = Console.ForegroundColor;
            try
            {
                Console.WriteLine(e.Line);
                wc.UploadString(url, new SlackObj(e.Line).ToString());
            }
            catch (WebException ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("WebException: ");
                try
                {
                    var resp = new StreamReader(ex.Response.GetResponseStream()).ReadToEnd();
                    Console.WriteLine(resp);
                }
                catch (Exception)
                {
                    Console.WriteLine("Failed to get exception body");
                }
                Console.ForegroundColor = oldCol;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("Exception: ");
                if (Environment.GetCommandLineArgs().Any(a => a == "--debug"))
                {
                    Console.WriteLine(ex);
                }
                else
                {
                    Console.WriteLine(ex.GetBaseException().Message);
                }
                Console.ForegroundColor = oldCol;
            }
        }
    }

    class SlackObj
    {
        public SlackObj(string txt)
        {
            this.text = $"{DateTime.UtcNow.ToString("HH:mm:ss")} {txt}";
        }

        public string text { get; set; }

        public override string ToString()
        {
            return SimpleJson.SerializeObject(this);
        }
    }
}
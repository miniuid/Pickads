using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

static class HostsSecurityChecks
{
    static Assembly assembly;
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    static object Call(string type, string method, params object[] args)
    {
        try { return assembly.GetType("PickAds." + type, true).GetMethod(method).Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static void Reject(Action operation, string message)
    {
        try { operation(); }
        catch (InvalidDataException) { checks++; return; }
        catch (IOException) { checks++; return; }
        throw new Exception(message);
    }
    static byte[] Transform(byte[] bytes, bool enabled, params string[] domains)
    {
        return (byte[])Call("HostsDocument", "Transform", bytes, domains, enabled);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string name, string existing, IntPtr reserved);

    static int Main(string[] args)
    {
        try
        {
            assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var original = new byte[] { 0xff, 0x80 }.Concat(Encoding.ASCII.GetBytes(" comment\r\n127.0.0.1 localhost\n# Other tool\n192.0.2.1 private.example\n")).ToArray();
            var enabled = Transform(original, true, "ads.example.com", "ads.example.com");
            Check(enabled.Take(original.Length).SequenceEqual(original), "Unrelated original bytes changed");
            var text = Encoding.ASCII.GetString(enabled);
            Check(text.Contains("0.0.0.0 ads.example.com\r\n:: ads.example.com"), "Both IP families must be blocked");
            Check(text.Split(new[] { "0.0.0.0 ads.example.com" }, StringSplitOptions.None).Length == 2, "Duplicate domain emitted");
            Check(Transform(enabled, false).SequenceEqual(original), "Disable must restore unrelated bytes");
            Check(Transform(enabled, true, "ads.example.com").SequenceEqual(enabled), "Applying twice must be idempotent");
            var changed = Transform(enabled, true, "other.example.com");
            Check(!Encoding.ASCII.GetString(changed).Contains("ads.example.com"), "Updating must remove old domains");
            Check(Transform(original, false).SequenceEqual(original), "Disable without a section must be a no-op");
            Check(Encoding.ASCII.GetString(Transform(Encoding.ASCII.GetBytes("localhost"), true)).StartsWith("localhost\r\n# >>>"), "Append must not merge lines");

            foreach (string bad in new[] { "# >>> PickAds START\n", "# <<< PickAds END\n",
                "# >>> PickAds START\n# >>> PickAds START\n# <<< PickAds END\n",
                "# >>> PickAds START\n# <<< PickAds END\n# >>> PickAds START\n# <<< PickAds END\n" })
                Reject(() => Transform(Encoding.ASCII.GetBytes(bad), false), "Malformed markers were accepted");
            foreach (string bad in new[] { "ads.com\n", "ads.com\r\n127.0.0.1 bank.com", "ads.com #comment", "127.0.0.1", "localhost", "-ads.com", "$(cmd).com", "UPPER.com" })
                Reject(() => Transform(original, true, bad), "Injection or invalid domain accepted");
            Reject(() => Transform(original, true, Enumerable.Repeat("ads.com", 5001).ToArray()), "Oversized list accepted");

            var request = new StringWriter();
            Call("HostsProtocol", "Write", request, "enable", new[] { "ads.example.com" });
            var parsed = Call("HostsProtocol", "Read", new StringReader(request.ToString()));
            Check((string)parsed.GetType().GetField("Action").GetValue(parsed) == "enable", "Protocol action lost");
            Check(((string[])parsed.GetType().GetField("Domains").GetValue(parsed)).SequenceEqual(new[] { "ads.example.com" }), "Protocol domains lost");
            foreach (string bad in new[] { "UNKNOWN\nenable\n0\n", "PICKADS-HOSTS-1\ncmd\n0\n",
                "PICKADS-HOSTS-1\nenable\n5001\n", "PICKADS-HOSTS-1\nenable\n-1\n",
                "PICKADS-HOSTS-1\nflush\n1\nads.com\n", "PICKADS-HOSTS-1\nenable\n1\n",
                "PICKADS-HOSTS-1\nenable\n1\n" + new string('a', 10000) + "\n" })
                Reject(() => Call("HostsProtocol", "Read", new StringReader(bad)), "Invalid helper request accepted");
            Check((int)Call("HostsElevation", "RunHelper", "arbitrary-path") == 1, "Invalid helper argument must be refused before any action");

            string folder = Path.Combine(Path.GetTempPath(), "PickAds-security-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path = Path.Combine(folder, "hosts");
                File.WriteAllBytes(path, original);
                Call("HostsFile", "Apply", path, new[] { "ads.example.com" }, true);
                Check(File.ReadAllBytes(path + ".pickads-backup").SequenceEqual(original), "First backup must contain exact original bytes");
                Call("HostsFile", "Apply", path, new[] { "other.example.com" }, true);
                Check(File.ReadAllBytes(path + ".pickads-backup").SequenceEqual(original), "Backup must not be overwritten");
                Call("HostsFile", "Apply", path, new string[0], false);
                Check(File.ReadAllBytes(path).SequenceEqual(original), "Disk disable must restore original bytes");
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                Reject(() => Call("HostsFile", "Apply", path, new string[0], true), "Read-only protection bypassed");
                Check((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0, "Read-only flag changed");
                File.SetAttributes(path, FileAttributes.Normal);
                File.WriteAllText(path, "# >>> PickAds START\n");
                Reject(() => Call("HostsFile", "Apply", path, new string[0], false), "Malformed file was edited");
                Check(File.ReadAllText(path) == "# >>> PickAds START\n", "Rejected write changed disk contents");
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    File.WriteAllBytes(path, original);
                    string link = Path.Combine(folder, "linked-hosts");
                    if (!CreateHardLink(link, path, IntPtr.Zero)) throw new Exception("Could not prepare hard-link refusal test");
                    Reject(() => Call("HostsFile", "Apply", link, new string[0], true), "Hard-linked file accepted");
                    Check(File.ReadAllBytes(path).SequenceEqual(original), "Hard-link target modified");
                    File.Delete(link);
                }
                else Console.WriteLine("NOT RUN: Windows hard-link metadata check and live UAC/named-pipe authorization.");
            }
            finally { Directory.Delete(folder, true); }
            Console.WriteLine("PASS: " + checks + " security assertions. No system hosts file or startup settings were touched.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

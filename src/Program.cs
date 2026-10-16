// PickAds - blocks ads in Minecraft launchers (Lunar, Dawn, Modrinth App, ...)
// by adding ad domains to the Windows hosts file. Runs in the system tray and
// watches the DNS cache in the background to catch new ad domains automatically.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("PickAds")]
[assembly: AssemblyProduct("PickAds")]
[assembly: AssemblyDescription("PickAds - blocks ads in Minecraft launchers using the Windows hosts file")]
[assembly: AssemblyCompany("PickAds")]
[assembly: AssemblyCopyright("Copyright (c) 2026 PickAds. MIT License.")]
[assembly: AssemblyInformationalVersion("1.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PickAds
{
    static class Program
    {
        public const string AppName = "PickAds";
        public const string ShowEventName = "Local\\PickAds_ShowWindow";

        [STAThread]
        static void Main(string[] args)
        {
            bool startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

            // Editing the hosts file needs admin rights. The manifest normally takes care
            // of this, but relaunch elevated just in case.
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && !IsAdmin())
            {
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, startHidden ? "--tray" : "");
                    psi.Verb = "runas";
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch
                {
                    MessageBox.Show("PickAds needs administrator permission because it blocks ads " +
                        "through the Windows hosts file.\n\nPlease open it again and click Yes when Windows asks.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            bool firstInstance;
            using (var mutex = new Mutex(true, "Local\\PickAds_SingleInstance", out firstInstance))
            {
                if (!firstInstance)
                {
                    // Already running: ask the running copy to show its window.
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("UI error: " + e.Exception);
                Application.Run(new TrayApp(startHidden));
                GC.KeepAlive(mutex);
            }
        }

        static bool IsAdmin()
        {
            try
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------------
    // Simple log file (helps when someone reports a problem)
    // ------------------------------------------------------------------
    static class Log
    {
        static readonly object Gate = new object();

        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Store.DataDir);
                    string path = Path.Combine(Store.DataDir, "log.txt");
                    if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                        File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // Which domains count as ads
    // ------------------------------------------------------------------
    static class AdRules
    {
        // Known ad / tracking companies. Matches the domain and every subdomain.
        static readonly string[] AdSuffixes = {
            "doubleclick.net","googlesyndication.com","googletagservices.com","googleadservices.com","2mdn.net",
            "adnxs.com","adsrvr.org","amazon-adsystem.com","publisher-services.amazon.dev","rubiconproject.com",
            "pubmatic.com","openx.net","casalemedia.com","indexww.com","tribalfusion.com","yieldmo.com",
            "applovin.com","connatix.com","kargo.com","agkn.com","nexx360.io","gumgum.com","servicesadmedia.com",
            "rlcdn.com","id5-sync.com","dv.tech","doubleverify.com","adsafeprotected.com","moatads.com",
            "onetag-sys.com","rfihub.com","tapad.com","adentifi.com","smartytech.io","scalibur.io","rtbscale.com",
            "unrulymedia.com","simpli.fi","cognitivlabs.com","criteo.com","criteo.net","taboola.com","outbrain.com",
            "3lift.com","sharethrough.com","teads.tv","media.net","lijit.com","sovrn.com","contextweb.com",
            "bidswitch.net","smartadserver.com","adform.net","quantserve.com","scorecardresearch.com",
            "crwdcntrl.net","bluekai.com","exelator.com","everesttech.net","adition.com","33across.com",
            "yahoo-ads.com","adsymptotic.com","bidr.io","sitescout.com","mathtag.com","turn.com","zemanta.com",
            "advertising.com","adcolony.com","inmobi.com","vungle.com","unityads.unity3d.com","adsterra.com",
            "propellerads.com","popads.net","exoclick.com","adskeeper.com","mgid.com","revcontent.com",
            "playwire.com","venatus.com","venatusmedia.com","nitropay.com","ezoic.net","ezojs.com",
            "adthrive.com","raptive.com","mediavine.com","freestar.com","freestar.io","pubfuture.com",
            "aniview.com","primis.tech","vidazoo.com","sonobi.com","emxdgt.com","yieldlab.net","adkernel.com",
            "e-planning.net","rhythmone.com","gammaplatform.com","undertone.com","stickyadstv.com","spotxchange.com",
            "spotx.tv","springserve.com","lkqd.net","tremorhub.com","adsco.re","imasdk.googleapis.com"
        };

        // Domain labels that clearly mean "ads", e.g. ads.example.com, rtb0.example.com
        static readonly Regex AdLabel = new Regex(
            @"^(ad|ads|adserver|adservice|adsrv|adserve|rtb\d*|prebid.*|.*adsystem.*|.*doubleclick.*|pagead\d*|securepubads|googleads\d*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Never auto-block these (launchers, Minecraft login/skins, Windows, common apps).
        static readonly string[] SafeSuffixes = {
            "lunarclient.com","lunarclientcdn.com","lunarclientprod.com","lunar.gg","modrinth.com","modrinth.app",
            "modrinth.dev","dawnclient.com","dawn.gg","minecraft.net","mojang.com","minecraftservices.com",
            "microsoft.com","microsoftonline.com","live.com","live.net","xboxlive.com","xbox.com","windows.com",
            "windows.net","windowsupdate.com","msftconnecttest.com","msedge.net","bing.com","office.com",
            "anthropic.com","claude.ai","overwolf.com","curseforge.com","forgecdn.net","github.com",
            "githubusercontent.com","discord.com","discordapp.com","discord.gg","google.com","googleapis.com",
            "gstatic.com","youtube.com","ytimg.com","cloudflare.com","steampowered.com","steamcommunity.com",
            "apple.com","icloud.com","amazon.com","amazon.ca","amazonaws.com","temu.com","linkedin.com","nvidia.com",
            "amd.com","epicgames.com","twitch.tv"
        };

        // Exceptions to the safe list (specific ad hosts living on a "safe" company domain).
        static readonly string[] AdOverrides = { "imasdk.googleapis.com", "px.ads.linkedin.com" };

        static readonly Regex ValidHost = new Regex(@"^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,62}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static bool HasSuffix(string domain, IEnumerable<string> suffixes)
        {
            foreach (string s in suffixes)
                if (domain == s || domain.EndsWith("." + s, StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool IsValidHost(string domain)
        {
            return domain != null && ValidHost.IsMatch(domain);
        }

        public static bool IsAd(string domain)
        {
            if (!IsValidHost(domain)) return false;
            domain = domain.ToLowerInvariant();
            if (HasSuffix(domain, AdOverrides)) return true;
            if (HasSuffix(domain, SafeSuffixes)) return false;
            if (HasSuffix(domain, AdSuffixes)) return true;
            foreach (string label in domain.Split('.'))
                if (AdLabel.IsMatch(label)) return true;
            return false;
        }

        // Turns things like "https://ads.example.com/path" into "ads.example.com"
        public static string Clean(string input)
        {
            if (input == null) return "";
            string s = input.Trim().ToLowerInvariant();
            int i = s.IndexOf("://", StringComparison.Ordinal);
            if (i >= 0) s = s.Substring(i + 3);
            i = s.IndexOfAny(new[] { '/', ':', '?', '#' });
            if (i >= 0) s = s.Substring(0, i);
            return s.Trim('.');
        }
    }

    // ------------------------------------------------------------------
    // Blocklist, settings and hosts file
    // ------------------------------------------------------------------
    static class Store
    {
        public static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PickAds");
        static readonly string OldDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MCAdBlocker");
        static readonly string ListPath = Path.Combine(DataDir, "blocklist.txt");
        static readonly string SettingsPath = Path.Combine(DataDir, "settings.txt");
        static readonly string AllowPath = Path.Combine(DataDir, "never-block.txt");
        public static readonly string HostsPath = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");

        const string StartMarker = "# >>> PickAds START";
        const string EndMarker = "# <<< PickAds END";
        // Markers written by the older version (still recognised so they get cleaned up).
        const string OldStartMarker = "# >>> MC-AdBlock START";
        const string OldEndMarker = "# <<< MC-AdBlock END";

        static bool IsStart(string t) { return t == StartMarker || t == OldStartMarker; }
        static bool IsEnd(string t) { return t == EndMarker || t == OldEndMarker; }

        // Ad hosts seen in Minecraft launchers, so blocking works right away.
        static readonly string[] Starter = {
            "a.tribalfusion.com","ad.doubleclick.net","ade.googlesyndication.com","ads.yieldmo.com",
            "aax.amazon-adsystem.com","aax-events-cell02-cf.us-west.3px.axp.amazon-adsystem.com",
            "b.applovin.com","beacon.lynx.cognitivlabs.com","c.amazon-adsystem.com","capi.connatix.com",
            "crb.kargo.com","d.agkn.com","direct.adsrvr.org","fast.nexx360.io","g2.gumgum.com","gcdn.2mdn.net",
            "googleads.g.doubleclick.net","googleads4.g.doubleclick.net","gpm.servicesadmedia.com",
            "hbopenbid.pubmatic.com","ib.adnxs.com","id.rlcdn.com","id5-sync.com","imasdk.googleapis.com",
            "maestro-ue1.tps.dv.tech","onetag-sys.com","p.rfihub.com","pagead2.googlesyndication.com",
            "pixel.tapad.com","prebid-server.rubiconproject.com",
            "prod.us-east-1.cxm-bcn.publisher-services.amazon.dev","px.ads.linkedin.com","rtb.adentifi.com",
            "rtb0.doubleverify.com","s.amazon-adsystem.com","s0.2mdn.net","secure.adnxs.com",
            "securepubads.g.doubleclick.net","server.smartytech.io","srv.scalibur.io","stats.g.doubleclick.net",
            "t.rtbscale.com","targeting.unrulymedia.com","tpc.googlesyndication.com","u.openx.net","um.simpli.fi",
            "web-video.ads.aps.amazon-adsystem.com","www.googletagservices.com","a6316.casalemedia.com",
            "fastlane.rubiconproject.com","ads.pubmatic.com","static.doubleclick.net","cm.g.doubleclick.net",
            "z.moatads.com","pixel.adsafeprotected.com","static.adsafeprotected.com","acdn.adnxs.com"
        };

        static readonly object Gate = new object();
        static SortedSet<string> blocklist;
        static SortedSet<string> allowlist;
        static Dictionary<string, string> settings;

        // ---- Settings ----
        public static bool AutoDetect { get { return GetBool("auto", false); } set { SetBool("auto", value); } }
        public static bool Notify { get { return GetBool("notify", true); } set { SetBool("notify", value); } }
        public static bool SeenTrayTip { get { return GetBool("traytip", false); } set { SetBool("traytip", value); } }

        static bool GetBool(string key, bool def)
        {
            lock (Gate)
            {
                LoadSettings();
                string v;
                return settings.TryGetValue(key, out v) ? v == "1" : def;
            }
        }

        static void SetBool(string key, bool value)
        {
            lock (Gate)
            {
                LoadSettings();
                settings[key] = value ? "1" : "0";
                try
                {
                    Directory.CreateDirectory(DataDir);
                    File.WriteAllLines(SettingsPath, settings.Select(kv => kv.Key + "=" + kv.Value).ToArray());
                }
                catch (Exception ex) { Log.Write("Could not save settings: " + ex.Message); }
            }
        }

        static void LoadSettings()
        {
            if (settings != null) return;
            settings = new Dictionary<string, string>();
            try
            {
                if (File.Exists(SettingsPath))
                    foreach (string line in File.ReadAllLines(SettingsPath))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) settings[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
            }
            catch { }
        }

        // ---- Blocklist ----
        public static void Init()
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DataDir);
                // Carry over blocklist and settings from the older "MC Ad Blocker" version.
                if (!File.Exists(ListPath) && Directory.Exists(OldDataDir))
                {
                    foreach (string f in Directory.GetFiles(OldDataDir))
                    {
                        string dest = Path.Combine(DataDir, Path.GetFileName(f));
                        try { if (!File.Exists(dest)) File.Copy(f, dest); } catch { }
                    }
                    settings = null;
                }
                bool firstRun = !File.Exists(ListPath);
                LoadList();
                if (firstRun)
                {
                    foreach (string d in Starter) blocklist.Add(d);
                    // Bring over anything the older PowerShell version blocked.
                    foreach (string d in ReadBlockedFromHosts()) if (AdRules.IsValidHost(d)) blocklist.Add(d);
                    string oldList = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "blocklist.txt");
                    if (File.Exists(oldList))
                        foreach (string d in ReadDomainFile(oldList)) blocklist.Add(d);
                    SaveList();
                }
            }
        }

        static IEnumerable<string> ReadDomainFile(string path)
        {
            try
            {
                return File.ReadAllLines(path)
                    .Select(l => AdRules.Clean(l))
                    .Where(l => l.Length > 0 && !l.StartsWith("#") && AdRules.IsValidHost(l))
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        static void LoadList()
        {
            if (blocklist != null) return;
            blocklist = new SortedSet<string>(StringComparer.Ordinal);
            if (File.Exists(ListPath))
                foreach (string d in ReadDomainFile(ListPath)) blocklist.Add(d);
            allowlist = new SortedSet<string>(StringComparer.Ordinal);
            if (File.Exists(AllowPath))
                foreach (string d in ReadDomainFile(AllowPath)) allowlist.Add(d);
        }

        static void SaveAllow()
        {
            var lines = new List<string>();
            lines.Add("# Sites you unblocked. The automatic scan will never block these again.");
            lines.AddRange(allowlist);
            File.WriteAllLines(AllowPath, lines.ToArray());
        }

        // True if the automatic scan may block this domain.
        public static bool CanAutoBlock(string d)
        {
            lock (Gate) { LoadList(); return !blocklist.Contains(d) && !allowlist.Contains(d); }
        }

        static void SaveList()
        {
            var lines = new List<string>();
            lines.Add("# PickAds - one domain per line. Lines starting with # are ignored.");
            lines.AddRange(blocklist);
            File.WriteAllLines(ListPath, lines.ToArray());
        }

        public static List<string> Blocklist
        {
            get { lock (Gate) { LoadList(); return blocklist.ToList(); } }
        }

        // Adds domains; returns the ones that were actually new.
        // manual = the user added it themselves (overrides an earlier unblock).
        public static List<string> Add(IEnumerable<string> domains, bool manual)
        {
            lock (Gate)
            {
                LoadList();
                var added = new List<string>();
                bool allowChanged = false;
                foreach (string raw in domains)
                {
                    string d = AdRules.Clean(raw);
                    if (!AdRules.IsValidHost(d)) continue;
                    if (manual) allowChanged |= allowlist.Remove(d);
                    else if (allowlist.Contains(d)) continue;
                    if (blocklist.Add(d)) added.Add(d);
                }
                if (allowChanged) SaveAllow();
                if (added.Count > 0)
                {
                    SaveList();
                    if (IsBlockingOn()) WriteHosts(true);
                }
                return added;
            }
        }

        public static void Remove(IEnumerable<string> domains)
        {
            lock (Gate)
            {
                LoadList();
                bool changed = false;
                foreach (string d in domains)
                {
                    if (blocklist.Remove(d)) { changed = true; allowlist.Add(d); }
                }
                if (changed)
                {
                    SaveList();
                    SaveAllow();
                    if (IsBlockingOn()) WriteHosts(true);
                }
            }
        }

        // ---- Hosts file ----
        static List<string> ReadHosts()
        {
            if (!File.Exists(HostsPath)) return new List<string>();
            return File.ReadAllLines(HostsPath).ToList();
        }

        public static bool IsBlockingOn()
        {
            try { return ReadHosts().Any(l => IsStart(l.Trim())); }
            catch { return false; }
        }

        static List<string> ReadBlockedFromHosts()
        {
            var result = new List<string>();
            try
            {
                bool inside = false;
                foreach (string line in ReadHosts())
                {
                    string t = line.Trim();
                    if (IsStart(t)) { inside = true; continue; }
                    if (IsEnd(t)) { inside = false; continue; }
                    if (!inside || t.StartsWith("#")) continue;
                    string[] parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) result.Add(parts[1].ToLowerInvariant());
                }
            }
            catch { }
            return result.Distinct().ToList();
        }

        public static void SetBlocking(bool on)
        {
            lock (Gate)
            {
                LoadList();
                WriteHosts(on);
            }
        }

        static void WriteHosts(bool includeBlock)
        {
            var lines = new List<string>();
            bool inside = false;
            foreach (string line in ReadHosts())
            {
                string t = line.Trim();
                if (IsStart(t)) { inside = true; continue; }
                if (IsEnd(t)) { inside = false; continue; }
                if (!inside) lines.Add(line);
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);

            if (includeBlock)
            {
                lines.Add("");
                lines.Add(StartMarker);
                lines.Add("# Added by PickAds. Turn blocking off in the app to remove these lines.");
                foreach (string d in blocklist)
                {
                    lines.Add("0.0.0.0 " + d);
                    lines.Add(":: " + d);
                }
                lines.Add(EndMarker);
            }

            if (inside)
                throw new IOException("The hosts file has an unmatched PickAds marker. No changes were made.");

            // Preserve the original bytes before the first edit; fail closed if backup fails.
            string backup = HostsPath + ".pickads-backup";
            if (File.Exists(HostsPath) && !File.Exists(backup)) File.Copy(HostsPath, backup, false);

            string text = string.Join("\r\n", lines.ToArray()) + "\r\n";

            // The hosts file can be briefly locked by Windows or antivirus; retry a few times.
            Exception last = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var fi = new FileInfo(HostsPath);
                    if (fi.Exists && fi.IsReadOnly)
                        throw new IOException("The hosts file is read-only. PickAds will not override that protection.");
                    File.WriteAllText(HostsPath, text, new UTF8Encoding(false));
                    last = null;
                    break;
                }
                catch (Exception ex) { last = ex; Thread.Sleep(300); }
            }
            if (last != null)
                throw new IOException("Could not edit the Windows hosts file. Your antivirus may be protecting it.\n\n" + last.Message, last);

            Dns.Flush();
        }
    }

    // ------------------------------------------------------------------
    // Reading and clearing the Windows DNS cache
    // ------------------------------------------------------------------
    static class Dns
    {
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        static extern bool DnsFlushResolverCache();

        public static void Flush()
        {
            try { DnsFlushResolverCache(); }
            catch
            {
                try { RunHidden(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns"); } catch { }
            }
        }

        public static HashSet<string> ReadCache()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\StandardCimv2", "SELECT Entry, Name FROM MSFT_DNSClientCache"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementBaseObject o in results)
                    {
                        using (o)
                        {
                            AddName(set, o["Entry"] as string);
                            AddName(set, o["Name"] as string);
                        }
                    }
                }
                return set;
            }
            catch (Exception ex)
            {
                Log.Write("WMI DNS read failed, using ipconfig: " + ex.Message);
            }

            // Fallback: parse "ipconfig /displaydns" (works in any language).
            try
            {
                string output = RunHidden(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/displaydns");
                foreach (Match m in Regex.Matches(output, @":\s*([A-Za-z0-9._-]+\.[A-Za-z][A-Za-z0-9-]*)\s*$", RegexOptions.Multiline))
                    AddName(set, m.Groups[1].Value);
            }
            catch (Exception ex) { Log.Write("ipconfig DNS read failed: " + ex.Message); }
            return set;
        }

        static void AddName(HashSet<string> set, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            string d = name.Trim().TrimEnd('.').ToLowerInvariant();
            if (AdRules.IsValidHost(d)) set.Add(d);
        }

        public static string RunHidden(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (var p = Process.Start(psi))
            {
                var errTask = p.StandardError.ReadToEndAsync();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                return output + errTask.Result;
            }
        }


    }

    // Explicitly approved elevated logon task; executable stored with a protected ACL.
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "PickAdsReviewed";
        static string Sid { get { return WindowsIdentity.GetCurrent().User.Value; } }
        static string TaskName { get { return "PickAds-Reviewed-" + Sid; } }

        static int RunTaskTool(string args, out string output)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (var process = Process.Start(psi))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(20000))
                {
                    process.Kill();
                    throw new IOException("Windows Task Scheduler did not respond in time. Check Task Scheduler before retrying.");
                }
                output = stdout.Result + stderr.Result;
                return process.ExitCode;
            }
        }

        public static bool IsEnabled()
        {
            string output;
            return RunTaskTool("/Query /TN \"" + TaskName + "\"", out output) == 0;
        }

        static void RemoveOldRunEntry()
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (key != null) key.DeleteValue(ValueName, false);
        }

        public static void SetEnabled(bool enabled)
        {
            string output;
            if (!enabled)
            {
                if (IsEnabled() && RunTaskTool("/Delete /TN \"" + TaskName + "\" /F", out output) != 0)
                    throw new IOException("Could not remove startup task: " + output);
                RemoveOldRunEntry();
                return;
            }
            // Use a fresh directory, never an existing user-controlled installation.
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrEmpty(programFiles) ||
                (File.GetAttributes(programFiles) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A normal protected Program Files folder is required.");
            string folder = Path.Combine(programFiles, "PickAds-Startup-" + Guid.NewGuid().ToString("N"));
            var acl = new System.Security.AccessControl.DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (var role in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinUsersSid })
            {
                var rights = role == WellKnownSidType.BuiltinUsersSid
                    ? System.Security.AccessControl.FileSystemRights.ReadAndExecute
                    : System.Security.AccessControl.FileSystemRights.FullControl;
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    new SecurityIdentifier(role, null), rights,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            }
            Directory.CreateDirectory(folder, acl);
            string executable = Path.Combine(folder, "PickAds.exe");
            File.Copy(Application.ExecutablePath, executable, false);
            string xmlPath = Path.Combine(folder, "startup.xml");
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>Opt-in PickAds startup with administrator privileges.</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + Sid + "</UserId><Delay>PT15S</Delay></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"User\"><UserId>" + Sid + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>" +
                "<Actions Context=\"User\"><Exec><Command>" + SecurityElement.Escape(executable) +
                "</Command><Arguments>--tray</Arguments><WorkingDirectory>" + SecurityElement.Escape(folder) + "</WorkingDirectory></Exec></Actions></Task>";
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            try
            {
                if (RunTaskTool("/Create /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\" /F", out output) != 0)
                    throw new IOException("Could not enable startup: " + output);
                RemoveOldRunEntry();
            }
            finally { File.Delete(xmlPath); }
            // Retain the protected EXE. Disabling removes the task, not installed files.
        }
    }

    // ------------------------------------------------------------------
    // Icons (drawn in code so the app is a single file)
    // ------------------------------------------------------------------
    static class Icons
    {
        // Black rounded tile with a green (or grey) pickaxe.
        public static Icon Make(bool on, int size)
        {
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    Theme.DrawLogo(g, new RectangleF(0, 0, size, size), on);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // ------------------------------------------------------------------
    // Tray icon + background watcher
    // ------------------------------------------------------------------
    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray;
        readonly MainForm form;
        readonly Icon iconOn, iconOff;
        readonly System.Windows.Forms.Timer timer;
        readonly EventWaitHandle showEvent;
        readonly ToolStripMenuItem menuBlocking, menuAuto;
        int busy;
        int pendingNotify;
        DateTime lastNotify = DateTime.MinValue;

        public TrayApp(bool startHidden)
        {
            try { Store.Init(); }
            catch (Exception ex) { Log.Write("Init failed: " + ex); }

            int small = Math.Max(16, SystemInformation.SmallIconSize.Width);
            iconOn = Icons.Make(true, small);
            iconOff = Icons.Make(false, small);

            form = new MainForm(this);
            form.Icon = iconOn;
            var unused = form.Handle; // create the handle now so background work can report back

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open PickAds", null, (s, e) => ShowWindow());
            menu.Items.Add(new ToolStripSeparator());
            menuBlocking = new ToolStripMenuItem("Block ads", null, (s, e) => SetBlocking(!Store.IsBlockingOn()));
            menuAuto = new ToolStripMenuItem("Find new ads automatically", null, (s, e) => SetAutoDetect(!Store.AutoDetect));
            menu.Items.Add(menuBlocking);
            menu.Items.Add(menuAuto);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitApp());
            menu.Opening += (s, e) => { menuBlocking.Checked = Store.IsBlockingOn(); menuAuto.Checked = Store.AutoDetect; };

            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => ShowWindow();
            tray.BalloonTipClicked += (s, e) => ShowWindow();
            tray.Visible = true;

            // Let a second launch bring this window up.
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            var t = new Thread(() =>
            {
                while (true)
                {
                    showEvent.WaitOne();
                    try { form.BeginInvoke((MethodInvoker)ShowWindow); } catch { return; }
                }
            });
            t.IsBackground = true;
            t.Start();

            // Blocking and DNS monitoring require explicit user action.

            RefreshState();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15000;
            timer.Tick += (s, e) => { if (Store.AutoDetect) ScanInBackground(false); };
            timer.Start();

            if (!startHidden) ShowWindow();
            if (Store.AutoDetect) ScanInBackground(false);
        }

        public void ShowWindow()
        {
            form.RefreshAll(true);
            form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
        }

        public void OnWindowHidden()
        {
            if (!Store.SeenTrayTip)
            {
                Store.SeenTrayTip = true;
                tray.ShowBalloonTip(5000, Program.AppName,
                    "Still running here and blocking ads. Right-click this icon to exit.", ToolTipIcon.Info);
            }
        }

        public void RefreshState()
        {
            bool on = Store.IsBlockingOn();
            tray.Icon = on ? iconOn : iconOff;
            string text = Program.AppName + (on ? " - blocking ON" : " - blocking OFF");
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            form.Icon = on ? iconOn : iconOff;
        }

        public void SetBlocking(bool on)
        {
            try { Store.SetBlocking(on); }
            catch (Exception ex) { ShowError(ex); }
            RefreshState();
            form.RefreshAll();
        }

        public void SetAutoDetect(bool on)
        {
            Store.AutoDetect = on;
            form.RefreshAll();
            if (on) ScanInBackground(false);
        }

        // Looks at the DNS cache and blocks any new ad domains it finds.
        public void ScanInBackground(bool manual)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<string> added = new List<string>();
                string error = null;
                try
                {
                    var found = Dns.ReadCache().Where(d => AdRules.IsAd(d) && Store.CanAutoBlock(d)).ToList();
                    if (found.Count > 0) added = Store.Add(found, false);
                }
                catch (Exception ex) { error = ex.Message; Log.Write("Scan failed: " + ex); }
                finally { Interlocked.Exchange(ref busy, 0); }

                try { form.BeginInvoke((MethodInvoker)(() => OnScanDone(added, error, manual))); } catch { }
            });
        }

        void OnScanDone(List<string> added, string error, bool manual)
        {
            if (added.Count > 0)
            {
                Log.Write("Blocked: " + string.Join(", ", added.ToArray()));
                form.AddActivity(added);
            }
            form.SetLastCheck(added.Count, error);
            if (added.Count == 0 && form.Visible) form.RefreshAll();
            RefreshState();

            if (manual)
            {
                if (error != null)
                    MessageBox.Show(form, "Scan failed:\n\n" + error, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else if (added.Count == 0)
                    MessageBox.Show(form, "No new ad sites found.\n\nTip: open the launcher and wait for the ads to show, then scan again.",
                        Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(form, "Blocked " + added.Count + " new ad site(s).\n\nClose and reopen the launcher to remove the ads.",
                        Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Background finds: show a small notification, at most once every 2 minutes.
            if (added.Count > 0 && Store.Notify && Store.IsBlockingOn())
            {
                pendingNotify += added.Count;
                if (DateTime.Now - lastNotify > TimeSpan.FromMinutes(2))
                {
                    tray.ShowBalloonTip(5000, Program.AppName,
                        "Blocked " + pendingNotify + " new ad site(s). If ads still show, restart the launcher.",
                        ToolTipIcon.Info);
                    pendingNotify = 0;
                    lastNotify = DateTime.Now;
                }
            }
        }

        public void ShowError(Exception ex)
        {
            Log.Write("Error: " + ex);
            MessageBox.Show(form.Visible ? form : null, ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void ExitApp()
        {
            IWin32Window owner = form.Visible ? form : null;
            var answer = MessageBox.Show(owner,
                "Exit PickAds?\n\nAds you already blocked stay blocked. It just stops finding new ones until you open it again.",
                Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            form.AllowClose = true;
            form.Close();
            ExitThread();
        }
    }

    // ------------------------------------------------------------------
    // Look and feel
    // ------------------------------------------------------------------
    static class Theme
    {
        public static bool Dark;
        public static float Scale = 1f;

        public static Color Bg, Card, Text, Sub, Accent, AccentSoft, AccentText, OffSoft, OffText,
            Neutral, NeutralHover, NeutralPress, Selection, SelectionText, TrackOff, Knob;

        public static void Init()
        {
            Dark = false;
            try
            {
                string force = Environment.GetEnvironmentVariable("MCADBLOCKER_THEME");
                if (force == "dark") Dark = true;
                else if (force != "light")
                {
                    object v = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                    Dark = v is int && (int)v == 0;
                }
            }
            catch { }

            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f; } catch { Scale = 1f; }

            if (Dark)
            {
                Bg = Hex(0x111315); Card = Hex(0x1C1F23); Text = Hex(0xF3F4F6); Sub = Hex(0x9CA3AF);
                Accent = Hex(0x22C55E); AccentSoft = Hex(0x153A22); AccentText = Hex(0x052E16);
                OffSoft = Hex(0x3A1E1E); OffText = Hex(0xF87171);
                Neutral = Hex(0x2A2E34); NeutralHover = Hex(0x343941); NeutralPress = Hex(0x3E444D);
                Selection = Hex(0x153A22); SelectionText = Hex(0xBBF7D0); TrackOff = Hex(0x4B5563); Knob = Hex(0xFFFFFF);
            }
            else
            {
                Bg = Hex(0xF2F3F5); Card = Hex(0xFFFFFF); Text = Hex(0x111827); Sub = Hex(0x6B7280);
                Accent = Hex(0x16A34A); AccentSoft = Hex(0xDCFCE7); AccentText = Hex(0xFFFFFF);
                OffSoft = Hex(0xFEE2E2); OffText = Hex(0xDC2626);
                Neutral = Hex(0xF2F3F5); NeutralHover = Hex(0xE7E9ED); NeutralPress = Hex(0xDADDE2);
                Selection = Hex(0xDCFCE7); SelectionText = Hex(0x14532D); TrackOff = Hex(0xD1D5DB); Knob = Hex(0xFFFFFF);
            }
        }

        static Color Hex(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        public static int S(float v) { return (int)Math.Round(v * Scale); }

        public static Font Font(float size, bool bold)
        {
            try
            {
                if (bold)
                {
                    var f = new Font("Segoe UI Semibold", size);
                    if (f.Name == "Segoe UI Semibold") return f;
                    f.Dispose();
                    return new Font("Segoe UI", size, FontStyle.Bold);
                }
                return new Font("Segoe UI", size);
            }
            catch { return new Font(FontFamily.GenericSansSerif, size, bold ? FontStyle.Bold : FontStyle.Regular); }
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Smooth pickaxe drawn in a 32x32 design grid, scaled into box.
        public static void DrawPickaxe(Graphics g, RectangleF box, Color color)
        {
            float k = box.Width / 32f;
            Func<float, float, PointF> P = (x, y) => new PointF(box.X + x * k, box.Y + y * k);

            // Handle: thick rounded line from bottom-left up to the head.
            using (var pen = new Pen(color, 4.6f * k))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, P(5.5f, 28.5f), P(21.5f, 10.5f));
            }

            // Head: a curved crescent with pointed tips, perpendicular to the handle.
            PointF a = P(9.2f, 2.6f), b = P(29.4f, 22.8f);
            PointF outer = P(30.5f, 1.5f), inner = P(21.4f, 10.6f);
            using (var path = new GraphicsPath())
            {
                path.AddBezier(a, Lerp(a, outer, 2f / 3f), Lerp(b, outer, 2f / 3f), b);
                path.AddBezier(b, Lerp(b, inner, 2f / 3f), Lerp(a, inner, 2f / 3f), a);
                path.CloseFigure();
                using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
            }
        }

        // App logo: black rounded tile with a green (or grey when off) pickaxe.
        public static void DrawLogo(Graphics g, RectangleF r, bool on)
        {
            using (var path = Round(r, r.Width * 0.24f))
            using (var b = new SolidBrush(Color.FromArgb(14, 15, 17)))
                g.FillPath(b, path);
            float inset = r.Width * 0.2f;
            DrawPickaxe(g, new RectangleF(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset),
                on ? Color.FromArgb(34, 197, 94) : Color.FromArgb(120, 124, 130));
        }

        static PointF Lerp(PointF p, PointF q, float t)
        {
            return new PointF(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);
        }
    }

    // Rounded white card that holds other controls.
    class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(18)))
            using (var b = new SolidBrush(Theme.Card))
                g.FillPath(b, path);
        }
    }

    // Pill-shaped button.
    class PillButton : Control
    {
        bool hover, down;
        public bool Primary;

        public PillButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            Font = Theme.Font(9.5f, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Card)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill, fore;
            if (Primary)
            {
                fill = Theme.Accent;
                if (hover) fill = ControlPaint.Light(fill, 0.15f);
                if (down) fill = ControlPaint.Dark(fill, 0.05f);
                fore = Theme.AccentText;
            }
            else
            {
                fill = down ? Theme.NeutralPress : hover ? Theme.NeutralHover : Theme.Neutral;
                fore = Theme.Text;
            }
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
            using (var b = new SolidBrush(fill))
                g.FillPath(b, path);

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // iOS-style on/off switch with a little slide animation.
    class ToggleSwitch : Control
    {
        bool isOn;
        float pos;          // 0 = off, 1 = on (animated)
        readonly System.Windows.Forms.Timer anim;
        public event EventHandler Toggled;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            anim = new System.Windows.Forms.Timer();
            anim.Interval = 15;
            anim.Tick += (s, e) =>
            {
                float target = isOn ? 1f : 0f;
                pos += (target - pos) * 0.35f;
                if (Math.Abs(target - pos) < 0.02f) { pos = target; anim.Stop(); }
                Invalidate();
            };
        }

        public bool On
        {
            get { return isOn; }
            set
            {
                if (isOn == value) return;
                isOn = value;
                if (Visible && IsHandleCreated) anim.Start(); else { pos = isOn ? 1f : 0f; Invalidate(); }
            }
        }

        protected override void OnClick(EventArgs e)
        {
            On = !On;
            if (Toggled != null) Toggled(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) anim.Dispose();
            base.Dispose(disposing);
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Card)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float h = Height - 1, w = Width - 1;
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, w - 0.5f, h - 0.5f), h / 2f))
            using (var b = new SolidBrush(Mix(Theme.TrackOff, Theme.Accent, pos)))
                g.FillPath(b, path);

            float pad = Theme.S(3);
            float d = h - pad * 2;
            float x = pad + (w - d - pad * 2) * pos;
            using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                g.FillEllipse(shadow, x, pad + 1, d, d);
            using (var knob = new SolidBrush(Theme.Knob))
                g.FillEllipse(knob, x, pad, d, d);
        }
    }

    // Big round status badge.
    class Badge : Control
    {
        public bool On = true;

        public Badge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Card)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.DrawLogo(g, new RectangleF(0, 0, Width, Height), On);
        }
    }

    // Borderless list with rounded selection highlights.
    class SiteList : ListBox
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public SiteList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            SelectionMode = SelectionMode.MultiExtended;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.Font(9.5f, false);
            ItemHeight = Theme.S(28);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Dark scrollbar in dark mode, modern scrollbar in light mode.
            try { SetWindowTheme(Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, e.Bounds);
            if (e.Index < 0 || e.Index >= Items.Count) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            if (selected)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 6, e.Bounds.Height - 4);
                using (var path = Theme.Round(r, r.Height / 2f))
                using (var b = new SolidBrush(Theme.Selection))
                    g.FillPath(b, path);
            }

            var textRect = new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(20), e.Bounds.Height);
            TextRenderer.DrawText(g, Items[e.Index].ToString(), Font, textRect,
                selected ? Theme.SelectionText : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // ------------------------------------------------------------------
    // Main window
    // ------------------------------------------------------------------
    class MainForm : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly TrayApp app;
        public bool AllowClose;

        readonly Badge badge;
        readonly Label statusLabel, detailLabel, listLabel, hintLabel, lastCheckLabel;
        readonly PillButton toggleButton, scanButton, clearDnsButton, addButton, removeButton;
        readonly ToggleSwitch autoSwitch, startupSwitch, notifySwitch;
        readonly SiteList list;
        bool updating;

        public MainForm(TrayApp app)
        {
            this.app = app;
            Theme.Init();

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = Program.AppName;
            ClientSize = new Size(400, 616);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.Font(9.5f, false);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            DoubleBuffered = true;

            // ---- Status card ----
            var hero = new Card();
            hero.SetBounds(16, 16, 368, 164);
            Controls.Add(hero);

            badge = new Badge();
            badge.SetBounds(20, 20, 64, 64);
            hero.Controls.Add(badge);

            statusLabel = MakeLabel(hero, 100, 24, 252, 30, Theme.Font(15f, true), Theme.Text);
            detailLabel = MakeLabel(hero, 101, 56, 251, 24, Theme.Font(9.5f, false), Theme.Sub);

            toggleButton = new PillButton();
            toggleButton.Font = Theme.Font(10.5f, true);
            toggleButton.SetBounds(20, 100, 328, 44);
            toggleButton.Click += (s, e) => app.SetBlocking(!Store.IsBlockingOn());
            hero.Controls.Add(toggleButton);

            // ---- Settings card ----
            var settings = new Card();
            settings.SetBounds(16, 192, 368, 164);
            Controls.Add(settings);

            autoSwitch = MakeSettingRow(settings, 0, "Auto-block new ads", "Checks for new ads in the background");
            autoSwitch.Toggled += (s, e) => { if (!updating) app.SetAutoDetect(autoSwitch.On); };
            startupSwitch = MakeSettingRow(settings, 1, "Start with Windows", "Starts in the tray without asking each time");
            startupSwitch.Toggled += (s, e) => { if (!updating) SetStartup(startupSwitch.On); };
            notifySwitch = MakeSettingRow(settings, 2, "Notifications", "Tell me when new ads get blocked");
            notifySwitch.Toggled += (s, e) => { if (!updating) Store.Notify = notifySwitch.On; };

            // ---- Blocked sites card ----
            var sites = new Card();
            sites.SetBounds(16, 368, 368, 212);
            Controls.Add(sites);

            listLabel = MakeLabel(sites, 20, 14, 160, 22, Theme.Font(10.5f, true), Theme.Text);
            hintLabel = MakeLabel(sites, 168, 16, 180, 20, Theme.Font(8.5f, false), Theme.Sub);
            hintLabel.TextAlign = ContentAlignment.MiddleRight;
            hintLabel.Text = "Select a site to unblock it";

            list = new SiteList();
            list.SetBounds(12, 42, 344, 116);
            sites.Controls.Add(list);

            scanButton = MakeSmallButton(sites, 0, "Scan now");
            scanButton.Click += (s, e) => app.ScanInBackground(true);
            clearDnsButton = MakeSmallButton(sites, 1, "Clear DNS");
            clearDnsButton.Click += (s, e) =>
            {
                Dns.Flush();
                MessageBox.Show(this, "DNS cache cleared.\n\nNow open the launcher and wait for the ads. They will be found and blocked automatically.",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            addButton = MakeSmallButton(sites, 2, "Add site");
            addButton.Click += (s, e) => AddSite();
            removeButton = MakeSmallButton(sites, 3, "Unblock");
            removeButton.Click += (s, e) => RemoveSelected();

            // ---- Footer ----
            lastCheckLabel = MakeLabel(this, 20, 588, 360, 20, Theme.Font(8.5f, false), Theme.Sub);
            lastCheckLabel.TextAlign = ContentAlignment.MiddleCenter;
            lastCheckLabel.Text = "Closing this window keeps it running in the tray";

            ResumeLayout(false);
        }

        static Label MakeLabel(Control parent, int x, int y, int w, int h, Font font, Color color)
        {
            var l = new Label();
            l.AutoSize = false;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.UseMnemonic = false;
            l.SetBounds(x, y, w, h);
            parent.Controls.Add(l);
            return l;
        }

        static ToggleSwitch MakeSettingRow(Control card, int row, string title, string sub)
        {
            int top = 12 + row * 48;
            MakeLabel(card, 20, top + 4, 250, 22, Theme.Font(10f, true), Theme.Text).Text = title;
            MakeLabel(card, 20, top + 24, 250, 18, Theme.Font(8.5f, false), Theme.Sub).Text = sub;
            var sw = new ToggleSwitch();
            sw.SetBounds(368 - 20 - 46, top + 10, 46, 26);
            card.Controls.Add(sw);
            return sw;
        }

        static PillButton MakeSmallButton(Control card, int index, string text)
        {
            var b = new PillButton();
            b.Text = text;
            b.Font = Theme.Font(9f, true);
            int w = 78, gap = 8;
            b.SetBounds(16 + index * (w + gap), 166, w, 32);
            card.Controls.Add(b);
            return b;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = Theme.Dark ? 1 : 0;
                if (DwmSetWindowAttribute(Handle, 20, ref dark, 4) != 0)   // dark title bar (Win10 20H1+)
                    DwmSetWindowAttribute(Handle, 19, ref dark, 4);        // older Win10
                int caption = Theme.Bg.R | (Theme.Bg.G << 8) | (Theme.Bg.B << 16);
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);         // title bar matches background (Win11)
                int round = 2;
                DwmSetWindowAttribute(Handle, 33, ref round, 4);           // rounded corners (Win11)
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                app.OnWindowHidden();
                return;
            }
            base.OnFormClosing(e);
        }

        public void RefreshAll() { RefreshAll(false); }

        public void RefreshAll(bool checkStartup)
        {
            updating = true;
            try
            {
                bool on = Store.IsBlockingOn();
                var domains = Store.Blocklist;

                badge.On = on;
                badge.Invalidate();
                statusLabel.Text = on ? "Ads are blocked" : "Blocking is off";
                detailLabel.Text = on ? domains.Count + " ad sites blocked" : "Ads will show in your launchers";
                toggleButton.Primary = !on;
                toggleButton.Text = on ? "Turn off blocking" : "Turn on blocking";
                toggleButton.Invalidate();

                autoSwitch.On = Store.AutoDetect;
                notifySwitch.On = Store.Notify;
                if (checkStartup)
                {
                    try { startupSwitch.On = Startup.IsEnabled(); }
                    catch (Exception ex) { startupSwitch.On = false; Log.Write("Startup read failed: " + ex.Message); }
                }

                listLabel.Text = "Blocked sites  (" + domains.Count + ")";
                var selected = new HashSet<string>(list.SelectedItems.Cast<string>());
                int top = list.TopIndex;
                list.BeginUpdate();
                list.Items.Clear();
                foreach (string d in domains)
                {
                    int i = list.Items.Add(d);
                    if (selected.Contains(d)) list.SetSelected(i, true);
                }
                if (top < list.Items.Count) list.TopIndex = top;
                list.EndUpdate();
            }
            finally { updating = false; }
        }

        public void SetLastCheck(int added, string error)
        {
            string time = DateTime.Now.ToString("h:mm tt");
            if (error != null) lastCheckLabel.Text = "Last check " + time + "  ·  failed (see log)";
            else if (added > 0) lastCheckLabel.Text = "Last check " + time + "  ·  blocked " + added + " new";
            else lastCheckLabel.Text = "Last check " + time + "  ·  nothing new";
        }

        public void AddActivity(List<string> added)
        {
            // Newly blocked sites get selected so they're easy to spot (and to unblock if needed).
            RefreshAll();
            list.ClearSelected();
            foreach (string d in added)
            {
                int i = list.Items.IndexOf(d);
                if (i >= 0) { list.SetSelected(i, true); list.TopIndex = i; }
            }
        }

        void SetStartup(bool on)
        {
            if (on && MessageBox.Show(this,
                "Start PickAds when you sign in to Windows?\n\n" +
                "This installs a protected copy in Program Files and creates a Windows scheduled task. " +
                "That copy runs with administrator rights at sign-in without another permission prompt.\n\n" +
                "Turn this switch off to remove the task. Installed files remain. " +
                "To update the startup copy later, turn this off, open the new EXE, then turn it on again. " +
                "Older PickAds tasks are not changed.",
                Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                RefreshAll(true);
                return;
            }
            try { Startup.SetEnabled(on); }
            catch (Exception ex) { app.ShowError(ex); }
            RefreshAll(true);
        }

        void AddSite()
        {
            string input = Prompt("Type the ad site to block, like ads.example.com");
            if (input == null) return;
            string d = AdRules.Clean(input);
            if (!AdRules.IsValidHost(d))
            {
                MessageBox.Show(this, "That doesn't look like a website name.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { Store.Add(new[] { d }, true); }
            catch (Exception ex) { app.ShowError(ex); }
            RefreshAll();
        }

        void RemoveSelected()
        {
            var selected = list.SelectedItems.Cast<string>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Click a site in the list first (hold Ctrl to pick several).", Program.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Store.Remove(selected); }
            catch (Exception ex) { app.ShowError(ex); }
            list.ClearSelected();
            RefreshAll();
        }

        string Prompt(string message)
        {
            using (var f = new PromptForm(message))
                return f.ShowDialog(this) == DialogResult.OK ? f.Value : null;
        }
    }

    // Small rounded "Add site" dialog.
    class PromptForm : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly TextBox box;
        public string Value { get { return box.Text; } }

        public PromptForm(string message)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Add site";
            ClientSize = new Size(360, 164);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            Font = Theme.Font(9.5f, false);

            var card = new Card();
            card.SetBounds(12, 12, 336, 140);
            Controls.Add(card);

            var lbl = new Label();
            lbl.Text = message;
            lbl.ForeColor = Theme.Text;
            lbl.BackColor = Color.Transparent;
            lbl.SetBounds(18, 16, 300, 20);
            card.Controls.Add(lbl);

            // Text box sits inside a rounded "field" panel so it looks like a pill.
            var field = new FieldPanel();
            field.SetBounds(16, 44, 304, 36);
            card.Controls.Add(field);
            box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Neutral;
            box.ForeColor = Theme.Text;
            box.Font = Theme.Font(10f, false);
            box.SetBounds(14, 9, 276, 20);
            field.Controls.Add(box);

            var cancel = new PillButton();
            cancel.Text = "Cancel";
            cancel.SetBounds(124, 94, 92, 32);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            card.Controls.Add(cancel);

            var ok = new PillButton();
            ok.Primary = true;
            ok.Text = "Block";
            ok.SetBounds(224, 94, 96, 32);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            card.Controls.Add(ok);

            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DialogResult = DialogResult.OK; Close(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; Close(); }
            };
            ResumeLayout(false);
            ActiveControl = box;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = Theme.Dark ? 1 : 0;
                if (DwmSetWindowAttribute(Handle, 20, ref dark, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref dark, 4);
                int caption = Theme.Bg.R | (Theme.Bg.G << 8) | (Theme.Bg.B << 16);
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);
            }
            catch { }
        }
    }

    class FieldPanel : Panel
    {
        public FieldPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Neutral;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Card)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
            using (var b = new SolidBrush(Theme.Neutral))
                g.FillPath(b, path);
        }
    }
}

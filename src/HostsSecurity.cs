using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace PickAds
{
    // Only this bounded domain protocol crosses the UAC boundary. No caller paths,
    // scripts, registry keys or command names are accepted by the elevated mode.
    static class HostsProtocol
    {
        public const int MaxDomains = 5000;
        static readonly Regex Host = new Regex(@"\A(?=.{4,253}\z)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,62}\z", RegexOptions.CultureInvariant);
        public static string[] Validate(IEnumerable<string> domains)
        {
            if (domains == null) throw new InvalidDataException("Missing domains.");
            var values = domains.Take(MaxDomains + 1).ToArray();
            if (values.Length > MaxDomains || values.Any(v => v == null || !Host.IsMatch(v)))
                throw new InvalidDataException("Invalid domain list. Use at most 5,000 plain lowercase domain names.");
            return values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        }

        public sealed class Request
        {
            public string Action;
            public string[] Domains;
        }

        public static void Write(TextWriter writer, string action, IEnumerable<string> domains)
        {
            if (action != "enable" && action != "disable" && action != "flush")
                throw new InvalidDataException("Unsupported operation.");
            var values = Validate(domains);
            if (action != "enable" && values.Length != 0)
                throw new InvalidDataException("This operation must not contain domains.");
            writer.WriteLine("PICKADS-HOSTS-1");
            writer.WriteLine(action);
            writer.WriteLine(values.Length.ToString(CultureInfo.InvariantCulture));
            foreach (string domain in values) writer.WriteLine(domain);
            writer.Flush();
        }

        static string Line(TextReader reader, int limit)
        {
            var result = new StringBuilder();
            int c;
            while ((c = reader.Read()) != -1 && c != '\n')
            {
                if (result.Length >= limit) throw new InvalidDataException("Helper request line is too long.");
                result.Append((char)c);
            }
            if (c == -1) throw new EndOfStreamException("Incomplete helper request.");
            return result.ToString().TrimEnd('\r');
        }

        public static Request Read(TextReader reader)
        {
            if (Line(reader, 32) != "PICKADS-HOSTS-1") throw new InvalidDataException("Unknown protocol.");
            string action = Line(reader, 16);
            if (action != "enable" && action != "disable" && action != "flush")
                throw new InvalidDataException("Unsupported operation.");
            int count;
            if (!int.TryParse(Line(reader, 5), NumberStyles.None, CultureInfo.InvariantCulture, out count) || count > MaxDomains)
                throw new InvalidDataException("Invalid domain count.");
            if (action != "enable" && count != 0) throw new InvalidDataException("Unexpected domains.");
            var values = new string[count];
            for (int i = 0; i < count; i++) values[i] = Line(reader, 254);
            return new Request { Action = action, Domains = Validate(values) };
        }
    }

    static class HostsDocument
    {
        const string Start = "# >>> PickAds START";
        const string End = "# <<< PickAds END";
        const string OldStart = "# >>> MC-AdBlock START";
        const string OldEnd = "# <<< MC-AdBlock END";

        public static byte[] Transform(byte[] original, string[] domains, bool enabled)
        {
            domains = HostsProtocol.Validate(domains);
            int start = -1, end = -1;
            for (int pos = 0; pos < original.Length;)
            {
                int next = Array.IndexOf(original, (byte)'\n', pos);
                next = next < 0 ? original.Length : next + 1;
                string token = Encoding.ASCII.GetString(original, pos, next - pos).Trim(' ', '\t', '\r', '\n');
                if (token == Start || token == OldStart)
                {
                    if (start >= 0 || end >= 0) throw new InvalidDataException("Duplicate or nested PickAds hosts markers. No changes made.");
                    start = pos;
                }
                if (token == End || token == OldEnd)
                {
                    if (start < 0 || end >= 0) throw new InvalidDataException("Unmatched PickAds hosts marker. No changes made.");
                    end = next;
                }
                pos = next;
            }
            if ((start < 0) != (end < 0)) throw new InvalidDataException("Incomplete PickAds hosts section. No changes made.");
            var block = new StringBuilder();
            if (enabled)
            {
                block.Append(Start).Append("\r\n# Managed by PickAds. Disable blocking to remove.\r\n");
                foreach (string domain in domains)
                    block.Append("0.0.0.0 ").Append(domain).Append("\r\n:: ").Append(domain).Append("\r\n");
                block.Append(End).Append("\r\n");
            }
            using (var output = new MemoryStream())
            {
                if (start < 0)
                {
                    output.Write(original, 0, original.Length);
                    if (enabled && original.Length > 0 && original[original.Length - 1] != '\n')
                        output.Write(new byte[] { 13, 10 }, 0, 2);
                }
                else output.Write(original, 0, start);
                var bytes = Encoding.ASCII.GetBytes(block.ToString());
                output.Write(bytes, 0, bytes.Length);
                if (end >= 0) output.Write(original, end, original.Length - end);
                return output.ToArray();
            }
        }
    }

    static class HostsFile
    {
        const int MaxBytes = 4 * 1024 * 1024;
        [StructLayout(LayoutKind.Sequential)]
        struct FileInformation
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

        static void RefuseReparse(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("PickAds will not edit linked or redirected hosts paths.");
        }

        public static void Apply(string path, string[] domains, bool enabled)
        {
            // Production always passes Store.HostsPath, never a path from IPC.
            domains = HostsProtocol.Validate(domains);
            RefuseReparse(path);
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                throw new IOException("The hosts file is read-only. Its protection was not changed.");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    FileInformation info;
                    if (!GetFileInformationByHandle(stream.SafeFileHandle, out info))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (info.Links != 1) throw new IOException("PickAds will not edit a hard-linked hosts file.");
                }
                if (stream.Length > MaxBytes) throw new IOException("Hosts is unexpectedly large; no changes made.");
                var original = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < original.Length)
                {
                    int read = stream.Read(original, offset, original.Length - offset);
                    if (read == 0) throw new EndOfStreamException();
                    offset += read;
                }
                var replacement = HostsDocument.Transform(original, domains, enabled);
                if (original.SequenceEqual(replacement)) return;
                string backup = path + ".pickads-backup";
                if (File.Exists(backup)) RefuseReparse(backup);
                else
                    using (var recovery = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        recovery.Write(original, 0, original.Length);
                        recovery.Flush(true);
                    }
                try
                {
                    stream.Position = 0;
                    stream.Write(replacement, 0, replacement.Length);
                    stream.SetLength(replacement.Length);
                    stream.Flush(true);
                }
                catch
                {
                    stream.Position = 0;
                    stream.Write(original, 0, original.Length);
                    stream.SetLength(original.Length);
                    stream.Flush(true);
                    throw;
                }
            }
        }
    }

    static class HostsElevation
    {
        static readonly Regex PipeName = new Regex(@"\APickAdsHosts-[a-f0-9]{32}\z", RegexOptions.CultureInvariant);
        public static void Apply(string[] domains, bool enabled)
        {
            Execute(enabled ? "enable" : "disable", enabled ? domains : new string[0]);
        }
        public static void Flush() { Execute("flush", new string[0]); }

        static void Execute(string action, string[] domains)
        {
            domains = HostsProtocol.Validate(domains);
            string name = "PickAdsHosts-" + Guid.NewGuid().ToString("N");
            var acl = new PipeSecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
            using (var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, acl))
            {
                try
                {
                    using (var process = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--hosts-helper " + name)
                    { UseShellExecute = true, Verb = "runas" }))
                    {
                        var operation = Task.Run(() =>
                        {
                            pipe.WaitForConnection();
                            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
                            using (var reader = new StreamReader(pipe, new UTF8Encoding(false, true), false, 4096, true))
                            {
                                HostsProtocol.Write(writer, action, domains);
                                return reader.ReadLine();
                            }
                        });
                        if (!operation.Wait(30000)) throw new IOException("System operation timed out. Check the hosts status before retrying.");
                        if (operation.Result != "OK" && operation.Result != "OK-CACHE-WARNING")
                            throw new IOException("System operation failed. " + (operation.Result ?? "No reply from helper."));
                        if (process != null && (!process.WaitForExit(5000) || process.ExitCode != 0))
                            throw new IOException("Helper completion was not confirmed. Check the hosts status.");
                        if (operation.Result == "OK-CACHE-WARNING")
                            MessageBox.Show("Hosts was updated, but the DNS cache could not be cleared. Restart your launcher.", "PickAds");
                    }
                }
                catch (Win32Exception ex)
                {
                    if (ex.NativeErrorCode == 1223) throw new IOException("Administrator approval was cancelled. No change was confirmed.");
                    throw;
                }
            }
        }

        public static int RunHelper(string name)
        {
            if (!PipeName.IsMatch(name) || Environment.OSVersion.Platform != PlatformID.Win32NT || !Program.IsAdmin()) return 1;
            // The unprivileged server must never impersonate an elevated client.
            using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous))
            {
                pipe.Connect(15000);
                using (var reader = new StreamReader(pipe, new UTF8Encoding(false, true), false, 4096, true))
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                {
                    try
                    {
                        var pending = Task.Run(() => HostsProtocol.Read(reader));
                        if (!pending.Wait(15000)) throw new IOException("Helper request timed out.");
                        var request = pending.Result;
                        if (request.Action != "flush")
                        {
                            HostsFile.Apply(Store.HostsPath, request.Domains, request.Action == "enable");
                            try { Dns.Flush(); }
                            catch
                            {
                                writer.WriteLine("OK-CACHE-WARNING");
                                return 0;
                            }
                        }
                        else Dns.Flush();
                        writer.WriteLine("OK");
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine("ERROR " + ex.GetBaseException().Message.Replace('\r', ' ').Replace('\n', ' '));
                        return 1;
                    }
                }
            }
        }
    }
}

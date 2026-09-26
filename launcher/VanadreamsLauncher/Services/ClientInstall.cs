using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    public sealed class ClientChunk
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
        /// <summary>Folder under the install root the zip unpacks into: "FINAL FANTASY XI" or "PlayOnlineViewer".</summary>
        public string Dest { get; set; }
        public long Unpacked { get; set; }
    }

    public sealed class ClientManifest
    {
        public string Version { get; set; }
        public List<string> CreateDirs { get; set; } = new List<string>();
        public List<string> ViewerCreateDirs { get; set; } = new List<string>();
        public List<ClientChunk> Chunks { get; set; } = new List<ClientChunk>();
        public long TotalSize => Chunks.Sum(c => c.Size);
        public long TotalUnpacked => Chunks.Sum(c => c.Unpacked);

        public static ClientManifest Parse(string json)
        {
            var d = Json.ParseObject(json) ?? throw new InvalidDataException("The game's download list could not be read.");
            var m = new ClientManifest { Version = Json.Str(d, "client_ver"), CreateDirs = Json.Strings(d, "create_dirs") };
            m.ViewerCreateDirs = Json.Strings(d, "viewer_create_dirs");
            object chunks;
            d.TryGetValue("chunks", out chunks);
            foreach (var o in Json.List(chunks))
            {
                var c = Json.Obj(o);
                m.Chunks.Add(new ClientChunk
                {
                    Name = Json.Str(c, "name"),
                    Size = Json.Long(c, "size"),
                    Sha256 = Json.Str(c, "sha256"),
                    Dest = Json.Str(c, "dest", ClientInstall.GameFolderName),
                    Unpacked = Json.Long(c, "unpacked"),
                });
            }
            if (string.IsNullOrEmpty(m.Version) || m.Chunks.Count == 0) throw new InvalidDataException("The game's download list is empty.");
            return m;
        }
    }

    public sealed class InstallProgress
    {
        public int Part { get; set; }
        public int Parts { get; set; }
        public long Done { get; set; }
        public long Total { get; set; }
        public string Stage { get; set; }
    }

    /// <summary>
    /// Installs the game from the Vanadreams package: each zip is downloaded (resuming), checked against the
    /// manifest's sha256, unpacked and deleted; a record in the install folder lets a stopped install carry on.
    /// xiloader needs PlayOnline's polcore.dll and the game's FFXi.dll registered as COM objects and both folders
    /// in the PlayOnlineUS InstallFolder key, so the last step is one elevated command script.
    /// </summary>
    public static class ClientInstall
    {
        public const string BaseUrl = "https://fairywitch.ca/download/client/";
        public const string GameFolderName = "FINAL FANTASY XI";
        public const string ViewerFolderName = "PlayOnlineViewer";
        public const string RecordName = ".vanadreams-client.txt";

        public static string DefaultRoot => @"C:\Games\Vanadreams";
        public static string ManifestUrl(string version) => BaseUrl + version + "/manifest.json";
        public static string ChunkUrl(string version, string name) => BaseUrl + version + "/" + name;
        public static string GameFolder(string root) => Path.Combine(root, GameFolderName);
        public static string ViewerFolder(string root) => Path.Combine(root, ViewerFolderName);

        /// <summary>Zips already unpacked into this root for this version, from the record.</summary>
        public static HashSet<string> Done(string root, string version)
        {
            var path = Path.Combine(root, RecordName);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return done;
            foreach (var line in File.ReadAllLines(path))
            {
                var f = line.Split(' ');
                if (f.Length >= 3 && f[0] == version) done.Add(f[1] + " " + f[2]);
            }
            return done;
        }

        /// <summary>What is still to do: a zip counts as done only with the same name and hash.</summary>
        public static List<ClientChunk> Remaining(ClientManifest m, HashSet<string> done) =>
            m.Chunks.Where(c => !done.Contains(c.Name + " " + c.Sha256)).ToList();

        public static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        public static async Task InstallFilesAsync(Downloader downloader, ClientManifest m, string root, string downloads,
            IProgress<InstallProgress> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(root);
            var todo = Remaining(m, Done(root, m.Version));
            var total = m.TotalSize;
            var before = total - todo.Sum(c => c.Size);
            var staging = Path.Combine(downloads, "client", m.Version);
            for (var i = 0; i < todo.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var c = todo[i];
                var zip = Path.Combine(staging, c.Name);
                var part = m.Chunks.IndexOf(c) + 1;
                var dl = new Progress<DownloadProgress>(p => progress?.Report(new InstallProgress { Part = part, Parts = m.Chunks.Count, Done = before + p.Done, Total = total, Stage = "Downloading" }));
                if (!File.Exists(zip) || new FileInfo(zip).Length != c.Size)
                    await downloader.DownloadFileAsync(ChunkUrl(m.Version, c.Name), zip, dl, c.Name, c.Size, ct).ConfigureAwait(false);
                progress?.Report(new InstallProgress { Part = part, Parts = m.Chunks.Count, Done = before + c.Size, Total = total, Stage = "Checking and unpacking" });
                if (!string.Equals(Sha256Of(zip), c.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(zip);
                    throw new IOException(c.Name + " came down damaged and was thrown away. Press Install game again to fetch it.");
                }
                await Task.Run(() => Downloader.ExtractZipOverwrite(zip, Path.Combine(root, c.Dest)), ct).ConfigureAwait(false);
                File.AppendAllText(Path.Combine(root, RecordName), $"{m.Version} {c.Name} {c.Sha256}{Environment.NewLine}");
                File.Delete(zip);
                before += c.Size;
            }
            foreach (var d in m.CreateDirs) Directory.CreateDirectory(Path.Combine(GameFolder(root), d));
            foreach (var d in m.ViewerCreateDirs) Directory.CreateDirectory(Path.Combine(ViewerFolder(root), d.Replace('/', '\\')));
        }

        public static int RegisterSteps => 7 + ComServers.Length;

        /// <summary>The elevated step, 32-bit registry view as PlayOnline writes it: both folders into InstallFolder,
        /// the PlayOnline language xiloader reads (English), a controller mapping so a gamepad works from the first
        /// launch (Lee's, 25 Sept 2026; only written where the player has none), and both COM servers registered with
        /// the 32-bit regsvr32. Other game settings are left to the game's own defaults.
        /// Exit code is the number of the first step that failed.</summary>
        public static string RegisterScript(string root)
        {
            var game = GameFolder(root) + "\\";
            var viewer = ViewerFolder(root);
            const string folders = @"HKLM\SOFTWARE\PlayOnlineUS\InstallFolder";
            const string settings = @"HKLM\SOFTWARE\PlayOnlineUS\SquareEnix\PlayOnlineViewer\Settings";
            const string ffxi = @"HKLM\SOFTWARE\PlayOnlineUS\SquareEnix\FinalFantasyXI";
            const string regsvr = @"%SystemRoot%\SysWOW64\regsvr32.exe";
            // a controller already set up on this PC is kept: the value is only added when it is missing
            Func<string, string, int, string> padIfMissing = (name, value, step) =>
                $"reg query \"{ffxi}\" /v {name} /reg:32 >nul 2>&1 || reg add \"{ffxi}\" /v {name} /t REG_SZ /d \"{value}\" /f /reg:32 || exit /b {step}";
            return string.Join("\r\n", new[]
            {
                "@echo off",
                $"reg add \"{folders}\" /v 0001 /t REG_SZ /d \"{game}\\\" /f /reg:32 || exit /b 1",
                $"reg add \"{folders}\" /v 1000 /t REG_SZ /d \"{viewer}\" /f /reg:32 || exit /b 2",
                $"reg add \"{settings}\" /v Language /t REG_DWORD /d 1 /f /reg:32 || exit /b 3",
                $"reg add \"{settings}\" /v SupportLanguage /t REG_DWORD /d 1 /f /reg:32 || exit /b 4",
                padIfMissing("padguid000", "{00000000-0000-0000-0000-000000000000}", 5),
                padIfMissing("padmode000", "1,1,0,0,0,1", 6),
                padIfMissing("padsin000", "8,9,13,12,10,0,1,3,2,15,-1,-1,14,-33,-33,32,32,-36,-36,35,35,6,7,5,4,11,-1", 7),
            }.Concat(ComServers.Select((rel, i) => $"\"{regsvr}\" /s \"{Path.Combine(root, rel)}\" || exit /b {8 + i}"))
             .Concat(new[] { "exit /b 0" })) + "\r\n";
        }

        /// <summary>Every COM server Square Enix's installer registers (read from a retail PC, 25 Sept 2026).
        /// xiloader needs polcore and FFXi; PlayOnline itself, for the Retail profile, uses the rest.</summary>
        public static readonly string[] ComServers =
        {
            @"PlayOnlineViewer\viewer\com\polcore.dll",
            @"PlayOnlineViewer\viewer\com\app.dll",
            @"PlayOnlineViewer\viewer\ax\polmvf.dll",
            @"PlayOnlineViewer\viewer\ax\polmvfINT.dll",
            @"PlayOnlineViewer\viewer\contents\PolContents.dll",
            @"PlayOnlineViewer\viewer\contents\polcontentsINT.dll",
            @"FINAL FANTASY XI\FFXi.dll",
            @"FINAL FANTASY XI\FFXiMain.dll",
            @"FINAL FANTASY XI\FFXiVersions.dll",
        };

        /// <summary>Runs the register script with one administrator prompt. False if the prompt was refused.</summary>
        public static bool RunRegister(string root, string downloads)
        {
            Directory.CreateDirectory(downloads);
            var script = Path.Combine(downloads, "register-game.cmd");
            File.WriteAllText(script, RegisterScript(root));
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new InvalidOperationException("Registering the game failed at step " + p.ExitCode + " of " + RegisterSteps + ".");
                }
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; }   // the prompt was cancelled
        }

        /// <summary>True when this root is what the registry points xiloader and the launcher at.</summary>
        public static bool IsRegistered(string root) =>
            string.Equals(ClientVersion.FindFfxiFolder()?.TrimEnd('\\'), GameFolder(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}

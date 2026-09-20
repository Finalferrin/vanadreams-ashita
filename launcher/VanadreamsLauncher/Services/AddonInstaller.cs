using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    /// <summary>Installing a catalogue item whose files are a folder of a GitHub repo.</summary>
    public static class AddonInstaller
    {
        public static string TargetFolder(string ashitaRoot, CatalogItem item) =>
            item.Install == InstallAction.PivotOverlay ? Path.Combine(PivotConfig.OverlaysRoot(ashitaRoot), item.Id)
                                                       : Path.Combine(ashitaRoot, "addons", item.LoadName ?? item.Id);

        /// <summary>
        /// Brings the item's folder in step with the repo (see RepoSync): only what is missing or changed is
        /// downloaded, and files an earlier install put there are removed once the repo has dropped them.
        /// report is called with (file, number, of how many) for each download.
        /// </summary>
        public static async Task<RepoSync.Plan> InstallRepoFolderAsync(Downloader downloader, LauncherSettings settings, string ashitaRoot, CatalogItem item, Action<string, int, int> report = null)
        {
            var files = await downloader.ListRepoFolderAsync(item.Repo, item.Path, item.Branch).ConfigureAwait(false);
            if (files.Count == 0) throw new InvalidOperationException("Nothing found at " + item.Repo + "/" + item.Path + ".");

            var target = TargetFolder(ashitaRoot, item);
            var plan = await Task.Run(() => RepoSync.Make(files, target)).ConfigureAwait(false);

            var n = 0;
            foreach (var f in plan.Download)
            {
                n++;
                report?.Invoke(f.Rel, n, plan.Download.Count);
                await downloader.DownloadFileAsync(f.Url, RepoSync.LocalPath(target, f.Rel)).ConfigureAwait(false);
            }
            foreach (var rel in plan.Delete)
            {
                try { File.Delete(RepoSync.LocalPath(target, rel)); }
                catch (Exception ex) { Log.Warn("could not remove " + rel + ": " + ex.Message); }
            }
            RepoSync.WriteManifest(target, files.Where(f => RepoSync.IsSafeRel(f.Rel)).Select(f => f.Rel));

            settings.InstalledVersions[item.Id] = item.Version ?? DateTime.Now.ToString("yyyy-MM-dd");
            if (item.Install == InstallAction.PivotOverlay) PivotConfig.AddOverlay(ashitaRoot, item.Id);
            return plan;
        }

        /// <summary>The on-by-default items this player has not been given yet.</summary>
        public static List<CatalogItem> DefaultsToGive(Catalog catalog, IEnumerable<string> alreadyGiven) =>
            catalog.Items.Where(i => i.OnByDefault && i.HasV4 && string.IsNullOrEmpty(i.HeldBack)
                                     && !(alreadyGiven ?? Enumerable.Empty<string>()).Contains(i.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    }
}

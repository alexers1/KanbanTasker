using System;
using System.IO;
using System.Linq;

namespace Kanban_WinUI.Services
{
    /// <summary>
    /// Task attachments live as plain files in one configurable folder (which can be a
    /// network share like \\server\kanban). The database stores only relative file
    /// names — never file contents — so the database stays far under the Express size
    /// limit and moving to a new computer is just copying one folder.
    /// </summary>
    public class AttachmentService
    {
        private static readonly string RootSettingPath = Path.Combine(App.AppDataFolder, "attachments.root");

        private static readonly string DefaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KanbanTasker Attachments");

        /// <summary>The folder that holds all attachment files.</summary>
        public string RootFolder
        {
            get
            {
                try
                {
                    if (File.Exists(RootSettingPath))
                    {
                        var saved = File.ReadAllText(RootSettingPath).Trim();
                        if (!string.IsNullOrWhiteSpace(saved))
                            return saved;
                    }
                }
                catch { }
                return DefaultRoot;
            }
        }

        private void SaveRoot(string root)
        {
            Directory.CreateDirectory(App.AppDataFolder);
            File.WriteAllText(RootSettingPath, root);
        }

        /// <summary>
        /// Copies a file into the attachments folder and returns the relative name
        /// stored in the database. Never stores the file inside the database.
        /// </summary>
        public string AddAttachment(string sourceFilePath)
        {
            Directory.CreateDirectory(RootFolder);

            string name = Path.GetFileName(sourceFilePath);
            string dest = Path.Combine(RootFolder, name);
            if (File.Exists(dest))
            {
                // Unique name when a file with the same name already exists
                name = $"{Path.GetFileNameWithoutExtension(name)}_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(name)}";
                dest = Path.Combine(RootFolder, name);
            }

            File.Copy(sourceFilePath, dest);
            return name;
        }

        /// <summary>Absolute path of a stored attachment name.</summary>
        public string ResolvePath(string relativeName) => Path.Combine(RootFolder, relativeName);

        /// <summary>
        /// Moves the whole attachment store to a new folder (e.g. when changing
        /// computers or pointing at a network share). Files are copied, the setting
        /// is updated, and the database needs no change because it stores only
        /// relative names. Returns the number of files copied.
        /// </summary>
        public int MigrateTo(string newRoot)
        {
            if (string.IsNullOrWhiteSpace(newRoot))
                throw new ArgumentException("Target folder is empty.", nameof(newRoot));

            string oldRoot = RootFolder;
            Directory.CreateDirectory(newRoot);

            int copied = 0;
            if (Directory.Exists(oldRoot) &&
                !string.Equals(Path.GetFullPath(oldRoot), Path.GetFullPath(newRoot), StringComparison.OrdinalIgnoreCase))
            {
                foreach (var file in Directory.EnumerateFiles(oldRoot))
                {
                    File.Copy(file, Path.Combine(newRoot, Path.GetFileName(file)), overwrite: true);
                    copied++;
                }
            }

            SaveRoot(newRoot);
            return copied;
        }
    }
}

using System;
using System.IO;

namespace Kanban_WinUI.Services
{
    /// <summary>
    /// Attachments are stored as <b>path references</b>, not uploaded files: the database
    /// keeps a "<c>MACHINE\full\path</c>" string that points at a file or a whole folder on
    /// the machine that created it. Nothing is copied and no bytes are stored — the point is
    /// only to remember <i>which machine</i> and <i>where</i> a task's material lives, and to
    /// open it in File Explorer from the desktop client.
    /// </summary>
    public class AttachmentService
    {
        /// <summary>The name of this computer (the reference prefix for new attachments).</summary>
        public static string ThisMachine => Environment.MachineName;

        /// <summary>Builds a stored reference for a picked file/folder: "MACHINE\full\path".</summary>
        public string MakeReference(string fullPath) => $"{ThisMachine}\\{fullPath}";

        /// <summary>Splits a stored reference into its machine name and the local path.
        /// References without a machine prefix (legacy names) resolve to this machine.</summary>
        public (string Machine, string Path) Parse(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return (ThisMachine, "");

            int slash = reference.IndexOf('\\');
            // A drive-qualified path ("D:\...") or UNC has no machine prefix.
            if (slash <= 0 || reference.Length > 1 && reference[1] == ':')
                return (ThisMachine, reference);

            return (reference.Substring(0, slash), reference.Substring(slash + 1));
        }

        /// <summary>True when the reference points at a location on the current machine.</summary>
        public bool IsOnThisMachine(string reference) =>
            string.Equals(Parse(reference).Machine, ThisMachine, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Opens the reference in File Explorer: a folder opens directly; a file opens its
        /// containing folder with the file selected. Returns an error message, or null on success.
        /// </summary>
        public string? OpenInExplorer(string reference)
        {
            var (machine, path) = Parse(reference);
            if (!string.Equals(machine, ThisMachine, StringComparison.OrdinalIgnoreCase))
                return $"This attachment is on '{machine}'. Open it from that computer.";

            try
            {
                if (Directory.Exists(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                else if (File.Exists(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                else
                    return $"Not found on this machine: {path}";
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}

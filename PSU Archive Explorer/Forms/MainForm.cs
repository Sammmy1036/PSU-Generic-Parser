using Microsoft.WindowsAPICodePack.Dialogs;
using psu_archive_explorer.FileViewers;
using psu_archive_explorer.Forms;
using psu_archive_explorer.Forms.FileViewers;
using psu_archive_explorer.Forms.FileViewers.Enemies;
using psu_archive_explorer;
using PSULib;
using PSULib.FileClasses.Archives;
using PSULib.FileClasses.Bosses;
using PSULib.FileClasses.Characters;
using PSULib.FileClasses.Enemies;
using PSULib.FileClasses.General;
using PSULib.FileClasses.General.Scripts;
using PSULib.FileClasses.Items;
using PSULib.FileClasses.Maps;
using PSULib.FileClasses.Missions;
using PSULib.FileClasses.Models;
using PSULib.FileClasses.Textures;
using PSULib.Support;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace psu_archive_explorer
{
    public partial class MainForm : Form
    {
        ContainerFile loadedContainer;
        PsuFile currentRight;
        MainSettings settings;
        private HexEditForm currentFileHexForm;
        private byte[] pendingAdxReplacementBytes;
        public string gameDirectory
        {
            get { return Properties.Settings.Default.GameDirectory ?? ""; }
            set
            {
                Properties.Settings.Default.GameDirectory = value ?? "";
                Properties.Settings.Default.Save();
            }
        }
        public bool batchPngExport = true;
        public bool batchWavExport = true;
        public bool batchDat2WavExport = true;
        public bool batchRecursive = true;
        public bool batchExportSubArchiveFiles = false;
        public bool compressNMLL = false;
        public bool compressTMLL = false;
        public bool exportMetaData = true;

        private class FileTreeNodeTag
        {
            public ContainerFile OwnerContainer { get; set; }
            public string FileName { get; set; }
            public string FullPath { get; set; }
        }

        public MainForm()
        {
            InitializeComponent();

            // Keep the selected file visibly highlighted (gray) when focus moves
            // off the tree, e.g. when the user clicks into the right-hand panel.
            treeView1.HideSelection = false;

            Text += System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            setAFSEnabled(false);

            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            this.MinimumSize = new Size(900, 600);

            string indexPath = Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath),
                "psu_file_index.gz");
            FileIndex.LoadFromFile(indexPath);

            // Show welcome screen on first launch (before any archive has been loaded).
            // It is torn down by HideWelcomeScreen() as soon as a file loads.
            this.Shown += (s, e) => ShowWelcomeScreen();
        }

        /// <summary>
        /// Clears all controls from the right panel, disposing them first so any
        /// IDisposable resources (audio playback, file handles, etc.) are released.
        /// Always use this instead of calling splitContainer1.Panel2.Controls.Clear() directly.
        /// </summary>
        private void ClearRightPanel()
        {
            // If the welcome screen is up, tear it down — the user is about to view content.
            if (welcomeVisible) HideWelcomeScreen();

            var toDispose = splitContainer1.Panel2.Controls.Cast<Control>().ToList();
            splitContainer1.Panel2.Controls.Clear();   // ← restore this line
            foreach (var c in toDispose)
            {
                c.Dispose();
            }
        }

        // ====================== Welcome Screen (shown only on first launch) ======================
        private const string GitHubUrl = "https://github.com/Sammmy1036/PSU-Archive-Explorer/";

        // References so we can tear the welcome screen down cleanly when a file is loaded.
        private PictureBox welcomeLogoBox;
        private Panel welcomePanel;
        private bool welcomeVisible = false;

        /// <summary>
        /// Displays a logo in the left tree panel and a welcome message in the right panel.
        /// Only intended to be shown on first launch, before any archive has been loaded.
        /// The logo is inserted UNDERNEATH the existing tree/searchResults controls so that
        /// the search box and search results continue to function normally — when the user
        /// starts typing, searchResults becomes visible and naturally covers the logo.
        /// </summary>
        private void ShowWelcomeScreen()
        {
            if (welcomeVisible) return;

            // ---- LEFT: logo occupies the empty tree region ----
            // The tree is empty at launch, so we hide it to let the logo show through.
            // searchResults is already hidden by default and will be shown by RunSearch
            // as soon as the user types a query (at which point it layers over the logo).
            treeView1.Visible = false;

            welcomeLogoBox = new PictureBox
            {
                Name = "welcomeLogoBox",
                Location = new Point(0, 26),
                Size = new Size(
                    splitContainer1.Panel1.ClientSize.Width,
                    splitContainer1.Panel1.ClientSize.Height - 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                       | AnchorStyles.Left | AnchorStyles.Right,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = splitContainer1.Panel1.BackColor,
            };

            // Prefer the embedded high-resolution logo resource. Fall back to the
            // form icon if the resource is missing (e.g. during early development
            // before the logo has been added to Properties/Resources).
            try
            {
                System.Drawing.Image logoImage = Properties.Resources.Logo;
                if (logoImage != null)
                {
                    welcomeLogoBox.Image = logoImage;
                }
                else if (this.Icon != null)
                {
                    welcomeLogoBox.Image = this.Icon.ToBitmap();
                }
            }
            catch
            {
                // If anything goes wrong loading the embedded resource, try the icon;
                // if that also fails, leave the PictureBox empty rather than crashing.
                try
                {
                    if (this.Icon != null)
                        welcomeLogoBox.Image = this.Icon.ToBitmap();
                }
                catch { }
            }

            splitContainer1.Panel1.Controls.Add(welcomeLogoBox);
            // Put the logo at the BACK of the z-order. searchBox / searchResults / treeView1
            // all need to be able to sit on top of it when they become visible.
            welcomeLogoBox.SendToBack();

            // ---- RIGHT: welcome message ----
            // Use a container Panel so we can dispose everything in one go via ClearRightPanel().
            welcomePanel = new Panel
            {
                Name = "welcomePanel",
                Dock = DockStyle.Fill,
                BackColor = splitContainer1.Panel2.BackColor,
                Padding = new Padding(30, 40, 30, 30),
            };

            var titleLabel = new Label
            {
                Text = "PSU Archive Explorer v1.0.0.0",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = SystemColors.ControlText,
                AutoSize = true,
                Location = new Point(30, 40),
            };

            var checkReleaseLabel = new Label
            {
                Text = "Check GitHub for the latest release:",
                Font = new Font("Segoe UI", 10F),
                ForeColor = SystemColors.ControlText,
                AutoSize = true,
                Location = new Point(30, 100),
            };

            var githubLink = new LinkLabel
            {
                Text = GitHubUrl,
                Font = new Font("Segoe UI", 10F),
                AutoSize = true,
                Location = new Point(30, 125),
            };
            githubLink.LinkClicked += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = GitHubUrl,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not open link: " + ex.Message,
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            var helpfulLabel = new Label
            {
                Text = "Getting started:\r\n\r\n"
                     + "• File ▸ Open or Ctrl + O to open a PSU archive\r\n\r\n"
                     + "• Use the tree on the left to browse the contents of the container\r\n\r\n"
                     + "• Use the search box above the tree panel on the left to find files by name or hash\r\n\r\n"
                     + "• Right click a file for extraction / replacement / renaming options\r\n\r\n"
                     + "• Batch ▸ Extract All In Folder to bulk extract a directory of archives\r\n\r\n"
                     + "Happy Modding!",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = SystemColors.ControlText,
                AutoSize = true,
                Location = new Point(30, 175),
            };

            welcomePanel.Controls.Add(titleLabel);
            welcomePanel.Controls.Add(checkReleaseLabel);
            welcomePanel.Controls.Add(githubLink);
            welcomePanel.Controls.Add(helpfulLabel);

            splitContainer1.Panel2.Controls.Add(welcomePanel);

            welcomeVisible = true;

            // Move focus off the search box. Because treeView1 is hidden, the form's
            // default tab focus would otherwise land on searchBox, firing its Enter
            // event and clearing the "Search files..." placeholder before the user
            // has even clicked it.
            this.ActiveControl = null;
        }

        /// <summary>
        /// Removes the welcome screen and restores the normal tree/right-panel view.
        /// Safe to call even if the welcome screen is not currently visible.
        /// </summary>
        private void HideWelcomeScreen()
        {
            if (!welcomeVisible) return;

            if (welcomeLogoBox != null)
            {
                splitContainer1.Panel1.Controls.Remove(welcomeLogoBox);
                welcomeLogoBox.Image?.Dispose();
                welcomeLogoBox.Dispose();
                welcomeLogoBox = null;
            }
            treeView1.Visible = true;

            if (welcomePanel != null)
            {
                splitContainer1.Panel2.Controls.Remove(welcomePanel);
                welcomePanel.Dispose();
                welcomePanel = null;
            }

            welcomeVisible = false;
        }

        // ====================== Official ADX Header Check ======================
        private bool IsValidAdxFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath) || new FileInfo(filePath).Length < 16)
                    return false;

                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] header = new byte[16];
                    int bytesRead = fs.Read(header, 0, 16);
                    if (bytesRead < 8) return false;

                    if (header[0] != 0x80 || header[1] != 0x00) return false;

                    ushort headerSize = (ushort)((header[2] << 8) | header[3]);
                    if (headerSize < 8 || headerSize > 4096) return false;

                    if (header[4] != 0x03 && header[4] != 0x04) return false;
                    if (header[5] != 0x12) return false;           // most PSU ADX use 0x12
                    byte channels = header[7];
                    if (channels == 0 || channels > 8) return false;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // ====================== Open File Handler ======================
        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                string fileName = fileDialog.FileName;
                this.Text = "PSU Archive Explorer " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + " " + Path.GetFileName(fileName);
                ClearRightPanel();
                pendingAdxReplacementBytes = null;

                bool success = openPSUArchive(fileName, treeView1.Nodes);

                if (!success)
                {
                    TryOpenAsAdx(fileName);
                }
            }
        }

        // ====================== Smart ADX Detection & Open ======================
        private void TryOpenAsAdx(string fileName)
        {
            if (!IsValidAdxFile(fileName))
            {
                ShowAdxSuggestion(fileName);
                return;
            }

            //  Hashed ADX files
            if (!fileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase))
            {
                if (IsHashedAdxFilename(fileName))
                {
                    // Does NOT rename hashed files, hashes with a singular ADX file get the extension
                    OpenSingleFileAsAdx(fileName);
                    return;
                }

                // Normal ADX files get auto renamed appending the .adx
                string newPath = Path.ChangeExtension(fileName, ".adx");

                try
                {
                    if (File.Exists(newPath))
                    {
                        DialogResult dr = MessageBox.Show(
                            $"A file named '{Path.GetFileName(newPath)}' already exists.\n\nOverwrite it?",
                            "ADX Rename Conflict",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (dr != DialogResult.Yes)
                        {
                            OpenSingleFileAsAdx(fileName);
                            return;
                        }

                        File.Delete(newPath);
                    }

                    File.Move(fileName, newPath);
                    fileName = newPath;

                    MessageBox.Show(
                        $"Valid ADX audio file detected!\n\nRenamed → {Path.GetFileName(fileName)}\n\nAdded to tree view.",
                        "ADX Auto-Renamed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not auto-rename:\n{ex.Message}\n\nOpening anyway.",
                        "Rename Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            OpenSingleFileAsAdx(fileName);
        }

        private bool IsHashedAdxFilename(string fileName)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (baseName.Length != 32)
                return false;

            return baseName.All(c => "0123456789abcdefABCDEF".Contains(c));
        }

        // ====================== Open ADX as tree node ======================
        private void OpenSingleFileAsAdx(string filePath)
        {
            treeView1.BeginUpdate();
            try
            {
                treeView1.Nodes.Clear();
                ClearRightPanel();

                loadedContainer = null;   // Since there are no real containers for ADX files

                string fileNameOnDisk = Path.GetFileName(filePath);

                // Hashed ADX files will append .adx to the tree view only
                string displayName = fileNameOnDisk;

                string cleaned = fileNameOnDisk.TrimStart('-');   // removes any leading dashes the container might add
                if (cleaned.Length == 32 && cleaned.All(c => "0123456789abcdefABCDEF".Contains(c)))
                {
                    displayName = cleaned.ToLowerInvariant() + ".adx";
                }

                TreeNode adxNode = new TreeNode(displayName);
                adxNode.Tag = new FileTreeNodeTag
                {
                    OwnerContainer = null,
                    FileName = displayName,           // Extraction will now save as hash.adx
                    FullPath = filePath
                };

                adxNode.ContextMenuStrip = arbitraryFileContextMenuStrip;

                treeView1.Nodes.Add(adxNode);
                treeView1.SelectedNode = adxNode;

                LoadAdxIntoRightPanel(filePath, adxNode);
            }
            finally
            {
                treeView1.EndUpdate();
            }
        }

        // ====================== Display ADX Info + Preview in right panel ======================
        private void LoadAdxIntoRightPanel(string filePath, TreeNode node)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                string filename = Path.GetFileName(filePath);

                if (currentFileHexForm != null && !currentFileHexForm.IsDisposed)
                    currentFileHexForm.Close();

                // Clearing controls disposes the previous AdxPreviewPanel (if any),
                // which in turn stops playback and releases NAudio resources.
                ClearRightPanel();

                // Derive the hash (sans .adx) from the filename, if applicable,
                // and look up the original sound title in the ADX hash map.
                string hashKey = Path.GetFileNameWithoutExtension(filename).TrimStart('-');
                string mappedTitle = null;
                if (hashKey.Length == 32
                    && hashKey.All(c => "0123456789abcdefABCDEF".Contains(c)))
                {
                    AdxHashMap.TryGetValue(hashKey.ToLowerInvariant(), out mappedTitle);
                }

                string infoText =
                    "ADX audio file detected.\n\n" +
                    "If you wish to replace this file, convert a .wav to .adx and rename it\n" +
                    "to the hash name without the .adx extension, or replace the .adx in\n" +
                    "this software with your new .adx sound file and save the hash.\n\n" +
                    $"File name: {filename}";

                if (mappedTitle != null)
                {
                    infoText += $"\n\nADX Mapping: {mappedTitle}";
                }

                var previewPanel = new AdxPreviewPanel(filePath, infoText, mappedTitle ?? filename);
                splitContainer1.Panel2.Controls.Add(previewPanel);

                byte[] header = new byte[4];
                if (data.Length >= 4)
                {
                    Array.Copy(data, 0, header, 0, 4);
                }
                currentRight = new UnpointeredFile(filename, data, header);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load ADX file:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                currentRight = null;
            }
        }

        // ====================== ADX Suggestion Dialog ======================
        private void ShowAdxSuggestion(string fileName)
        {
            string message = "Could not load this file as a PSU archive.\n\n" +
                             "Would you like to open the containing folder?";

            DialogResult result = MessageBox.Show(message,
                "Unknown File Format",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + fileName + "\"");
                }
                catch (Exception ex) { Console.WriteLine("Failed to open Explorer: " + ex); }
            }
        }

        // ====================== Display DAT Sound Preview in right panel ======================
        private void LoadDatSoundIntoRightPanel(string filePath, TreeNode node)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                string filename = Path.GetFileName(filePath);

                if (currentFileHexForm != null && !currentFileHexForm.IsDisposed)
                    currentFileHexForm.Close();

                // ClearRightPanel disposes the previous preview panel (if any),
                // which in turn stops playback and releases NAudio resources.
                ClearRightPanel();

                string infoText =
                    "DAT sound file detected.\n\n" +
                    "This is a raw PCM sound container used by PSU.\n" +
                    "You can preview playback below, or use Extract Selected\n" +
                    "to save it as either the raw .dat or a converted .wav.\n\n" +
                    $"File name: {filename}";

                var previewPanel = new DatPreviewPanel(filePath, infoText);
                splitContainer1.Panel2.Controls.Add(previewPanel);

                byte[] header = new byte[4];
                if (data.Length >= 4)
                {
                    Array.Copy(data, 0, header, 0, 4);
                }
                currentRight = new UnpointeredFile(filename, data, header);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load DAT file:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                currentRight = null;
            }
        }

        /// <summary>
        /// Loads an archive-embedded .dat file without blocking the UI thread. Shows
        /// a DatPreviewPanel in its "Decoding..." state immediately, then extracts
        /// and decodes the file on a background thread. If the .dat turns out to be
        /// non-sound, falls back to the normal parsed-file viewer path.
        /// </summary>
        private void LoadArchiveDatAsync(ContainerFile parent, int index, string fileName)
        {
            // Capture the selected TreeNode and container so we can detect stale
            // completions (user clicked away before we finished).
            TreeNode nodeAtStart = treeView1.SelectedNode;

            // Show a placeholder panel immediately in "Decoding..." state.
            string infoText =
                "DAT file detected. Checking for sound data...\n\n" +
                $"File name: {fileName}";

            var panel = new DatPreviewPanel(infoText, externalProvider: true);
            splitContainer1.Panel2.Controls.Add(panel);

            // If the panel gets disposed (e.g. user clicks another node), its
            // cancellation token flips. We check it before touching UI.
            CancellationToken panelCt = panel.DecodeCancellationToken;

            Task.Run(() =>
            {
                try
                {
                    // Step 1: extract/parse (the slow part for archive DATs)
                    PsuFile parsed = parent.getFileParsed(index);
                    panelCt.ThrowIfCancellationRequested();

                    byte[] raw = null;
                    if (parsed is UnpointeredFile unpointed && unpointed.theData != null)
                    {
                        raw = unpointed.theData;
                    }
                    else
                    {
                        RawFile rf = parent.getFileRaw(index);
                        raw = rf?.fileContents ?? rf?.WriteToBytes(false);
                    }

                    panelCt.ThrowIfCancellationRequested();

                    // Step 2: quick 8KB signature scan
                    if (raw == null || !DatConverter.IsSoundDat(raw))
                    {
                        return new ArchiveDatResult { IsSound = false, Parsed = parsed };
                    }

                    // As soon as we know it's a sound DAT, update the hint text so the
                    // user isn't staring at "Checking for sound data..." while the decode
                    // finishes. SetInfoText marshals to the UI thread internally.
                    string soundInfo =
                        "DAT sound file detected.\n\n" +
                        "This is a raw PCM sound container used by PSU.\n" +
                        "You can preview playback below, or use Extract Selected\n" +
                        "to save it as either the raw .dat or a converted .wav.\n\n" +
                        $"File name: {fileName}";
                    panel.SetInfoText(soundInfo);

                    // Step 3: the actual decode (also slow, but now the user already
                    // sees the correct hint and a "Decoding..." status).
                    panelCt.ThrowIfCancellationRequested();
                    byte[] wav = DatConverter.DecodeToWav(raw);
                    return new ArchiveDatResult
                    {
                        IsSound = true,
                        Parsed = parsed,
                        WavBytes = wav,
                        RawBytes = raw
                    };
                }
                catch (OperationCanceledException)
                {
                    return new ArchiveDatResult { Canceled = true };
                }
                catch (Exception ex)
                {
                    return new ArchiveDatResult { Error = ex.Message };
                }
            }, panelCt).ContinueWith(t =>
            {
                if (panel.IsCancelledOrDisposed) return;
                if (this.IsDisposed) return;

                // Also bail if the user has already navigated to a different node.
                if (!ReferenceEquals(treeView1.SelectedNode, nodeAtStart)) return;

                ArchiveDatResult result = t.Result;
                if (result.Canceled) return;

                try
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        if (panel.IsCancelledOrDisposed) return;
                        if (!ReferenceEquals(treeView1.SelectedNode, nodeAtStart)) return;

                        if (!string.IsNullOrEmpty(result.Error))
                        {
                            panel.SetDecodeError($"Preview failed: {result.Error}");
                            return;
                        }

                        if (result.IsSound)
                        {
                            currentRight = result.Parsed;
                            panel.SetDecodedWav(result.WavBytes);
                        }
                        else
                        {
                            // Non-sound .dat — swap the placeholder panel out for the
                            // normal viewer path.
                            ClearRightPanel();
                            setRightPanel(result.Parsed);
                        }
                    }));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }, TaskScheduler.Default);
        }

        // Small DTO for marshaling background results back to the UI thread.
        private class ArchiveDatResult
        {
            public bool IsSound;
            public bool Canceled;
            public PsuFile Parsed;
            public byte[] RawBytes;
            public byte[] WavBytes;
            public string Error;
        }

        private bool openPSUArchive(string fileName, TreeNodeCollection treeNodeCollection)
        {
            bool isValidArchive = false;
            byte[] formatName = new byte[4];

            treeView1.BeginUpdate();
            try
            {
                using (Stream stream = File.Open(fileName, FileMode.Open))
                {
                    int headerBytesRead = stream.Read(formatName, 0, 4);
                    if (headerBytesRead < 4)
                    {
                        return false;
                    }

                    string identifier = Encoding.ASCII.GetString(formatName, 0, 4);
                    if (identifier == "NMLL" || identifier == "NMLB")
                    {
                        setAFSEnabled(false);
                        treeNodeCollection.Clear();
                        loadedContainer = new NblLoader(stream);
                        ClearRightPanel();
                        addChildFiles(treeNodeCollection, loadedContainer);
                        compressNMLL = loadedContainer.Compressed;
                        compressTMLL = loadedContainer.getFilenames().Count > 1 && ((NblChunk)loadedContainer.getFileParsed(1)).Compressed;
                        isValidArchive = true;
                    }
                    else if (identifier == "AFS\0")
                    {
                        setAFSEnabled(true);
                        treeNodeCollection.Clear();
                        loadedContainer = new AfsLoader(stream);
                        ClearRightPanel();
                        addChildFiles(treeNodeCollection, loadedContainer);
                        isValidArchive = true;
                    }
                    else if (BitConverter.ToInt16(formatName, 0) == 0x50AF)
                    {
                        setAFSEnabled(false);
                        treeNodeCollection.Clear();
                        loadedContainer = new MiniAfsLoader(stream);
                        ClearRightPanel();
                        addChildFiles(treeNodeCollection, loadedContainer);
                        isValidArchive = true;
                    }
                }
            }
            finally
            {
                treeView1.EndUpdate();
            }

            return isValidArchive;
        }

        private void setAFSEnabled(bool isActive)
        {
            zoneUD.Enabled = isActive;
            addZoneButton.Enabled = isActive;
            setZoneButton.Enabled = isActive;
            addFileButton.Enabled = isActive;
            setQuestButton.Enabled = isActive;
        }

        /// <summary>
        /// Adds a container file's children to a given node collection.
        /// </summary>
        /// <param name="currNode">node collection</param>
        /// <param name="toRead">container file</param>
        private void addChildFiles(TreeNodeCollection currNode, ContainerFile toRead)
        {
            List<string> filenames = toRead.getFilenames();
            for (int i = 0; i < filenames.Count; i++)
            {
                string filename = filenames[i];
                TreeNode temp = new TreeNode(filename);
                if (toRead is NblLoader)
                {
                    temp.ContextMenuStrip = nblChunkContextMenuStrip;
                }
                else
                {
                    temp.ContextMenuStrip = arbitraryFileContextMenuStrip;
                }

                if (toRead is AfsLoader || toRead is NblLoader || toRead is MiniAfsLoader)
                {
                    PsuFile child = toRead.getFileParsed(i);
                    if (child != null && child is ContainerFile)
                    {
                        addChildFiles(temp.Nodes, (ContainerFile)child);
                        if (((ContainerFile)child).Compressed)
                        {
                            temp.ForeColor = Color.Green;
                        }
                    }
                }
                else //NBL chunk as parent
                {
                    //For an NBL chunk, only read parsed children if they're containers.
                    //This is sort of a mediocre variety of lazy loading...
                    RawFile raw = toRead.getFileRaw(i);
                    if (filename.EndsWith(".nbl") || raw.fileheader == "NMLL" || raw.fileheader == "TMLL")
                    {
                        ContainerFile parsed = (ContainerFile)toRead.getFileParsed(i);
                        addChildFiles(temp.Nodes, parsed);
                        if (parsed.Compressed)
                        {
                            temp.ForeColor = Color.Green;
                        }
                    }
                }
                temp.Tag = new FileTreeNodeTag { OwnerContainer = toRead, FileName = filename };
                currNode.Add(temp);
            }
        }

        private void extractPSUArchive(string fileName, string outDirectory)
        {
            string baseName = Path.GetFileName(fileName);
            string finalDirectory = Path.Combine(outDirectory, baseName + "_ext");
            byte[] formatName = new byte[4];

            bool handled = false;
            using (Stream stream = File.Open(fileName, FileMode.Open))
            {
                int headerBytesRead = stream.Read(formatName, 0, 4);
                if (headerBytesRead < 4)
                {
                }
                else
                {
                    string identifier = Encoding.ASCII.GetString(formatName, 0, 4);
                    short shortId = BitConverter.ToInt16(formatName, 0);

                    if (identifier == "NMLL" || identifier == "NMLB")
                    {
                        loadedContainer = new NblLoader(stream);
                        exportChildFiles(loadedContainer, finalDirectory);
                        handled = true;
                    }
                    else if (identifier == "AFS\0")
                    {
                        loadedContainer = new AfsLoader(stream);
                        exportChildFiles(loadedContainer, finalDirectory);
                        handled = true;
                    }
                    else if (shortId == 0x50AF)
                    {
                        loadedContainer = new MiniAfsLoader(stream);
                        exportChildFiles(loadedContainer, finalDirectory);
                        handled = true;
                    }
                }
            }

            if (!handled)
            {
                // Standalone ADX on disk — either a hashed filename (32 hex chars)
                // or a regular *.adx file. Validate by header, then either convert
                // to WAV or copy the raw bytes depending on the batchWavExport setting.
                bool isHashedAdx = IsHashedAdxFilename(baseName) && IsValidAdxFile(fileName);
                bool isPlainAdx = baseName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) && IsValidAdxFile(fileName);

                if (isHashedAdx || isPlainAdx)
                {
                    try
                    {
                        Directory.CreateDirectory(finalDirectory);

                        // Hashed files get .adx appended (matches the single-file Extract All
                        // behavior in exportNode); plain .adx files keep their name as-is.
                        string outBase = isHashedAdx ? baseName + ".adx" : baseName;

                        if (batchWavExport)
                        {
                            string wavName = Path.ChangeExtension(outBase, ".wav");
                            string wavPath = Path.Combine(finalDirectory, wavName);

                            try
                            {
                                byte[] adxBytes = File.ReadAllBytes(fileName);
                                byte[] wavBytes = AdxDecoder.DecodeToWav(adxBytes);
                                File.WriteAllBytes(wavPath, wavBytes);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(
                                    $"ADX->WAV conversion failed for {baseName}: {ex.Message}. " +
                                    "Writing raw .adx instead.");
                                string adxPath = Path.Combine(finalDirectory, outBase);
                                File.Copy(fileName, adxPath, overwrite: true);
                            }
                        }
                        else
                        {
                            string destFile = Path.Combine(finalDirectory, outBase);
                            File.Copy(fileName, destFile, overwrite: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Unable to process ADX " + baseName + ": " + ex.Message);
                    }
                }
                else if (baseName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                {
                    // Standalone DAT on disk. Signature check decides whether it's a sound
                    // DAT (convert) or a non-sound DAT (copy raw). Setting off → always raw.
                    try
                    {
                        Directory.CreateDirectory(finalDirectory);

                        if (batchDat2WavExport && DatConverter.IsSoundDat(fileName))
                        {
                            string wavName = Path.ChangeExtension(baseName, ".wav");
                            string wavPath = Path.Combine(finalDirectory, wavName);

                            try
                            {
                                byte[] datBytes = File.ReadAllBytes(fileName);
                                byte[] wavBytes = DatConverter.DecodeToWav(datBytes);
                                File.WriteAllBytes(wavPath, wavBytes);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(
                                    $"DAT->WAV conversion failed for {baseName}: {ex.Message}. " +
                                    "Writing raw .dat instead.");
                                string datPath = Path.Combine(finalDirectory, baseName);
                                File.Copy(fileName, datPath, overwrite: true);
                            }
                        }
                        else
                        {
                            // Non-sound .dat, or setting off — copy raw.
                            string destFile = Path.Combine(finalDirectory, baseName);
                            File.Copy(fileName, destFile, overwrite: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Unable to process DAT " + baseName + ": " + ex.Message);
                    }
                }
            }
        }

        private void exportChildFiles(ContainerFile toRead, string outDirectory)
        {
            Directory.CreateDirectory(outDirectory);
            List<string> filenames = toRead.getFilenames();
            List<string> writtenFiles = new List<string>();

            for (int i = 0; i < filenames.Count; i++)
            {
                bool isArchive = false;
                string filename = filenames[i];

                bool isKnownRawType =
                    filename.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase) ||
                    filename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);

                if (!isKnownRawType)
                {
                    if (toRead is AfsLoader || toRead is NblLoader || toRead is MiniAfsLoader)
                    {
                        PsuFile child = toRead.getFileParsed(i);
                        if (child != null && child is ContainerFile)
                        {
                            isArchive = true;
                            if (filename == "NMLL chunk" || filename == "TMLL chunk")
                                exportChildFiles((ContainerFile)child, outDirectory);
                            else
                                exportChildFiles((ContainerFile)child, Path.Combine(outDirectory, filename + "_ext"));
                        }
                    }
                    else
                    {
                        RawFile raw = toRead.getFileRaw(i);
                        if (filename.EndsWith(".nbl") || raw.fileheader == "NMLL" || raw.fileheader == "TMLL")
                        {
                            isArchive = true;
                            exportChildFiles((ContainerFile)toRead.getFileParsed(i), outDirectory);
                        }
                    }
                }

                try
                {
                    if (isArchive)
                    {
                        if (batchExportSubArchiveFiles)
                            extractFile(toRead.getFileParsed(i), Path.Combine(outDirectory, filename));
                        continue;
                    }
                    else if (filename.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase))
                    {
                        if (toRead is AfsLoader || toRead is MiniAfsLoader)
                            filename = CheckForDupeFilenames(writtenFiles, filename);

                        RawFile sfdRaw = toRead.getFileRaw(i);
                        if (sfdRaw?.fileContents != null)
                        {
                            File.WriteAllBytes(Path.Combine(outDirectory, filename), sfdRaw.fileContents);
                            writtenFiles.Add(filename);
                        }
                    }
                    else if (filename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase))
                    {
                        if (toRead is AfsLoader || toRead is MiniAfsLoader)
                            filename = CheckForDupeFilenames(writtenFiles, filename);

                        RawFile adxRaw = toRead.getFileRaw(i);
                        if (adxRaw?.fileContents != null)
                        {
                            if (batchWavExport)
                            {
                                // Try ADX → WAV. On any failure (non-PSU variant,
                                // corrupt data, etc.) fall back to writing the raw
                                // .adx so batch extraction never loses a file.
                                string wavName = Path.ChangeExtension(filename, ".wav");

                                // Re-check dupes against the .wav name — rare, but
                                // AFS containers can have repeated filenames.
                                if (toRead is AfsLoader || toRead is MiniAfsLoader)
                                    wavName = CheckForDupeFilenames(writtenFiles, wavName);

                                try
                                {
                                    byte[] wavBytes = AdxDecoder.DecodeToWav(adxRaw.fileContents);
                                    File.WriteAllBytes(Path.Combine(outDirectory, wavName), wavBytes);
                                    writtenFiles.Add(wavName);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(
                                        $"ADX→WAV conversion failed for {filename}: {ex.Message}. " +
                                        "Writing raw .adx instead.");
                                    File.WriteAllBytes(Path.Combine(outDirectory, filename), adxRaw.fileContents);
                                    writtenFiles.Add(filename);
                                }
                            }
                            else
                            {
                                File.WriteAllBytes(Path.Combine(outDirectory, filename), adxRaw.fileContents);
                                writtenFiles.Add(filename);
                            }
                        }
                    }
                    else if (filename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                    {
                        if (toRead is AfsLoader || toRead is MiniAfsLoader)
                            filename = CheckForDupeFilenames(writtenFiles, filename);

                        RawFile datRaw = toRead.getFileRaw(i);
                        if (datRaw?.fileContents != null)
                        {
                            // Only attempt conversion if: setting is on AND bytes actually
                            // look like a sound DAT. Non-sound .dat files (and any other
                            // case) write raw — no spam, no wasted conversion attempts.
                            if (batchDat2WavExport && DatConverter.IsSoundDat(datRaw.fileContents))
                            {
                                string wavName = Path.ChangeExtension(filename, ".wav");

                                if (toRead is AfsLoader || toRead is MiniAfsLoader)
                                    wavName = CheckForDupeFilenames(writtenFiles, wavName);

                                try
                                {
                                    byte[] wavBytes = DatConverter.DecodeToWav(datRaw.fileContents);
                                    File.WriteAllBytes(Path.Combine(outDirectory, wavName), wavBytes);
                                    writtenFiles.Add(wavName);
                                }
                                catch (Exception ex)
                                {
                                    // Genuine failure on a file that did have the signature —
                                    // log it and fall back to raw.
                                    Console.WriteLine(
                                        $"DAT→WAV conversion failed for {filename}: {ex.Message}. " +
                                        "Writing raw .dat instead.");
                                    File.WriteAllBytes(Path.Combine(outDirectory, filename), datRaw.fileContents);
                                    writtenFiles.Add(filename);
                                }
                            }
                            else
                            {
                                // Non-sound .dat, or setting off — raw extract.
                                File.WriteAllBytes(Path.Combine(outDirectory, filename), datRaw.fileContents);
                                writtenFiles.Add(filename);
                            }
                        }
                    }
                    else if (filename.Contains(".xvr") && batchPngExport)
                    {
                        if (toRead is AfsLoader || toRead is MiniAfsLoader)
                            filename = CheckForDupeFilenames(writtenFiles, filename);
                        filename = filename.Replace(".xvr", ".png");
                        ((ITextureFile)toRead.getFileParsed(i)).mipMaps[0].Save(Path.Combine(outDirectory, filename));
                    }
                    else
                    {
                        if (toRead is AfsLoader || toRead is MiniAfsLoader)
                            filename = CheckForDupeFilenames(writtenFiles, filename);
                        File.WriteAllBytes(Path.Combine(outDirectory, filename), toRead.getFileRaw(i).WriteToBytes(exportMetaData));
                    }
                }
                catch
                {
                    Console.WriteLine("Unable to extract " + filename + ". The file may be in use, inaccessible, or incompatible. Skipping.");
                }
            }
        }

        private static string CheckForDupeFilenames(List<string> writtenFiles, string filename)
        {
            if (writtenFiles.Contains(filename))
            {
                string nameOnly = Path.GetFileNameWithoutExtension(filename);
                string ext = Path.GetExtension(filename);
                int j = 0;
                string candidate;
                do
                {
                    candidate = nameOnly + $"_{j}" + ext;
                    j++;
                }
                while (writtenFiles.Contains(candidate));
                filename = candidate;
                writtenFiles.Add(filename);
            }
            else
            {
                writtenFiles.Add(filename);
            }

            return filename;
        }

        private void setRightPanel(PsuFile toRead)
        {
            ClearRightPanel();
            currentRight = null;
            currentRight = toRead;
            UserControl toAdd = new UserControl();

            if (toRead is ITextureFile texFile)
            {
                toAdd = new TextureViewer(texFile);
            }
            else if (toRead is PointeredFile pointeredFile)
            {
                toAdd = new PointeredFileViewer(pointeredFile);
            }
            else if (toRead is ActDataFile actDataFile)
            {
                toAdd = new ActDataFileViewer(actDataFile);
            }
            else if (toRead is EnemySoundEffectFile seDataFile)
            {
                toAdd = new EnemySoundEffectFileViewer(seDataFile);
            }
            else if (toRead is ListFile listFile)
            {
                toAdd = new ListFileViewer(listFile);
            }
            else if (toRead is XntFile xntFile)
            {
                toAdd = new XntFileViewer(xntFile);
            }
            else if (toRead is XnaFile xnaFile)
            {
                toAdd = new XnaFileViewer(xnaFile);
            }
            else if (toRead is XncpFile xncpFile)
            {
                toAdd = new XncpFileViewer(xncpFile);
            }
            else if (toRead is XnrFile xnrFile)
            {
                toAdd = new XnrFileViewer(xnrFile);
            }
            else if (toRead is XncfFile xncfFile)
            {
                toAdd = new XncfFileViewer(xncfFile);
            }
            else if (toRead is NomFile nomFile)
            {
                toAdd = new NomFileViewer(nomFile);
            }
            else if (toRead is EnemyLayoutFile enemyLayoutFile)
            {
                toAdd = new EnemyLayoutViewer(enemyLayoutFile);
            }
            else if (toRead is ItemTechParamFile itemTechParamFile)
            {
                toAdd = new ItemTechParamViewer(itemTechParamFile);
            }
            else if (toRead is ItemSkillParamFile itemSkillParamFile)
            {
                toAdd = new ItemSkillParamViewer(itemSkillParamFile);
            }
            else if (toRead is ItemBulletParamFile itemBulletParamFile)
            {
                toAdd = new ItemBulletParamViewer(itemBulletParamFile);
            }
            else if (toRead is RmagBulletParamFile rmagBulletParamFile)
            {
                toAdd = new RmagBulletViewer(rmagBulletParamFile);
            }
            else if (toRead is TextFile textFile)
            {
                toAdd = new TextViewer(textFile);
            }
            else if (toRead is ScriptFile scriptFile)
            {
                toAdd = new ScriptFileViewer(scriptFile);
            }
            else if (toRead is EnemyLevelParamFile enemyLevelParamFile)
            {
                toAdd = new EnemyStatEditor(enemyLevelParamFile);
            }
            else if (toRead is WeaponListFile weaponListFile)
            {
                toAdd = new WeaponListEditor(weaponListFile);
            }
            else if (toRead is PartsInfoFile partsInfoFile)
            {
                toAdd = new PartsInfoViewer(partsInfoFile);
            }
            else if (toRead is ItemPriceFile itemPriceFile)
            {
                toAdd = new ItemPriceViewer(itemPriceFile);
            }
            else if (toRead is EnemyDropFile enemyDropFile)
            {
                toAdd = new EnemyDropViewer(enemyDropFile);
            }
            else if (toRead is SetFile setFile)
            {
                toAdd = new SetFileViewer(setFile);
            }
            else if (toRead is ThinkDragonFile thinkDragonFile)
            {
                toAdd = new ThinkDragonViewer(thinkDragonFile);
            }
            else if (toRead is WeaponParamFile weaponParamFile)
            {
                toAdd = new WeaponParamViewer(weaponParamFile);
            }
            else if (toRead is ItemSuitParamFile itemSuitParamFile)
            {
                toAdd = new ClothingFileViewer(itemSuitParamFile);
            }
            else if (toRead is ItemUnitParamFile itemUnitParamFile)
            {
                toAdd = new UnitParamViewer(itemUnitParamFile);
            }
            else if (toRead is ItemCommonInfoFile itemCommonInfoFile)
            {
                toAdd = new ItemCommonInfoViewer(itemCommonInfoFile);
            }
            else if (toRead is QuestListFile questListFile)
            {
                toAdd = new QuestListViewer(questListFile);
            }
            else if (toRead is ObjectParticleInfoFile objectParticleInfoFile)
            {
                toAdd = new ObjectParticleInfoFileViewer(objectParticleInfoFile);
            }
            else if (toRead is ObjectParamFile objParamFile)
            {
                toAdd = new ObjParamViewer(objParamFile);
            }
            else if (toRead is EnemyParamFile enemyParamFile)
            {
                toAdd = new EnemyParamFileViewer(enemyParamFile);
            }
            else if (toRead is AtkDatFile atkDatFile)
            {
                toAdd = new AtkDatFileViewer(atkDatFile);
            }
            else if (toRead is DamageDataFile damageDataFile)
            {
                toAdd = new DamageDataFileViewer(damageDataFile);
            }
            else if (toRead is EnemyMotTblFile enemyMotTblFile)
            {
                toAdd = new EnemyMotTblFileViewer(enemyMotTblFile);
            }
            else if (toRead is LndCommonFile lndCommonFile)
            {
                toAdd = new LndCommonEditor(lndCommonFile);
            }
            else if (toRead is UnpointeredFile unpointeredFile)
            {
                // ADX interception — if this UnpointeredFile is an archive-embedded
                // .adx, show the AdxPreviewPanel (audio preview) instead of the
                // raw/hex viewer. Standalone .adx on disk is handled earlier in
                // treeView1_AfterSelect via LoadAdxIntoRightPanel; this branch
                // covers the case where an ADX lives inside a real container.
                bool isAdx = unpointeredFile.filename?.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) == true;

                // Sound DAT interception — if this UnpointeredFile is a .dat that
                // passes the xobxDDNS / xobxKPTD signature check, show the audio
                // preview panel instead of the raw/hex viewer.
                bool isDat = unpointeredFile.filename?.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) == true;

                if (isAdx && unpointeredFile.theData != null)
                {
                    // Mirror the filename-hash lookup from LoadAdxIntoRightPanel so
                    // the info panel shows the mapped sound title when available.
                    string hashKey = Path.GetFileNameWithoutExtension(unpointeredFile.filename ?? "").TrimStart('-');
                    string mappedTitle = null;
                    if (hashKey.Length == 32
                        && hashKey.All(c => "0123456789abcdefABCDEF".Contains(c)))
                    {
                        AdxHashMap.TryGetValue(hashKey.ToLowerInvariant(), out mappedTitle);
                    }

                    string infoText =
                        "ADX audio file detected.\n\n" +
                        "If you wish to replace this file, convert a .wav to .adx.\n" +
                        "Replace one of the .adx files in the container with a valid .adx file\n" +
                        "and save your hashed file.\n\n" +
                        $"File name: {unpointeredFile.filename}";

                    if (mappedTitle != null)
                    {
                        infoText += $"\n\nADX Mapping: {mappedTitle}";
                    }

                    toAdd = new AdxPreviewPanel(unpointeredFile.theData, infoText, mappedTitle ?? unpointeredFile.filename);
                }
                else if (isDat
                    && unpointeredFile.theData != null
                    && DatConverter.IsSoundDat(unpointeredFile.theData))
                {
                    string infoText =
                        "DAT sound file detected (xobxDDNS / xobxKPTD).\n\n" +
                        "This is a raw PCM sound container used by PSU.\n" +
                        "You can preview playback below, or use Extract Selected\n" +
                        "to save it as either the raw .dat or a converted .wav.\n\n" +
                        $"File name: {unpointeredFile.filename}";

                    toAdd = new DatPreviewPanel(unpointeredFile.theData, infoText);
                }
                else
                {
                    toAdd = new UnpointeredFileViewer(unpointeredFile);
                }
            }
            splitContainer1.Panel2.Controls.Add(toAdd);
            toAdd.Dock = DockStyle.Fill;
        }

        private void exportBlob_Click(object sender, EventArgs e)
        {
            if (loadedContainer is NblLoader)
            {
                CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
                goodOpenFileDialog.IsFolderPicker = true;
                if (goodOpenFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    ((NblLoader)loadedContainer).exportDataBlob(goodOpenFileDialog.FileName);
                }
            }

        }

        private async void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loadedContainer == null)
            {
                TreeNode node = treeView1.SelectedNode;
                if (node != null &&
                    node.Tag is FileTreeNodeTag tag &&
                    tag.OwnerContainer == null &&
                    tag.FileName != null &&
                    tag.FileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tag.FullPath))
                {
                    byte[] bytesToSave = pendingAdxReplacementBytes;
                    if (bytesToSave == null)
                    {
                        try { bytesToSave = File.ReadAllBytes(tag.FullPath); }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Could not read the original file:\n{ex.Message}",
                                "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                    string hashName = Path.GetFileNameWithoutExtension(tag.FullPath);
                    saveFileDialog1.FileName = hashName;
                    saveFileDialog1.Filter = "All files (*.*)|*.*";
                    if (saveFileDialog1.ShowDialog() != DialogResult.OK) return;
                    string destPath = saveFileDialog1.FileName;
                    if (destPath.EndsWith(".adx", StringComparison.OrdinalIgnoreCase))
                        destPath = destPath.Substring(0, destPath.Length - 4);
                    try { File.WriteAllBytes(destPath, bytesToSave); }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not save file:\n{ex.Message}",
                            "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    MessageBox.Show(
                        $"Saved to:\n{destPath}\n\n" +
                        $"(The .adx extension was stripped so the filename matches the hash.)",
                        "Save Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    pendingAdxReplacementBytes = null;
                    return;
                }
            }

            if (loadedContainer != null)
            {
                saveFileDialog1.FileName = fileDialog.FileName;
                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    string destPath = saveFileDialog1.FileName;
                    var containerToSave = loadedContainer;

                    // Lock down anything that could mutate the model mid-serialize.
                    // The form itself stays enabled — user can drag the window,
                    // scroll the tree, click around to view things.
                    arbitraryFileContextMenuStrip.Enabled = false;
                    nblChunkContextMenuStrip.Enabled = false;
                    splitContainer1.Panel2.Enabled = false;

                    // Indeterminate progress bar — PRS compression doesn't report progress,
                    // so we use the bouncing/marquee style to show "still working".
                    var prevStyle = actionProgressBar.Style;
                    actionProgressBar.Style = ProgressBarStyle.Marquee;
                    actionProgressBar.MarqueeAnimationSpeed = 30;
                    string prevStatus = progressStatusLabel.Text;
                    progressStatusLabel.Text = "Saving... (compressing archive, please wait)";

                    try
                    {
                        byte[] savedContainer = await Task.Run(() => containerToSave.ToRaw());
                        await Task.Run(() => File.WriteAllBytes(destPath, savedContainer));

                        this.Text = "PSU Archive Explorer " + Path.GetFileName(destPath);
                        fileDialog.FileName = destPath;
                        progressStatusLabel.Text = $"Saved {Path.GetFileName(destPath)} ({savedContainer.Length:N0} bytes)";

                        // Clear the message after 4 seconds, but only if it hasn't been replaced by something else
                        string savedText = progressStatusLabel.Text;
                        _ = Task.Delay(4000).ContinueWith(_ =>
                        {
                            if (progressStatusLabel.Text == savedText)
                                progressStatusLabel.Text = "Progress:";
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    }
                    catch (ScriptValidationException exc)
                    {
                        string joinedErrors = String.Join("\r\n", exc.ScriptValidationErrors.Select(error =>
                            error.LineNumber != -1
                                ? $"{error.FunctionName}, line {error.LineNumber}: {error.Description}"
                                : $"{error.FunctionName}: {error.Description}"));
                        MessageBox.Show($"Could not save archive. \r\nFile \"{exc.FileName}\" failed to validate for the following reasons: \r\n{joinedErrors}",
                            "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        progressStatusLabel.Text = prevStatus;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Save failed:\n{ex.Message}",
                            "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        progressStatusLabel.Text = prevStatus;
                    }
                    finally
                    {
                        actionProgressBar.Style = prevStyle;
                        actionProgressBar.MarqueeAnimationSpeed = 0;
                        actionProgressBar.Value = 0;
                        arbitraryFileContextMenuStrip.Enabled = true;
                        nblChunkContextMenuStrip.Enabled = true;
                        splitContainer1.Panel2.Enabled = true;
                    }
                }
            }
        }

        private void setQuest_Click(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (importDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).setQuest(importDialog.OpenFile());
                }
            }
        }

        private void setZone_Click_1(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (importDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).setZone((int)zoneUD.Value, importDialog.OpenFile());
                }
            }
        }

        private void addZone_Click_1(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (importDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).addZone((int)zoneUD.Value, importDialog.OpenFile());
                }
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            ClearRightPanel();

            if (!(e.Node.Tag is FileTreeNodeTag tag))
                return;

            // Standalone .adx on disk route to ADX preview
            if (tag.OwnerContainer == null && tag.FileName?.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) == true)
            {
                string pathToUse = tag.FullPath ?? fileDialog.FileName;
                if (File.Exists(pathToUse))
                    LoadAdxIntoRightPanel(pathToUse, e.Node);
                return;
            }

            // Standalone .dat on disk if it's a sound DAT, route to DAT preview.
            if (tag.OwnerContainer == null && tag.FileName?.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) == true)
            {
                string pathToUse = tag.FullPath ?? fileDialog.FileName;
                if (File.Exists(pathToUse) && DatConverter.IsSoundDat(pathToUse))
                {
                    LoadDatSoundIntoRightPanel(pathToUse, e.Node);
                    return;
                }
            }

            // Standalone .sfd on disk route to SFD preview.
            if (tag.OwnerContainer == null && tag.FileName?.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase) == true)
            {
                string pathToUse = tag.FullPath ?? fileDialog.FileName;
                if (File.Exists(pathToUse))
                {
                    long size = 0;
                    try { size = new FileInfo(pathToUse).Length; } catch { }
                    if (size > 0 && size <= MAX_SFD_PREVIEW_SIZE)
                    {
                        LoadSfdIntoRightPanelFromFile(pathToUse, e.Node);
                        return;
                    }
                    // Too big fall through to the large file warning panel below.
                }
            }

            if (tag.OwnerContainer != null)
            {
                ContainerFile parent = tag.OwnerContainer;
                int index = e.Node.Index;

                string fileName = tag.FileName ?? "Unknown";

                bool isNblFile = fileName.EndsWith(".nbl", StringComparison.OrdinalIgnoreCase);
                bool isSfdVideo = fileName.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase);
                bool isAdxFile = fileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                bool isDatFile = fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

                if (isDatFile)
                {
                    LoadArchiveDatAsync(parent, index, fileName);
                    return;
                }

                bool shouldSkipSizeCheck = isNblFile ||
                                           fileName.IndexOf("NMLL chunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           fileName.IndexOf("TMLL chunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           isAdxFile;

                if (shouldSkipSizeCheck)
                {
                    setRightPanel(parent.getFileParsed(index));
                    return;
                }

                const long MAX_SAFE_SIZE = 500 * 1024 * 1024; // 500 MB (generic preview cap)

                long fileSize = 0;
                string displayName = fileName;

                try
                {
                    RawFile raw = parent.getFileRaw(index);
                    if (raw != null)
                    {
                        displayName = raw.filename ?? fileName;

                        if (raw.fileContents != null && raw.fileContents.Length > 0)
                        {
                            fileSize = raw.fileContents.LongLength;
                        }
                        else
                        {
                            byte[] data = raw.WriteToBytes(false);
                            fileSize = data?.LongLength ?? 0;
                        }
                    }
                }
                catch
                {
                    fileSize = long.MaxValue;
                }

                // Archive-embedded .sfd route to the in-panel video preview
                // if it's under the SFD ceiling, otherwise drop to warning.
                if (isSfdVideo && fileSize <= MAX_SFD_PREVIEW_SIZE)
                {
                    LoadSfdIntoRightPanel(parent, index, displayName, e.Node);
                    return;
                }

                bool isLargeOrVideo = (fileSize > MAX_SAFE_SIZE) || isSfdVideo;

                if (isLargeOrVideo)
                {
                    try { currentRight = parent.getFileParsed(index); } catch (Exception ex) { Console.WriteLine("getFileParsed failed: " + ex); }

                    var warningPanel = new Panel
                    {
                        Dock = DockStyle.Fill,
                        BackColor = Color.FromArgb(229, 229, 229)
                    };

                    var lbl = new Label
                    {
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 10.5f),
                        Text = $"Preview unavailable due to file size.\n\n" +
                               "• Right click the file and Extract Selected to save it.\n" +
                               "• .sfd videos can be opened with VLC Media Player.\n\n" +
                               $"File name: {displayName}"
                    };

                    warningPanel.Controls.Add(lbl);
                    splitContainer1.Panel2.Controls.Add(warningPanel);
                    return;
                }

                setRightPanel(parent.getFileParsed(index));
            }
        }

        // ---------------------------------------------------------------------
        // SFD preview helpers
        // ---------------------------------------------------------------------

        // 2 GB ceiling for in-panel SFD preview. Set conservatively
        // demuxer produces two more in memory buffers (video ES + ADX)
        // on top of the source. If the app is x86 or lacks
        // <gcAllowVeryLargeObjects> in App.config, you'll OOM well before this.
        // The SfdPreviewPanel catches OutOfMemoryException and shows a status
        // message rather than crashing.
        private const long MAX_SFD_PREVIEW_SIZE = 2L * 1024L * 1024L * 1024L; // 2 GB

        /// <summary>
        /// Load an archive embedded .sfd into the right panel.
        /// </summary>
        private void LoadSfdIntoRightPanel(ContainerFile parent, int index, string displayName, TreeNode node)
        {
            try
            {
                RawFile raw = parent.getFileRaw(index);
                byte[] bytes = raw?.fileContents;
                if (bytes == null || bytes.Length == 0)
                {
                    bytes = raw?.WriteToBytes(false);
                }

                if (bytes == null || bytes.Length == 0)
                {
                    ShowSfdError("Could not read SFD data from archive.");
                    return;
                }

                var panel = new SfdPreviewPanel { Dock = DockStyle.Fill };
                splitContainer1.Panel2.Controls.Add(panel);
                panel.LoadSfd(bytes, displayName);
            }
            catch (OutOfMemoryException)
            {
                ShowSfdError("Out of memory — this SFD is too large to preview in the current build.");
            }
            catch (Exception ex)
            {
                ShowSfdError("Could not preview SFD: " + ex.Message);
            }
        }

        /// <summary>
        /// Load a standalone .sfd from disk into the right panel.
        /// </summary>
        private void LoadSfdIntoRightPanelFromFile(string path, TreeNode node)
        {
            try
            {
                var panel = new SfdPreviewPanel { Dock = DockStyle.Fill };
                splitContainer1.Panel2.Controls.Add(panel);
                panel.LoadSfdFromFile(path);
            }
            catch (OutOfMemoryException)
            {
                ShowSfdError("Out of memory — this SFD is too large to preview in the current build.");
            }
            catch (Exception ex)
            {
                ShowSfdError("Could not preview SFD: " + ex.Message);
            }
        }

        private void ShowSfdError(string message)
        {
            var warningPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(229, 229, 229)
            };
            var lbl = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10.5f),
                Text = message + "\n\nYou can right-click the file and choose Extract Selected " +
                       "to save it, then open it with VLC Media Player."
            };
            warningPanel.Controls.Add(lbl);
            splitContainer1.Panel2.Controls.Add(warningPanel);
        }

        private void addFile_Click(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).addFile(fileDialog.SafeFileName, fileDialog.OpenFile());
                }
            }
        }

        private void createAFSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK && saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                string folderToOpen = folderBrowserDialog1.SelectedPath;
                string fileToSave = saveFileDialog1.FileName;
                AfsLoader.createFromDirectory(folderToOpen, fileToSave);
            }
        }

        private void replaceFileTreeContextItem_Click(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            if (node == null || !(node.Tag is FileTreeNodeTag tag))
                return;

            if (tag.OwnerContainer == null &&
                tag.FileName != null &&
                tag.FileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(tag.FullPath) &&
                File.Exists(tag.FullPath))
            {
                OpenFileDialog adxReplaceDialog = new OpenFileDialog
                {
                    Filter = "ADX audio files (*.adx)|*.adx|All files (*.*)|*.*",
                    Title = "Select an ADX file to replace the hashed file with"
                };

                if (adxReplaceDialog.ShowDialog() != DialogResult.OK)
                    return;

                string sourcePath = adxReplaceDialog.FileName;

                if (!IsValidAdxFile(sourcePath))
                {
                    MessageBox.Show(
                        "The selected file is not a valid ADX audio file.\n\n" +
                        "Please pick a file with a proper ADX header.",
                        "Invalid ADX File",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    pendingAdxReplacementBytes = File.ReadAllBytes(sourcePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not read the selected ADX file:\n{ex.Message}",
                        "Read Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string filename = Path.GetFileName(tag.FullPath);
                byte[] header = new byte[4];
                if (pendingAdxReplacementBytes.Length >= 4)
                {
                    Array.Copy(pendingAdxReplacementBytes, 0, header, 0, 4);
                }
                currentRight = new UnpointeredFile(filename, pendingAdxReplacementBytes, header);

                MessageBox.Show(
                    $"'{Path.GetFileName(sourcePath)}' loaded!\n\n" +
                    $"Click File → Save As to save to hash.\n\n" +
                    $"(The .adx extension will be stripped and match the hash name.)",
                    "ADX Replacement",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ContainerFile owningFile = tag.OwnerContainer;
            OpenFileDialog replaceDialog = new OpenFileDialog();
            replaceDialog.FileName = tag.FileName;
            if (replaceDialog.ShowDialog() == DialogResult.OK)
            {
                RawFile file = new RawFile(replaceDialog.OpenFile(), Path.GetFileName(replaceDialog.FileName));
                if (owningFile is FilenameAwareContainerFile awareContainerFile)
                {
                    string filename = file.filename;
                    if (filename != tag.FileName && !awareContainerFile.ValidateFilename(filename))
                    {
                        FileRenameForm rename = new FileRenameForm(filename);
                        while (!awareContainerFile.ValidateFilename(filename))
                        {
                            if (rename.ShowDialog() == DialogResult.OK)
                            {
                                filename = rename.FileName;
                            }
                            else
                            {
                                return;
                            }
                        }
                    }
                    if (filename != file.filename)
                    {
                        file.filename = filename;
                    }
                }
                owningFile.replaceFile(node.Index, file);
                node.Text = file.filename;
                tag.FileName = file.filename;
                PsuFile parsedFile = owningFile.getFileParsed(node.Index);
                if (parsedFile is ContainerFile)
                {
                    node.Nodes.Clear();
                    addChildFiles(node.Nodes, (ContainerFile)parsedFile);
                }
                var sel = treeView1.SelectedNode;
                treeView1.SelectedNode = null;
                treeView1.SelectedNode = sel;
            }
        }

        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            ((TreeView)sender).SelectedNode = e.Node;
        }

        private void disableScriptParsingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            disableScriptParsingToolStripMenuItem.Checked = !disableScriptParsingToolStripMenuItem.Checked;
            PsuFiles.parseScripts = !disableScriptParsingToolStripMenuItem.Checked;
        }

        private void exportAllWeaponsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loadedContainer is NblLoader && loadedContainer.getFilenames().Count > 0)
            {
                if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                {
                    //getFilenames is relatively expensive.
                    NblChunk nmllChunk = (NblChunk)loadedContainer.getFileParsed(0);
                    var nmllFilenames = nmllChunk.getFilenames();
                    foreach (string filename in nmllFilenames)
                    {
                        if (filename.Contains("itemWeaponParam") && nmllChunk.getFileParsed(filename) is WeaponParamFile weaponParamFile)
                        {
                            MemoryStream memStream = new MemoryStream();
                            weaponParamFile.saveTextFile(memStream);
                            File.WriteAllBytes(folderBrowserDialog1.SelectedPath + "\\" + filename + ".txt", memStream.ToArray());
                        }
                    }
                }
            }
        }

        private void importAllWeaponsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loadedContainer is NblLoader && loadedContainer.getFilenames().Count > 0)
            {
                if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                {
                    var files = Directory.GetFiles(folderBrowserDialog1.SelectedPath);
                    //getFilenames is relatively expensive.
                    var nmllFilenames = ((NblChunk)loadedContainer.getFileParsed(0)).getFilenames();
                    foreach (string filename in files)
                    {
                        if (filename.Contains("itemWeaponParam"))
                        {
                            //try replacing .txt with nothing (e.g itemWeaponParam_01DKSword.xnr.txt)
                            if (!tryImportWeaponTextFile((NblLoader)loadedContainer, nmllFilenames, filename, Path.GetFileName(filename).Replace(".txt", "")))
                            {
                                //try replacing .txt with .xnr (e.g itemWeaponParam_01DKSword.txt) -- parser doesn't do this, but other people may.
                                if (!tryImportWeaponTextFile((NblLoader)loadedContainer, nmllFilenames, filename, Path.GetFileName(filename).Replace(".txt", ".xnr")))
                                {

                                }
                            }
                        }
                    }
                }
            }
        }

        private bool tryImportWeaponTextFile(NblLoader nbl, List<string> nmllFilenames, string filepath, string attemptFilename)
        {
            if (nmllFilenames.Contains(attemptFilename) && (nbl.chunks[0].getFileParsed(attemptFilename) is WeaponParamFile))
            {
                WeaponParamFile paramFile = (WeaponParamFile)nbl.chunks[0].getFileParsed(attemptFilename);
                using (FileStream inStream = new FileStream(filepath, FileMode.Open))
                {
                    paramFile.loadTextFile(inStream);
                }
                return true;
            }
            return false;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void insertNMLLFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loadedContainer is NblLoader && treeView1.SelectedNode != null && treeView1.SelectedNode.Level == 1)
            {
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    using (Stream inStream = fileDialog.OpenFile())
                    {
                        ((ContainerFile)loadedContainer.getFileParsed(0)).addFile(treeView1.SelectedNode.Index, new RawFile(inStream, Path.GetFileName(fileDialog.FileName)));
                    }
                    treeView1.Nodes.Clear();
                    addChildFiles(treeView1.Nodes, loadedContainer);
                }
            }
        }

        private void decryptNMLBToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                byte[] fileContents = File.ReadAllBytes(fileDialog.FileName);

                MemoryStream fileStream = new MemoryStream(fileContents);
                fileStream.Seek(3, SeekOrigin.Begin);
                byte endian = (byte)fileStream.ReadByte();
                fileStream.Seek(0, SeekOrigin.Begin);
                BinaryReader fileLoader;
                bool bigEndian = false;
                if (endian == 0x42)
                {
                    fileLoader = new BigEndianBinaryReader(fileStream);
                    bigEndian = true;
                }
                else
                    fileLoader = new BinaryReader(fileStream);

                string formatName = new String(fileLoader.ReadChars(4));
                ushort fileVersion = fileLoader.ReadUInt16();
                ushort chunkFilenameLength = fileLoader.ReadUInt16();
                uint headerSize = fileLoader.ReadUInt32();
                uint nmllCount = fileLoader.ReadUInt32();
                uint uncompressedSize = fileLoader.ReadUInt32();
                uint compressedSize = fileLoader.ReadUInt32();
                uint pointerLength = fileLoader.ReadUInt32() / 4;
                uint blowfishKey = fileLoader.ReadUInt32();
                uint tmllHeaderSize = fileLoader.ReadUInt32();
                uint tmllDataSizeUncomp = fileLoader.ReadUInt32();
                uint tmllDataSizeComp = fileLoader.ReadUInt32();
                uint tmllCount = fileLoader.ReadUInt32();
                uint tmllHeaderLoc = 0;

                uint pointerLoc = 0;

                uint size = compressedSize == 0 ? uncompressedSize : compressedSize;

                uint nmllDataLoc = (uint)((headerSize + 0x7FF) & 0xFFFFF800);
                pointerLoc = (uint)(nmllDataLoc + size + 0x7FF) & 0xFFFFF800;
                if (tmllCount > 0)
                    tmllHeaderLoc = (pointerLoc + pointerLength * 4 + 0x7FF) & 0xFFFFF800;

                BlewFish fish = new BlewFish(blowfishKey, bigEndian);

                for (int i = 0; i < nmllCount; i++)
                {
                    int headerLoc = 0x40 + i * 0x60;
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);
                }

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < tmllCount; i++)
                {
                    uint headerLoc = (uint)(tmllHeaderLoc + 0x30 + i * 0x60);
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);

                    sb.Append(Encoding.ASCII.GetString(toDecrypt, 0, 0x20).Split('\0')[0] + "\t");
                }

                fileStream.Seek(nmllDataLoc, SeekOrigin.Begin);
                byte[] encryptedNmll = fileLoader.ReadBytes((int)size);
                byte[] decryptedNmll = fish.decryptBlock(encryptedNmll);
                byte[] decompressedNmll = compressedSize != 0 ? PrsCompDecomp.Decompress(decryptedNmll, uncompressedSize) : decryptedNmll;

                File.WriteAllText(fileDialog.FileName + ".tml.list", sb.ToString());
                File.WriteAllBytes(fileDialog.FileName + ".decrypt", fileContents);
                File.WriteAllBytes(fileDialog.FileName + ".encryptNmll", encryptedNmll);
                File.WriteAllBytes(fileDialog.FileName + ".decryptNmll", decryptedNmll);
                File.WriteAllBytes(fileDialog.FileName + ".decompressNmll", decompressedNmll);
            }
        }

        private void decryptNMLLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                byte[] fileContents = File.ReadAllBytes(fileDialog.FileName);

                MemoryStream fileStream = new MemoryStream(fileContents);
                BinaryReader fileLoader = new BinaryReader(fileStream);

                string formatName = new String(fileLoader.ReadChars(4));
                ushort fileVersion = fileLoader.ReadUInt16();
                ushort chunkFilenameLength = fileLoader.ReadUInt16();
                uint headerSize = fileLoader.ReadUInt32();
                uint nmllCount = fileLoader.ReadUInt32();
                uint uncompressedSize = fileLoader.ReadUInt32();
                uint compressedSize = fileLoader.ReadUInt32();
                uint pointerLength = fileLoader.ReadUInt32() / 4;
                uint blowfishKey = fileLoader.ReadUInt32();
                uint tmllHeaderSize = fileLoader.ReadUInt32();
                uint tmllDataSizeUncomp = fileLoader.ReadUInt32();
                uint tmllDataSizeComp = fileLoader.ReadUInt32();
                uint tmllCount = fileLoader.ReadUInt32();
                uint tmllHeaderLoc = 0;

                uint pointerLoc = 0;

                uint size = compressedSize == 0 ? uncompressedSize : compressedSize;

                uint nmllDataLoc = (uint)((headerSize + 0x7FF) & 0xFFFFF800);
                pointerLoc = (uint)(nmllDataLoc + size + 0x7FF) & 0xFFFFF800;
                if (tmllCount > 0)
                    tmllHeaderLoc = (pointerLoc + pointerLength * 4 + 0x7FF) & 0xFFFFF800;

                BlewFish fish = new BlewFish(blowfishKey);

                for (int i = 0; i < nmllCount; i++)
                {
                    int headerLoc = 0x40 + i * 0x60;
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);
                }
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < tmllCount; i++)
                {
                    uint headerLoc = (uint)(tmllHeaderLoc + 0x30 + i * 0x60);
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);

                    sb.Append(Encoding.ASCII.GetString(toDecrypt, 0, 0x20).Split('\0')[0] + "\t");
                    sb.Append(BitConverter.ToUInt16(fileContents, (int)(headerLoc + 0x4C)) + "\t");
                    sb.Append(BitConverter.ToUInt16(fileContents, (int)(headerLoc + 0x4E)) + "\n");
                }
                File.WriteAllText(fileDialog.FileName + ".tml.list", sb.ToString());
                File.WriteAllBytes(fileDialog.FileName + ".decrypt", fileContents);
            }
        }

        private void copyHashToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopySelectedSearchResultField(hit => hit.Archive);
        }

        private void copyFilenameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopySelectedSearchResultField(hit => hit.FileName);
        }

        private void copyPathToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopySelectedSearchResultField(hit => hit.InnerPath);
        }

        private void CopySelectedSearchResultField(Func<FileIndex.SearchResult, string> selector)
        {
            if (searchResults.SelectedItems.Count == 0) return;
            var hit = searchResults.SelectedItems[0].Tag as FileIndex.SearchResult;
            if (hit == null) return;

            string value = selector(hit);
            if (string.IsNullOrEmpty(value)) return;

            try
            {
                Clipboard.SetText(value);
                searchStatusLabel.Text = $"Copied: {value}";
            }
            catch
            {
                // Clipboard occasionally fails not worth crashing over
            }
        }

        private class TextureEntry
        {
            public RawFile fileContents;
            public List<string> containingFiles = new List<string>();
        }

        private void textureCatalogueToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                Dictionary<string, Dictionary<string, TextureEntry>> textureEntries = new Dictionary<string, Dictionary<string, TextureEntry>>();
                using (MD5 md5 = MD5.Create())
                {
                    foreach (string file in Directory.EnumerateFiles(folderBrowserDialog1.SelectedPath))
                    {
                        using (Stream s = new FileStream(file, FileMode.Open))
                        {
                            byte[] identifier = new byte[4];
                            s.Read(identifier, 0, 4);
                            s.Seek(0, SeekOrigin.Begin);
                            if (identifier.SequenceEqual(new byte[] { 0x4E, 0x4D, 0x4C, 0x4C }))
                            {
                                NblLoader nbl = new NblLoader(s);
                                if (nbl.chunks.Count > 1)
                                {
                                    //This means there's a TMLL...
                                    foreach (RawFile raw in nbl.chunks[1].fileContents)
                                    {
                                        byte[] fileMd5 = md5.ComputeHash(raw.fileContents);
                                        string md5String = BitConverter.ToString(fileMd5).Replace("-", "");
                                        if (!textureEntries.ContainsKey(raw.filename))
                                        {
                                            textureEntries[raw.filename] = new Dictionary<string, TextureEntry>();
                                        }
                                        if (!textureEntries[raw.filename].ContainsKey(md5String))
                                        {
                                            TextureEntry entry = new TextureEntry();
                                            entry.fileContents = raw;
                                            textureEntries[raw.filename][md5String] = entry;
                                        }
                                        if (!textureEntries[raw.filename][md5String].containingFiles.Contains(Path.GetFileName(file)))
                                        {
                                            textureEntries[raw.filename][md5String].containingFiles.Add(Path.GetFileName(file));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                foreach (var ent in textureEntries)
                {
                    if (ent.Value.Values.Count > 1)
                    {
                        Console.Out.WriteLine("Texture: " + ent.Key);
                        foreach (var val in ent.Value)
                        {
                            Directory.CreateDirectory(folderBrowserDialog1.SelectedPath + "\\categorized\\conflicted\\" + ent.Key + "\\" + val.Key);
                            using (Stream outStream = new FileStream(folderBrowserDialog1.SelectedPath + "\\categorized\\conflicted\\" + ent.Key + "\\" + val.Key + "\\" + val.Value.fileContents.filename, FileMode.Create))
                            {
                                val.Value.fileContents.WriteToStream(outStream);
                            }
                            XvrTextureFile xvr = new XvrTextureFile(val.Value.fileContents.subHeader, val.Value.fileContents.fileContents, val.Value.fileContents.filename);
                            xvr.mipMaps[0].Save(folderBrowserDialog1.SelectedPath + "\\categorized\\conflicted\\" + ent.Key + "\\" + val.Key + "\\" + val.Value.fileContents.filename.Replace(".xvr", ".png"));
                            Console.Out.WriteLine("\t" + val.Key + ": " + string.Join(", ", val.Value.containingFiles));
                        }
                        Console.Out.WriteLine();
                    }
                    else
                    {

                        string hash = ent.Value.Keys.First();
                        RawFile raw = ent.Value[hash].fileContents;
                        Directory.CreateDirectory(folderBrowserDialog1.SelectedPath + "\\categorized\\" + ent.Key);
                        using (Stream outStream = new FileStream(folderBrowserDialog1.SelectedPath + "\\categorized\\" + ent.Key + "\\" + raw.filename, FileMode.Create))
                        {
                            raw.WriteToStream(outStream);
                        }
                        XvrTextureFile xvr = new XvrTextureFile(raw.subHeader, raw.fileContents, raw.filename);
                        xvr.mipMaps[0].Save(folderBrowserDialog1.SelectedPath + "\\categorized\\" + ent.Key + "\\" + raw.filename.Replace(".xvr", ".png"));
                    }
                }
            }
        }

        private void exportSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            exportSelected();
        }

        private void extractFileTreeContextItem_Click(object sender, EventArgs e)
        {
            exportSelected();
        }

        private void exportAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //It is a sin to use the standard folder dialog
            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;

            if (goodOpenFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                exportAll(treeView1.Nodes, goodOpenFileDialog.FileName);
            }
        }

        private void exportAll(TreeNodeCollection treeNodes, string folderName)
        {
            Directory.CreateDirectory(folderName);
            foreach (TreeNode node in treeNodes)
            {
                exportNode(node, folderName);
            }
        }

        private void exportSelected()
        {
            SaveFileDialog exportFileDialog = new SaveFileDialog();

            if (currentRight != null)
            {
                string suggestedName = currentRight.filename;

                if (treeView1.SelectedNode?.Tag is FileTreeNodeTag tag && !string.IsNullOrEmpty(tag.FileName))
                {
                    suggestedName = tag.FileName;
                }

                exportFileDialog.FileName = suggestedName;

                // Special handling for ADX files (works for both standalone hashed ADX and normal .adx)
                bool isAdxFile = suggestedName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                // Special handling for DAT sound files (xobxDDNS / xobxKPTD)
                bool isDatFile = suggestedName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

                if (currentRight is ITextureFile)
                {
                    exportFileDialog.FileName = exportFileDialog.FileName.Replace(".xvr", ".png");
                    exportFileDialog.Filter = "Portable Network Graphics (*.png)|*.png|Xbox PowerVR Texture (*.xvr)|*.xvr";
                }
                else if (currentRight is TextFile)
                {
                    exportFileDialog.FileName = exportFileDialog.FileName.Replace(".bin", ".txt");
                    exportFileDialog.Filter = "Text (*.txt)|*.txt|Binary File (*.bin)|*.bin";
                }
                else if (isAdxFile)
                {
                    if (batchWavExport)
                    {
                        // Default to .wav when the ADX to WAV setting is on. User can still
                        // switch back to .adx via the filter dropdown.
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".wav");
                        exportFileDialog.Filter = "WAV Audio (*.wav)|*.wav|ADX Audio (*.adx)|*.adx|All Files (*.*)|*.*";
                    }
                    else
                    {
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".adx");
                        exportFileDialog.Filter = "ADX Audio (*.adx)|*.adx|All Files (*.*)|*.*";
                    }
                }
                else if (isDatFile)
                {
                    // Only suggest .wav if the in memory bytes look like a real sound DAT.
                    // Non sound .dat files fall through to raw extraction.
                    bool datIsSound = currentRight is UnpointeredFile unpointedCheck
                                      && DatConverter.IsSoundDat(unpointedCheck.theData);

                    if (batchDat2WavExport && datIsSound)
                    {
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".wav");
                        exportFileDialog.Filter = "WAV Audio (*.wav)|*.wav|DAT File (*.dat)|*.dat|All Files (*.*)|*.*";
                    }
                    else
                    {
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".dat");
                        exportFileDialog.Filter = "DAT File (*.dat)|*.dat|All Files (*.*)|*.*";
                    }
                }

                if (exportFileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (currentRight is ITextureFile &&
                        Path.GetExtension(exportFileDialog.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        ((ITextureFile)currentRight).mipMaps[0].Save(exportFileDialog.FileName);
                    }
                    else if (currentRight is TextFile &&
                             Path.GetExtension(exportFileDialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ((TextFile)currentRight).saveToTextFile(exportFileDialog.OpenFile());
                    }
                    else if (isAdxFile)
                    {
                        string chosenExt = Path.GetExtension(exportFileDialog.FileName);
                        bool saveAsWav = chosenExt.Equals(".wav", StringComparison.OrdinalIgnoreCase);

                        if (saveAsWav && currentRight is UnpointeredFile unpointed)
                        {
                            try
                            {
                                // theData holds the raw ADX bytes exactly as loaded no
                                // metadata wrapper, which is what AdxDecoder expects.
                                byte[] adxBytes = unpointed.theData;
                                byte[] wavBytes = AdxDecoder.DecodeToWav(adxBytes);
                                File.WriteAllBytes(exportFileDialog.FileName, wavBytes);
                            }
                            catch (Exception ex)
                            {
                                // AdxDecoder rejects any non-PSU-standard ADX variant.
                                // Offer the user a raw .adx fallback rather than silently failing.
                                DialogResult fallback = MessageBox.Show(
                                    $"ADX → WAV conversion failed:\n\n{ex.Message}\n\n" +
                                    "Would you like to save the raw .adx file instead?",
                                    "ADX Decode Failed",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning);

                                if (fallback == DialogResult.Yes)
                                {
                                    string adxPath = Path.ChangeExtension(exportFileDialog.FileName, ".adx");
                                    bool originalExportMetaData = exportMetaData;
                                    exportMetaData = false;
                                    extractFile(currentRight, adxPath);
                                    exportMetaData = originalExportMetaData;
                                }
                            }
                        }
                        else
                        {
                            // User picked .adx or any non .wav) from the filter dropdown,
                            // or currentRight isn't an UnpointeredFile for some reason
                            // write raw via the existing extract path.
                            bool originalExportMetaData = exportMetaData;
                            exportMetaData = false;
                            extractFile(currentRight, exportFileDialog.FileName);
                            exportMetaData = originalExportMetaData;
                        }
                    }
                    else if (isDatFile)
                    {
                        string chosenExt = Path.GetExtension(exportFileDialog.FileName);
                        bool saveAsWav = chosenExt.Equals(".wav", StringComparison.OrdinalIgnoreCase);

                        if (saveAsWav && currentRight is UnpointeredFile unpointedDat)
                        {
                            try
                            {
                                byte[] wavBytes = DatConverter.DecodeToWav(unpointedDat.theData);
                                File.WriteAllBytes(exportFileDialog.FileName, wavBytes);
                            }
                            catch (Exception ex)
                            {
                                // Either a non-sound .dat or a corrupt sound .dat offer
                                // the user a raw .dat fallback rather than silently failing.
                                DialogResult fallback = MessageBox.Show(
                                    $"DAT → WAV conversion failed:\n\n{ex.Message}\n\n" +
                                    "Would you like to save the raw .dat file instead?",
                                    "DAT Decode Failed",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning);

                                if (fallback == DialogResult.Yes)
                                {
                                    string datPath = Path.ChangeExtension(exportFileDialog.FileName, ".dat");
                                    bool originalExportMetaData = exportMetaData;
                                    exportMetaData = false;
                                    extractFile(currentRight, datPath);
                                    exportMetaData = originalExportMetaData;
                                }
                            }
                        }
                        else
                        {
                            // If user picked .dat or any non .wav from the filter dropdown
                            // write raw via the existing extract path.
                            bool originalExportMetaData = exportMetaData;
                            exportMetaData = false;
                            extractFile(currentRight, exportFileDialog.FileName);
                            exportMetaData = originalExportMetaData;
                        }
                    }
                    else
                    {
                        extractFile(currentRight, exportFileDialog.FileName);
                    }
                }
            }
        }

        private void exportNode(TreeNode node, string fileDirectory)
        {
            if (!(node.Tag is FileTreeNodeTag tag))
                return;

            string originalFilename = tag.FileName;

            // ---- Standalone file case (fake container: OwnerContainer is null,
            // bytes live on disk at tag.FullPath). Used for single-file opens like
            // a raw .adx. There's nothing to recurse into, so we handle it here
            // and return.
            if (tag.OwnerContainer == null)
            {
                if (string.IsNullOrEmpty(tag.FullPath) || !File.Exists(tag.FullPath))
                    return;

                bool isAdx = originalFilename != null &&
                             originalFilename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                bool isDat = originalFilename != null &&
                             originalFilename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

                if (isAdx && batchWavExport)
                {
                    string wavName = Path.ChangeExtension(originalFilename, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] adxBytes = File.ReadAllBytes(tag.FullPath);
                        byte[] wavBytes = AdxDecoder.DecodeToWav(adxBytes);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"ADX->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .adx instead.");
                        string adxPath = Path.Combine(fileDirectory, originalFilename);
                        File.Copy(tag.FullPath, adxPath, overwrite: true);
                    }
                }
                else if (isDat && batchDat2WavExport && DatConverter.IsSoundDat(tag.FullPath))
                {
                    string wavName = Path.ChangeExtension(originalFilename, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] datBytes = File.ReadAllBytes(tag.FullPath);
                        byte[] wavBytes = DatConverter.DecodeToWav(datBytes);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"DAT->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .dat instead.");
                        string datPath = Path.Combine(fileDirectory, originalFilename);
                        File.Copy(tag.FullPath, datPath, overwrite: true);
                    }
                }
                else
                {
                    // Not an audio file (or conversion setting off, or non-sound .dat)
                    // just copy the file through.
                    string destPath = Path.Combine(fileDirectory, originalFilename);
                    try { File.Copy(tag.FullPath, destPath, overwrite: true); }
                    catch (Exception ex) { Console.WriteLine($"Copy failed for {originalFilename}: {ex.Message}"); }
                }

                return;
            }

            // ---- Normal container case ----
            ContainerFile parent = tag.OwnerContainer;
            int fileIndex = node.Index;
            List<string> parentFilenames = parent.getFilenames();
            PsuFile file = parent.getFileParsed(fileIndex);

            //NBLs only have "NML(B/L)" or "TML(B/L)" chunks as children.
            if (!(parent is NblLoader))
            {
                if (file is ITextureFile && batchPngExport)
                {
                    string filename = Path.Combine(fileDirectory, Path.GetFileName(originalFilename + ".png"));
                    ((ITextureFile)file).mipMaps[0].Save(filename);
                }
                else if (batchWavExport
                         && originalFilename != null
                         && originalFilename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase)
                         && file is UnpointeredFile adxFile)
                {
                    string uniqueName = getUniqueFilename(originalFilename, fileIndex, parentFilenames);
                    string wavName = Path.ChangeExtension(uniqueName, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] wavBytes = AdxDecoder.DecodeToWav(adxFile.theData);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"ADX->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .adx instead.");
                        string adxPath = Path.Combine(fileDirectory, uniqueName);
                        extractFile(file, adxPath);
                    }
                }
                else if (batchDat2WavExport
                         && originalFilename != null
                         && originalFilename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                         && file is UnpointeredFile datUnpointed
                         && DatConverter.IsSoundDat(datUnpointed.theData))
                {
                    string uniqueName = getUniqueFilename(originalFilename, fileIndex, parentFilenames);
                    string wavName = Path.ChangeExtension(uniqueName, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] wavBytes = DatConverter.DecodeToWav(datUnpointed.theData);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"DAT->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .dat instead.");
                        string datPath = Path.Combine(fileDirectory, uniqueName);
                        extractFile(file, datPath);
                    }
                }
                else
                {
                    if (batchExportSubArchiveFiles || !(file is ContainerFile))
                    {
                        string filename = Path.Combine(fileDirectory, getUniqueFilename(originalFilename, fileIndex, parentFilenames));
                        extractFile(file, filename);
                    }
                }
            }

            if (file is ContainerFile)
            {
                string newFolder = fileDirectory + @"\" + getUniqueFilename(originalFilename, fileIndex, parentFilenames) + "_ext";
                exportAll(node.Nodes, newFolder);
            }
            else
            {
                foreach (TreeNode nodeChild in node.Nodes)
                {
                    exportNode(nodeChild, fileDirectory);
                }
            }
        }

        private string getUniqueFilename(string originalFilename, int fileIndex, List<string> parentFilenames)
        {
            string usedFilename;
            if (parentFilenames.Count(filename => filename == originalFilename) > 1)
            {
                usedFilename = Path.GetFileName(originalFilename) + "_" + (fileIndex - parentFilenames.FindIndex(name => name == originalFilename)) + Path.GetExtension(originalFilename);
            }
            else
            {
                usedFilename = originalFilename;
            }
            return usedFilename;
        }

        private void extractFile(PsuFile psuFile, string filename)
        {
            RawFile file = psuFile.ToRawFile(0);
            byte[] bytes = file.WriteToBytes(exportMetaData);
            try
            {
                File.WriteAllBytes(filename, bytes);
            }
            catch
            {
                MessageBox.Show("Unable to extract " + filename + ". The file may be in use or otherwise inaccessible. Skipping.");
            }
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new MainSettings(this);
            }
            settings.Show();
            settings.BringToFront();
        }

        private void extractAllInFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;

            if (goodOpenFileDialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            string rootFolder = goodOpenFileDialog.FileName;

            string[] fileNames = batchRecursive
                ? Directory.GetFiles(rootFolder, "*.*", SearchOption.AllDirectories)
                : Directory.GetFiles(rootFolder);

            actionProgressBar.Value = 0;
            actionProgressBar.Maximum = fileNames.Length;
            progressStatusLabel.Text = $"Progress: 0/{fileNames.Length} Files. Please wait, this can take time.";

            menuStrip1.Enabled = false;

            var worker = new System.ComponentModel.BackgroundWorker
            {
                WorkerReportsProgress = true
            };

            worker.DoWork += (s, args) =>
            {
                var files = (string[])args.Argument;
                for (int i = 0; i < files.Length; i++)
                {
                    string fileName = files[i];
                    string newFolder = Path.GetDirectoryName(fileName);

                    try
                    {
                        extractPSUArchive(fileName, newFolder);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to extract {fileName}: {ex.Message}");
                    }

                    worker.ReportProgress(i + 1, fileName);
                }
            };

            worker.ProgressChanged += (s, args) =>
            {
                actionProgressBar.Value = args.ProgressPercentage;
                progressStatusLabel.Text =
                    $"Progress: {args.ProgressPercentage}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
            };

            worker.RunWorkerCompleted += (s, args) =>
            {
                actionProgressBar.Value = 0;
                progressStatusLabel.Text = args.Error != null
                    ? "Progress: Failed — " + args.Error.Message
                    : "Progress: Done!";
                menuStrip1.Enabled = true;
                worker.Dispose();
            };

            worker.RunWorkerAsync(fileNames);
        }

        private void viewInHexButton_Click(object sender, EventArgs e)
        {
            if (currentRight != null)
            {
                PointeredFile pointeredFile = null;
                byte[] file = currentRight.ToRaw();
                if (currentRight.calculatedPointers != null)
                {
                    if (currentRight is PointeredFile)
                    {
                        pointeredFile = (PointeredFile)currentRight;
                    }
                    else
                    {
                        //For now, Big Endian files don't really need to be considered here since they'd be a PointeredFile already. Possibly add in further support if added elsewhere later
                        pointeredFile = new PointeredFile(currentRight.filename, file, currentRight.header, currentRight.calculatedPointers, 0, false);
                    }
                    pointeredFile.ToRaw();
                }

                string headingText = $"Selected File: {currentRight.filename}";

                if (currentFileHexForm != null)
                {
                    currentFileHexForm.Close();
                }
                if (pointeredFile != null)
                {
                    currentFileHexForm = new HexEditForm(file, headingText, true,
                        pointeredFile);
                }
                else
                {
                    currentFileHexForm = new HexEditForm(file, headingText, true,
                        null);
                }

                currentFileHexForm.Show();
            }
        }

        private void compressChunkToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                ContainerFile parent = tag.OwnerContainer;
                ((NblChunk)parent.getFileParsed(treeView1.SelectedNode.Index)).Compressed = compressChunkToolStripMenuItem.Checked;
                if (compressChunkToolStripMenuItem.Checked)
                {
                    treeView1.SelectedNode.ForeColor = Color.Green;
                }
                else
                {
                    treeView1.SelectedNode.ForeColor = Color.Black;
                }
                if (node.Parent != null)
                {
                    if (parent.Compressed)
                    {
                        node.Parent.ForeColor = Color.Green;
                    }
                    else
                    {
                        node.Parent.ForeColor = Color.Black;
                    }
                }
            }
        }

        public void setNmllCompressOverride(NblLoader.CompressionOverride settings)
        {
            NblLoader.NmllCompressionOverride = settings;
        }

        public void setTmllCompressOverride(NblLoader.CompressionOverride settings)
        {
            NblLoader.TmllCompressionOverride = settings;
        }

        private void nblChunkContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            FileTreeNodeTag tag = treeView1.SelectedNode.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                ContainerFile parent = tag.OwnerContainer;
                compressChunkToolStripMenuItem.Checked = ((NblChunk)parent.getFileParsed(treeView1.SelectedNode.Index)).Compressed;
            }
        }

        private void listAllObjparamsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;

            if (goodOpenFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                string[] fileNames = Directory.GetFiles(goodOpenFileDialog.FileName);
                actionProgressBar.Maximum = fileNames.Length;
                progressStatusLabel.Text = $"Progress: {actionProgressBar.Value}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
                progressStatusLabel.Refresh();
                Dictionary<int, Tuple<string, ObjectParamFile.ObjectEntry>> objects = new Dictionary<int, Tuple<string, ObjectParamFile.ObjectEntry>>();

                foreach (string fileName in fileNames)
                {
                    Console.WriteLine(fileName);
                    string newFolder = Path.GetDirectoryName(fileName);
                    byte[] formatName = new byte[4];
                    try
                    {
                        using (Stream stream = File.Open(fileName, FileMode.Open))
                        {
                            stream.Read(formatName, 0, 4);

                            string identifier = Encoding.ASCII.GetString(formatName, 0, 4);
                            if (identifier == "NMLL")
                            {
                                NblLoader nbl = new NblLoader(stream);
                                if (((NblChunk)nbl.getFileParsed(0)).doesFileExist("obj_param.xnr"))
                                {
                                    ObjectParamFile paramFile = (ObjectParamFile)((NblChunk)nbl.getFileParsed(0)).getFileParsed("obj_param.xnr");
                                    foreach (int objectId in paramFile.ObjectDefinitions.Keys)
                                    {
                                        if (objects.ContainsKey(objectId) && !objects[objectId].Item2.group2Entry.Equals(paramFile.ObjectDefinitions[objectId].group2Entry))
                                        {
                                            Console.WriteLine("Mismatched object, ID = " + objectId + " compared to " + objects[objectId].Item1);
                                        }
                                        else
                                        {
                                            objects[objectId] = new Tuple<string, ObjectParamFile.ObjectEntry>(fileName, paramFile.ObjectDefinitions[objectId]);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine("Error reading file");
                    }

                    actionProgressBar.Value++;
                    progressStatusLabel.Text = $"Progress: {actionProgressBar.Value}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
                    progressStatusLabel.Refresh();
                }

                foreach (int i in objects.Keys.OrderBy(a => a))
                {
                    var hitbox = objects[i].Item2.group2Entry;
                    Console.WriteLine("Object " + i + ", first found in " + objects[i].Item1 + ": group 0 = " + hitbox.hitboxShape + "; {" + hitbox.unknownFloat2 + ", " + hitbox.unknownFloat3 + ", " + hitbox.unknownFloat3 + "}; id 1 = " + hitbox.unknownInt5 + "; isolated float = " + hitbox.unknownFloat6 + "; last value = " + hitbox.unknownInt9);
                }
            }
        }

        private void listAllMonsterLayoutsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;

            if (goodOpenFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                string outputFileName = Path.Combine(goodOpenFileDialog.FileName, "report2.txt");
                StreamWriter writer = new StreamWriter(outputFileName);
                string[] fileNames = Directory.GetFiles(goodOpenFileDialog.FileName);
                actionProgressBar.Maximum = fileNames.Length;
                progressStatusLabel.Text = $"Progress: {actionProgressBar.Value}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
                progressStatusLabel.Refresh();
                Dictionary<int, Tuple<string, ObjectParamFile.ObjectEntry>> objects = new Dictionary<int, Tuple<string, ObjectParamFile.ObjectEntry>>();

                foreach (string fileName in fileNames)
                {
                    string newFolder = Path.GetDirectoryName(fileName);
                    byte[] formatName = new byte[4];
                    try
                    {
                        using (Stream stream = File.Open(fileName, FileMode.Open))
                        {
                            stream.Read(formatName, 0, 4);

                            string identifier = Encoding.ASCII.GetString(formatName, 0, 3);
                            if (identifier == "AFS")
                            {
                                writer.WriteLine(fileName);
                                AfsLoader afs = new AfsLoader(stream);
                                foreach (var file in afs.afsList)
                                {
                                    if (file.fileName.StartsWith("zone") && file.fileName.EndsWith("_ae.nbl"))
                                    {
                                        NblLoader nbl = (NblLoader)file.fileContents;
                                        foreach (var nblFile in ((ContainerFile)nbl.getFileParsed(0)).getFilenames())
                                        {
                                            if (nblFile.StartsWith("enemy") && nblFile.EndsWith(".xnr"))
                                            {
                                                EnemyLayoutFile layoutFile = (EnemyLayoutFile)((ContainerFile)nbl.getFileParsed(0)).getFileParsed(nblFile);
                                                writer.WriteLine("\t" + nblFile + ":");
                                                for (int i = 0; i < layoutFile.spawns.Length; i++)
                                                {
                                                    writer.WriteLine($"\t\tSpawn {i}:");
                                                    writer.WriteLine($"\t\tMonsters:");
                                                    for (int j = 0; j < layoutFile.spawns[i].monsters.Length; j++)
                                                    {
                                                        writer.WriteLine($"\t\t\tGroup {j}:");
                                                        for (int k = 0; k < layoutFile.spawns[i].monsters[j].Length; k++)
                                                        {
                                                            writer.WriteLine("\t\t\t\t" + layoutFile.spawns[i].monsters[j][k].ToString());
                                                        }
                                                    }
                                                    writer.WriteLine($"\t\tArrangements:");
                                                    for (int j = 0; j < layoutFile.spawns[i].arrangements.Length; j++)
                                                    {
                                                        writer.WriteLine("\t\t\t" + layoutFile.spawns[i].arrangements[j].ToString());
                                                    }
                                                    writer.WriteLine($"\t\tSpawn Data:");
                                                    for (int j = 0; j < layoutFile.spawns[i].spawnData.Length; j++)
                                                    {
                                                        writer.WriteLine("\t\t\t" + layoutFile.spawns[i].spawnData[j].ToString());
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine("Error reading file");
                    }

                    actionProgressBar.Value++;
                    progressStatusLabel.Text = $"Progress: {actionProgressBar.Value}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
                    progressStatusLabel.Refresh();
                }
                /*
                foreach (int i in objects.Keys.OrderBy(a => a))
                {
                    var hitbox = objects[i].Item2.group2Entry;
                    Console.WriteLine("Object " + i + ", first found in " + objects[i].Item1 + ": group 0 = " + hitbox.hitboxShape + "; {" + hitbox.unknownFloat2 + ", " + hitbox.unknownFloat3 + ", " + hitbox.unknownFloat3 + "}; id 1 = " + hitbox.unknownInt5 + "; isolated float = " + hitbox.unknownFloat6 + "; last value = " + hitbox.unknownInt9);
                }*/
            }
        }

        //TODO: This should be in a different program.
        private string convertDamageResists(int rawResists)
        {
            StringBuilder sb = new StringBuilder(3);

            switch (rawResists & 0x3)
            {
                default: break;
                case 1: sb.Append("s"); break;
                case 2: case 3: sb.Append("S"); break;
            }
            switch (rawResists & 0xC)
            {
                default: break;
                case 4: sb.Append("r"); break;
                case 8: case 0xC: sb.Append("R"); ; break;
            }
            switch (rawResists & 0x30)
            {
                default: break;
                case 4: sb.Append("t"); break;
                case 8: case 0xC: sb.Append("T"); break;
            }
            return sb.ToString();
        }

        private void catalogueEnemyparamToolStripMenuItem_Click(object sender, EventArgs e)
        {

            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {

                Dictionary<string, EnemyParamFile> paramFileMap = new Dictionary<string, EnemyParamFile>();
                Dictionary<string, ActDataFile> actDataFileMap = new Dictionary<string, ActDataFile>();
                Dictionary<string, DamageDataFile> damageDataFileMap = new Dictionary<string, DamageDataFile>();
                foreach (string file in Directory.EnumerateFiles(folderBrowserDialog1.SelectedPath))
                {
                    using (Stream s = new FileStream(file, FileMode.Open))
                    {
                        byte[] identifier = new byte[4];
                        s.Read(identifier, 0, 4);
                        s.Seek(0, SeekOrigin.Begin);
                        if (identifier.SequenceEqual(new byte[] { 0x4E, 0x4D, 0x4C, 0x4C }))
                        {
                            NblLoader nbl = new NblLoader(s);
                            if (nbl.chunks.Count > 0)
                            {
                                foreach (RawFile raw in nbl.chunks[0].fileContents)
                                {
                                    if (raw.filename.StartsWith("Param") && !raw.filename.Contains("ColtobaShare"))
                                    {
                                        paramFileMap[raw.filename] = (EnemyParamFile)nbl.chunks[0].getFileParsed(raw.filename);
                                    }
                                    else if (raw.filename.StartsWith("ActData") && !raw.filename.Contains("Quadruped_a"))
                                    {
                                        actDataFileMap[raw.filename] = (ActDataFile)nbl.chunks[0].getFileParsed(raw.filename);
                                    }
                                    else if (raw.filename.StartsWith("DamageData") && !raw.filename.Contains("Quadruped_a"))
                                    {
                                        damageDataFileMap[raw.filename] = (DamageDataFile)nbl.chunks[0].getFileParsed(raw.filename);
                                    }
                                }
                            }
                        }
                    }
                }

                /*
                foreach (var entry in paramFileMap.OrderBy(x => x.Key))
                {
                    EnemyParamFile file = entry.Value;
                    Console.Out.WriteLine(entry.Key);
                    
                    Console.Out.WriteLine("Base Stats:");
                    Console.Out.WriteLine("\tHpModifier: " + file.baseParams.HpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tAtpModifier: " + file.baseParams.AtpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tDfpModifier: " + file.baseParams.DfpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tAtaModifier: " + file.baseParams.AtaModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tEvpModifier: " + file.baseParams.EvpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tStaModifier: " + file.baseParams.StaModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tLckModifier: " + file.baseParams.LckModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tTpModifier: " + file.baseParams.TpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tMstModifier: " + file.baseParams.MstModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tElementModifier: " + file.baseParams.ElementModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tExpModifier: " + file.baseParams.ExpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tUnknownValue1: " + file.baseParams.UnknownValue1);
                    Console.Out.WriteLine("\tUnknownValue2: " + file.baseParams.UnknownValue2);
                    Console.Out.WriteLine("\tUnknownValue3: " + file.baseParams.UnknownValue3);
                    Console.Out.WriteLine("\tStatusResists: " + file.baseParams.StatusResists.ToString("X"));
                    Console.Out.WriteLine("\tDamageResists: " + convertDamageResists(file.baseParams.DamageResists));
                    Console.Out.WriteLine("\tUnknownModifier3: " + file.baseParams.UnknownModifier3.ToString("0.00##"));
                    Console.Out.WriteLine("\tUnknownModifier4: " + file.baseParams.UnknownModifier4.ToString("0.00##"));
                    Console.Out.WriteLine("\tUnknownValue4: " + file.baseParams.UnknownValue4);
                    Console.Out.WriteLine("\tUnknownValue5: " + file.baseParams.UnknownValue5);
                    Console.Out.WriteLine("\tUnknownModifier5: " + file.baseParams.UnknownModifier5.ToString("0.00##"));
                    Console.Out.WriteLine("\tUnknownModifier6: " + file.baseParams.UnknownModifier6.ToString("0.00##"));
                    Console.Out.WriteLine("\tUnknownModifier7: " + file.baseParams.UnknownModifier7.ToString("0.00##"));
                    string element = "UNKNOWN";
                    switch(file.baseParams.MonsterElement)
                    {
                        case 0: element = "Neutral"; break;
                        case 1: element = "Fire"; break;
                        case 2:
                            element = "Lightning"; break;
                        case 4:
                            element = "Light"; break;
                        case 9:
                            element = "Ice"; break;
                        case 10:
                            element = "Ground"; break;
                        case 12:
                            element = "Dark"; break;
                        default: break;
                    }
                    Console.Out.WriteLine("\tMonsterElement: " + element);
                    Console.Out.WriteLine();
                    Console.Out.WriteLine("Buffs:");
                    Console.Out.WriteLine("\t?\t??\t???\t????\t?????\tATP\tDFP\tATA\tEVP\tSTA\tLCK\tTP\tMST\tEXP\tSERes\tDmgRes");
                    //Console.Out.WriteLine("\t?\t??\t???\t????\t?????\tATP\tDFP\tATA\tEVP\tSTA\tLCK\tTP\tMST\tEXP\tUnused\tUnused\tUnused\tUnused\tSERes\tDmgRes");
                    foreach (var buff in file.buffParams)
                    {
                        Console.Out.Write("\t"); 
                        Console.Out.Write(buff.UnknownValue1 + "\t");
                        Console.Out.Write(buff.UnknownValue2 + "\t");
                        Console.Out.Write(buff.UnknownValue3 + "\t");
                        Console.Out.Write(buff.UnknownValue4 + "\t");
                        Console.Out.Write(buff.UnusedIntValue1 + "\t");
                        Console.Out.Write(buff.AtpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.DfpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.AtaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.EvpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.StaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.LckModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.TpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.MstModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.ExpModifier.ToString("0.00##") + "\t");
                        
                        //Console.Out.Write(buff.UnusedIntValue2 + "\t");
                        //Console.Out.Write(buff.UnusedModifier1 + "\t");
                        //Console.Out.Write(buff.UnusedModifier2 + "\t");
                        //Console.Out.Write(buff.UnusedModifier3 + "\t");
                        Console.Out.Write(buff.StatusResists.ToString("X") + "\t");
                        Console.Out.Write(convertDamageResists(buff.DamageResists));
                        //Console.Out.Write(buff.DamageResists.ToString("X"));
                        Console.Out.WriteLine();
                    }
                    Console.Out.WriteLine();
                    Console.Out.WriteLine("Attacks:");
                    //Console.Out.WriteLine("\tBone          \t?\t??\t???\t????\t?????\tOnhit\tSE(s)\tLevel\t??\t???\tHP\tATP\tDFP\tATA\tEVP\tSTA\tLCK\tTP\tMST\tELE%\tEXP\tUnused\tUnused\tUnused");
                    Console.Out.WriteLine("\tBone          \tX\tY\tZ\tWidth\tHeight\tOnhit\tSE(s)\tLevel\t??\t???\tHP\tATP\tDFP\tATA\tEVP\tSTA\tLCK\tTP\tMST\tELE%\tEXP");
                    foreach (var attack in file.attackParams)
                    {
                        Console.Out.Write("\t");
                        Console.Out.Write(attack.BoneName.PadRight(14) + "\t");
                        Console.Out.Write(attack.OffsetX.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.OffsetY.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.OffsetZ.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.BoundCylinderWidth.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.BoundCylinderHeight.ToString("0.00##") + "\t");

                        Console.Out.Write(attack.OnHitEffect.ToString("X4") + "\t");
                        Console.Out.Write(attack.StatusEffect.ToString("X4") + "\t");
                        Console.Out.Write(attack.UnknownSubgroup2Int3 + "\t");
                        Console.Out.Write(attack.UnknownSubgroup2Int4 + "\t");
                        Console.Out.Write(attack.UnknownSubgroup2Int5.ToString("X4") + "\t");

                        Console.Out.Write(attack.HpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.AtpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.DfpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.AtaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.EvpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.StaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.LckModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.TpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.MstModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.ElementModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.ExpModifier.ToString("0.00##"));
                        
                        //Console.Out.Write(attack.ExpModifier + "\t");
                        //Console.Out.Write(attack.UnusedModifier1 + "\t");
                        //Console.Out.Write(attack.UnusedModifier2 + "\t");
                        //Console.Out.Write(attack.UnusedModifier3);
                        Console.Out.WriteLine();
                    }
                    Console.Out.WriteLine();
                    Console.Out.WriteLine("Hitboxes:");
                    Console.Out.WriteLine("\tCanHit\tBone          \tX\tY\tZ\tWidth\tHeight\tHP\tATP\tDFP\tATA\tEVP\tSTA\tLCK\tTP\tMST\tELE%\tEXP\tUnused\tUnused\tUnused");
                    foreach (var hitbox in file.hitboxParams)
                    {
                        Console.Out.Write("\t");
                        Console.Out.Write(hitbox.Targetable + "\t");
                        Console.Out.Write((hitbox.BoneName != null ? hitbox.BoneName : "").PadRight(14) + "\t");
                        Console.Out.Write(hitbox.OffsetX.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.OffsetY.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.OffsetZ.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.BoundCylinderWidth.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.BoundCylinderHeight.ToString("0.00##") + "\t");

                        Console.Out.Write(hitbox.HpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.AtpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.DfpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.AtaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.EvpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.StaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.LckModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.TpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.MstModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.ElementModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.ExpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.UnusedModifier1.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.UnusedModifier2.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.UnusedModifier3.ToString("0.00##"));
                        Console.Out.WriteLine();
                    }
                    
                    Console.Out.WriteLine("\tGroup 2:");
                    foreach (var subentry1 in file.unknownSubEntry1List)
                    {
                        Console.Out.Write("\t");
                        Console.Out.Write("\t" + subentry1.UnknownInt1);
                        Console.Out.Write("\t" + subentry1.OffsetX.ToString("0.00##"));
                        Console.Out.Write("\t" + subentry1.OffsetY.ToString("0.00##"));
                        Console.Out.Write("\t" + subentry1.OffsetZ.ToString("0.00##"));
                        Console.Out.Write("\t" + subentry1.Scale1.ToString("0.00##"));
                        Console.Out.WriteLine("\t" + subentry1.Scale2.ToString("0.00##"));
                    }
                    Console.Out.WriteLine();
                    Console.Out.WriteLine("\tGroup 2:");
                    foreach (var subentry2 in file.unknownSubEntry2List)
                    {
                        Console.Out.Write("\t");
                        Console.Out.Write("\t" + subentry2.UnknownInt1);
                        Console.Out.Write("\t" + subentry2.UnknownInt2);
                        Console.Out.Write("\t" + subentry2.UnknownFloat1.ToString("0.00##"));
                        Console.Out.Write("\t" + subentry2.UnknownInt3);
                        Console.Out.Write("\t" + subentry2.UnknownInt4);
                        Console.Out.Write("\t" + subentry2.UnknownInt5);
                        Console.Out.Write("\t" + subentry2.UnknownInt6);
                        Console.Out.Write("\t" + subentry2.UnknownInt7);
                        Console.Out.WriteLine("\t" + subentry2.UnknownInt8);
                    }
                    Console.Out.WriteLine();
                    Console.Out.WriteLine();
                }
                */
                /*
                foreach(var entry in actDataFileMap)
                {
                    ActDataFile actDataFile = entry.Value;
                    Console.Out.WriteLine(entry.Key);
                    Console.Out.WriteLine("whatever");
                    for(int i = 0; i < actDataFile.Actions.Count; i++)
                    {
                        Console.Out.WriteLine("Action " + i);
                        
                        foreach (var action in actDataFile.Actions[i].ActionEntries)
                        {
                            Console.Out.Write("\t" + action.UnknownInt1);
                            Console.Out.Write("\t" + action.MotTblID);
                            Console.Out.Write("\t" + action.UnknownFloatAt3);
                            Console.Out.Write("\t" + action.VerticalExaggeration);
                            Console.Out.Write("\t" + action.MotionFloat1);
                            Console.Out.Write("\t" + action.MotionFloat2);
                            Console.Out.Write("\t" + action.HorizontalUnknown);
                            Console.Out.Write("\t" + action.UnknownFloatAt8);
                            Console.Out.Write("\t" + action.UnknownFloatAt9);
                            Console.Out.Write("\t" + action.UnknownAngleDegrees1);
                            Console.Out.Write("\t" + action.UnknownIntAt11);
                            Console.Out.Write("\t" + action.UnknownAngleDegrees2);
                            Console.Out.Write("\t" + action.UnknownAngleDegrees3);
                            Console.Out.Write("\t" + action.UnknownStateValue);
                            Console.Out.Write("\t" + action.UnknownStateModifier1);
                            Console.Out.Write("\t" + action.UnknownStateModifier2);
                            Console.Out.Write("\t" + action.AttackID);
                            Console.Out.Write("\t" + action.UnknownInt15);
                            Console.Out.Write("\t" + action.UnknownFloat6);
                            Console.Out.Write("\t" + action.UnknownInt16);
                            Console.Out.Write("\t" + action.UnknownFloat7);
                            Console.Out.Write("\t" + action.DamageDataList);
                            Console.Out.Write("\t" + action.UnknownInt18);
                            Console.Out.Write("\t" + action.UnknownInt19);
                            Console.Out.Write("\t" + action.UnknownInt20);
                            Console.Out.Write("\t" + action.UnknownFloatAt21);
                            Console.Out.Write("\t" + action.UnusedInt22);
                            Console.Out.Write("\t" + action.UnusedInt23);
                            Console.Out.Write("\t" + action.UnusedInt24);
                            Console.Out.Write("\t" + action.UnusedInt25);
                            Console.Out.WriteLine();
                        }
                        */
                /*
                for(int j = 0; j < actDataFile.Actions[i].ActionEntries.Count; j++)
                {
                    if (actDataFile.Actions[i].ActionEntries[j].SubEntryList1.Count > 0 || actDataFile.Actions[i].ActionEntries[j].SubEntryList2.Count > 0)
                    {
                        Console.Out.WriteLine("\tSubaction " + j);
                        if (actDataFile.Actions[i].ActionEntries[j].SubEntryList1.Count > 0)
                        {
                            Console.Out.WriteLine("\tSublist 1:");
                            for (int k = 0; k < actDataFile.Actions[i].ActionEntries[j].SubEntryList1.Count; k++)
                            {
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList1[k].UnknownInt1);
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList1[k].UnknownFloat.ToString("0.00##"));
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList1[k].UnknownInt2);
                                Console.Out.WriteLine();
                            }
                        }
                        if (actDataFile.Actions[i].ActionEntries[j].SubEntryList2.Count > 0)
                        {
                            Console.Out.WriteLine("\tSublist 2:");
                            for (int k = 0; k < actDataFile.Actions[i].ActionEntries[j].SubEntryList2.Count; k++)
                            {
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList2[k].UnknownInt1);
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList2[k].UnknownFloat.ToString("0.00##"));
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList2[k].UnknownInt2);
                                Console.Out.WriteLine();
                            }
                        }
                        Console.Out.WriteLine();
                    }
                }*/
                /*
                Console.Out.WriteLine();
            }
            Console.Out.WriteLine();
        }
        */
                foreach (var entry in damageDataFileMap)
                {
                    DamageDataFile damageDataFile = entry.Value;
                    Console.Out.WriteLine(entry.Key);
                    for (int i = 0; i < damageDataFile.DamageTypeEntries.Count; i++)
                    {
                        Console.Out.WriteLine("Damage lookup " + i);
                        for (int j = 0; j < damageDataFile.DamageTypeEntries[i].Count; j++)
                        {
                            Console.Out.WriteLine("\tDamage index " + j + ", Type: " + damageDataFile.DamageTypeEntries[i][j].DamageType + ", Angle count: " + damageDataFile.DamageTypeEntries[i][j].Angles.Count);
                            foreach (var angleEntry in damageDataFile.DamageTypeEntries[i][j].Angles)
                            {
                                Console.Out.Write("\t\t" + angleEntry.UnknownInt1);
                                Console.Out.Write("\t" + angleEntry.UnknownInt2);
                                Console.Out.Write("\tActions: " + string.Join(", ", angleEntry.ActionList));
                                Console.Out.WriteLine();
                            }
                            Console.Out.WriteLine();
                        }
                        Console.Out.WriteLine();
                    }
                    Console.Out.WriteLine();
                }
            }
        }

        AnimationNameHashDialog dialog;

        private void calculateAnimationNameHashToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dialog == null || dialog.IsDisposed)
            {
                dialog = new AnimationNameHashDialog();
            }
            if (currentRight != null && !(currentRight is NblChunk))
            {
                dialog.SetFileName(currentRight.filename);
            }
            if (!dialog.Visible)
            {
                dialog.Show();
            }
        }

        private void addFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                ContainerFile parent = tag.OwnerContainer;
                OpenFileDialog replaceDialog = new OpenFileDialog();

                if (replaceDialog.ShowDialog() == DialogResult.OK)
                {
                    NblChunk chunk = parent.getFileParsed(node.Index) as NblChunk;
                    if (chunk == null) return;

                    RawFile file = new RawFile(replaceDialog.OpenFile(), Path.GetFileName(replaceDialog.FileName));
                    string filename = file.filename;
                    if (!chunk.ValidateFilename(filename))
                    {
                        while (!chunk.ValidateFilename(filename))
                        {
                            using (FileRenameForm rename = new FileRenameForm(filename))
                            {
                                if (rename.ShowDialog() == DialogResult.OK)
                                {
                                    filename = rename.FileName;
                                }
                                else
                                {
                                    return;
                                }
                            }
                        }
                    }
                    if (filename != file.filename)
                    {
                        file.filename = filename;
                    }

                    chunk.addFile(file);

                    TreeNode newNode = new TreeNode(file.filename);
                    FileTreeNodeTag newTag = new FileTreeNodeTag();
                    newTag.OwnerContainer = chunk;
                    newTag.FileName = file.filename;
                    newNode.Tag = newTag;
                    newNode.ContextMenuStrip = arbitraryFileContextMenuStrip;
                    node.Nodes.Add(newNode);

                    if (file.fileheader == "NMLL" || file.fileheader == "NMLB")
                    {
                        addChildFiles(newNode.Nodes, (ContainerFile)chunk.getFileParsed(newNode.Index));
                    }
                    treeView1.SelectedNode = newNode;
                }
            }
        }

        private void deleteFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag == null) return;
            bool isAdxFile = tag.FileName?.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) == true;
            if (isAdxFile && !(tag.OwnerContainer is NblChunk))
            {
                MessageBox.Show(
                    "Cannot delete the ADX file.\n\n" +
                    "The game requires this data to be present. You can replace " +
                    "this with another ADX file, but it cannot be removed from " +
                    "the hashed container.",
                    "PSU Archive Explorer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            bool isSfdFile = tag.FileName?.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase) == true;
            if (isSfdFile && !(tag.OwnerContainer is NblChunk))
            {
                MessageBox.Show(
                    "Cannot delete the SFD file.\n\n" +
                    "The game requires this data to be present. You can replace " +
                    "this with another SFD file, but it cannot be removed from " +
                    "the hashed container.",
                    "PSU Archive Explorer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (tag.OwnerContainer is NblChunk chunk)
            {
                chunk.removeFile(node.Index);
                node.Remove();
            }
        }

        private void renameFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            if (node == null) return;
            node.BeginEdit();
        }

        private void treeView1_BeforeLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            var node = e.Node;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                e.CancelEdit = true;
            }
        }

        private void treeView1_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            var node = e.Node;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                e.CancelEdit = true;
            }
            else if (tag != null && e.Label != null)
            {
                if (!(tag.OwnerContainer is FilenameAwareContainerFile facf) || facf.ValidateFilename(e.Label))
                {
                    tag.OwnerContainer.renameFile(node.Index, e.Label);
                }
                else
                {
                    e.CancelEdit = true;
                }
            }
        }

        private void splitContainer1_Panel2_Paint(object sender, PaintEventArgs e)
        {

        }
        // ====================== File Index Search ======================

        private System.Windows.Forms.Timer searchDebounceTimer;
        private const string SearchPlaceholder = "Search files...";
        private bool searchBoxHasRealText = false;

        private void searchBox_Enter(object sender, EventArgs e)
        {
            if (!searchBoxHasRealText)
            {
                searchBox.Text = "";
                searchBox.ForeColor = System.Drawing.SystemColors.WindowText;
            }
        }

        private void searchBox_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(searchBox.Text))
            {
                searchBoxHasRealText = false;
                searchBox.ForeColor = System.Drawing.Color.Gray;
                searchBox.Text = SearchPlaceholder;
            }
        }

        private void searchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void searchBox_TextChanged(object sender, EventArgs e)
        {
            if (!searchBox.Focused && searchBox.Text == SearchPlaceholder) return;

            searchBoxHasRealText = !string.IsNullOrEmpty(searchBox.Text)
                                   && searchBox.Text != SearchPlaceholder;

            if (searchDebounceTimer == null)
            {
                searchDebounceTimer = new System.Windows.Forms.Timer();
                searchDebounceTimer.Interval = 250;
                searchDebounceTimer.Tick += (s, args) =>
                {
                    searchDebounceTimer.Stop();
                    RunSearch(searchBoxHasRealText ? searchBox.Text : "");
                };
            }

            searchDebounceTimer.Stop();
            searchDebounceTimer.Start();
        }

        private void RunSearch(string query)
        {
            if (!FileIndex.IsLoaded)
            {
                searchStatusLabel.Text = "PSU File Index was not detected. Please place psu_file_index.gz next to the .exe.";
                searchResults.Visible = false;
                treeView1.Visible = !welcomeVisible;
                return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                searchResults.Visible = false;
                treeView1.Visible = !welcomeVisible;
                searchStatusLabel.Text = "";
                return;
            }

            if (searchResults.Columns.Count == 0)
            {
                searchResults.Columns.Add("Filename", 140);
                searchResults.Columns.Add("Archive", 120);
            }

            var hits = FileIndex.Search(query, 2000);

            searchResults.BeginUpdate();
            searchResults.Items.Clear();
            foreach (var hit in hits)
            {
                var item = new ListViewItem(hit.FileName);
                item.SubItems.Add(hit.Archive);
                item.ToolTipText = hit.InnerPath;
                item.Tag = hit;
                searchResults.Items.Add(item);
            }
            searchResults.EndUpdate();

            treeView1.Visible = false;
            searchResults.Visible = true;
            searchResults.ShowItemToolTips = true;

            searchStatusLabel.Text = hits.Count >= 2500
                ? "Showing first 2500 matches"
                : $"{hits.Count} match{(hits.Count == 1 ? "" : "es")}";
        }

        private void searchResults_DoubleClick(object sender, EventArgs e)
        {
            if (searchResults.SelectedItems.Count == 0) return;
            var hit = searchResults.SelectedItems[0].Tag as FileIndex.SearchResult;
            if (hit == null) return;

            if (string.IsNullOrEmpty(gameDirectory))
            {
                var choice = MessageBox.Show(
                    "To open this file, PSU Archive Explorer needs to know where your " +
                    "game is installed (the folder containing online.exe and the 'data' folder).\n\n" +
                    "Would you like to select it now?",
                    "Game Directory Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (choice != DialogResult.Yes) return;

                if (!PromptForGameDirectory()) return;
            }

            string hashPath = Path.Combine(gameDirectory, "data", hit.Archive);

            if (!File.Exists(hashPath))
            {
                MessageBox.Show(
                    $"Couldn't find this hash in your game directory:\n{hashPath}\n\n" +
                    $"Filename: {hit.FileName}\n" +
                    $"Archive:  {hit.Archive}\n" +
                    $"Path:     {hit.InnerPath}",
                    "Not found in game directory",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            searchBoxHasRealText = false;
            searchBox.Text = SearchPlaceholder;
            searchBox.ForeColor = System.Drawing.Color.Gray;
            searchResults.Items.Clear();
            searchStatusLabel.Text = "";
            searchResults.Visible = false;
            treeView1.Visible = true;

            ClearRightPanel();
            pendingAdxReplacementBytes = null;
            bool success = openPSUArchive(hashPath, treeView1.Nodes);
            if (!success)
            {
                TryOpenAsAdx(hashPath);
                success = true;
            }

            if (success)
            {
                fileDialog.FileName = hashPath;
                Text = "PSU Archive Explorer " +
                       System.Reflection.Assembly.GetExecutingAssembly().GetName().Version +
                       " — " + hit.Archive;
            }
        }
        private bool PromptForGameDirectory()
        {
            using (var dlg = new CommonOpenFileDialog())
            {
                dlg.IsFolderPicker = true;
                dlg.Title = "Select your PSU game folder (contains online.exe and the 'data' folder)";

                if (dlg.ShowDialog() != CommonFileDialogResult.Ok) return false;

                string selected = dlg.FileName;
                string dataFolder = Path.Combine(selected, "data");

                if (!Directory.Exists(dataFolder))
                {
                    var result = MessageBox.Show(
                        $"No 'data' subfolder found in:\n{selected}\n\nUse this folder anyway?",
                        "Game Directory",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (result != DialogResult.Yes) return false;
                }

                gameDirectory = selected;
                return true;
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace HarmonyMerge
{
    public partial class Form1 : Form
    {
        private string harmonyPath;
        private string episodePath;
        private LogWindow _logWindow = new LogWindow();

        private static string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static string TargetDir = Path.Combine(AppData, @"Toon Boom Animation\Toon Boom Harmony Premium\2400-scripts");
        private static string StartupScriptPath = Path.Combine(TargetDir, "TB_sceneOpened.js");

        public Form1()
        {
            InitializeComponent();
            SetupUI();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // auto open log window
            //OpenLog();

        }

        private void SetupUI()
        {
            this.Text = "Harmony File Merger";
            this.Size = new System.Drawing.Size(1200, 900);

            buttBrowse.Click += async (s, e) => await LoadDataAsync();
            //renderListToolStripMenuItem.Click += (s, e) => ExportToXml();
            //importListToolStripMenuItem.Click += (s, e) => ImportFromXml();

            dataGridView.CellClick += DgvCompare_CellClick;
            //dataGridView.CellContentClick += DataGridView_CellContentClick;

            dataGridView.ColumnHeadersDefaultCellStyle.BackColor = dataGridView.BackgroundColor;
            dataGridView.ColumnHeadersDefaultCellStyle.SelectionBackColor = dataGridView.BackgroundColor;

            // auto fill Harmony default location
            if (string.IsNullOrEmpty(Properties.Settings.Default.HarmonyPath))
            {
                Properties.Settings.Default.HarmonyPath = @"C:\Program Files (x86)\Toon Boom Animation\Toon Boom Harmony 24.1 Premium\win64\bin\HarmonyPremium.exe";
                Properties.Settings.Default.Save();
            }

            harmonyPath = Properties.Settings.Default.HarmonyPath;

        }

        private void OpenLog()
        {
            if (_logWindow == null || _logWindow.IsDisposed)
                _logWindow = new LogWindow();

            _logWindow.StartPosition = FormStartPosition.Manual;
            _logWindow.Left = this.Left + this.Width;
            _logWindow.Top = this.Top;
            _logWindow.Show();
            _logWindow.BringToFront();
        }

        private async Task LoadDataAsync()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                string savedPath = Properties.Settings.Default.LastPath;
                if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
                    fbd.SelectedPath = savedPath;

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    episodePath = fbd.SelectedPath;
                    SaveLastPath(episodePath);

                    progressBar.Value = 0;
                    string folderName = System.IO.Path.GetFileName(episodePath);

                    using (var cts = new System.Threading.CancellationTokenSource())
                    {
                        // 2. Start the animation task on the UI thread without awaiting it yet
                        Task animationTask = AnimateLoadingTextAsync(folderName, cts.Token);

                        try
                        {
                            // 3. Run your heavy processing task
                            await Task.Run(() => ProcessDirectories(episodePath));
                        }
                        finally
                        {
                            // 4. Stop the animation as soon as the processing finishes or fails
                            cts.Cancel();
                            try { await animationTask; } catch (OperationCanceledException) { }
                        }
                    }


                    mergingTextOutput.Text = "PROCESSING COMPLETE";
                }
            }
        }

        private void ProcessDirectories(string rootPath)
        {
            var primaryFiles = ScanPrimaryFiles(rootPath);
            var animatorMap = ScanAnimators(rootPath);
            var psdFiles = ScanPsdFiles(rootPath);
            var folderColumns = animatorMap.Keys.ToList();

            this.Invoke(new Action(() => InitializeGridColumns(primaryFiles.Count, folderColumns)));

            for (int i = 0; i < primaryFiles.Count; i++)
            {
                var primary = primaryFiles[i];
                int step = i + 1;

                this.Invoke(new Action(() =>
                {
                    mergingTextOutput.Text = $"PROCESSING: {primary.Name}";
                    progressBar.Value = step;
                    mergingTextOutput.Refresh();
                    progressBar.Refresh();

                    AddFileRow(primary, folderColumns, animatorMap, psdFiles);
                }));
            }
            this.Invoke(new Action(() =>
            {
                mergingTextOutput.Text = "FINISHED LOADING.";
                progressBar.Value = 0;
            }));
        }

        private void SaveLastPath(string path)
        {
            Properties.Settings.Default.LastPath = path;
            Properties.Settings.Default.Save();
        }

        private List<dynamic> ScanPrimaryFiles(string rootPath)
        {
            return Directory.GetDirectories(rootPath)
                .SelectMany(d => Directory.GetFiles(d, "*.xstage", SearchOption.TopDirectoryOnly))
                .Where(f => !Path.GetFileName(f).Contains("_render"))
                .Select(f => (dynamic)new { Name = Path.GetFileNameWithoutExtension(f), FullPath = f })
                .ToList();
        }

        private Dictionary<string, List<string>> ScanAnimators(string rootPath)
        {
            var map = new Dictionary<string, List<string>>();
            string animPath = Path.Combine(rootPath, "ANIMATORS");
            if (Directory.Exists(animPath))
            {
                foreach (var dir in Directory.GetDirectories(animPath))
                    map.Add(Path.GetFileName(dir), Directory.GetFiles(dir, "*.xstage", SearchOption.AllDirectories).ToList());
            }
            return map;
        }

        private List<string> ScanPsdFiles(string rootPath)
        {
            DirectoryInfo di = new DirectoryInfo(rootPath);
            string bgPath = Path.Combine(di.Parent?.Parent?.FullName ?? "", "03BACKGROUND", di.Name);
            return Directory.Exists(bgPath) ? Directory.GetFiles(bgPath, "*.psd", SearchOption.TopDirectoryOnly).ToList() : new List<string>();
        }

        private void InitializeGridColumns(int totalFiles, List<string> animatorFolders)
        {
            dataGridView.Columns.Clear();
            dataGridView.Rows.Clear();
            progressBar.Maximum = Math.Max(1, totalFiles);

            dataGridView.Columns.Add("Primary", "Primary Index");
            foreach (var folder in animatorFolders) dataGridView.Columns.Add(folder, folder);

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "PSD", HeaderText = "PSDs", Width = 200 });
            dataGridView.Columns.Add(new DataGridViewButtonColumn { Name = "Merge", Text = "Merge", Width = 50, UseColumnTextForButtonValue = true });
            dataGridView.Columns.Add(new DataGridViewButtonColumn { Name = "OpenOne", Text = "Open", Width = 50, UseColumnTextForButtonValue = true });
            dataGridView.Columns.Add(new DataGridViewButtonColumn { Name = "OpenAll", Text = "Open All", Width = 60, UseColumnTextForButtonValue = true });
            //dataGridView.Columns.Add(new DataGridViewImageColumn { Name = "Merge", HeaderText = "Merge", Image = Properties.Resources.MERGE_FILES, ImageLayout = DataGridViewImageCellLayout.Zoom, Width = 50 });
            //dataGridView.Columns.Add(new DataGridViewImageColumn { Name = "OpenOne", HeaderText = "Open First", Image = Properties.Resources.OPEN_FIRST, ImageLayout = DataGridViewImageCellLayout.Zoom, Width = 50 });
            //dataGridView.Columns.Add(new DataGridViewImageColumn { Name = "OpenAll", HeaderText = "Open All", Image = Properties.Resources.OPEN_ALL, ImageLayout = DataGridViewImageCellLayout.Zoom, Width = 50 });
            dataGridView.Columns.Add(new DataGridViewImageColumn { Name = "Status", Image = Properties.Resources.STATUS_EMPTY, Width = 30 });
        }

        private void AddFileRow(dynamic primary, List<string> folders, Dictionary<string, List<string>> animMap, List<string> psds)
        {
            int rowIndex = dataGridView.Rows.Add();
            var row = dataGridView.Rows[rowIndex];
            row.Cells["Primary"].Value = primary.Name;
            var rowPaths = new List<string> { primary.FullPath };

            // Animator Matching
            foreach (var folder in folders)
            {
                var match = animMap[folder].FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(primary.Name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    row.Cells[folder].Value = primary.Name;
                    rowPaths.Add(match);

                    _logWindow.AppendLog($"FOUND TPL: {match}");
                    //Console.WriteLine("FOUND TPL: " + match);
                }
            }

            // PSD Matching (Highest version logic)
            string codeStr = ((string[])primary.Name.Split('-')).LastOrDefault();
            // Convert primary code to integer to ignore leading zero differences
            if (!string.IsNullOrEmpty(codeStr) && int.TryParse(codeStr, out int primaryCode))
            {
                var bestPsd = psds.Where(p =>
                {
                    string fn = Path.GetFileNameWithoutExtension(p);
                    string[] parts = fn.Split('_');

                    // Must have prefix, scene codes, and a version number (at least 3 parts)
                    if (parts.Length < 3) return false;

                    // Dynamically extract all parts between the prefix and the version
                    var sceneCodeStrings = parts.Skip(1).Take(parts.Length - 2);

                    // Convert PSD scene parts to integers and look for a numeric match
                    return sceneCodeStrings.Any(s => int.TryParse(s, out int psdCode) && psdCode == primaryCode);
                })
                .OrderByDescending(p =>
                {
                    string fn = Path.GetFileNameWithoutExtension(p);
                    string versionStr = fn.Split('_').Last();
                    int.TryParse(versionStr, out int version);
                    return version;
                })
                .FirstOrDefault();

                if (bestPsd != null)
                {
                    row.Cells["PSD"].Value = Path.GetFileNameWithoutExtension(bestPsd);
                    rowPaths.Add(bestPsd);
                    _logWindow.AppendLog($"FOUND PSD: {bestPsd}");
                }
            }

            row.Tag = rowPaths;
        }

        private void DataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            // Ignore header clicks (RowIndex will be -1)
            if (e.RowIndex < 0) return;

            string columnName = dataGridView.Columns[e.ColumnIndex].Name;

            switch (columnName)
            {
                case "Merge":
                    DgvCompare_CellClick(sender, e);
                    break;

                case "OpenOne":
                    DgvCompare_CellClick(sender, e);
                    break;

                case "OpenAll":
                    DgvCompare_CellClick(sender, e);
                    break;
            }
        }

        private async void DgvCompare_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            string colName = dataGridView.Columns[e.ColumnIndex].Name;

            // Check if either button was clicked
            if (colName == "Merge" || colName == "OpenAll" || colName == "OpenOne")
            {
                List<string> paths = null;
                var tag = dataGridView.Rows[e.RowIndex].Tag;

                if (tag is List<string> list) paths = list;
                else if (tag is string str) paths = str.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                if (paths != null && paths.Count > 0)
                {
                    int currentRow = e.RowIndex;
                    string files = string.Join(";", paths);

                    progressBar.Minimum = 0;
                    progressBar.Maximum = Math.Max(0, paths.Count);
                    progressBar.Value = 0;

                    var progress = new Progress<int>(value =>
                    {
                        progressBar.Value = value;
                        mergingTextOutput.Text = $"{(colName == "OpenAll" ? "OPENING" : "PROCESSING")} FILE {value} OF {paths.Count}";
                    });

                    // Route to the correct method based on which button was clicked
                    if (colName == "Merge")
                    {
                        await Task.Run(() => ExportMergeAndImportAllFiles(files, progress, currentRow));
                        mergingTextOutput.Text = "MERGE COMPLETE!";
                    }
                    else if (colName == "OpenAll")
                    {
                        await Task.Run(() => OpenAllFiles(files, progress, currentRow));
                        mergingTextOutput.Text = "FIRST FILE OPENED!";
                    }
                    else if (colName == "OpenOne")
                    {
                        await Task.Run(() => OpenAllFiles(files, progress, currentRow, true));
                        mergingTextOutput.Text = "FILES OPENED!";
                    }

                    progressBar.Value = 0;
                }
            }
        }

        public void ExportMergeAndImportAllFiles(string inputList, IProgress<int> progress, int rowIndex)
        {
            string[] paths = inputList.Split(';');
            if (paths.Length < 2) return;

            string mainScene = paths[0];
            string psdPath = paths.Last();
            var convertToTpl = paths.Skip(1).Take(paths.Length - 2).ToList();

            if (paths.Last().EndsWith(".psd", StringComparison.OrdinalIgnoreCase))
            {
                psdPath = paths.Last();
                convertToTpl = paths.Skip(1).Take(paths.Length - 2).ToList();
            }
            else
            {
                psdPath = "";
                convertToTpl = paths.Skip(1).ToList();
            }

            if (convertToTpl.Count == 0 && string.IsNullOrEmpty(psdPath))
            {
                Console.WriteLine($"Row {rowIndex}: Nothing to process (No Animators or PSD).");
                return;
            }

            DirectoryInfo di = new DirectoryInfo(episodePath);
            string folderPrefix = di.Name.Substring(0, Math.Min(di.Name.Length, 4));
            string libPath = Path.Combine(di.Parent?.Parent?.FullName ?? "", "IMPORT_LIBRARY", folderPrefix);
            if (!Directory.Exists(libPath))
            {
                CreateOrFullySecurePath(libPath);
                System.Threading.Thread.Sleep(2000);
            }

            string rootPath = di.Parent?.Parent?.FullName;

            //KillHarmony();

            // TPL BLOCK
            if (convertToTpl != null && convertToTpl.Count > 0)
            {
                System.Threading.Thread.Sleep(1000);

                Console.WriteLine("MY LIBRARY: " + libPath);

                var envLibVars = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "HARMONY_TASK", "EXPORT" },
                    { "MY_LIB_PATH", libPath }
                };

                int currentCount = 0;

                foreach (string sceneFile in convertToTpl)
                {
                    var exportSc = rootPath + "\\RC_ExportTPL.js";
                    RunHarmonyBatch(harmonyPath, sceneFile, exportSc, envLibVars, rowIndex, true);

                    currentCount++;
                    progress.Report(currentCount);
                }

                var importSc = rootPath + "\\RC_ImportTPL.js";
                var envTplVars = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "HARMONY_TASK", "IMPORT" },
                    { "MY_LIB_PATH", libPath },
                    { "TPL_COUNT", convertToTpl.Count.ToString() }
                };

                RunHarmonyBatch(harmonyPath, mainScene, importSc, envLibVars, rowIndex, false);

                System.Threading.Thread.Sleep(1000);
            }
            else
            {
                Console.WriteLine("No TPL files to process. Skipping Export/Import blocks.");
            }

            // PSD BLOCK
            if (!string.IsNullOrWhiteSpace(psdPath) && File.Exists(psdPath))
            {
                System.Threading.Thread.Sleep(1000);
                Console.WriteLine("Importing PSD: " + psdPath);

                var psdSc = Path.Combine(rootPath, "RC_ImportPSD.js");
                var envPsdVars = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "TARGET_PSD", psdPath },
                };

                RunHarmonyBatch(harmonyPath, mainScene, psdSc, envPsdVars, rowIndex, false);
            }
            else
            {
                Console.WriteLine("PSD path is empty or file not found. Skipping PSD Import.");
            }
        }

        private void RunHarmonyBatch(string appPath, string sceneFile, string scriptFile, Dictionary<string, string> env, int rowIndex, bool isReadOnly)
        {
            // Arguments = $" -user usabatch \"{sceneFile}\" -compile \"{scriptFile}\" {(isReadOnly ? "-readonly " : "")}",
            // Arguments = $" -batch -compile \"{scriptFile}\" \"{sceneFile}\" {(isReadOnly ? "-readonly " : "")}",
            // Arguments = $" \"{sceneFile}\" -compile \"{scriptFile}\" {(isReadOnly ? "-readonly " : "")}",
            // Arguments = $" \"{sceneFile}\" {(isPsd ? "-compile \"{scriptFile}\" " : "")} {(isReadOnly ? "-readonly " : "")}",

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = appPath,
                Arguments = $" -user usabatch \"{sceneFile}\" -compile \"{scriptFile}\" {(isReadOnly ? "-readonly " : "")}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true // Keep it clean
            };

            if (env != null)
            {
                foreach (var v in env)
                {
                    //Environment.SetEnvironmentVariable(v.Key, v.Value, EnvironmentVariableTarget.User);
                    startInfo.EnvironmentVariables[v.Key] = v.Value;

                    Console.WriteLine($"[ENV] send variable: {startInfo.EnvironmentVariables[v.Key] = v.Value} ");
                }
            }

            Console.WriteLine($"[LOG] Processing: {Path.GetFileName(sceneFile)}...");

            using (Process p = new Process())
            {
                p.StartInfo = startInfo;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;

                // Set up real-time log capture BEFORE starting
                p.OutputDataReceived += (s, e) => { if (e.Data != null) _logWindow.AppendLog(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) _logWindow.AppendLog("ERROR: " + e.Data); };

                _logWindow.AppendLog($"STARTING: {Path.GetFileName(sceneFile)}");

                // START ONLY ONCE
                p.Start();

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                p.WaitForExit();

                // UI Updates
                this.Invoke(new Action(() =>
                {
                    dataGridView.Rows[rowIndex].Cells["Status"].Value =
                        (p.ExitCode == 0) ? Properties.Resources.STATUS_DONE : Properties.Resources.STATUS_ERROR;
                }));

                if (p.ExitCode == 0)
                {
                    Console.WriteLine($"[SUCCESS] Harmony finished {Path.GetFileName(sceneFile)}.");
                }
                else
                {
                    Console.WriteLine($"[ERROR] Harmony exited with code {p.ExitCode}");
                }

                // Cleanup script
                //if (File.Exists(StartupScriptPath))
                //{
                //    File.Delete(StartupScriptPath);
                //    Console.WriteLine("Cleanup: TB_sceneOpened.js removed.");
                //}
            }
        }

        public void OpenAllFiles(string inputList, IProgress<int> progress, int rowIndex, bool openOnlyFirst = false)
        {
            // Split and remove any empty entries
            string[] paths = inputList.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length == 0) return;

            // IF openOnlyFirst is true, slice the array down to just the first path
            if (openOnlyFirst)
            {
                paths = new string[] { paths[0] };
            }

            List<string> harmonyFiles = new List<string>();
            string psdPath = "";

            // 1. Identify PSD vs Harmony files
            if (paths.Last().EndsWith(".psd", StringComparison.OrdinalIgnoreCase))
            {
                psdPath = paths.Last();
                harmonyFiles = paths.Take(paths.Length - 1).ToList();
            }
            else
            {
                harmonyFiles = paths.ToList();
            }

            int currentCount = 0;
            int totalItems = harmonyFiles.Count + (string.IsNullOrEmpty(psdPath) ? 0 : 1);

            // 2. Open Harmony Scenes
            foreach (string sceneFile in harmonyFiles)
            {
                if (File.Exists(sceneFile))
                {
                    RunFileProcess(harmonyPath, sceneFile, isHarmony: true);
                }
                currentCount++;
                progress?.Report(currentCount);
            }

            // 3. Open PSD file if it exists
            if (!string.IsNullOrEmpty(psdPath) && File.Exists(psdPath))
            {
                RunFileProcess("", psdPath, isHarmony: false);
                currentCount++;
                progress?.Report(currentCount);
            }
        }

        public void RunFileProcess(string appPath, string filePath, bool isHarmony)
        {
            ProcessStartInfo processInfo;

            if (isHarmony)
            {
                processInfo = new ProcessStartInfo
                {
                    FileName = appPath,
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                // For PSDs, use ShellExecute to open with the default system app (Photoshop)
                processInfo = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
            }

            try
            {
                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening {filePath}: {ex.Message}");
            }
        }


        private void importListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog { Filter = "XML Files (*.xml)|*.xml" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    XDocument doc = XDocument.Load(ofd.FileName);
                    this.episodePath = doc.Root.Element("EpisodePath")?.Value ?? "";
                    var rows = doc.Descendants("Row").ToList();

                    if (rows.Count == 0) return;

                    var animatorFolders = rows.First().Elements("Column")
                        .Select(c => c.Attribute("Name")?.Value)
                        .Where(name => name != "Primary" && name != "PSD")
                        .ToList();

                    this.Invoke(new Action(() =>
                    {
                        InitializeGridColumns(rows.Count, animatorFolders);
                        progressBar.Value = 0;
                    }));

                    // 3. Populate Rows
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var xmlRow = rows[i];
                        int step = i + 1;

                        var rowData = new List<object>();

                        rowData.Add(xmlRow.Elements("Column").FirstOrDefault(x => x.Attribute("Name")?.Value == "Primary")?.Value ?? "");

                        foreach (var animator in animatorFolders)
                        {
                            rowData.Add(xmlRow.Elements("Column").FirstOrDefault(x => x.Attribute("Name")?.Value == animator)?.Value ?? "");
                        }

                        rowData.Add(xmlRow.Elements("Column").FirstOrDefault(x => x.Attribute("Name")?.Value == "PSD")?.Value ?? "");

                        // UI Update
                        this.Invoke(new Action(() =>
                        {
                            int rowIndex = dataGridView.Rows.Add(rowData.ToArray());

                            // Store the HiddenPaths in the Tag property for later use (Merging)
                            dataGridView.Rows[rowIndex].Tag = xmlRow.Element("HiddenPaths")?.Value;

                            mergingTextOutput.Text = $"IMPORTING: {rowData[0]}";
                            progressBar.Value = step;
                        }));
                    }

                    //dataGridView.CellClick -= DgvCompare_CellClick;
                    //dataGridView.CellClick += DgvCompare_CellClick;
                    dataGridView.CellClick -= DataGridView_CellContentClick;
                    dataGridView.CellClick += DataGridView_CellContentClick;

                    this.Invoke(new Action(() =>
                    {
                        mergingTextOutput.Text = "XML IMPORT FINISHED.";
                        progressBar.Value = 0;
                    }));
                }
            }
        }

        private void renderListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog { Filter = "XML Files (*.xml)|*.xml", FileName = "HarmonyGridExport.xml" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                    using (XmlWriter writer = XmlWriter.Create(sfd.FileName, settings))
                    {
                        writer.WriteStartDocument();
                        writer.WriteStartElement("HarmonyData");
                        writer.WriteElementString("EpisodePath", episodePath);

                        foreach (DataGridViewRow row in dataGridView.Rows)
                        {
                            if (row.IsNewRow) continue;

                            writer.WriteStartElement("Row");

                            // 1. Save all cell values by Column Name
                            foreach (DataGridViewColumn col in dataGridView.Columns)
                            {
                                // Skip buttons and status images, just save text/data
                                if (col is DataGridViewTextBoxColumn || col.Name == "Primary")
                                {
                                    writer.WriteStartElement("Column");
                                    writer.WriteAttributeString("Name", col.Name);
                                    writer.WriteValue(row.Cells[col.Index].Value?.ToString() ?? "");
                                    writer.WriteEndElement();
                                }
                            }

                            // 2. Save the row.Tag (List<string> rowPaths)
                            if (row.Tag is List<string> paths)
                            {
                                writer.WriteElementString("HiddenPaths", string.Join(";", paths));
                            }

                            writer.WriteEndElement(); // Row
                        }

                        writer.WriteEndElement();
                        writer.WriteEndDocument();
                    }
                    MessageBox.Show("Export Complete!");
                }
            }
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProgramSettings ps = new ProgramSettings();
            ps.ShowDialog();
        }

        private async void buttRenderAll_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to process all files in the list?",
                "Confirm Batch Processing",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            buttRenderAll.Enabled = false;

            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                if (row.IsNewRow || row.Tag == null) continue;

                dataGridView.ClearSelection();
                row.Selected = true;

                List<string> paths = null;
                if (row.Tag is List<string> list) paths = list;
                else if (row.Tag is string str) paths = str.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                // Only proceed if we successfully extracted paths
                if (paths != null && paths.Count > 0)
                {
                    string files = string.Join(";", paths);
                    int rowIndex = row.Index;

                    row.Cells["Status"].Value = Properties.Resources.STATUS_EMPTY;

                    var progress = new Progress<int>(value =>
                    {
                        progressBar.Value = value;
                        mergingTextOutput.Text = $"ROW {rowIndex + 1}: PROCESSING FILE {value} OF {paths.Count}";
                    });

                    await Task.Run(() => ExportMergeAndImportAllFiles(files, progress, rowIndex));
                }
            }

            mergingTextOutput.Text = "BATCH COMPLETE!";
            buttRenderAll.Enabled = true;
        }

        private void logWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenLog();
        }

        public void CreateOrFullySecurePath(string fullPath)
        {
            DirectorySecurity securityRules = new DirectorySecurity();
            SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            securityRules.AddAccessRule(new FileSystemAccessRule(
                everyone,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            securityRules.SetAccessRuleProtection(true, false);

            Directory.CreateDirectory(fullPath, securityRules);

            DirectoryInfo dInfo = new DirectoryInfo(fullPath);
            dInfo.SetAccessControl(securityRules);
        }

        public void RepairFolderPermissions(string folderPath)
        {
            DirectoryInfo dInfo = new DirectoryInfo(folderPath);

            DirectorySecurity dSecurity = dInfo.GetAccessControl();
            dSecurity.SetAccessRuleProtection(false, false);

            SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
            FileSystemAccessRule fullControlRule = new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            dSecurity.AddAccessRule(fullControlRule);

            dInfo.SetAccessControl(dSecurity);
        }

        public static void KillHarmony()
        {
            string[] harmonyProcesses = { "HarmonyPremium", "HarmonyAdvanced", "HarmonyEssentials" };

            foreach (var name in harmonyProcesses)
            {
                var processes = Process.GetProcessesByName(name);

                foreach (var process in processes)
                {
                    try
                    {
                        Console.WriteLine($"Found {name} (PID: {process.Id}). Ending process...");

                        process.Kill();

                        // Optional: Wait for the process to fully exit
                        //process.WaitForExit(5000);

                        Console.WriteLine("Process ended successfully.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not end process: {ex.Message}");
                    }
                }
            }
        }

        public static void CopyScriptToHarmonyAppData(string sourceDir)
        {
            string[] scriptFiles = { "TB_sceneOpened.js", "RC_ImportTPL.js", "RC_ExportTPL.js" };

            if (!Directory.Exists(TargetDir)) Directory.CreateDirectory(TargetDir);

            foreach (string fileName in scriptFiles)
            {
                string sourceFile = Path.Combine(sourceDir, fileName);
                string destFile = Path.Combine(TargetDir, fileName);

                if (File.Exists(sourceFile))
                {
                    File.Copy(sourceFile, destFile, true);
                }
            }
            Console.WriteLine("Scripts copied and overwritten in Harmony folder.");
        }

        private async Task AnimateLoadingTextAsync(string folderName, System.Threading.CancellationToken token)
        {
            int dotCount = 0;
            while (!token.IsCancellationRequested)
            {
                string dots = new string('.', dotCount);
                mergingTextOutput.Text = $"{folderName} - START PROCESSING {dots}";

                dotCount = (dotCount + 1) % 4; // Cycles through 0, 1, 2, 3 dots

                try
                {
                    // Wait 500ms before adding the next dot
                    await Task.Delay(500, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}


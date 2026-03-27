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
            OpenLog();

        }

        private void SetupUI()
        {
            this.Text = "Harmony File Merger";
            this.Size = new System.Drawing.Size(1200, 900);

            buttBrowse.Click += async (s, e) => await LoadDataAsync();
            //renderListToolStripMenuItem.Click += (s, e) => ExportToXml();
            //importListToolStripMenuItem.Click += (s, e) => ImportFromXml();

            dataGridView.CellClick += DgvCompare_CellClick;

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
                    mergingTextOutput.Text = "START PROCESSING";

                    await Task.Run(() => ProcessDirectories(episodePath));

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
            this.Invoke(new Action(() => mergingTextOutput.Text = "FINISHED LOADING."));
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
            dataGridView.Columns.Add(new DataGridViewButtonColumn { Name = "Merge", Text = "Merge", UseColumnTextForButtonValue = true });
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
                    row.Cells[folder].Value = primary.Name; rowPaths.Add(match);

                    _logWindow.AppendLog($"FOUND TPL: {match}");
                    //Console.WriteLine("FOUND TPL: " + match);
                }
            }

            // PSD Matching (Highest version logic)
            string code = ((string[])primary.Name.Split('-')).LastOrDefault();
            if (!string.IsNullOrEmpty(code))
            {
                var bestPsd = psds.Where(p => Path.GetFileNameWithoutExtension(p).Split('_').Contains(code))
                    .OrderByDescending(p =>
                    {
                        string fn = Path.GetFileNameWithoutExtension(p);
                        int.TryParse(fn.Substring(Math.Max(0, fn.Length - 2)), out int v);
                        return v;
                    }).FirstOrDefault();

                if (bestPsd != null)
                {
                    row.Cells["PSD"].Value = Path.GetFileNameWithoutExtension(bestPsd);
                    rowPaths.Add(bestPsd);

                    _logWindow.AppendLog($"FOUND PSD: {bestPsd}");
                    //Console.WriteLine("FOUND PSD: " + bestPsd);
                }
            }
            row.Tag = rowPaths;
        }

        private async void DgvCompare_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            mergingTextOutput.Text = "";

            if (e.RowIndex >= 0 && dataGridView.Columns[e.ColumnIndex].Name == "Merge")
            {
                if (dataGridView.Rows[e.RowIndex].Tag is List<string> paths)
                {
                    int currentRow = e.RowIndex;
                    string files = string.Join(";", paths);
                    Console.WriteLine("PATHS SENDING TO HARMONY: " + files);

                    progressBar.Minimum = 0;
                    progressBar.Maximum = Math.Max(0, paths.Count);
                    progressBar.Value = 0;

                    var progress = new Progress<int>(value =>
                    {
                        progressBar.Value = value;
                        mergingTextOutput.Text = $"PROCESSING FILE {value} OF {paths.Count}";
                        progressBar.Update();
                    });

                    await Task.Run(() => ExportMergeAndImportAllFiles(files, progress, currentRow));

                    mergingTextOutput.Text = "ALL TASKS COMPLETE!";
                    progressBar.Value = 0;

                    Console.WriteLine("BUTTON: " + currentRow);
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


        private void importListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "XML Files (*.xml)|*.xml";
                ofd.Title = "Merge Comparison Data";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    DataSet ds = new DataSet();
                    ds.ReadXml(ofd.FileName);
                    DataTable dt = ds.Tables[0];

                    dataGridView.Columns.Clear();
                    dataGridView.Rows.Clear();

                    foreach (DataColumn dc in dt.Columns)
                    {
                        if (dc.ColumnName.EndsWith("_Path") || dc.ColumnName == "Status") continue;
                        dataGridView.Columns.Add(dc.ColumnName, dc.ColumnName);
                    }

                    dataGridView.Columns.Add(new DataGridViewButtonColumn { Name = "Merge", Text = "Merge", UseColumnTextForButtonValue = true });
                    dataGridView.Columns.Add(new DataGridViewImageColumn { Name = "Status", HeaderText = "Status", Image = Properties.Resources.STATUS_EMPTY, ImageLayout = DataGridViewImageCellLayout.Zoom, Width = 30 });

                    // Populate rows and restore Tags
                    foreach (DataRow dr in dt.Rows)
                    {
                        int rowIndex = dataGridView.Rows.Add();
                        var row = dataGridView.Rows[rowIndex];

                        List<string> rowPaths = new List<string>();

                        foreach (DataGridViewColumn col in dataGridView.Columns)
                        {
                            if (col is DataGridViewButtonColumn || col.Name == "Status") continue;

                            row.Cells[col.Name].Value = dr[col.Name].ToString();
                            string pathKey = col.Name + "_Path";
                            if (dt.Columns.Contains(pathKey) && !string.IsNullOrEmpty(dr[pathKey].ToString()))
                            {
                                rowPaths.Add(dr[pathKey].ToString());
                            }
                        }
                        row.Tag = rowPaths;

                        Console.WriteLine($"Row {rowIndex} tag count: {rowPaths.Count}");
                    }

                    dataGridView.CellClick -= DgvCompare_CellClick;
                    dataGridView.CellClick += DgvCompare_CellClick;

                    //MessageBox.Show("Data imported and UI reconstructed successfully.");
                }
            }
        }

        private void renderListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "XML Files (*.xml)|*.xml";
                sfd.Title = "Save Comparison Data";
                sfd.FileName = "XStageComparison.xml";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    DataTable dt = new DataTable("XStageData");

                    foreach (DataGridViewColumn col in dataGridView.Columns)
                    {
                        if (col is DataGridViewButtonColumn) continue;
                        dt.Columns.Add(col.Name);
                        dt.Columns.Add(col.Name + "_Path"); // Hidden column with full path (Tag)
                    }

                    foreach (DataGridViewRow row in dataGridView.Rows)
                    {
                        if (row.IsNewRow) continue;
                        DataRow dr = dt.NewRow();

                        List<string> paths = row.Tag as List<string>;

                        foreach (DataGridViewColumn col in dataGridView.Columns)
                        {
                            if (col is DataGridViewButtonColumn) continue;

                            dr[col.Name] = row.Cells[col.Name].Value?.ToString() ?? "";

                            if (col.Name == "Primary" && paths != null && paths.Count > 0)
                                dr[col.Name + "_Path"] = paths[0];

                            else if (col.Name == "PSD" && paths != null && paths.Count > 0 && paths.Last().EndsWith(".psd"))
                                dr[col.Name + "_Path"] = paths.Last();

                            else if (paths != null)
                            {
                                string cellVal = row.Cells[col.Name].Value?.ToString();
                                if (!string.IsNullOrEmpty(cellVal))
                                {
                                    dr[col.Name + "_Path"] = paths.FirstOrDefault(p =>
                                        Path.GetFileNameWithoutExtension(p) == cellVal && !p.EndsWith(".psd"));
                                }
                            }
                        }
                        dt.Rows.Add(dr);
                    }

                    dt.WriteXml(sfd.FileName, XmlWriteMode.WriteSchema);
                    //MessageBox.Show($"Data successfully exported to: {Path.GetFileName(sfd.FileName)}");
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

                if (row.Tag is List<string> paths)
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
    }
}


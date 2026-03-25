using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

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
                {
                    fbd.SelectedPath = savedPath;
                }

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    Properties.Settings.Default.LastPath = fbd.SelectedPath;
                    Properties.Settings.Default.Save();

                    episodePath = fbd.SelectedPath;
                    await Task.Run(() => ProcessDirectories(episodePath));
                }
            }
        }

        private void ProcessDirectories(string rootPath)
        {
            // 1. Get Primary Index 
            var primaryFiles = Directory.GetDirectories(rootPath)
                .SelectMany(d => Directory.GetFiles(d, "*.xstage", SearchOption.TopDirectoryOnly))
                .Where(f => !Path.GetFileName(f).Contains("_render"))
                .Select(f => new { Name = Path.GetFileNameWithoutExtension(f), FullPath = f })
                .ToList();

            // 2. ANIMATORS 
            string animPath = Path.Combine(rootPath, "ANIMATORS");
            var animatorMap = new Dictionary<string, List<string>>(); 

            if (Directory.Exists(animPath))
            {
                foreach (var dir in Directory.GetDirectories(animPath))
                {
                    string folderName = Path.GetFileName(dir);
                    var files = Directory.GetFiles(dir, "*.xstage", SearchOption.AllDirectories).ToList();
                    animatorMap.Add(folderName, files);

                    //Console.WriteLine("DIR: " + folderName);
                }
            }

            // 3. PSD 
            DirectoryInfo di = new DirectoryInfo(rootPath);
            string bgPath = Path.Combine(di.Parent?.Parent?.FullName ?? "", "03BACKGROUND", di.Name);
            var psdFiles = Directory.GetFiles(bgPath, "*.psd", SearchOption.TopDirectoryOnly).ToList();

            this.Invoke(new Action(() =>
            {
                dataGridView.Columns.Clear();
                dataGridView.Rows.Clear();

                // Build Columns
                dataGridView.Columns.Add("Primary", "Primary Index");
                var folderColumns = animatorMap.Keys.ToList();
                foreach (var colName in folderColumns)
                {
                    dataGridView.Columns.Add(colName, colName);
                }

                // Add PSD Column
                dataGridView.Columns.Add("PSD", "PSDs");

                // Add Export and Merge All Button Column
                dataGridView.Columns.Add(new DataGridViewButtonColumn { Name = "Merge", Text = "Merge", UseColumnTextForButtonValue = true });

                // Add image column
                var statusCol = new DataGridViewImageColumn { Name = "Status", HeaderText = "Status", Image = Properties.Resources.STATUS_EMPTY, ImageLayout = DataGridViewImageCellLayout.Zoom, Width = 30 };
                dataGridView.Columns.Add(statusCol);

                // Fill Rows
                foreach (var primary in primaryFiles)
                {
                    int rowIndex = dataGridView.Rows.Add();
                    var row = dataGridView.Rows[rowIndex];
                    row.Cells[0].Value = primary.Name; // Column 1: Primary Index

                    var rowPaths = new List<string> { primary.FullPath };

                    for (int i = 0; i < folderColumns.Count; i++)
                    {
                        string folderName = folderColumns[i];
                        var match = animatorMap[folderName].FirstOrDefault(f =>
                            Path.GetFileNameWithoutExtension(f).Equals(primary.Name, StringComparison.OrdinalIgnoreCase));

                        if (match != null)
                        {
                            row.Cells[i + 1].Value = primary.Name;
                            rowPaths.Add(match);

                            //Console.WriteLine("CELL FILE: " + match);
                        }
                    }

                    // Match PSDs  K_01_05_01)
                    string primaryCode = primary.Name.Split('-').LastOrDefault();

                    if (!string.IsNullOrEmpty(primaryCode))
                    {
                        var matchedPsds = psdFiles.Where(psdPath =>
                        {
                            string psdName = Path.GetFileNameWithoutExtension(psdPath);
                            string baseName = psdName.Length > 3
                                ? psdName.Substring(0, psdName.Length - 3)
                                : psdName;

                            string[] psdSegments = baseName.Split('_');

                            return psdSegments.Contains(primaryCode);
                        }).ToList();

                        if (matchedPsds.Any())
                        {
                            row.Cells["PSD"].Value = string.Join(", ", matchedPsds.Select(Path.GetFileNameWithoutExtension));
                            rowPaths.AddRange(matchedPsds);
                        }
                    }

                    row.Tag = rowPaths;
                }
            }));
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

                    progressBar.Minimum = 0;
                    progressBar.Maximum = paths.Count - 1;
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

                    //Console.WriteLine("BUTTON: " + envStr);
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

            DirectoryInfo di = new DirectoryInfo(episodePath);
            string folderPrefix = di.Name.Substring(0, Math.Min(di.Name.Length, 4));
            string libPath = Path.Combine(di.Parent?.Parent?.FullName ?? "", "IMPORT_LIBRARY", folderPrefix);
            if (!Directory.Exists(libPath))
            {
                CreateOrFullySecurePath(libPath);
                System.Threading.Thread.Sleep(2000);
            }
            
            string rootPath = di.Parent?.Parent?.FullName;

            KillHarmony();

            //CopyScriptToHarmonyAppData(rootPath);
            //RepairFolderPermissions(mainScene);

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

            var psdSc = rootPath + "\\RC_ImportPSD.js";
            var envPsdVars = new System.Collections.Generic.Dictionary<string, string>
            {
                { "TARGET_PSD", psdPath },
            };

            RunHarmonyBatch(harmonyPath, mainScene, psdSc, envPsdVars, rowIndex, false);
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
                this.Invoke(new Action(() => {
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
                        if (dc.ColumnName.EndsWith("_Path")) continue;
                        dataGridView.Columns.Add(dc.ColumnName, dc.ColumnName);
                    }

                    DataGridViewButtonColumn btnCol = new DataGridViewButtonColumn { Name = "Merge", Text = "Merge", UseColumnTextForButtonValue = true };
                    dataGridView.Columns.Add(btnCol);

                    // Populate rows and restore Tags
                    foreach (DataRow dr in dt.Rows)
                    {
                        int rowIndex = dataGridView.Rows.Add();
                        foreach (DataGridViewColumn col in dataGridView.Columns)
                        {
                            if (col is DataGridViewButtonColumn) continue;

                            dataGridView.Rows[rowIndex].Cells[col.Name].Value = dr[col.Name];
                            dataGridView.Rows[rowIndex].Cells[col.Name].Tag = dr[col.Name + "_Path"]; // Hidden column with full path (Tag)
                        }
                    }

                    MessageBox.Show("Data imported and UI reconstructed successfully.");
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
                        DataRow dr = dt.NewRow();
                        foreach (DataGridViewColumn col in dataGridView.Columns)
                        {
                            if (col is DataGridViewButtonColumn) continue;
                            dr[col.Name] = row.Cells[col.Name].Value;
                            dr[col.Name + "_Path"] = row.Cells[col.Name].Tag; // Save the full path
                        }
                        dt.Rows.Add(dr);
                    }

                    dt.WriteXml(sfd.FileName, XmlWriteMode.WriteSchema);
                    MessageBox.Show($"Data successfully exported to: {Path.GetFileName(sfd.FileName)}");
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


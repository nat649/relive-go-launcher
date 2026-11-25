using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ReliveGOLauncher
{
    public class ManifestFile
    {
        public string path { get; set; }
        public string url { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
        public bool backup { get; set; }
    }

    public class ManifestData
    {
        public string version { get; set; }
        public string launcher_version { get; set; }
        public string gc_server_url { get; set; }
        public string updated_at { get; set; }
        public List<ManifestFile> files { get; set; }
    }

    public class LauncherForm : Form
    {
        private const string LAUNCHER_VERSION = "1.0.0";
        private const string MANIFEST_URL = "https://raw.githubusercontent.com/nat649/relive-go-launcher/main/manifest.json";
        private const string GC_SERVER_URL = "https://rlgo.specs.qzz.io";

        private static readonly Color BgDark = Color.FromArgb(15, 19, 26);
        private static readonly Color CardBg = Color.FromArgb(24, 30, 41);
        private static readonly Color BorderCol = Color.FromArgb(44, 54, 72);
        private static readonly Color AccentGold = Color.FromArgb(230, 168, 23);
        private static readonly Color AccentGreen = Color.FromArgb(46, 204, 113);
        private static readonly Color AccentRed = Color.FromArgb(231, 76, 60);
        private static readonly Color TextMain = Color.FromArgb(238, 242, 248);
        private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);

        private TextBox txtGameDir;
        private TextBox txtLaunchArgs;
        private Label lblServerStatus;
        private Label lblUpdateStatus;
        private Label lblNewsTitle;
        private Label lblNewsBody;
        private ProgressBar progressBar;
        private Button btnBrowse;
        private Button btnCheck;
        private Button btnPlay;

        private ManifestData currentManifest;
        private List<ManifestFile> outdatedFiles = new List<ManifestFile>();
        private bool isBusy = false;

        private string SettingsPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReliveGO");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "launcher.ini");
            }
        }

        public LauncherForm()
        {
            Text = "Relive:GO (RL:GO) Launcher v" + LAUNCHER_VERSION;
            Size = new Size(680, 520);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            ForeColor = TextMain;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            BuildUI();
            LoadSettingsAndDetect();

            Shown += async (s, e) =>
            {
                await RefreshServerInfoAsync();
                await CheckForUpdatesAsync();
            };
        }

        private void BuildUI()
        {
            // Header panel
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 84,
                BackColor = Color.FromArgb(20, 25, 35)
            };
            header.Paint += (s, e) =>
            {
                using (Pen p = new Pen(AccentGold, 2))
                    e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            Label lblLogo = new Label
            {
                Text = "RELIVE:GO",
                Font = new Font("Segoe UI", 22f, FontStyle.Bold),
                ForeColor = AccentGold,
                Location = new Point(22, 12),
                AutoSize = true
            };
            Label lblSub = new Label
            {
                Text = "Client Launcher & Auto-Updater • CS:GO Legacy (AppID 4465480)",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = TextMuted,
                Location = new Point(25, 52),
                AutoSize = true
            };
            lblServerStatus = new Label
            {
                Text = "● Connexion au serveur Relive:GO...",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = TextMuted,
                Location = new Point(360, 28),
                Size = new Size(295, 28),
                TextAlign = ContentAlignment.MiddleRight
            };
            header.Controls.Add(lblLogo);
            header.Controls.Add(lblSub);
            header.Controls.Add(lblServerStatus);
            Controls.Add(header);

            // News card
            Panel newsCard = CreateCard(22, 102, 620, 118);
            Label lblNewsBadge = new Label
            {
                Text = "ACTUALITÉS RELIVE:GO",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = AccentGold,
                Location = new Point(14, 10),
                AutoSize = true
            };
            lblNewsTitle = new Label
            {
                Text = "Chargement des actualités...",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = TextMain,
                Location = new Point(14, 30),
                Size = new Size(590, 24)
            };
            lblNewsBody = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = TextMuted,
                Location = new Point(14, 58),
                Size = new Size(590, 50)
            };
            newsCard.Controls.Add(lblNewsBadge);
            newsCard.Controls.Add(lblNewsTitle);
            newsCard.Controls.Add(lblNewsBody);
            Controls.Add(newsCard);

            // Install folder & launch options card
            Panel cfgCard = CreateCard(22, 234, 620, 124);
            Label lblDir = new Label
            {
                Text = "Dossier CS:GO Legacy :",
                ForeColor = TextMuted,
                Location = new Point(14, 14),
                AutoSize = true
            };
            txtGameDir = new TextBox
            {
                Location = new Point(14, 34),
                Size = new Size(495, 26),
                BackColor = BgDark,
                ForeColor = TextMain,
                BorderStyle = BorderStyle.FixedSingle
            };
            btnBrowse = new Button
            {
                Text = "Parcourir...",
                Location = new Point(516, 33),
                Size = new Size(90, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(38, 48, 66),
                ForeColor = TextMain,
                Cursor = Cursors.Hand
            };
            btnBrowse.FlatAppearance.BorderColor = BorderCol;
            btnBrowse.Click += OnBrowseClick;

            Label lblArgs = new Label
            {
                Text = "Options de lancement :",
                ForeColor = TextMuted,
                Location = new Point(14, 68),
                AutoSize = true
            };
            txtLaunchArgs = new TextBox
            {
                Text = "-steam -novid -tickrate 128",
                Location = new Point(14, 88),
                Size = new Size(592, 26),
                BackColor = BgDark,
                ForeColor = TextMain,
                BorderStyle = BorderStyle.FixedSingle
            };
            cfgCard.Controls.Add(lblDir);
            cfgCard.Controls.Add(txtGameDir);
            cfgCard.Controls.Add(btnBrowse);
            cfgCard.Controls.Add(lblArgs);
            cfgCard.Controls.Add(txtLaunchArgs);
            Controls.Add(cfgCard);

            // Update status + progress
            lblUpdateStatus = new Label
            {
                Text = "Vérification des fichiers sur GitHub...",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = TextMuted,
                Location = new Point(22, 372),
                Size = new Size(490, 22)
            };
            btnCheck = new Button
            {
                Text = "Vérifier",
                Location = new Point(552, 369),
                Size = new Size(90, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(38, 48, 66),
                ForeColor = TextMain,
                Cursor = Cursors.Hand
            };
            btnCheck.FlatAppearance.BorderColor = BorderCol;
            btnCheck.Click += async (s, e) => await CheckForUpdatesAsync();

            progressBar = new ProgressBar
            {
                Location = new Point(22, 400),
                Size = new Size(620, 10),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100
            };

            btnPlay = new Button
            {
                Text = "JOUER À RELIVE:GO",
                Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                Location = new Point(22, 422),
                Size = new Size(620, 46),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentGold,
                ForeColor = Color.FromArgb(18, 22, 28),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnPlay.FlatAppearance.BorderSize = 0;
            btnPlay.Click += OnPlayClick;

            Controls.Add(lblUpdateStatus);
            Controls.Add(btnCheck);
            Controls.Add(progressBar);
            Controls.Add(btnPlay);
        }

        private Panel CreateCard(int x, int y, int w, int h)
        {
            Panel p = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = CardBg
            };
            p.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(BorderCol, 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }

        private void LoadSettingsAndDetect()
        {
            string savedDir = "";
            if (File.Exists(SettingsPath))
            {
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    if (line.StartsWith("game_dir=")) savedDir = line.Substring(9).Trim();
                    else if (line.StartsWith("launch_args=")) txtLaunchArgs.Text = line.Substring(12).Trim();
                }
            }

            if (IsValidCsgoDir(savedDir))
            {
                txtGameDir.Text = savedDir;
                return;
            }

            string detected = DetectCsgoLegacyDir();
            if (!string.IsNullOrEmpty(detected))
            {
                txtGameDir.Text = detected;
                SaveSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllLines(SettingsPath, new[]
                {
                    "game_dir=" + txtGameDir.Text.Trim(),
                    "launch_args=" + txtLaunchArgs.Text.Trim()
                });
            }
            catch { }
        }

        private static bool IsValidCsgoDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
            return File.Exists(Path.Combine(dir, "csgo", "steam.inf")) || File.Exists(Path.Combine(dir, "csgo.exe"));
        }

        private static string DetectCsgoLegacyDir()
        {
            var candidates = new List<string>();
            try
            {
                string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(steamPath))
                {
                    steamPath = steamPath.Replace('/', '\\');
                    candidates.Add(steamPath);

                    string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                    {
                        string content = File.ReadAllText(vdf);
                        foreach (Match m in Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\""))
                        {
                            string lib = m.Groups[1].Value.Replace("\\\\", "\\");
                            if (!candidates.Contains(lib)) candidates.Add(lib);
                        }
                    }
                }
            }
            catch { }

            string[] folderNames = { "csgo legacy", "Counter-Strike Global Offensive" };
            foreach (string lib in candidates)
            {
                foreach (string name in folderNames)
                {
                    string full = Path.Combine(lib, "steamapps", "common", name);
                    if (IsValidCsgoDir(full)) return full;
                }
            }
            return "";
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Sélectionne le dossier de CS:GO Legacy (contenant csgo.exe et le dossier csgo/)";
                if (Directory.Exists(txtGameDir.Text)) dlg.SelectedPath = txtGameDir.Text;
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    txtGameDir.Text = dlg.SelectedPath;
                    SaveSettings();
                    var _ = CheckForUpdatesAsync();
                }
            }
        }

        private async Task RefreshServerInfoAsync()
        {
            try
            {
                using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                {
                    wc.Headers.Add("User-Agent", "ReliveGOLauncher/" + LAUNCHER_VERSION);
                    string healthJson = await wc.DownloadStringTaskAsync(GC_SERVER_URL + "/api/health");
                    var ser = new JavaScriptSerializer();
                    var h = ser.Deserialize<Dictionary<string, object>>(healthJson);
                    int srvs = h.ContainsKey("active_servers") ? Convert.ToInt32(h["active_servers"]) : 0;
                    int searching = h.ContainsKey("searching") ? Convert.ToInt32(h["searching"]) : 0;
                    int matches = h.ContainsKey("active_matches") ? Convert.ToInt32(h["active_matches"]) : 0;
                    lblServerStatus.Text = string.Format("● En ligne • {0} srv • {1} en file • {2} match(s)", srvs, searching, matches);
                    lblServerStatus.ForeColor = AccentGreen;
                }
            }
            catch
            {
                lblServerStatus.Text = "● Serveur Relive:GO hors ligne";
                lblServerStatus.ForeColor = AccentRed;
            }

            try
            {
                using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                {
                    wc.Headers.Add("User-Agent", "ReliveGOLauncher/" + LAUNCHER_VERSION);
                    string evJson = await wc.DownloadStringTaskAsync(GC_SERVER_URL + "/api/news/events");
                    var ser = new JavaScriptSerializer();
                    var root = ser.Deserialize<Dictionary<string, object>>(evJson);
                    var events = root.ContainsKey("events") ? root["events"] as System.Collections.ArrayList : null;
                    if (events != null && events.Count > 0)
                    {
                        var first = events[0] as Dictionary<string, object>;
                        lblNewsTitle.Text = first != null && first.ContainsKey("event_name") ? Convert.ToString(first["event_name"]) : "Relive:GO";
                        var bodyObj = first != null && first.ContainsKey("announcement_body") ? first["announcement_body"] as Dictionary<string, object> : null;
                        lblNewsBody.Text = bodyObj != null && bodyObj.ContainsKey("body") ? Convert.ToString(bodyObj["body"]) : "";
                    }
                    else
                    {
                        lblNewsTitle.Text = "Bienvenue sur Relive:GO (RL:GO)";
                        lblNewsBody.Text = "Matchmaking Officiel, inventaires Cloud sécurisés et serveurs dédiés 128-tick.";
                    }
                }
            }
            catch
            {
                lblNewsTitle.Text = "Relive:GO (RL:GO)";
                lblNewsBody.Text = "Impossible de charger les actualités pour le moment.";
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            if (isBusy) return;
            isBusy = true;
            btnPlay.Enabled = false;
            btnCheck.Enabled = false;
            outdatedFiles.Clear();
            progressBar.Value = 0;

            string gameDir = txtGameDir.Text.Trim();
            if (!IsValidCsgoDir(gameDir))
            {
                lblUpdateStatus.Text = "⚠ Sélectionne le dossier CS:GO Legacy (ex: steamapps\\common\\csgo legacy)";
                lblUpdateStatus.ForeColor = AccentRed;
                btnCheck.Enabled = true;
                isBusy = false;
                return;
            }

            lblUpdateStatus.Text = "Vérification des mises à jour sur GitHub...";
            lblUpdateStatus.ForeColor = TextMuted;

            try
            {
                string manifestJson;
                using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                {
                    wc.Headers.Add("User-Agent", "ReliveGOLauncher/" + LAUNCHER_VERSION);
                    manifestJson = await wc.DownloadStringTaskAsync(MANIFEST_URL + "?t=" + DateTime.UtcNow.Ticks);
                }

                var ser = new JavaScriptSerializer();
                currentManifest = ser.Deserialize<ManifestData>(manifestJson);
                if (currentManifest == null || currentManifest.files == null)
                    throw new Exception("manifest.json invalide.");

                await Task.Run(() =>
                {
                    foreach (var f in currentManifest.files)
                    {
                        string localPath = Path.Combine(gameDir, f.path.Replace('/', '\\'));
                        string localHash = ComputeSha256(localPath);
                        if (!string.Equals(localHash, f.sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            outdatedFiles.Add(f);
                        }
                    }
                });

                if (outdatedFiles.Count == 0)
                {
                    lblUpdateStatus.Text = string.Format("✓ Jeu à jour (v{0}) — Tous les fichiers vérifiés (SHA-256 OK)", currentManifest.version);
                    lblUpdateStatus.ForeColor = AccentGreen;
                    progressBar.Value = 100;
                    btnPlay.Text = "JOUER À RELIVE:GO";
                    btnPlay.BackColor = AccentGreen;
                    btnPlay.Enabled = true;
                }
                else
                {
                    lblUpdateStatus.Text = string.Format("⬇ Mise à jour v{0} disponible ({1} fichier(s) à télécharger)", currentManifest.version, outdatedFiles.Count);
                    lblUpdateStatus.ForeColor = AccentGold;
                    btnPlay.Text = string.Format("METTRE À JOUR ({0} FICHIER(S)) & JOUER", outdatedFiles.Count);
                    btnPlay.BackColor = AccentGold;
                    btnPlay.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                lblUpdateStatus.Text = "⚠ Erreur vérification GitHub : " + ex.Message;
                lblUpdateStatus.ForeColor = AccentRed;
                // Allow offline launch if csgo.exe already exists
                if (File.Exists(Path.Combine(gameDir, "csgo.exe")))
                {
                    btnPlay.Text = "LANCER QUAND MÊME";
                    btnPlay.BackColor = AccentGold;
                    btnPlay.Enabled = true;
                }
            }
            finally
            {
                btnCheck.Enabled = true;
                isBusy = false;
            }
        }

        private async void OnPlayClick(object sender, EventArgs e)
        {
            if (isBusy) return;
            SaveSettings();
            string gameDir = txtGameDir.Text.Trim();
            if (!IsValidCsgoDir(gameDir))
            {
                MessageBox.Show("Dossier CS:GO Legacy invalide.", "Relive:GO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (outdatedFiles.Count > 0)
            {
                if (Process.GetProcessesByName("csgo").Length > 0)
                {
                    MessageBox.Show("Ferme CS:GO avant d'appliquer la mise à jour.", "Relive:GO", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                isBusy = true;
                btnPlay.Enabled = false;
                btnCheck.Enabled = false;
                btnBrowse.Enabled = false;

                try
                {
                    int idx = 0;
                    foreach (var f in outdatedFiles)
                    {
                        idx++;
                        lblUpdateStatus.Text = string.Format("⬇ Téléchargement ({0}/{1}) : {2}...", idx, outdatedFiles.Count, f.path);
                        lblUpdateStatus.ForeColor = AccentGold;

                        string dest = Path.Combine(gameDir, f.path.Replace('/', '\\'));
                        string destDir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                        if (f.backup && File.Exists(dest) && !File.Exists(dest + ".bak"))
                        {
                            File.Copy(dest, dest + ".bak", false);
                        }

                        string tmpFile = dest + ".dl";
                        using (var wc = new WebClient())
                        {
                            wc.Headers.Add("User-Agent", "ReliveGOLauncher/" + LAUNCHER_VERSION);
                            int basePct = (int)(((idx - 1) * 100.0) / outdatedFiles.Count);
                            double slice = 100.0 / outdatedFiles.Count;
                            wc.DownloadProgressChanged += (s, ev) =>
                            {
                                int pct = Math.Min(100, basePct + (int)(ev.ProgressPercentage * slice / 100.0));
                                if (IsHandleCreated) BeginInvoke(new Action(() => progressBar.Value = pct));
                            };
                            await wc.DownloadFileTaskAsync(new Uri(f.url), tmpFile);
                        }

                        string dlHash = ComputeSha256(tmpFile);
                        if (!string.Equals(dlHash, f.sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(tmpFile);
                            throw new Exception("Hash SHA-256 invalide pour " + f.path);
                        }

                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(tmpFile, dest);
                    }

                    outdatedFiles.Clear();
                    progressBar.Value = 100;
                    lblUpdateStatus.Text = "✓ Mise à jour terminée ! Lancement de Relive:GO...";
                    lblUpdateStatus.ForeColor = AccentGreen;
                    btnPlay.Text = "JOUER À RELIVE:GO";
                    btnPlay.BackColor = AccentGreen;
                }
                catch (Exception ex)
                {
                    lblUpdateStatus.Text = "⚠ Échec mise à jour : " + ex.Message;
                    lblUpdateStatus.ForeColor = AccentRed;
                    MessageBox.Show("Échec du téléchargement :\n" + ex.Message, "Relive:GO", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    btnPlay.Enabled = true;
                    btnCheck.Enabled = true;
                    btnBrowse.Enabled = true;
                    isBusy = false;
                    return;
                }

                btnPlay.Enabled = true;
                btnCheck.Enabled = true;
                btnBrowse.Enabled = true;
                isBusy = false;
            }

            // Launch CS:GO
            try
            {
                string exePath = Path.Combine(gameDir, "csgo.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = txtLaunchArgs.Text.Trim(),
                    WorkingDirectory = gameDir,
                    UseShellExecute = true
                };
                Process.Start(psi);
                lblUpdateStatus.Text = "✓ CS:GO Legacy (Relive:GO) lancé !";
                lblUpdateStatus.ForeColor = AccentGreen;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible de lancer csgo.exe :\n" + ex.Message, "Relive:GO", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string ComputeSha256(string filePath)
        {
            if (!File.Exists(filePath)) return "";
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(64);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        [STAThread]
        public static void Main()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
        }
    }
}

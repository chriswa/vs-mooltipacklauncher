using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace VSAutoModLauncher
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private AppData appData;

        private void LogEnvDir(string dirId, string envVar, string resolvedDir)
        {
            console.AppendText($"{dirId}: {resolvedDir}" + Environment.NewLine);
            if (Environment.GetEnvironmentVariable(envVar) != null)
            {
                console.AppendText($"  - specified by ENV \"{envVar}\"" + Environment.NewLine);
            }
            else
            {
                console.AppendText($"  - defaulted, can be overriden with ENV \"{envVar}\"" + Environment.NewLine);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            LogEnvDir("GameInstallDir", "VINTAGE_STORY", Launcher.InstallDir);
            LogEnvDir("GameDataDir", "VINTAGE_STORY_DATA", Launcher.DataDir);
            LogEnvDir("MooltiPackDir", "MOOLTIPACK_DATA", Launcher.ConfigDir);
            console.AppendText($"Loading Server List from {AppDataFilePath}" + Environment.NewLine);

            appData = File.Exists(ResolvePath(AppDataFilePath)) ? JsonConvert.DeserializeObject<AppData>(File.ReadAllText(ResolvePath(AppDataFilePath))) : new AppData();
            foreach (var server in appData.serverList)
            {
                this.listBox.Items.Add(server.name);
            }
            if (this.listBox.Items.Contains(appData.lastServerName))
            {
                this.listBox.SelectedItem = appData.lastServerName;
                this.deleteButton.Enabled = true;
            }

        }

        private void SaveServerList()
        {
            Directory.CreateDirectory(ResolvePath($"{Launcher.ConfigDir}"));
            File.WriteAllText(ResolvePath(AppDataFilePath), JsonConvert.SerializeObject(appData, Formatting.Indented));
        }

        private void connectButton_Click(object sender, EventArgs e)
        {
            this.connectButton.Enabled = false;

            bool didUpdate = false;
            foreach (var server in appData.serverList)
            {
                if (server.name == this.name.Text)
                {
                    server.host = this.host.Text;
                    server.password = this.password.Text;
                    didUpdate = true;
                }
            }
            if (!didUpdate)
            {
                appData.serverList.Insert(0, new ServerDetails() { name = this.name.Text, host = this.host.Text, password = this.password.Text });
                this.listBox.Items.Insert(0, this.name.Text);
                this.listBox.SelectedIndex = 0;
            }
            appData.lastServerName = this.name.Text;
            SaveServerList();

            var launcher = new Launcher(this.name.Text, this.console);
            launcher.Launch(this.host.Text, this.password.Text);

            this.connectButton.Enabled = true;
        }

        private string AppDataFilePath { get { return $"{Launcher.ConfigDir}/appData.json"; } }

        private string ResolvePath(string path)
        {
            return Environment.ExpandEnvironmentVariables(path);
        }

        private void listBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            foreach (var server in appData.serverList)
            {
                if ((string)this.listBox.SelectedItem == server.name)
                {
                    this.name.Text = server.name;
                    this.host.Text = server.host;
                    this.password.Text = server.password;
                }
            }
        }

        private void name_TextChanged(object sender, EventArgs e)
        {
            if (this.listBox.Items.Contains(this.name.Text))
            {
                this.listBox.SelectedItem = this.name.Text;
                this.deleteButton.Enabled = true;
            }
            else
            {
                this.listBox.ClearSelected();
                this.deleteButton.Enabled = false;
            }
        }

        private void deleteButton_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure to delete this item?", "Delete", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                var launcher = new Launcher(this.name.Text, this.console);
                if (MessageBox.Show($"Do you also want to delete the {launcher.ServerDir} directory associated with this server?", "Delete", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    launcher.DeleteServerDir();
                }
                this.listBox.Items.Remove(this.name.Text);
                appData.serverList.RemoveAll((server) => server.name == this.name.Text);
                SaveServerList();
                this.name.Text = "";
                this.host.Text = "";
                this.password.Text = "";
            }
        }
    }
}

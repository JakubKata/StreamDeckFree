using System;
using System.IO.Ports;
using System.Windows.Forms;
using SuchByte.MacroDeck.Plugins;

namespace StreamDeckFree
{
    public class ConfigWindow : Form
    {
        private ComboBox _portComboBox;
        private Button _saveButton;
        private MacroDeckPlugin _plugin;

        public ConfigWindow(MacroDeckPlugin plugin)
        {
            _plugin = plugin;

            Text = "CYD Device Configuration";
            Width = 350;
            Height = 150;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label label = new Label()
            {
                Left = 20,
                Top = 22,
                Text = "Select COM Port:"
            };

            _portComboBox = new ComboBox()
            {
                Left = 140,
                Top = 20,
                Width = 150
            };
            
            try
            {
                _portComboBox.Items.AddRange(SerialPort.GetPortNames());
            }
            catch (Exception)
            {
                for (int i = 1; i <= 10; i++)
                {
                    _portComboBox.Items.Add($"COM{i}");
                }
            }

            string savedPort = PluginConfiguration.GetValue(_plugin, "CydPort");
            if (!string.IsNullOrEmpty(savedPort) && _portComboBox.Items.Contains(savedPort))
            {
                _portComboBox.SelectedItem = savedPort;
            }

            _saveButton = new Button()
            {
                Text = "Save",
                Left = 140,
                Top = 60,
                Width = 150
            };
            _saveButton.Click += HandleSaveButtonClick;

            Controls.Add(label);
            Controls.Add(_portComboBox);
            Controls.Add(_saveButton);
        }

        private void HandleSaveButtonClick(object sender, EventArgs e)
        {
            if (_portComboBox.SelectedItem != null)
            {
                PluginConfiguration.SetValue(_plugin, "CydPort", _portComboBox.SelectedItem.ToString());
            }
            Close();
        }
    }
}
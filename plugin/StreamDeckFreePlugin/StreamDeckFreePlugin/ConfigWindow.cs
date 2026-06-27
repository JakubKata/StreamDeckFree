using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.Plugins;

namespace StreamDeckFree
{
    public class ConfigWindow : Form
    {
        private readonly ComboBox _portComboBox;
        private readonly Button _saveButton;
        private readonly Button _refreshButton;
        private readonly MacroDeckPlugin _plugin;

        public ConfigWindow(MacroDeckPlugin plugin)
        {
            _plugin = plugin;

            Text = "CYD Device Configuration";
            Width = 430;
            Height = 185;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label label = new Label
            {
                Left = 20,
                Top = 22,
                Width = 120,
                Text = "ESP32 COM port:"
            };

            _portComboBox = new ComboBox
            {
                Left = 145,
                Top = 18,
                Width = 130,
                DropDownStyle = ComboBoxStyle.DropDown
            };

            _refreshButton = new Button
            {
                Text = "Odśwież",
                Left = 285,
                Top = 17,
                Width = 90
            };
            _refreshButton.Click += (sender, args) => LoadPorts();

            Label hint = new Label
            {
                Left = 20,
                Top = 55,
                Width = 360,
                Height = 35,
                Text = "Możesz też wpisać ręcznie, np. COM7. Ten wariant nie używa System.IO.Ports."
            };

            _saveButton = new Button
            {
                Text = "Zapisz",
                Left = 145,
                Top = 95,
                Width = 230
            };
            _saveButton.Click += HandleSaveButtonClick;

            Controls.Add(label);
            Controls.Add(_portComboBox);
            Controls.Add(_refreshButton);
            Controls.Add(hint);
            Controls.Add(_saveButton);

            LoadPorts();
        }

        private void LoadPorts()
        {
            string savedPort = PluginConfiguration.GetValue(_plugin, "CydPort");
            string currentText = !string.IsNullOrWhiteSpace(_portComboBox.Text) ? _portComboBox.Text : savedPort;

            _portComboBox.Items.Clear();

            foreach (string portName in Win32SerialPort.GetPortNames())
            {
                _portComboBox.Items.Add(portName);
            }

            if (!string.IsNullOrWhiteSpace(savedPort) && !_portComboBox.Items.Contains(savedPort))
            {
                _portComboBox.Items.Insert(0, savedPort);
            }

            if (!string.IsNullOrWhiteSpace(currentText))
            {
                _portComboBox.Text = currentText.Trim().ToUpperInvariant();
            }
            else if (_portComboBox.Items.Count > 0)
            {
                _portComboBox.SelectedIndex = 0;
            }
        }

        private void HandleSaveButtonClick(object sender, EventArgs e)
        {
            string selectedPort = _portComboBox.Text?.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(selectedPort))
            {
                PluginConfiguration.SetValue(_plugin, "CydPort", selectedPort);
            }
            Close();
        }
    }
}

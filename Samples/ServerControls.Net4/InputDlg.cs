using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace Opc.Ua.Server.Controls
{
    public partial class InputDlg : Form
    {
        public InputDlg()
        {
            InitializeComponent();
        }

        public static string Show(string text, bool hideInput)
        {
            var inputDlg = new InputDlg();
            if (hideInput)
                inputDlg.textBoxInput.PasswordChar = '*';
            inputDlg.labelText.Text = text;
            inputDlg.ShowDialog();
            return inputDlg.textBoxInput.Text;
        }

        private void buttonOk_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}


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
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Server.Controls
{
    public partial class InputDlg : SampleForm
    {
        public InputDlg()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Asks the user for a line of text.
        /// </summary>
        /// <param name="windows">The window factory of the sample.</param>
        /// <param name="text">The prompt shown above the input.</param>
        /// <param name="hideInput">Whether what is typed is a secret.</param>
        public static string Show(IWindowFactory windows, string text, bool hideInput)
        {
            var inputDlg = windows.Create<InputDlg>();
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


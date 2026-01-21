using System.IO;
using System.Windows;

namespace ProtoTestTool
{
    public partial class WorkspaceDialog : Window
    {
        public string? SelectedPath { get; private set; }

        public WorkspaceDialog()
        {
            InitializeComponent();
        }

        public WorkspaceDialog(string? initialPath) : this()
        {
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                PathTextBox.Text = initialPath;
                OkBtn.IsEnabled = Directory.Exists(initialPath);
            }
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Workspace 폴더 선택"
            };

            if (!string.IsNullOrWhiteSpace(PathTextBox.Text) && Directory.Exists(PathTextBox.Text))
            {
                dialog.InitialDirectory = PathTextBox.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                PathTextBox.Text = dialog.FolderName;
                OkBtn.IsEnabled = true;
            }
        }

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            SelectedPath = PathTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

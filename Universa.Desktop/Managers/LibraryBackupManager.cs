using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Universa.Desktop.Dialogs;

namespace Universa.Desktop.Managers
{
    public class LibraryBackupManager
    {
        private readonly Window _owner;
        private readonly string _libraryPath;

        public LibraryBackupManager(Window owner, string libraryPath)
        {
            _owner = owner;
            _libraryPath = libraryPath;
        }

        public async Task CreateBackupAsync()
        {
            if (string.IsNullOrEmpty(_libraryPath) || !Directory.Exists(_libraryPath))
            {
                MessageBox.Show("Library path is not configured or doesn't exist.", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Title = "Save Library Backup",
                Filter = "Zip files (*.zip)|*.zip",
                DefaultExt = ".zip",
                FileName = $"Universa_Library_Backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var progressDialog = new ProgressDialog
                {
                    Title = "Creating Backup",
                    Message = "Creating library backup...",
                    Owner = _owner
                };

                // Show the progress dialog before starting the backup
                progressDialog.Show();

                try
                {
                    await Task.Run(() =>
                    {
                        CreateBackupZip(_libraryPath, saveDialog.FileName, progressDialog);
                    });

                    _owner.Dispatcher.Invoke(() =>
                    {
                        progressDialog.Close();
                        MessageBox.Show("Library backup created successfully!", "Backup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    _owner.Dispatcher.Invoke(() =>
                    {
                        progressDialog.Close();
                        MessageBox.Show($"Error creating backup: {ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
        }

        private void CreateBackupZip(string sourcePath, string zipPath, ProgressDialog progress)
        {
            try
            {
                // Create a temporary directory for the backup
                var tempDir = Path.Combine(Path.GetTempPath(), "UniversaBackup_" + Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                // Copy directories
                foreach (var dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
                {
                    if (dirPath.Contains("\\.git")) continue;
                    
                    var relativePath = Path.GetRelativePath(sourcePath, dirPath);
                    var targetDir = Path.Combine(tempDir, relativePath);
                    Directory.CreateDirectory(targetDir);
                }

                // Copy files
                foreach (var filePath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
                {
                    if (filePath.Contains("\\.git")) continue;
                    
                    var relativePath = Path.GetRelativePath(sourcePath, filePath);
                    var targetPath = Path.Combine(tempDir, relativePath);
                    File.Copy(filePath, targetPath);
                }

                // Create zip file
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, zipPath);

                // Clean up temp directory
                Directory.Delete(tempDir, true);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
} 
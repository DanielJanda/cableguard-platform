using System.Windows;

namespace CableGuard.ControlCenter.Views;

public partial class CredentialsDialog : Window
{
    public CredentialsDialog(string cameraName, string credentialRef, string currentUsername, bool hasPassword)
    {
        InitializeComponent();
        HeaderText.Text = cameraName;
        RefText.Text = $"Credential ref: {credentialRef}";
        UsernameBox.Text = currentUsername;
        PasswordHint.Text = hasPassword ? "Aktuální heslo: ******** (vyplň jen při změně)" : "Heslo zatím není uloženo.";
    }

    public string EnteredUsername => UsernameBox.Text.Trim();
    public string EnteredPassword => PasswordBox.Password;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EnteredUsername) || EnteredPassword.Length == 0)
        {
            MessageBox.Show("Vyplň username i password.", "Camera credentials",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}

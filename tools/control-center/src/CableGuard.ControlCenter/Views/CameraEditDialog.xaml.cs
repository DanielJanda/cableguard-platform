using System.Windows;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Views;

public partial class CameraEditDialog : Window
{
    public CameraEditDialog(CameraEntry camera)
    {
        InitializeComponent();
        IdBox.Text = camera.CameraId;
        NameBox.Text = camera.DisplayName;
        HostBox.Text = camera.Host;
        PortBox.Text = camera.RtspPort.ToString();
        ProfileBox.Text = camera.Profile;
        TransportBox.Text = string.IsNullOrWhiteSpace(camera.Transport) ? "tcp" : camera.Transport;
        PathBox.Text = camera.MediaMtxPath;
        CredBox.Text = camera.CredentialRef;
        SiteBox.Text = camera.SiteId;
        StationBox.Text = camera.StationId;
        Result = Clone(camera);
    }

    public CameraEntry Result { get; private set; }

    private static CameraEntry Clone(CameraEntry c) => new()
    {
        CameraId = c.CameraId, DisplayName = c.DisplayName, SiteId = c.SiteId, StationId = c.StationId,
        Host = c.Host, RtspPort = c.RtspPort, Profile = c.Profile, Transport = c.Transport,
        Enabled = c.Enabled, CredentialRef = c.CredentialRef, MediaMtxPath = c.MediaMtxPath,
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port))
        {
            MessageBox.Show("Neplatný RTSP port.");
            return;
        }
        Result = new CameraEntry
        {
            CameraId = IdBox.Text.Trim(),
            DisplayName = NameBox.Text.Trim(),
            Host = HostBox.Text.Trim(),
            RtspPort = port,
            Profile = ProfileBox.Text.Trim(),
            Transport = TransportBox.Text.Trim().ToLowerInvariant(),
            MediaMtxPath = PathBox.Text.Trim(),
            CredentialRef = CredBox.Text.Trim(),
            SiteId = SiteBox.Text.Trim(),
            StationId = StationBox.Text.Trim(),
            Enabled = true,
        };
        DialogResult = true;
    }
}

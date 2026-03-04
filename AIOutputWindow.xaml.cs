using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MizuLauncher
{
    public partial class AIOutputWindow : Window
    {
        public AIOutputWindow()
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        }

        public void SetResponse(string response)
        {
            TxtAIResponse.Text = response;
            Scroller.ScrollToEnd();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        public void ShowAtPosition(double left, double top)
        {
            this.Left = left;
            this.Top = top - this.Height - 10;
            this.Show();
            this.Activate();
        }
    }
}

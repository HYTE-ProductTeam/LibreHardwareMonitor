using System.Windows;
using System.Windows.Controls;
using LibreHardwareMonitor.Hardware.PawnIO;

namespace pawnIO_test_app.Components
{
    /// <summary>
    /// Interaction logic for SideMenu.xaml
    /// </summary>
    public partial class SideMenu : UserControl
    {
        private MenuButton _selectedButton;
        public Action<MenuButton> ButtonClicked { get; set; }

        public SideMenu()
        {
            InitializeComponent();
            FamilyTB.Text = $"Family {PawnIoBootstrap.Family}";
            ModelTB.Text = $"Model {PawnIoBootstrap.Model}";
        }

        private void Info_Button_Click(object sender, RoutedEventArgs e)
        {
            _selectedButton = MenuButton.Info;
            ButtonClicked?.Invoke(_selectedButton);
        }

        private void Fan_Button_Click(object sender, RoutedEventArgs e)
        {
            _selectedButton = MenuButton.Fan;
            ButtonClicked?.Invoke(_selectedButton);
        }
    }

    public enum MenuButton
    {
        Info,
        Fan,
    }
}

using System.Windows;
using DMS.ViewModels;

namespace DMS.Views
{
    public partial class UsernameWindow : Window
    {
        public UsernameWindow(UsernameViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseAction = Close;
        }
    }
}

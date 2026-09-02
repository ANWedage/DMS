using System.Windows.Controls;

namespace DMS.Views
{
    public partial class UserSectionPage : Page
    {
        public UserSectionPage(string sectionName)
        {
            InitializeComponent();
            SectionTitleText.Text = sectionName;
        }
    }
}

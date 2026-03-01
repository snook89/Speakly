using System.Windows.Controls;

namespace Speakly.Pages
{
    public partial class GeneralPage : UserControl
    {
        public GeneralPage()
        {
            InitializeComponent();
            DataContext = App.ViewModel;
        }
    }
}

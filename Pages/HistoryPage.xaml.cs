using System.Windows.Controls;

namespace Speakly.Pages
{
    public partial class HistoryPage : UserControl
    {
        public HistoryPage()
        {
            InitializeComponent();
            DataContext = App.ViewModel;
        }
    }
}

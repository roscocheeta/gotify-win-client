namespace GotifyWin.Views;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        if (DataContext is ViewModels.SettingsViewModel vm)
        {
            vm.RequestScrollToAbout += OnRequestScrollToAbout;
        }
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ViewModels.SettingsViewModel oldVm)
        {
            oldVm.RequestScrollToAbout -= OnRequestScrollToAbout;
        }
        if (e.NewValue is ViewModels.SettingsViewModel newVm)
        {
            newVm.RequestScrollToAbout -= OnRequestScrollToAbout;
            newVm.RequestScrollToAbout += OnRequestScrollToAbout;
        }
    }

    private void OnRequestScrollToAbout()
    {
        if (!IsLoaded)
        {
            System.Windows.RoutedEventHandler? onLoaded = null;
            onLoaded = (s, e) =>
            {
                Loaded -= onLoaded;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AboutSectionBorder?.BringIntoView();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };
            Loaded += onLoaded;
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            AboutSectionBorder?.BringIntoView();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
}

using System.Windows;
using TheatreCRM.App.Models;

namespace TheatreCRM.App;

public partial class SavedViewWindow : Window
{
    private readonly SavedView _view;

    public SavedViewWindow(SavedView view)
    {
        InitializeComponent();
        _view = view;
        NameBox.Text = view.Name;
        ShowInSidebarBox.IsChecked = view.IsShownInSidebar;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Название вкладки обязательно.", "Сохранить вкладку", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _view.Name = NameBox.Text.Trim();
        _view.IsShownInSidebar = ShowInSidebarBox.IsChecked == true;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

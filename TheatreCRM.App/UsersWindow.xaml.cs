using System.Windows;
using System.Windows.Controls;
using TheatreCRM.App.Data;
using TheatreCRM.App.Models;
using TheatreCRM.App.Services;

namespace TheatreCRM.App;

public partial class UsersWindow : Window
{
    private readonly TheatreRepository _repository;
    private readonly AppPaths _paths;

    public UsersWindow(TheatreRepository repository, AppPaths paths)
    {
        InitializeComponent();
        _repository = repository;
        _paths = paths;
        RefreshUsers();
    }

    private void RefreshUsers()
    {
        UsersList.ItemsSource = null;
        UsersList.ItemsSource = _repository.GetUsers();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Введите имя пользователя.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Users (Id, FullName, CreatedAt, UpdatedAt) VALUES ($id, $name, $now, $now)";
        cmd.Parameters.AddWithValue("$id", System.Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$now", System.DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        NameBox.Text = "";
        RefreshUsers();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (UsersList.SelectedItem is not DirectoryUser selectedUser)
        {
            MessageBox.Show("Выберите пользователя из списка.", "Удаление", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"Удалить пользователя \"{selectedUser.FullName}\"?",
            "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Users WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", selectedUser.Id);
        cmd.ExecuteNonQuery();

        RefreshUsers();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
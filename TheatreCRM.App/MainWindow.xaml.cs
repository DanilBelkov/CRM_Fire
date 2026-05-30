using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheatreCRM.App.Data;
using TheatreCRM.App.Models;
using TheatreCRM.App.Services;

namespace TheatreCRM.App;

public partial class MainWindow : Window
{
    private readonly AppPaths _paths;
    private readonly TheatreRepository _repository;
    private readonly ExcelDataService _excelDataService;
    private readonly BackupService _backupService;
    private readonly ObservableCollection<CatalogItem> _items = [];
    private readonly Dictionary<CatalogItemType, Dictionary<string, string>> _filters = [];
    private CatalogItemType _currentType = CatalogItemType.Clothing;
    private bool _isTrashMode;

    public MainWindow()
    {
        InitializeComponent();
        _paths = new AppPaths();
        _paths.EnsureCreated();
        _repository = new TheatreRepository(_paths.DatabasePath);
        _repository.Initialize();
        _excelDataService = new ExcelDataService(_repository, _paths);
        _backupService = new BackupService(_paths);
        ItemsList.ItemsSource = _items;
        LoadSection(CatalogItemType.Clothing);
        _ = TryCreateAutomaticBackup();
    }

    private void SectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CatalogItemType type })
        {
            LoadSection(type);
        }
    }

    private void TrashButton_Click(object sender, RoutedEventArgs e)
    {
        _isTrashMode = true;
        SectionTitle.Text = "Корзина";
        SectionSubtitle.Text = "";
        CreateButton.Visibility = Visibility.Collapsed;
        TrashButtons.Visibility = Visibility.Visible;
        MainButtons.Visibility = Visibility.Collapsed;
        UpdateFilterButtonHighlight();
        RefreshList();
        ClearDetails();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var includePhotos = MessageBox.Show("Добавить в Excel пути к фотографиям?", "Экспорт Excel", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        var path = _excelDataService.ExportDatabase(includePhotos);
        MessageBox.Show($"Экспорт готов:\n{path}", "Экспорт Excel", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите Excel-файл",
            Filter = "Excel или CSV|*.xlsx;*.csv|Excel|*.xlsx|CSV|*.csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var count = _excelDataService.ImportCatalog(dialog.FileName);
        RefreshList();
        MessageBox.Show($"Импортировано карточек: {count}", "Импорт Excel", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите backup",
            Filter = "Backup zip|*.zip"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await Task.Run(() => _backupService.RestoreBackup(dialog.FileName));
        _repository.Initialize();
        _filters.Clear();
        UpdateFilterButtonHighlight();
        RefreshList();
        MessageBox.Show("Backup восстановлен. Перед восстановлением создана текущая резервная копия.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ManageUsersButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new UsersWindow(_repository, _paths) { Owner = this };
        window.ShowDialog();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTrashMode)
            return;

        var currentFilter = _filters.TryGetValue(_currentType, out var existing) ? existing : null;
        var window = new FilterWindow(_repository, _currentType, currentFilter) { Owner = this };
        if (window.ShowDialog() == true && window.FilterValues is not null)
        {
            _filters[_currentType] = window.FilterValues;
            UpdateFilterButtonHighlight();
            RefreshList();
        }
    }

    private void UpdateFilterButtonHighlight()
    {
        if (_isTrashMode || !_filters.TryGetValue(_currentType, out var filterValues) || filterValues.Count == 0)
        {
            FilterButton.Background = new SolidColorBrush(Color.FromRgb(29, 41, 37));
            FilterButton.BorderBrush = new SolidColorBrush(Color.FromRgb(43, 58, 53));
            FilterButton.Foreground = new SolidColorBrush(Color.FromRgb(238, 245, 241));
            return;
        }

        FilterButton.Background = new SolidColorBrush(Color.FromRgb(33, 166, 107));
        FilterButton.BorderBrush = new SolidColorBrush(Color.FromRgb(44, 199, 127));
        FilterButton.Foreground = new SolidColorBrush(Color.FromRgb(6, 17, 12));
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        _filters.Remove(_currentType);
        UpdateFilterButtonHighlight();
        RefreshList();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTrashMode)
        {
            return;
        }

        var item = new CatalogItem { Type = _currentType };
        OpenEditor(item, isEditing: true);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is CatalogItem item)
        {
            OpenEditor(item);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is not CatalogItem item)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Удалить карточку \"{item.Title}\"? Она будет скрыта из списка.",
            "Удаление карточки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _repository.SoftDelete(item.Id);
        RefreshList();
        ClearDetails();
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is CatalogItem item)
        {
            ShowDetails(item);
        }
        else
        {
            ClearDetails();
        }
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsList.SelectedItem is CatalogItem item && !_isTrashMode)
        {
            OpenEditor(item);
        }
    }

    private void RelatedItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CatalogSummary summary })
        {
            return;
        }

        LoadSection(summary.Type);
        var selected = _items.FirstOrDefault(item => item.Id == summary.Id);
        if (selected is not null)
        {
            ItemsList.SelectedItem = selected;
            ItemsList.ScrollIntoView(selected);
        }
    }

    private void LoadSection(CatalogItemType type)
    {
        _isTrashMode = false;
        _currentType = type;
        SectionTitle.Text = type.ToRussian();
        CreateButton.Visibility = Visibility.Visible;
        CreateButton.Content = $"Создать";
        TrashButtons.Visibility = Visibility.Collapsed;
        MainButtons.Visibility = Visibility.Visible;
        SearchBox.Text = "";
        UpdateFilterButtonHighlight();
        RefreshList();
        ClearDetails();
    }

    private void RefreshList()
    {
        _items.Clear();
        if (_isTrashMode)
        {
            foreach (var deletedItem in _repository.GetTrash())
            {
                _items.Add(deletedItem);
            }
            SectionSubtitle.Text = $"{_items.Count} карточек в корзине";
            return;
        }

        var searchText = SearchBox.Text.Trim();
        var items = _repository.Search(_currentType, searchText);

        if (_filters.TryGetValue(_currentType, out var filterValues) && filterValues.Count > 0)
        {
            items = items.Where(item => MatchFilter(item, filterValues)).ToList();
        }

        foreach (var item in items)
        {
            _items.Add(item);
        }

        SectionSubtitle.Text = $"{_items.Count} карточек";
    }

    private static bool MatchFilter(CatalogItem item, Dictionary<string, string> filterValues)
    {
        foreach (var (field, value) in filterValues)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            // Фильтрация по связанным сущностям
            if (field == "RelatedClothing")
            {
                var matching = item.RelatedItems.Any(r => r.Type == CatalogItemType.Clothing && r.Title.Contains(value, StringComparison.CurrentCultureIgnoreCase));
                if (!matching) return false;
                continue;
            }
            if (field == "RelatedProps")
            {
                var matching = item.RelatedItems.Any(r => r.Type == CatalogItemType.Prop && r.Title.Contains(value, StringComparison.CurrentCultureIgnoreCase));
                if (!matching) return false;
                continue;
            }
            if (field == "RelatedCostumes")
            {
                var matching = item.RelatedItems.Any(r => r.Type == CatalogItemType.Costume && r.Title.Contains(value, StringComparison.CurrentCultureIgnoreCase));
                if (!matching) return false;
                continue;
            }
            if (field == "RelatedPerformances")
            {
                var matching = item.RelatedItems.Any(r => r.Type == CatalogItemType.Performance && r.Title.Contains(value, StringComparison.CurrentCultureIgnoreCase));
                if (!matching) return false;
                continue;
            }
            if (field == "RelatedSectors")
            {
                var matching = item.RelatedItems.Any(r => r.Type == CatalogItemType.Sector && r.Title.Contains(value, StringComparison.CurrentCultureIgnoreCase));
                if (!matching) return false;
                continue;
            }

            var fieldValue = field switch
            {
                "Title" => item.Title,
                "Description" => item.Description,
                "InventoryNumber" => item.InventoryNumber,
                "StorageLocation" => item.StorageLocation,
                "Condition" => item.Condition,
                "ResponsiblePerson" => item.ResponsiblePerson,
                "Notes" => item.Notes,
                "Size" => item.Size,
                "Color" => item.Color,
                "Material" => item.Material,
                "CareInstructions" => item.CareInstructions,
                "Dimensions" => item.Dimensions,
                "Weight" => item.Weight,
                "Fragility" => item.Fragility,
                "CharacterName" => item.CharacterName,
                "ActorName" => item.ActorName,
                "Director" => item.Director,
                "PremiereDate" => item.PremiereDate,
                "Season" => item.Season,
                "Movement" => item.Movement,
                _ => ""
            };

            if (!fieldValue.Contains(value, StringComparison.CurrentCultureIgnoreCase))
                return false;
        }
        return true;
    }

    private void OpenEditor(CatalogItem item, bool isEditing = false)
    {
        var editor = new EntityEditorWindow(_repository, _paths, item, isEditing: isEditing) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            RefreshList();
            var saved = _items.FirstOrDefault(x => x.Id == item.Id);
            if (saved is not null)
            {
                ItemsList.SelectedItem = saved;
            }
        }
    }

    private void ShowDetails(CatalogItem item)
    {
        DetailsTitle.Text = item.Title;
        DetailsDescription.Text = string.IsNullOrWhiteSpace(item.Description) ? "Описание не заполнено." : item.Description;

        var metaLines = new List<string>
        {
            $"Раздел: {item.Type.ToRussian()}",
            $"Инвентарный номер: {ValueOrDash(item.InventoryNumber)}",
            $"Место хранения: {ValueOrDash(item.StorageLocation)}",
            $"Состояние: {ValueOrDash(item.Condition)}",
            $"Ответственный: {ValueOrDash(item.ResponsiblePerson)}",
        };

        if (item.Type is CatalogItemType.Clothing or CatalogItemType.Prop or CatalogItemType.Costume)
        {
            if (!string.IsNullOrWhiteSpace(item.Movement))
                metaLines.Add($"Передвижение: {item.Movement}");
        }
        if (item.Type == CatalogItemType.Prop)
        {
            if (!string.IsNullOrWhiteSpace(item.Dimensions))
                metaLines.Add($"Габариты: {item.Dimensions}");
            if (!string.IsNullOrWhiteSpace(item.Fragility))
                metaLines.Add($"Хрупкость: {item.Fragility}");
        }
        metaLines.Add(item.SectorsText);

        DetailsMeta.Text = string.Join(Environment.NewLine, metaLines);
        RelatedItems.ItemsSource = item.RelatedItems;

        AuditItems.ItemsSource = _repository.GetAuditLog(item.Id);
        HistoryExpander.IsExpanded = false;

        var photos = _repository.GetPhotos(item.Id);
        if (photos.Count > 0)
        {
            SetDetailsImage(photos[0]);
        }
        else
        {
            SetDetailsImage(item.MainPhotoPath);
        }
    }

    private void ClearDetails()
    {
        DetailsTitle.Text = "Выберите карточку";
        DetailsDescription.Text = "";
        DetailsMeta.Text = "";
        RelatedItems.ItemsSource = null;
        AuditItems.ItemsSource = null;
        DetailsImage.Source = null;
        HistoryExpander.IsExpanded = false;
    }

    private void RestoreItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is not CatalogItem item || !_isTrashMode)
        {
            return;
        }

        _repository.RestoreFromTrash(item.Id);
        RefreshList();
        ClearDetails();
    }

    private void PermanentDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is not CatalogItem item || !_isTrashMode)
        {
            return;
        }

        var result = MessageBox.Show($"Окончательно удалить \"{item.Title}\"? Это действие нельзя отменить.", "Удалить навсегда", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _repository.PermanentlyDelete(item.Id);
        RefreshList();
        ClearDetails();
    }

    private void SetDetailsImage(string relativePath)
    {
        DetailsImage.Source = null;
        var absolutePath = _paths.ToAbsolutePhotoPath(relativePath);
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            return;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(absolutePath);
        image.EndInit();
        DetailsImage.Source = image;
    }

    private static string ValueOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;


    private async Task TryCreateAutomaticBackup()
    {
        try
        {
            var todayPrefix = $"theatre-crm-backup-{DateTime.Now:yyyyMMdd}";
            if (!Directory.Exists(_paths.BackupsPath) || !Directory.EnumerateFiles(_paths.BackupsPath, $"{todayPrefix}*.zip").Any())
            {
                await Task.Run(() => _backupService.CreateBackup());
            }
        }
        catch
        {
        }
    }
}
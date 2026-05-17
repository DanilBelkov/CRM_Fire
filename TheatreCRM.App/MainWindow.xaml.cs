using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using TheatreCRM.App.Data;
using TheatreCRM.App.Models;
using TheatreCRM.App.Services;

namespace TheatreCRM.App;

public partial class MainWindow : Window
{
    private readonly AppPaths _paths;
    private readonly TheatreRepository _repository;
    private readonly ObservableCollection<CatalogItem> _items = [];
    private readonly ObservableCollection<SavedView> _savedViews = [];
    private CatalogItemType _currentType = CatalogItemType.Clothing;
    private bool _isLoadingFilters;

    public MainWindow()
    {
        InitializeComponent();
        _paths = new AppPaths();
        _paths.EnsureCreated();
        _repository = new TheatreRepository(_paths.DatabasePath);
        _repository.Initialize();
        ItemsList.ItemsSource = _items;
        SavedViewsList.ItemsSource = _savedViews;
        LoadFilters();
        LoadSavedViews();
        LoadSection(CatalogItemType.Clothing);
    }

    private void SectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CatalogItemType type })
        {
            LoadSection(type);
        }
    }

    private void TagsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new TagManagerWindow(_repository) { Owner = this };
        window.ShowDialog();
        LoadFilters();
        LoadSavedViews();
        RefreshList();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void TagGroupFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingFilters)
        {
            return;
        }

        LoadTagsForSelectedGroup();
        RefreshList();
    }

    private void TagFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoadingFilters)
        {
            RefreshList();
        }
    }

    private void GroupByTagCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoadingFilters)
        {
            RefreshList();
        }
    }

    private void SaveViewButton_Click(object sender, RoutedEventArgs e)
    {
        var tagGroup = TagGroupFilter.SelectedItem as TagGroup;
        var tag = TagFilter.SelectedItem as Tag;
        var view = new SavedView
        {
            SectionType = _currentType,
            TagGroupId = tagGroup?.Id,
            TagId = tag?.Id,
            GroupByTagGroup = GroupByTagCheck.IsChecked == true,
            IsShownInSidebar = true
        };

        var window = new SavedViewWindow(view) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _repository.SaveView(view);
            LoadSavedViews();
        }
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _isLoadingFilters = true;
        TagGroupFilter.SelectedItem = null;
        TagFilter.ItemsSource = null;
        TagFilter.SelectedItem = null;
        GroupByTagCheck.IsChecked = false;
        _isLoadingFilters = false;
        RefreshList();
    }

    private void SavedView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SavedView view })
        {
            ApplySavedView(view);
        }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var item = new CatalogItem { Type = _currentType, Title = _currentType.CreateTitle() };
        OpenEditor(item);
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
        _currentType = type;
        SectionTitle.Text = type.ToRussian();
        CreateButton.Content = $"Создать: {type.CreateTitle().ToLowerInvariant()}";
        SearchBox.Text = "";
        RefreshList();
        ClearDetails();
    }

    private void RefreshList()
    {
        if (_repository is null)
        {
            return;
        }

        _items.Clear();
        var tagGroup = TagGroupFilter.SelectedItem as TagGroup;
        var tag = TagFilter.SelectedItem as Tag;
        var groupId = GroupByTagCheck.IsChecked == true ? tagGroup?.Id : null;
        foreach (var item in _repository.Search(_currentType, SearchBox.Text, groupId, tag?.Id))
        {
            _items.Add(item);
        }

        SectionSubtitle.Text = $"{_items.Count} карточек";
        ApplyGrouping();
    }

    private void OpenEditor(CatalogItem item)
    {
        var editor = new EntityEditorWindow(_repository, _paths, item)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            LoadFilters();
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
        DetailsMeta.Text = string.Join(Environment.NewLine, new[]
        {
            $"Раздел: {item.Type.ToRussian()}",
            $"Инвентарный номер: {ValueOrDash(item.InventoryNumber)}",
            $"Место хранения: {ValueOrDash(item.StorageLocation)}",
            $"Состояние: {ValueOrDash(item.Condition)}",
            $"Ответственный: {ValueOrDash(item.ResponsiblePerson)}",
            $"Теги: {item.TagsText}"
        });
        RelatedItems.ItemsSource = item.RelatedItems;
        SetDetailsImage(item.MainPhotoPath);
    }

    private void ClearDetails()
    {
        DetailsTitle.Text = "Выберите карточку";
        DetailsDescription.Text = "";
        DetailsMeta.Text = "";
        RelatedItems.ItemsSource = null;
        DetailsImage.Source = null;
    }

    private void LoadFilters()
    {
        _isLoadingFilters = true;
        var previousGroupId = (TagGroupFilter.SelectedItem as TagGroup)?.Id;
        TagGroupFilter.ItemsSource = _repository.GetTagGroups();
        TagGroupFilter.SelectedItem = TagGroupFilter.Items.OfType<TagGroup>().FirstOrDefault(group => group.Id == previousGroupId);
        LoadTagsForSelectedGroup();
        _isLoadingFilters = false;
    }

    private void LoadTagsForSelectedGroup()
    {
        var previousTagId = (TagFilter.SelectedItem as Tag)?.Id;
        var group = TagGroupFilter.SelectedItem as TagGroup;
        TagFilter.ItemsSource = group is null ? null : _repository.GetTags(group.Id);
        TagFilter.SelectedItem = TagFilter.Items.OfType<Tag>().FirstOrDefault(tag => tag.Id == previousTagId);
    }

    private void LoadSavedViews()
    {
        _savedViews.Clear();
        foreach (var view in _repository.GetSavedViews(sidebarOnly: true))
        {
            _savedViews.Add(view);
        }
    }

    private void ApplySavedView(SavedView view)
    {
        _isLoadingFilters = true;
        _currentType = view.SectionType;
        SectionTitle.Text = view.SectionType.ToRussian();
        CreateButton.Content = $"Создать: {view.SectionType.CreateTitle().ToLowerInvariant()}";
        SearchBox.Text = "";
        TagGroupFilter.SelectedItem = TagGroupFilter.Items.OfType<TagGroup>().FirstOrDefault(group => group.Id == view.TagGroupId);
        LoadTagsForSelectedGroup();
        TagFilter.SelectedItem = TagFilter.Items.OfType<Tag>().FirstOrDefault(tag => tag.Id == view.TagId);
        GroupByTagCheck.IsChecked = view.GroupByTagGroup;
        _isLoadingFilters = false;
        RefreshList();
        ClearDetails();
    }

    private void ApplyGrouping()
    {
        var view = CollectionViewSource.GetDefaultView(ItemsList.ItemsSource);
        if (view is null)
        {
            return;
        }

        view.GroupDescriptions.Clear();
        if (GroupByTagCheck.IsChecked == true && TagGroupFilter.SelectedItem is not null)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CatalogItem.GroupHeader)));
        }

        view.Refresh();
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
}

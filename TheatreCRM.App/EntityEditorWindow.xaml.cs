using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheatreCRM.App.Data;
using TheatreCRM.App.Models;
using TheatreCRM.App.Services;

namespace TheatreCRM.App;

public partial class EntityEditorWindow : Window
{
    private readonly TheatreRepository _repository;
    private readonly AppPaths _paths;
    private readonly CatalogItem _item;
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly Dictionary<string, ComboBox> _comboFields = [];
    private readonly Dictionary<string, TextBox> _comboReadOnlyFields = [];
    private readonly Dictionary<string, LinkSelectorState> _linkSelectors = [];
    private readonly List<string> _newGalleryPhotos = [];
    private string _selectedSourcePhoto = "";
    private readonly List<CatalogSummary> _allRelatedItems = [];
    private bool _isEditing;

    public EntityEditorWindow(TheatreRepository repository, AppPaths paths, CatalogItem item)
    {
        InitializeComponent();
        _repository = repository;
        _paths = paths;
        _item = item;
        BuildForm();
        LoadValues();
        BuildRelationshipsView();
        SetViewMode();
    }

    private void BuildForm()
    {
        HeaderText.Text = $"{_item.Type.ToRussian()}: карточка";
        Title = HeaderText.Text;
        ResponsibleBox.ItemsSource = _repository.GetUsers();

        // Для сектора скрываем блок не-Sector полей
        if (_item.Type == CatalogItemType.Sector)
            NonSectorFields.Visibility = Visibility.Collapsed;
        else
            NonSectorFields.Visibility = Visibility.Visible;

        // Для спектакля скрываем только инвентарный номер
        if (_item.Type == CatalogItemType.Performance)
            InventoryPanel.Visibility = Visibility.Collapsed;
        else
            InventoryPanel.Visibility = Visibility.Visible;

        // Скрываем привязки редактирования для сектора в режиме редактирования — показываем позже
        AddSpecificFields();
        AddRelationshipSelectors();
    }

    private void AddSpecificFields()
    {
        string GetMovementTooltip() => "Кто? Куда? Дата выдачи? Дата возврата?";

        switch (_item.Type)
        {
            case CatalogItemType.Clothing:
                AddField("Size", "Размер");
                AddField("Color", "Цвет");
                AddField("Material", "Материал");
                AddField("CareInstructions", "Уход");
                AddLargeField("Movement", "Передвижение", GetMovementTooltip());
                break;
            case CatalogItemType.Prop:
                AddComboField("Dimensions", "Габариты", ["Маленький", "Средний", "Большой"]);
                AddField("Weight", "Вес");
                AddField("Material", "Материал");
                AddComboField("Fragility", "Хрупкость", ["Низкая", "Средняя", "Высокая", "Очень высокая"]);
                AddLargeField("Movement", "Передвижение", GetMovementTooltip());
                break;
            case CatalogItemType.Costume:
                AddField("CharacterName", "Персонаж");
                AddField("ActorName", "Актер");
                AddField("CareInstructions", "Уход");
                AddLargeField("Movement", "Передвижение", GetMovementTooltip());
                break;
            case CatalogItemType.Performance:
                AddField("Director", "Режиссер");
                AddField("PremiereDate", "Дата премьеры");
                AddField("Season", "Сезон");
                break;
        }
    }

    private void AddRelationshipSelectors()
    {
        foreach (var config in GetLinkConfigs())
        {
            AddLinkSelector(config);
        }
    }

    private IEnumerable<LinkConfig> GetLinkConfigs()
    {
        return _item.Type switch
        {
            CatalogItemType.Clothing =>
            [
                new("CostumeClothingItems", "Костюмы", CatalogItemType.Costume, "CostumeClothingItems", "CostumeId", "ClothingItemId", false),
                new("PerformanceClothingItems", "Спектакли", CatalogItemType.Performance, "PerformanceClothingItems", "PerformanceId", "ClothingItemId", false),
                new("SectorClothingItems", "Секторы", CatalogItemType.Sector, "SectorClothingItems", "SectorId", "ClothingItemId", false)
            ],
            CatalogItemType.Prop =>
            [
                new("CostumeProps", "Костюмы", CatalogItemType.Costume, "CostumeProps", "CostumeId", "PropId", false),
                new("PerformanceProps", "Спектакли", CatalogItemType.Performance, "PerformanceProps", "PerformanceId", "PropId", false),
                new("SectorProps", "Секторы", CatalogItemType.Sector, "SectorProps", "SectorId", "PropId", false)
            ],
            CatalogItemType.Costume =>
            [
                new("CostumeClothingItems", "Одежда на подбор", CatalogItemType.Clothing, "CostumeClothingItems", "CostumeId", "ClothingItemId", true),
                new("CostumeProps", "Реквизит", CatalogItemType.Prop, "CostumeProps", "CostumeId", "PropId", true),
                new("PerformanceCostumes", "Спектакли", CatalogItemType.Performance, "PerformanceCostumes", "PerformanceId", "CostumeId", false),
                new("SectorCostumes", "Секторы", CatalogItemType.Sector, "SectorCostumes", "SectorId", "CostumeId", false)
            ],
            CatalogItemType.Performance =>
            [
                new("PerformanceCostumes", "Костюмы", CatalogItemType.Costume, "PerformanceCostumes", "PerformanceId", "CostumeId", true),
                new("PerformanceProps", "Реквизит", CatalogItemType.Prop, "PerformanceProps", "PerformanceId", "PropId", true),
                new("PerformanceClothingItems", "Одежда на подбор", CatalogItemType.Clothing, "PerformanceClothingItems", "PerformanceId", "ClothingItemId", true)
            ],
            CatalogItemType.Sector =>
            [
                new("SectorClothingItems", "Одежда на подбор", CatalogItemType.Clothing, "SectorClothingItems", "SectorId", "ClothingItemId", true),
                new("SectorProps", "Реквизит", CatalogItemType.Prop, "SectorProps", "SectorId", "PropId", true),
                new("SectorCostumes", "Костюмы", CatalogItemType.Costume, "SectorCostumes", "SectorId", "CostumeId", true)
            ],
            _ => []
        };
    }

    private void AddField(string key, string label, string? tooltip = null)
    {
        var textBlock = new TextBlock { Text = label, FontSize = 14 };
        var textBox = new TextBox { FontSize = 14 };
        if (!string.IsNullOrWhiteSpace(tooltip))
            textBox.ToolTip = tooltip;
        SpecificFields.Children.Add(textBlock);
        SpecificFields.Children.Add(textBox);
        _fields[key] = textBox;
    }

    private void AddLargeField(string key, string label, string? tooltip = null)
    {
        var textBlock = new TextBlock { Text = label, FontSize = 14 };
        var textBox = new TextBox
        {
            FontSize = 14,
            AcceptsReturn = true,
            Height = 88,
            TextWrapping = TextWrapping.Wrap,
            ToolTip = tooltip
        };
        SpecificFields.Children.Add(textBlock);
        SpecificFields.Children.Add(textBox);
        _fields[key] = textBox;
    }

    private void AddComboField(string key, string label, string[] items)
    {
        var textBlock = new TextBlock { Text = label, FontSize = 14 };
        var comboBox = new ComboBox
        {
            Background = new SolidColorBrush(Color.FromRgb(23, 32, 29)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(43, 58, 53)),
            FontSize = 14,
            IsEditable = false
        };
        foreach (var item in items)
            comboBox.Items.Add(new ComboBoxItem { Content = item, FontSize = 14 });
        var readOnlyBox = new TextBox
        {
            IsReadOnly = true,
            IsTabStop = false,
            BorderBrush = new SolidColorBrush(Color.FromRgb(29, 41, 37)),
            FontSize = 14,
            Visibility = Visibility.Collapsed
        };
        SpecificFields.Children.Add(textBlock);
        SpecificFields.Children.Add(comboBox);
        SpecificFields.Children.Add(readOnlyBox);
        _comboFields[key] = comboBox;
        _comboReadOnlyFields[key] = readOnlyBox;
    }

    private void AddLinkSelector(LinkConfig config)
    {
        var selector = new LinkSelectorState
        {
            Config = config,
            SearchBox = new TextBox { ToolTip = "Поиск по имени", Margin = new Thickness(0, 6, 0, 8), FontSize = 14 },
            ItemsHost = new StackPanel()
        };

        selector.SearchBox.TextChanged += (_, _) => RenderSelector(selector);
        selector.Options.AddRange(_repository.GetCandidates(config.CandidateType, _item.Id).Select(candidate => new LinkOption { Summary = candidate }));

        var content = new StackPanel();
        content.Children.Add(selector.SearchBox);
        content.Children.Add(new ScrollViewer
        {
            Height = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = selector.ItemsHost
        });

        RelationshipsPanel.Children.Add(new Expander
        {
            Header = config.Label,
            IsExpanded = false,
            Content = content,
            FontSize = 14
        });

        _linkSelectors[config.Key] = selector;
        RenderSelector(selector);
    }

    private void RenderSelector(LinkSelectorState selector)
    {
        selector.ItemsHost.Children.Clear();
        var filter = selector.SearchBox.Text.Trim();
        var filtered = selector.Options
            .Where(option => string.IsNullOrWhiteSpace(filter) || option.Summary.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
        {
            selector.ItemsHost.Children.Add(new TextBlock
            {
                Text = "Ничего не найдено.",
                Foreground = new SolidColorBrush(Color.FromRgb(142, 163, 154)),
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 14
            });
            return;
        }

        foreach (var option in filtered)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            var checkBox = new CheckBox
            {
                Content = option.Summary.Title,
                IsChecked = option.IsSelected,
                Foreground = Brushes.White,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            checkBox.Checked += (_, _) => option.IsSelected = true;
            checkBox.Unchecked += (_, _) => option.IsSelected = false;
            row.Children.Add(checkBox);

            var openButton = new Button
            {
                Content = "→",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 12,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(30, 60, 45)),
                BorderBrush = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = option.Summary,
                ToolTip = "Открыть карточку"
            };
            openButton.Click += LinkItemOpenButton_Click;
            row.Children.Add(openButton);

            selector.ItemsHost.Children.Add(row);
        }
    }

    private void LinkItemOpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CatalogSummary summary })
        {
            var item = _repository.GetById(summary.Id);
            if (item is not null)
            {
                CommitEditFields();
                var editor = new EntityEditorWindow(_repository, _paths, item) { Owner = this };
                editor.ShowDialog();
                ReloadAfterLinkEdit();
            }
        }
    }

    private void ReloadAfterLinkEdit()
    {
        RefreshAfterLinkEdit();
        LoadValues();
        RebuildRelationshipsView();
    }

    private void LoadValues()
    {
        var currentItem = _repository.GetById(_item.Id);
        if (currentItem is not null)
        {
            _allRelatedItems.Clear();
            foreach (var r in currentItem.RelatedItems)
                _allRelatedItems.Add(r);
        }

        TitleBox.Text = _item.Title;
        DescriptionBox.Text = _item.Description;
        InventoryBox.Text = _item.InventoryNumber;
        StorageBox.Text = _item.StorageLocation;

        SelectComboValue(ConditionBox, string.IsNullOrWhiteSpace(_item.Condition) ? "Хорошее" : _item.Condition);
        ConditionReadOnlyBox.Text = string.IsNullOrWhiteSpace(_item.Condition) ? "Хорошее" : _item.Condition;
        SelectResponsiblePerson(_item.ResponsiblePerson);
        ResponsibleReadOnlyBox.Text = string.IsNullOrWhiteSpace(_item.ResponsiblePerson) ? "-" : _item.ResponsiblePerson;

        var photos = _repository.GetPhotos(_item.Id);
        var absolutePhotos = photos
            .Select(p => _paths.ToAbsolutePhotoPath(p))
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .ToList();
        PhotosList.ItemsSource = absolutePhotos;

        LoadField("Size", _item.Size);
        LoadField("Color", _item.Color);
        LoadField("Material", _item.Material);
        LoadField("CareInstructions", _item.CareInstructions);
        LoadField("Weight", _item.Weight);
        LoadField("CharacterName", _item.CharacterName);
        LoadField("ActorName", _item.ActorName);
        LoadField("Director", _item.Director);
        LoadField("PremiereDate", _item.PremiereDate);
        LoadField("Season", _item.Season);
        LoadField("Movement", _item.Movement);
        LoadComboField("Dimensions", _item.Dimensions);
        LoadComboField("Fragility", _item.Fragility);

        foreach (var selector in _linkSelectors.Values)
        {
            var selectedIds = selector.Config.CurrentItemIsOwner
                ? _repository.GetLinkedIds(selector.Config.Table, selector.Config.OwnerColumn, selector.Config.LinkedColumn, _item.Id)
                : _repository.GetOwnerIds(selector.Config.Table, selector.Config.OwnerColumn, selector.Config.LinkedColumn, _item.Id);

            foreach (var option in selector.Options)
                option.IsSelected = selectedIds.Contains(option.Summary.Id);

            RenderSelector(selector);
        }
    }

    private void SelectResponsiblePerson(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            ResponsibleBox.SelectedItem = null;
            return;
        }
        foreach (var item in ResponsibleBox.Items)
        {
            if (item is DirectoryUser user &&
                string.Equals(user.FullName, fullName, StringComparison.CurrentCultureIgnoreCase))
            {
                ResponsibleBox.SelectedItem = item;
                return;
            }
        }
        ResponsibleBox.SelectedItem = null;
    }

    private void SetViewMode()
    {
        _isEditing = false;
        ButtonPanelView.Visibility = Visibility.Visible;
        ButtonPanelEdit.Visibility = Visibility.Collapsed;

        // Все TextBox поля — read-only
        SetReadOnly(TitleBox, true);
        SetReadOnly(DescriptionBox, true);
        SetReadOnly(InventoryBox, true);
        SetReadOnly(StorageBox, true);

        foreach (var field in _fields.Values)
            SetReadOnly(field, true);

        // ComboBox — скрываем, показываем read-only TextBox
        SetComboReadOnly(ConditionBox, ConditionReadOnlyBox);
        SetComboReadOnly(ResponsibleBox, ResponsibleReadOnlyBox);
        foreach (var key in _comboFields.Keys)
            SetComboReadOnly(_comboFields[key], _comboReadOnlyFields[key]);

        // Фото
        AddPhotoButton.Visibility = Visibility.Collapsed;
        var deleteBtns = FindVisualChildren<Button>(PhotosList).Where(b => b.Name == "DeletePhotoBtn");
        foreach (var btn in deleteBtns)
            btn.Visibility = Visibility.Collapsed;

        // Связи
        RelationshipsPanelView.Visibility = Visibility.Visible;
        RelationshipsPanel.Visibility = Visibility.Collapsed;
    }

    private void SetEditMode()
    {
        _isEditing = true;
        ButtonPanelView.Visibility = Visibility.Collapsed;
        ButtonPanelEdit.Visibility = Visibility.Visible;

        // Все TextBox поля — редактируемые
        SetReadOnly(TitleBox, false);
        SetReadOnly(DescriptionBox, false);
        SetReadOnly(InventoryBox, false);
        SetReadOnly(StorageBox, false);

        foreach (var field in _fields.Values)
            SetReadOnly(field, false);

        // ComboBox — показываем, скрываем read-only TextBox
        SetComboEditable(ConditionBox, ConditionReadOnlyBox);
        SetComboEditable(ResponsibleBox, ResponsibleReadOnlyBox);
        foreach (var key in _comboFields.Keys)
            SetComboEditable(_comboFields[key], _comboReadOnlyFields[key]);

        // Фото
        AddPhotoButton.Visibility = Visibility.Visible;
        var deleteBtns = FindVisualChildren<Button>(PhotosList).Where(b => b.Name == "DeletePhotoBtn");
        foreach (var btn in deleteBtns)
            btn.Visibility = Visibility.Visible;

        // Связи
        RelationshipsPanelView.Visibility = Visibility.Collapsed;
        RelationshipsPanel.Visibility = Visibility.Visible;
    }

    private static void SetReadOnly(TextBox textBox, bool readOnly)
    {
        textBox.IsReadOnly = readOnly;
        textBox.IsTabStop = !readOnly;
        textBox.BorderBrush = readOnly
            ? new SolidColorBrush(Color.FromRgb(29, 41, 37))
            : new SolidColorBrush(Color.FromRgb(43, 58, 53));
    }

    private static void SetComboReadOnly(ComboBox combo, TextBox readOnlyBox)
    {
        combo.Visibility = Visibility.Collapsed;
        readOnlyBox.Visibility = Visibility.Visible;
    }

    private static void SetComboEditable(ComboBox combo, TextBox readOnlyBox)
    {
        combo.Visibility = Visibility.Visible;
        readOnlyBox.Visibility = Visibility.Collapsed;
    }

    private void BuildRelationshipsView()
    {
        var groups = _allRelatedItems.GroupBy(r => r.Type);
        foreach (var group in groups)
        {
            var typeLabel = group.Key.ToRussian();
            var header = new TextBlock
            {
                Text = typeLabel,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(238, 245, 241)),
                Margin = new Thickness(0, 12, 0, 4)
            };
            RelationshipsPanelView.Children.Add(header);

            foreach (var related in group)
            {
                var linkButton = new Button
                {
                    Content = $"{related.Title}",
                    Tag = related,
                    Margin = new Thickness(0, 2, 0, 2),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(12, 7, 12, 7)
                };
                linkButton.Click += (s, e) =>
                {
                    if (s is Button { Tag: CatalogSummary summary })
                    {
                        var item = _repository.GetById(summary.Id);
                        if (item is not null)
                        {
                            DialogResult = true;
                            var editor = new EntityEditorWindow(_repository, _paths, item) { Owner = Owner };
                            editor.ShowDialog();
                        }
                    }
                };
                RelationshipsPanelView.Children.Add(linkButton);
            }
        }
    }

    private void RebuildRelationshipsView()
    {
        RelationshipsPanelView.Children.Clear();
        var currentItem = _repository.GetById(_item.Id);
        if (currentItem is null) return;

        _allRelatedItems.Clear();
        foreach (var r in currentItem.RelatedItems)
            _allRelatedItems.Add(r);

        var groups = _allRelatedItems.GroupBy(r => r.Type);
        foreach (var group in groups)
        {
            var typeLabel = group.Key.ToRussian();
            var header = new TextBlock
            {
                Text = typeLabel,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(238, 245, 241)),
                Margin = new Thickness(0, 12, 0, 4)
            };
            RelationshipsPanelView.Children.Add(header);

            foreach (var related in group)
            {
                var linkButton = new Button
                {
                    Content = $"{related.Title} ({related.Type.ToRussian()})",
                    Tag = related,
                    Margin = new Thickness(0, 2, 0, 2),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(12, 7, 12, 7)
                };
                linkButton.Click += (s, ev) =>
                {
                    if (s is Button { Tag: CatalogSummary summary })
                    {
                        var item = _repository.GetById(summary.Id);
                        if (item is not null)
                        {
                            DialogResult = true;
                            var editor = new EntityEditorWindow(_repository, _paths, item) { Owner = Owner };
                            editor.ShowDialog();
                        }
                    }
                };
                RelationshipsPanelView.Children.Add(linkButton);
            }
        }
    }

    private void RefreshAfterLinkEdit()
    {
        foreach (var selector in _linkSelectors.Values)
        {
            var selectedIds = selector.Config.CurrentItemIsOwner
                ? _repository.GetLinkedIds(selector.Config.Table, selector.Config.OwnerColumn, selector.Config.LinkedColumn, _item.Id)
                : _repository.GetOwnerIds(selector.Config.Table, selector.Config.OwnerColumn, selector.Config.LinkedColumn, _item.Id);

            foreach (var option in selector.Options)
                option.IsSelected = selectedIds.Contains(option.Summary.Id);

            RenderSelector(selector);
        }
        foreach (var selector in _linkSelectors.Values)
        {
            selector.Options.Clear();
            selector.Options.AddRange(_repository.GetCandidates(selector.Config.CandidateType, _item.Id).Select(candidate => new LinkOption { Summary = candidate }));
        }
    }

    private void PhotosList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PhotosList.SelectedItem is string absolutePath && File.Exists(absolutePath))
        {
            var viewer = new Window
            {
                Title = "Просмотр фото",
                Width = 800,
                Height = 600,
                Background = new SolidColorBrush(Color.FromRgb(16, 20, 19)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 42, 38)),
                BorderThickness = new Thickness(1),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.CanResize
            };
            var image = new Image
            {
                Source = new BitmapImage(new Uri(absolutePath)),
                Stretch = Stretch.Uniform,
                Margin = new Thickness(10)
            };
            viewer.Content = image;
            viewer.ShowDialog();
        }
    }

    private void AddGalleryPhotoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите фото для галереи",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Все файлы|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            var relativePath = _paths.CopyPhoto(dialog.FileName, _item.Type);
            _newGalleryPhotos.Add(relativePath);
            RefreshGallery();
        }
    }

    private void DeleteGalleryPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string absolutePath })
        {
            var result = MessageBox.Show("Удалить это фото из галереи?", "Удаление фото",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            var relativePath = _paths.ToRelativePhotoPath(absolutePath);
            if (_newGalleryPhotos.Remove(relativePath))
            {
                RefreshGallery();
                return;
            }

            _repository.DeletePhoto(_item.Id, relativePath);
            try { File.Delete(absolutePath); } catch { }
            RefreshGallery();
        }
    }

    private void RefreshGallery()
    {
        var dbPhotos = _repository.GetPhotos(_item.Id);
        var allRelative = dbPhotos.Concat(_newGalleryPhotos).ToList();
        var absolutePhotos = allRelative
            .Select(p => _paths.ToAbsolutePhotoPath(p))
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .ToList();
        PhotosList.ItemsSource = absolutePhotos;
    }

    private void EditButton_Click(object sender, RoutedEventArgs e) => SetEditMode();
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        CommitEditFields();
        SetViewMode();
    }
    private void CancelEditButton_Click(object sender, RoutedEventArgs e)
    {
        LoadValues();
        SetViewMode();
    }
    private void OkViewButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CommitEditFields()
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show("Название обязательно.", "Проверка данных", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _item.Title = TitleBox.Text.Trim();
        _item.Description = DescriptionBox.Text.Trim();
        _item.InventoryNumber = InventoryBox.Text.Trim();
        _item.StorageLocation = StorageBox.Text.Trim();
        _item.Condition = SelectedComboText(ConditionBox);
        _item.ResponsiblePerson = ResponsibleBox.SelectedItem is DirectoryUser user ? user.FullName : ResponsibleBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(_selectedSourcePhoto))
            _item.MainPhotoPath = _paths.CopyPhoto(_selectedSourcePhoto, _item.Type);

        _item.Size = GetField("Size");
        _item.Color = GetField("Color");
        _item.Material = GetField("Material");
        _item.CareInstructions = GetField("CareInstructions");
        _item.Weight = GetField("Weight");
        _item.CharacterName = GetField("CharacterName");
        _item.ActorName = GetField("ActorName");
        _item.Director = GetField("Director");
        _item.PremiereDate = GetField("PremiereDate");
        _item.Season = GetField("Season");
        _item.Movement = GetField("Movement");
        _item.Dimensions = GetComboField("Dimensions");
        _item.Fragility = GetComboField("Fragility");

        _repository.Save(_item);

        foreach (var relativePath in _newGalleryPhotos.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!_repository.GetPhotos(_item.Id).Contains(relativePath, StringComparer.OrdinalIgnoreCase))
                _repository.AddPhoto(_item.Id, relativePath);
        }

        SaveLinks();
        ResponsibleBox.ItemsSource = _repository.GetUsers();

        _selectedSourcePhoto = "";
        _newGalleryPhotos.Clear();
        RefreshGallery();

        // Обновляем read-only поля
        ConditionReadOnlyBox.Text = _item.Condition;
        ResponsibleReadOnlyBox.Text = string.IsNullOrWhiteSpace(_item.ResponsiblePerson) ? "-" : _item.ResponsiblePerson;

        RebuildRelationshipsView();
    }

    private void SaveLinks()
    {
        foreach (var selector in _linkSelectors.Values)
        {
            var ids = selector.Options.Where(option => option.IsSelected).Select(option => option.Summary.Id).ToList();
            if (selector.Config.CurrentItemIsOwner)
                _repository.ReplaceLinks(selector.Config.Table, selector.Config.OwnerColumn, selector.Config.LinkedColumn, _item.Id, ids);
            else
                _repository.ReplaceInverseLinks(selector.Config.Table, selector.Config.OwnerColumn, selector.Config.LinkedColumn, _item.Id, ids);
        }
    }

    private void LoadField(string key, string value)
    {
        if (_fields.TryGetValue(key, out var textBox))
            textBox.Text = value;
    }

    private void LoadComboField(string key, string value)
    {
        if (!_comboFields.TryGetValue(key, out var comboBox)) return;
        if (!_comboReadOnlyFields.TryGetValue(key, out var readOnlyBox)) return;

        if (string.IsNullOrWhiteSpace(value))
        {
            comboBox.SelectedIndex = 0;
            readOnlyBox.Text = "Не указано";
        }
        else
        {
            SelectComboValue(comboBox, value);
            readOnlyBox.Text = value;
        }
    }

    private string GetField(string key) => _fields.TryGetValue(key, out var textBox) ? textBox.Text.Trim() : "";
    private string GetComboField(string key) => _comboFields.TryGetValue(key, out var comboBox) ? SelectedComboText(comboBox) : "";

    private static string SelectedComboText(ComboBox comboBox)
        => comboBox.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "" : comboBox.Text;

    private static void SelectComboValue(ComboBox comboBox, string value)
    {
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }
        comboBox.SelectedIndex = 0;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T tChild) yield return tChild;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private sealed class LinkSelectorState
    {
        public required LinkConfig Config { get; init; }
        public required TextBox SearchBox { get; init; }
        public required StackPanel ItemsHost { get; init; }
        public List<LinkOption> Options { get; } = [];
    }

    private sealed class LinkOption
    {
        public required CatalogSummary Summary { get; init; }
        public bool IsSelected { get; set; }
    }

    private sealed record LinkConfig(
        string Key,
        string Label,
        CatalogItemType CandidateType,
        string Table,
        string OwnerColumn,
        string LinkedColumn,
        bool CurrentItemIsOwner);
}
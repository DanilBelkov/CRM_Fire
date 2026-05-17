using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
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
    private readonly Dictionary<string, ListBox> _linkLists = [];
    private string _selectedSourcePhoto = "";

    public EntityEditorWindow(TheatreRepository repository, AppPaths paths, CatalogItem item)
    {
        InitializeComponent();
        _repository = repository;
        _paths = paths;
        _item = item;
        BuildForm();
        LoadValues();
    }

    private void BuildForm()
    {
        HeaderText.Text = $"{_item.Type.ToRussian()}: карточка";
        Title = HeaderText.Text;

        AddSpecificFields();
        AddRelationshipSelectors();
    }

    private void LoadValues()
    {
        TitleBox.Text = _item.Title;
        DescriptionBox.Text = _item.Description;
        InventoryBox.Text = _item.InventoryNumber;
        StorageBox.Text = _item.StorageLocation;
        ResponsibleBox.Text = _item.ResponsiblePerson;
        TagsBox.Text = string.Join(", ", _item.Tags);
        PhotoBox.Text = _item.MainPhotoPath;
        SelectComboValue(ConditionBox, string.IsNullOrWhiteSpace(_item.Condition) ? "Хорошее" : _item.Condition);

        SetField("Size", _item.Size);
        SetField("Color", _item.Color);
        SetField("Material", _item.Material);
        SetField("Measurements", _item.Measurements);
        SetField("CareInstructions", _item.CareInstructions);
        SetField("Dimensions", _item.Dimensions);
        SetField("Weight", _item.Weight);
        SetField("Fragility", _item.Fragility);
        SetField("UsageNotes", _item.UsageNotes);
        SetField("CharacterName", _item.CharacterName);
        SetField("ActorName", _item.ActorName);
        SetField("SceneNotes", _item.SceneNotes);
        SetField("Director", _item.Director);
        SetField("PremiereDate", _item.PremiereDate);
        SetField("Season", _item.Season);

        foreach (var (key, list) in _linkLists)
        {
            var linkedIds = key switch
            {
                "CostumeClothingItems" => _repository.GetLinkedIds("CostumeClothingItems", "CostumeId", "ClothingItemId", _item.Id),
                "CostumeProps" => _repository.GetLinkedIds("CostumeProps", "CostumeId", "PropId", _item.Id),
                "PerformanceCostumes" => _repository.GetLinkedIds("PerformanceCostumes", "PerformanceId", "CostumeId", _item.Id),
                "PerformanceProps" => _repository.GetLinkedIds("PerformanceProps", "PerformanceId", "PropId", _item.Id),
                "PerformanceClothingItems" => _repository.GetLinkedIds("PerformanceClothingItems", "PerformanceId", "ClothingItemId", _item.Id),
                _ => []
            };

            foreach (var candidate in list.Items.OfType<CatalogSummary>())
            {
                if (linkedIds.Contains(candidate.Id))
                {
                    list.SelectedItems.Add(candidate);
                }
            }
        }
    }

    private void AddSpecificFields()
    {
        switch (_item.Type)
        {
            case CatalogItemType.Clothing:
                AddField("Size", "Размер");
                AddField("Color", "Цвет");
                AddField("Material", "Материал");
                AddField("Measurements", "Мерки");
                AddField("CareInstructions", "Уход");
                break;
            case CatalogItemType.Prop:
                AddField("Dimensions", "Габариты");
                AddField("Weight", "Вес");
                AddField("Material", "Материал");
                AddField("Fragility", "Хрупкость");
                AddField("UsageNotes", "Особенности использования");
                break;
            case CatalogItemType.Costume:
                AddField("CharacterName", "Персонаж");
                AddField("ActorName", "Актер");
                AddField("SceneNotes", "Сцены использования");
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
        if (_item.Type == CatalogItemType.Costume)
        {
            AddLinkList("CostumeClothingItems", "Одежда в костюме", CatalogItemType.Clothing);
            AddLinkList("CostumeProps", "Реквизит в костюме", CatalogItemType.Prop);
        }

        if (_item.Type == CatalogItemType.Performance)
        {
            AddLinkList("PerformanceCostumes", "Костюмы в спектакле", CatalogItemType.Costume);
            AddLinkList("PerformanceProps", "Реквизит напрямую в спектакле", CatalogItemType.Prop);
            AddLinkList("PerformanceClothingItems", "Одежда напрямую в спектакле", CatalogItemType.Clothing);
        }
    }

    private void AddField(string key, string label)
    {
        var textBlock = new TextBlock { Text = label };
        var textBox = new TextBox();
        SpecificFields.Children.Add(textBlock);
        SpecificFields.Children.Add(textBox);
        _fields[key] = textBox;
    }

    private void AddLinkList(string key, string label, CatalogItemType candidateType)
    {
        var title = new TextBlock
        {
            Text = label,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 6)
        };

        var list = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            Height = 130,
            DisplayMemberPath = nameof(CatalogSummary.Title),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = System.Windows.Media.Brushes.DarkSlateGray
        };

        foreach (var candidate in _repository.GetCandidates(candidateType, _item.Id))
        {
            list.Items.Add(candidate);
        }

        RelationshipsPanel.Children.Add(title);
        RelationshipsPanel.Children.Add(list);
        _linkLists[key] = list;
    }

    private void ChoosePhotoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите фото",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Все файлы|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _selectedSourcePhoto = dialog.FileName;
            PhotoBox.Text = dialog.FileName;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
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
        _item.ResponsiblePerson = ResponsibleBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(_selectedSourcePhoto))
        {
            _item.MainPhotoPath = _paths.CopyPhoto(_selectedSourcePhoto, _item.Type);
        }

        _item.Tags.Clear();
        foreach (var tag in TagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            _item.Tags.Add(tag);
        }

        _item.Size = GetField("Size");
        _item.Color = GetField("Color");
        _item.Material = GetField("Material");
        _item.Measurements = GetField("Measurements");
        _item.CareInstructions = GetField("CareInstructions");
        _item.Dimensions = GetField("Dimensions");
        _item.Weight = GetField("Weight");
        _item.Fragility = GetField("Fragility");
        _item.UsageNotes = GetField("UsageNotes");
        _item.CharacterName = GetField("CharacterName");
        _item.ActorName = GetField("ActorName");
        _item.SceneNotes = GetField("SceneNotes");
        _item.Director = GetField("Director");
        _item.PremiereDate = GetField("PremiereDate");
        _item.Season = GetField("Season");

        _repository.Save(_item);
        SaveLinks();
        DialogResult = true;
    }

    private void SaveLinks()
    {
        foreach (var (key, list) in _linkLists)
        {
            var ids = list.SelectedItems.OfType<CatalogSummary>().Select(x => x.Id).ToList();
            switch (key)
            {
                case "CostumeClothingItems":
                    _repository.ReplaceLinks("CostumeClothingItems", "CostumeId", "ClothingItemId", _item.Id, ids);
                    break;
                case "CostumeProps":
                    _repository.ReplaceLinks("CostumeProps", "CostumeId", "PropId", _item.Id, ids);
                    break;
                case "PerformanceCostumes":
                    _repository.ReplaceLinks("PerformanceCostumes", "PerformanceId", "CostumeId", _item.Id, ids);
                    break;
                case "PerformanceProps":
                    _repository.ReplaceLinks("PerformanceProps", "PerformanceId", "PropId", _item.Id, ids);
                    break;
                case "PerformanceClothingItems":
                    _repository.ReplaceLinks("PerformanceClothingItems", "PerformanceId", "ClothingItemId", _item.Id, ids);
                    break;
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SetField(string key, string value)
    {
        if (_fields.TryGetValue(key, out var textBox))
        {
            textBox.Text = value;
        }
    }

    private string GetField(string key) => _fields.TryGetValue(key, out var textBox) ? textBox.Text.Trim() : "";

    private static string SelectedComboText(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "" : comboBox.Text;
    }

    private static void SelectComboValue(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 1;
    }
}

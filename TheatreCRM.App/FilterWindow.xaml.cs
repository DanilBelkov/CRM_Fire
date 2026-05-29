using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TheatreCRM.App.Data;
using TheatreCRM.App.Models;

namespace TheatreCRM.App;

public partial class FilterWindow : Window
{
    private readonly TheatreRepository _repository;
    private readonly CatalogItemType _sectionType;
    private readonly Dictionary<string, string> _initialValues;
    private readonly Dictionary<string, TextBox> _textFields = [];
    private readonly Dictionary<string, ComboBox> _comboFields = [];
    private readonly Dictionary<string, string> _fieldLabels = new()
    {
        ["Title"] = "Название",
        ["Description"] = "Описание",
        ["InventoryNumber"] = "Инвентарный номер",
        ["StorageLocation"] = "Место хранения",
        ["Condition"] = "Состояние",
        ["ResponsiblePerson"] = "Ответственный",
        ["Notes"] = "Примечания",
        ["Size"] = "Размер",
        ["Color"] = "Цвет",
        ["Material"] = "Материал",
        ["CareInstructions"] = "Уход",
        ["Dimensions"] = "Габариты",
        ["Weight"] = "Вес",
        ["Fragility"] = "Хрупкость",
        ["CharacterName"] = "Персонаж",
        ["ActorName"] = "Актер",
        ["Director"] = "Режиссер",
        ["PremiereDate"] = "Дата премьеры",
        ["Season"] = "Сезон",
        ["Movement"] = "Передвижение предмета",
        // Поля для фильтрации по связям
        ["RelatedClothing"] = "Одежда на подбор",
        ["RelatedProps"] = "Реквизит",
        ["RelatedCostumes"] = "Костюм",
        ["RelatedPerformances"] = "Спектакль",
        ["RelatedSectors"] = "Сектор"
    };

    public Dictionary<string, string>? FilterValues { get; private set; }

    public FilterWindow(TheatreRepository repository, CatalogItemType sectionType, Dictionary<string, string>? initialValues = null)
    {
        InitializeComponent();
        _repository = repository;
        _sectionType = sectionType;
        _initialValues = initialValues ?? [];
        BuildFields();
    }

    private void BuildFields()
    {
        var fields = GetFieldsForType();

        foreach (var field in fields)
        {
            var label = _fieldLabels.GetValueOrDefault(field, field);
            var textBlock = new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) };

            // Check if it's a combo field
            if (field == "Dimensions" && _sectionType == CatalogItemType.Prop)
            {
                var comboBox = new ComboBox
                {
                    Background = new SolidColorBrush(Color.FromRgb(23, 32, 29)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(43, 58, 53)),
                    FontSize = 14,
                    IsEditable = false,
                    Margin = new Thickness(0, 2, 0, 8)
                };
                comboBox.Items.Add(new ComboBoxItem { Content = "" });
                comboBox.Items.Add(new ComboBoxItem { Content = "Маленький" });
                comboBox.Items.Add(new ComboBoxItem { Content = "Средний" });
                comboBox.Items.Add(new ComboBoxItem { Content = "Большой" });
                FilterFields.Children.Add(textBlock);
                FilterFields.Children.Add(comboBox);
                _comboFields[field] = comboBox;

                if (_initialValues.TryGetValue(field, out var val))
                    SelectComboValue(comboBox, val);
                continue;
            }

            if (field == "Fragility" && _sectionType == CatalogItemType.Prop)
            {
                var comboBox = new ComboBox
                {
                    Background = new SolidColorBrush(Color.FromRgb(23, 32, 29)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(43, 58, 53)),
                    FontSize = 14,
                    IsEditable = false,
                    Margin = new Thickness(0, 2, 0, 8)
                };
                comboBox.Items.Add(new ComboBoxItem { Content = "" });
                comboBox.Items.Add(new ComboBoxItem { Content = "Низкая" });
                comboBox.Items.Add(new ComboBoxItem { Content = "Средняя" });
                comboBox.Items.Add(new ComboBoxItem { Content = "Высокая" });
                comboBox.Items.Add(new ComboBoxItem { Content = "Очень высокая" });
                FilterFields.Children.Add(textBlock);
                FilterFields.Children.Add(comboBox);
                _comboFields[field] = comboBox;

                if (_initialValues.TryGetValue(field, out var val))
                    SelectComboValue(comboBox, val);
                continue;
            }

            // Text field
            var textBox = new TextBox();
            if (_initialValues.TryGetValue(field, out var value))
                textBox.Text = value;
            FilterFields.Children.Add(textBlock);
            FilterFields.Children.Add(textBox);
            _textFields[field] = textBox;
        }

        // Если есть секция связей — добавляем разделитель
        var linkFields = GetLinkFieldsForType();
        if (linkFields.Count > 0)
        {
            FilterFields.Children.Add(new TextBlock
            {
                Text = "Поиск по связям",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(238, 245, 241)),
                Margin = new Thickness(0, 16, 0, 4)
            });

            foreach (var field in linkFields)
            {
                var label = _fieldLabels.GetValueOrDefault(field, field);
                var tb = new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) };
                var txtBox = new TextBox();
                if (_initialValues.TryGetValue(field, out var val))
                    txtBox.Text = val;
                FilterFields.Children.Add(tb);
                FilterFields.Children.Add(txtBox);
                _textFields[field] = txtBox;
            }
        }
    }

    private List<string> GetFieldsForType()
    {
        var common = new List<string>
        {
            "Title", "Description", "InventoryNumber", "StorageLocation",
            "Condition", "ResponsiblePerson", "Notes"
        };

        var specific = _sectionType switch
        {
            CatalogItemType.Clothing => new List<string> { "Size", "Color", "Material", "CareInstructions", "Movement" },
            CatalogItemType.Prop => new List<string> { "Dimensions", "Weight", "Material", "Fragility", "Movement" },
            CatalogItemType.Costume => new List<string> { "CharacterName", "ActorName", "CareInstructions", "Movement" },
            CatalogItemType.Performance => new List<string> { "Director", "PremiereDate", "Season" },
            CatalogItemType.Sector => new List<string>(),
            _ => new List<string>()
        };

        common.AddRange(specific);
        return common;
    }

    private List<string> GetLinkFieldsForType()
    {
        return _sectionType switch
        {
            CatalogItemType.Clothing => ["RelatedCostumes", "RelatedPerformances", "RelatedSectors"],
            CatalogItemType.Prop => ["RelatedCostumes", "RelatedPerformances", "RelatedSectors"],
            CatalogItemType.Costume => ["RelatedClothing", "RelatedProps", "RelatedPerformances", "RelatedSectors"],
            CatalogItemType.Performance => ["RelatedCostumes", "RelatedProps", "RelatedClothing"],
            CatalogItemType.Sector => ["RelatedClothing", "RelatedProps", "RelatedCostumes"],
            _ => []
        };
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var values = new Dictionary<string, string>();

        foreach (var (field, textBox) in _textFields)
        {
            var val = textBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(val))
                values[field] = val;
        }

        foreach (var (field, comboBox) in _comboFields)
        {
            var val = SelectedComboText(comboBox);
            if (!string.IsNullOrWhiteSpace(val))
                values[field] = val;
        }

        FilterValues = values;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string SelectedComboText(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "" : "";
    }

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
    }
}
using System.Windows;
using System.Windows.Controls;
using TheatreCRM.App.Data;
using TheatreCRM.App.Models;

namespace TheatreCRM.App;

public partial class TagManagerWindow : Window
{
    private readonly TheatreRepository _repository;

    public TagManagerWindow(TheatreRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        LoadGroups();
    }

    private void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupsList.SelectedItem is TagGroup group)
        {
            GroupNameBox.Text = group.Name;
            GroupDescriptionBox.Text = group.Description;
        }
        else
        {
            GroupNameBox.Text = "";
            GroupDescriptionBox.Text = "";
        }

        LoadTags();
    }

    private void TagsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TagsList.SelectedItem is Tag tag)
        {
            TagNameBox.Text = tag.Name;
            TagDescriptionBox.Text = tag.Description;
            UsageText.Text = $"Тег используется в {tag.UsageCount} карточках.";
        }
        else
        {
            TagNameBox.Text = "";
            TagDescriptionBox.Text = "";
            UsageText.Text = "Выберите тег";
        }
    }

    private void SaveGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupNameBox.Text))
        {
            MessageBox.Show("Название группы обязательно.", "Группа тегов", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var group = GroupsList.SelectedItem as TagGroup ?? new TagGroup();
        group.Name = GroupNameBox.Text.Trim();
        group.Description = GroupDescriptionBox.Text.Trim();
        _repository.SaveTagGroup(group);
        LoadGroups(group.Id);
    }

    private void SaveTagButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsList.SelectedItem is not TagGroup group)
        {
            MessageBox.Show("Сначала выберите группу тегов.", "Тег", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(TagNameBox.Text))
        {
            MessageBox.Show("Название тега обязательно.", "Тег", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var tag = TagsList.SelectedItem as Tag ?? new Tag();
        tag.GroupId = group.Id;
        tag.Name = TagNameBox.Text.Trim();
        tag.Description = TagDescriptionBox.Text.Trim();
        _repository.SaveTag(tag);
        LoadTags(tag.Id);
    }

    private void ArchiveTagButton_Click(object sender, RoutedEventArgs e)
    {
        if (TagsList.SelectedItem is not Tag tag)
        {
            return;
        }

        var usage = _repository.GetTagUsageCount(tag.Id);
        var result = MessageBox.Show(
            $"Архивировать тег \"{tag.Name}\"? Он останется на {usage} карточках, но не будет предлагаться в обычных фильтрах.",
            "Архивирование тега",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _repository.ArchiveTag(tag.Id);
        LoadTags();
    }

    private void ReplaceTagButton_Click(object sender, RoutedEventArgs e)
    {
        if (TagsList.SelectedItem is not Tag source || ReplacementTagBox.SelectedItem is not Tag target || source.Id == target.Id)
        {
            return;
        }

        var usage = _repository.GetTagUsageCount(source.Id);
        var result = MessageBox.Show(
            $"Заменить тег \"{source.Name}\" на \"{target.Name}\" в {usage} карточках? Карточки сохранятся.",
            "Замена тега",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _repository.ReplaceTag(source.Id, target.Id);
        LoadTags(target.Id);
    }

    private void RemoveTagLinksButton_Click(object sender, RoutedEventArgs e)
    {
        if (TagsList.SelectedItem is not Tag tag)
        {
            return;
        }

        var usage = _repository.GetTagUsageCount(tag.Id);
        var result = MessageBox.Show(
            $"Снять тег \"{tag.Name}\" с {usage} карточек? Сами карточки не удалятся.",
            "Снять тег",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _repository.RemoveTagFromItems(tag.Id);
        LoadTags(tag.Id);
    }

    private void LoadGroups(string? selectedId = null)
    {
        GroupsList.ItemsSource = _repository.GetTagGroups();
        GroupsList.SelectedItem = GroupsList.Items.OfType<TagGroup>().FirstOrDefault(group => group.Id == selectedId) ?? GroupsList.Items.OfType<TagGroup>().FirstOrDefault();
    }

    private void LoadTags(string? selectedId = null)
    {
        var group = GroupsList.SelectedItem as TagGroup;
        var tags = _repository.GetTags(group?.Id, includeArchived: true);
        TagsList.ItemsSource = tags;
        ReplacementTagBox.ItemsSource = _repository.GetTags(includeArchived: false);
        TagsList.SelectedItem = TagsList.Items.OfType<Tag>().FirstOrDefault(tag => tag.Id == selectedId);
    }
}

using Avalonia.Controls;

namespace Composa.App;

public sealed partial class MainWindow
{
    private MenuItem BuildLanguageMenu()
    {
        var menu = new MenuItem { Header = "语言 / Language" };
        foreach (var (id, label) in new[] { ("system", L10n.Text("Follow system")), ("zh-CN", "简体中文"), ("en", "English") })
        {
            var item = new MenuItem
            {
                Header = label, ToggleType = MenuItemToggleType.Radio, IsChecked = settings.Language == id
            };
            item.Click += (_, _) =>
            {
                SetUiLanguage(id);
                foreach (var choice in menu.Items.OfType<MenuItem>()) choice.IsChecked = ReferenceEquals(choice, item);
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>Apply at next launch so changing language never closes a document or interrupts an open edit.</summary>
    public void SetUiLanguage(string language)
    {
        if (language is not ("system" or "en" or "zh-CN")) throw new ArgumentException("Unsupported UI language.", nameof(language));
        settings.Language = language;
        settings.Save();
        ShowNote(L10n.Format("Language saved. Restart {0} to apply.", AppInfo.DisplayName));
    }
}

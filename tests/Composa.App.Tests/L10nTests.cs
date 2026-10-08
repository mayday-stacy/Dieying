using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Composa.App.Dialogs;
using SkiaSharp;

namespace Composa.App.Tests;

public sealed class L10nTests : IDisposable
{
    private readonly string previousLanguage = L10n.CurrentLanguage;
    private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        CultureInfo.CurrentCulture = previousCulture;
        CultureInfo.CurrentUICulture = previousUiCulture;
        L10n.SetLanguage(previousLanguage);
    }

    [Fact]
    public void Language_changes_display_text_without_changing_numeric_culture_or_unknown_text()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        L10n.SetLanguage("zh-CN");
        Assert.Equal("zh-CN", L10n.CurrentLanguage);
        Assert.Equal("保存", L10n.Text("Save"));
        Assert.Equal("New untranslated text", L10n.Text("New untranslated text"));
        Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
        Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
        Assert.Equal("每 1,25 像素一个子网格。", L10n.Format("A subdivision every {0:0.##} pixels.", 1.25));
        Assert.Equal("已保存 C:\\作品\\Save.cmps", L10n.Format("Saved {0}", "C:\\作品\\Save.cmps"));
        L10n.SetLanguage("en");
        Assert.Equal("Save", L10n.Text("Save"));
    }

    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "en")]
    public void System_language_resolves_from_ui_culture(string system, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(system);
        L10n.SetLanguage("system");
        Assert.Equal(expected, L10n.CurrentLanguage);
    }

    [Fact]
    public void Translated_templates_preserve_each_placeholder_and_its_numeric_format()
    {
        var assembly = typeof(L10n).Assembly;
        var tested = 0;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Composa.App.Localization.") && n.EndsWith(".resources")))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new ResourceReader(stream);
            foreach (DictionaryEntry entry in reader)
            {
                if (entry.Key is not string key || entry.Value is not string translation || !Regex.IsMatch(key, @"\{\d")) continue;
                Assert.Equal(CompositeFormat.Parse(key).MinimumArgumentCount, CompositeFormat.Parse(translation).MinimumArgumentCount);
                string[] Placeholders(string text) => Regex.Matches(text, @"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}").Select(m => m.Value).Order().ToArray();
                Assert.Equal(Placeholders(key), Placeholders(translation));
                tested++;
            }
        }
        Assert.True(tested >= 10, "Embedded catalogs must contain translated templates.");
    }

    [AvaloniaFact]
    public void Translated_choices_keep_their_original_values_and_user_labels_stay_verbatim()
    {
        L10n.SetLanguage("zh-CN");
        string? changed = null;
        var combo = Ui.Combo(new[] { "Transparent", "White" }, "Transparent", s => s, s => changed = s);
        Assert.Equal(new[] { "透明", "白色" }, combo.Items.Cast<string>().ToArray());
        combo.SelectedIndex = 1;
        Assert.Equal("White", changed);
        Assert.Equal("白色", Ui.Label("White").Text);
        Assert.Equal("White", Ui.RawLabel("White").Text);
        var fonts = Ui.Combo(new[] { "White", "Arial" }, "White", s => s, _ => { }, localize: false);
        Assert.Equal(new[] { "White", "Arial" }, fonts.Items.Cast<string>().ToArray());
    }

    [AvaloniaFact]
    public void Chinese_new_canvas_dialog_creates_the_chosen_canvas()
    {
        L10n.SetLanguage("zh-CN");
        var owner = new Window { Width = 800, Height = 600 };
        owner.Show();
        var result = CanvasDialogs.NewCanvas(owner, SKColors.Blue);
        var dialog = Dialog(owner);
        Assert.Equal("新建画布", dialog.Title);
        var background = dialog.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Items.Cast<object>().Contains("白色"));
        background.SelectedIndex = 1;
        Assert.True(Screenshots.Save(dialog, "localization-new-canvas-zh-CN"));
        Press(dialog, "创建");
        Assert.True(result.IsCompleted);
        Assert.Equal(SKColors.White, result.GetAwaiter().GetResult()!.Background);
        owner.Close();
    }

    [AvaloniaFact]
    public void Chinese_save_prompt_does_not_translate_the_document_name()
    {
        L10n.SetLanguage("zh-CN");
        var owner = new Window { Width = 800, Height = 600 };
        owner.Show();
        var result = Prompts.SaveChanges(owner, "White");
        var dialog = Dialog(owner);
        Assert.Equal("未保存的更改", dialog.Title);
        Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "关闭前是否保存对“White”的更改？");
        Press(dialog, "不保存");
        Assert.True(result.IsCompleted);
        Assert.False(result.GetAwaiter().GetResult());
        owner.Close();
    }

    private static Window Dialog(Window owner)
    {
        Dispatcher.UIThread.RunJobs();
        return Assert.Single(owner.OwnedWindows.Where(w => w.IsVisible));
    }

    private static void Press(Window dialog, string label)
    {
        var button = dialog.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == label);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
}

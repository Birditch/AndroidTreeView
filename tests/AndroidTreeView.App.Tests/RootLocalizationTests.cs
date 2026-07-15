using System.Xml.Linq;
using Xunit;

namespace AndroidTreeView.App.Tests;

public sealed class RootLocalizationTests
{
    [Fact]
    public void English_and_simplified_chinese_resources_have_identical_keys()
    {
        var repositoryRoot = FindRepositoryRoot();
        var resources = Path.Combine(repositoryRoot, "src", "AndroidTreeView.App", "Resources");
        var english = ReadKeys(Path.Combine(resources, "Strings.resx"));
        var chinese = ReadKeys(Path.Combine(resources, "Strings.zh-Hans.resx"));

        Assert.Equal(english, chinese);
        Assert.Contains("root.wizard.title", english);
        Assert.Contains("root.confirm.flash.warning", english);
        Assert.Contains("root.error.flash.unknown", english);
    }

    [Fact]
    public void Flash_warning_message_is_translated_in_both_languages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var resources = Path.Combine(repositoryRoot, "src", "AndroidTreeView.App", "Resources");
        const string key = "root.confirm.flash.warning";

        var english = ReadValue(Path.Combine(resources, "Strings.resx"), key);
        var chinese = ReadValue(Path.Combine(resources, "Strings.zh-Hans.resx"), key);

        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.False(string.IsNullOrWhiteSpace(chinese));
        Assert.NotEqual(key, english);
        Assert.NotEqual(key, chinese);
    }

    private static string[] ReadKeys(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;

    private static string? ReadValue(string path, string key) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .Single(element => string.Equals((string?)element.Attribute("name"), key, StringComparison.Ordinal))
            .Element("value")
            ?.Value;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AndroidTreeView.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AndroidTreeView repository root.");
    }
}

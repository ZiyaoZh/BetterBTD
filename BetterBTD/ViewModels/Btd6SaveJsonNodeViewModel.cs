using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;

namespace BetterBTD.ViewModels;

public sealed class Btd6SaveJsonNodeViewModel
{
    public Btd6SaveJsonNodeViewModel(
        string name,
        string path,
        JToken token,
        int depth)
    {
        Name = name;
        Path = path;
        Type = token.Type.ToString();
        Depth = depth;
        ValuePreview = BuildValuePreview(token);

        Children = [];
        foreach (var child in EnumerateChildren(token, path, depth))
        {
            Children.Add(child);
        }
    }

    public string Name { get; }

    public string Path { get; }

    public string Type { get; }

    public int Depth { get; }

    public string ValuePreview { get; }

    public ObservableCollection<Btd6SaveJsonNodeViewModel> Children { get; }

    public bool HasChildren => Children.Count > 0;

    public string DisplayText => string.IsNullOrWhiteSpace(ValuePreview)
        ? $"{Name} ({Type})"
        : $"{Name}: {ValuePreview}";

    private static IEnumerable<Btd6SaveJsonNodeViewModel> EnumerateChildren(
        JToken token,
        string parentPath,
        int parentDepth)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                var childPath = string.IsNullOrEmpty(parentPath)
                    ? property.Name
                    : $"{parentPath}.{property.Name}";
                yield return new Btd6SaveJsonNodeViewModel(property.Name, childPath, property.Value, parentDepth + 1);
            }
        }
        else if (token is JArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var childPath = $"{parentPath}[{i}]";
                yield return new Btd6SaveJsonNodeViewModel($"[{i}]", childPath, array[i], parentDepth + 1);
            }
        }
    }

    private static string BuildValuePreview(JToken token)
    {
        if (token is JObject obj)
        {
            return $"{{{obj.Count} fields}}";
        }

        if (token is JArray array)
        {
            return $"[{array.Count} items]";
        }

        if (token.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return "null";
        }

        var preview = token.Type == JTokenType.String
            ? token.Value<string>() ?? string.Empty
            : token.ToString(Newtonsoft.Json.Formatting.None);

        return preview.Length <= 160 ? preview : $"{preview[..157]}...";
    }
}

public sealed class Btd6SaveSearchResultViewModel
{
    public required string Path { get; init; }

    public required string Type { get; init; }

    public required string ValuePreview { get; init; }
}

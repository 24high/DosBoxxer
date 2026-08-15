using System.Text;

namespace DosBoxxer.Core.Infrastructure.DosBox;

/// <summary>
/// A loss-preserving representation of a <c>dosbox.conf</c> file.
///
/// The file is an INI variant: <c>[section]</c> headers, <c>key=value</c> pairs and <c>#</c>
/// comments. Unknown lines, blank lines, comments and the original ordering are preserved so
/// that the generated configuration stays readable and diffable against the user's original.
/// </summary>
public sealed class DosBoxConfigDocument
{
    private readonly List<Section> _sections = new();

    private DosBoxConfigDocument()
    {
    }

    /// <summary>Section names in document order.</summary>
    public IReadOnlyList<string> SectionNames => _sections.Select(s => s.Name).ToList();

    public static DosBoxConfigDocument CreateEmpty() => new();

    public static DosBoxConfigDocument Parse(string content)
    {
        var document = new DosBoxConfigDocument();

        // A preamble section holds everything that appears before the first [section] header.
        var current = new Section(string.Empty);
        document._sections.Add(current);

        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
            {
                var name = trimmed[1..^1].Trim();
                current = document._sections.FirstOrDefault(
                    s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

                if (current is null)
                {
                    current = new Section(name);
                    document._sections.Add(current);
                }

                continue;
            }

            current.Lines.Add(line);
        }

        return document;
    }

    public static async Task<DosBoxConfigDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(content);
    }

    public bool HasSection(string name) =>
        _sections.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) && s.Name.Length > 0);

    /// <summary>
    /// Sets <paramref name="key"/> in <paramref name="sectionName"/>, replacing an existing
    /// assignment in place (so surrounding comments keep their meaning) or appending it.
    /// Creates the section when it does not exist yet.
    /// </summary>
    public void SetValue(string sectionName, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var section = GetOrCreateSection(sectionName);
        var newLine = key + "=" + value;

        for (var i = 0; i < section.Lines.Count; i++)
        {
            if (!TryGetKey(section.Lines[i], out var existingKey))
            {
                continue;
            }

            if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
            {
                section.Lines[i] = newLine;
                return;
            }
        }

        InsertBeforeTrailingBlankLines(section, newLine);
    }

    public string? GetValue(string sectionName, string key)
    {
        var section = _sections.FirstOrDefault(
            s => string.Equals(s.Name, sectionName, StringComparison.OrdinalIgnoreCase));

        if (section is null)
        {
            return null;
        }

        foreach (var line in section.Lines)
        {
            if (TryGetKey(line, out var existingKey) &&
                string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
            {
                var index = line.IndexOf('=', StringComparison.Ordinal);
                return line[(index + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Appends raw lines to the end of a section, creating it when necessary. Used for
    /// <c>[autoexec]</c>, whose content is a DOS batch script rather than key/value pairs.
    /// </summary>
    public void AppendLines(string sectionName, IEnumerable<string> lines)
    {
        var section = GetOrCreateSection(sectionName);

        // Remove trailing blank lines so the appended block sits directly below the existing
        // commands, then add exactly one separating blank line if there was content before.
        while (section.Lines.Count > 0 && string.IsNullOrWhiteSpace(section.Lines[^1]))
        {
            section.Lines.RemoveAt(section.Lines.Count - 1);
        }

        if (section.Lines.Count > 0)
        {
            section.Lines.Add(string.Empty);
        }

        section.Lines.AddRange(lines);
    }

    /// <summary>
    /// Applies a free-form block of configuration lines. Lines may switch sections with
    /// <c>[section]</c> headers; <c>key=value</c> lines override the corresponding entry.
    /// Invalid lines are ignored and reported through <paramref name="ignoredLines"/>.
    /// </summary>
    public void ApplyAdditionalLines(string? block, string defaultSection, out IReadOnlyList<string> ignoredLines)
    {
        var ignored = new List<string>();
        ignoredLines = ignored;

        if (string.IsNullOrWhiteSpace(block))
        {
            return;
        }

        var section = defaultSection;

        using var reader = new StringReader(block);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
            {
                section = trimmed[1..^1].Trim();
                continue;
            }

            var separator = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                ignored.Add(trimmed);
                continue;
            }

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();

            if (key.Length == 0 || section.Length == 0)
            {
                ignored.Add(trimmed);
                continue;
            }

            SetValue(section, key, value);
        }
    }

    public string Render()
    {
        var builder = new StringBuilder();

        foreach (var section in _sections)
        {
            if (section.Name.Length == 0)
            {
                if (section.Lines.Count == 0)
                {
                    continue;
                }
            }
            else
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append('[').Append(section.Name).Append("]\n");
            }

            foreach (var line in section.Lines)
            {
                builder.Append(line).Append('\n');
            }
        }

        return builder.ToString();
    }

    private Section GetOrCreateSection(string name)
    {
        var section = _sections.FirstOrDefault(
            s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) && s.Name.Length > 0);

        if (section is not null)
        {
            return section;
        }

        section = new Section(name);
        _sections.Add(section);
        return section;
    }

    private static void InsertBeforeTrailingBlankLines(Section section, string newLine)
    {
        var index = section.Lines.Count;
        while (index > 0 && string.IsNullOrWhiteSpace(section.Lines[index - 1]))
        {
            index--;
        }

        section.Lines.Insert(index, newLine);
    }

    private static bool TryGetKey(string line, out string key)
    {
        key = string.Empty;
        var trimmed = line.TrimStart();

        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return false;
        }

        var index = trimmed.IndexOf('=', StringComparison.Ordinal);
        if (index <= 0)
        {
            return false;
        }

        key = trimmed[..index].Trim();
        return key.Length > 0;
    }

    private sealed class Section
    {
        public Section(string name) => Name = name;

        public string Name { get; }

        public List<string> Lines { get; } = new();
    }
}

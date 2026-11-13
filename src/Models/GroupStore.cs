using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using ProjectDock.Services;

namespace ProjectDock.Models;

public sealed class GroupRule
{
    public string Exe { get; set; } = string.Empty;

    public string? Keyword { get; set; }

    public string GroupId { get; set; } = string.Empty;
}

public sealed class GroupStore
{
    private const int AutoGroupLimit = 10;

    private readonly string _path;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public GroupStore()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProjectDock",
            "groups.json");

        Ungrouped = new ProjectGroup("ungrouped", "未分组", "#8C93A3", isUngrouped: true)
        {
            IsExpanded = true,
        };

        Load();
    }

    public ObservableCollection<ProjectGroup> Groups { get; } = new();

    public ProjectGroup Ungrouped { get; }

    public List<GroupRule> Rules { get; } = new();

    public string StorePath => _path;

    public ProjectGroup CreateGroup(string name, bool save = true, string? color = null, bool isAuto = false)
    {
        var index = Groups.Count(g => !g.IsUngrouped);
        var group = new ProjectGroup(Guid.NewGuid().ToString("N"), name.Trim(), color ?? ProjectGroup.NextColor(index))
        {
            IsAuto = isAuto,
        };

        var ungroupedIndex = Groups.IndexOf(Ungrouped);
        if (ungroupedIndex < 0)
        {
            Groups.Add(group);
        }
        else
        {
            Groups.Insert(ungroupedIndex, group);
        }

        if (save)
        {
            Save();
        }

        return group;
    }

    public void RemoveGroup(ProjectGroup group)
    {
        if (group.IsUngrouped)
        {
            return;
        }

        while (group.Windows.Count > 0)
        {
            var item = group.Windows[0];
            group.Windows.RemoveAt(0);
            Ungrouped.Windows.Add(item);
        }

        Rules.RemoveAll(r => r.GroupId == group.Id);
        Groups.Remove(group);
        Save();
    }

    public void MoveTo(WindowItem item, ProjectGroup target, int? index = null)
    {
        ProjectGroup? source = null;

        foreach (var group in Groups)
        {
            if (group.Windows.Contains(item))
            {
                source = group;
                break;
            }
        }

        var sameGroup = ReferenceEquals(source, target);
        if (sameGroup && index is null)
        {
            return;
        }

        var current = target.Windows.IndexOf(item);
        var insert = index ?? target.Windows.Count;

        if (current >= 0)
        {
            target.Windows.RemoveAt(current);
            if (insert > current)
            {
                insert--;
            }
        }
        else
        {
            source?.Windows.Remove(item);
        }

        insert = Math.Clamp(insert, 0, target.Windows.Count);
        target.Windows.Insert(insert, item);
        target.IsExpanded = true;

        if (sameGroup || target.IsUngrouped)
        {
            Save();
            return;
        }

        var key = DetectKeyword(item);
        Rules.RemoveAll(r =>
            string.Equals(r.Exe, item.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Keyword ?? string.Empty, key, StringComparison.OrdinalIgnoreCase));
        Rules.Add(new GroupRule { Exe = item.ProcessName, Keyword = key, GroupId = target.Id });
        Save();
    }

    public void MoveGroup(ProjectGroup group, int index)
    {
        if (group.IsUngrouped)
        {
            return;
        }

        var ordered = Groups.Where(g => !g.IsUngrouped).ToList();
        var from = ordered.IndexOf(group);
        if (from < 0)
        {
            return;
        }

        ordered.RemoveAt(from);
        if (index > from)
        {
            index--;
        }

        index = Math.Clamp(index, 0, ordered.Count);
        ordered.Insert(index, group);

        for (var i = 0; i < ordered.Count; i++)
        {
            var current = Groups.IndexOf(ordered[i]);
            if (current != i)
            {
                Groups.Move(current, i);
            }
        }

        Save();
    }

    public ProjectGroup Assign(WindowInfo info)
    {
        var rule = Rules.LastOrDefault(r =>
            string.Equals(r.Exe, info.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(r.Keyword) ||
             info.Title.Contains(r.Keyword, StringComparison.OrdinalIgnoreCase)));

        if (rule is not null)
        {
            var mapped = Groups.FirstOrDefault(g => g.Id == rule.GroupId);
            if (mapped is not null)
            {
                return mapped;
            }
        }

        var hint = WindowService.DetectProjectName(info);
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var existing = Groups.FirstOrDefault(g =>
                !g.IsUngrouped &&
                string.Equals(g.Name, hint, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }

            if (Groups.Count(g => !g.IsUngrouped) < AutoGroupLimit)
            {
                return CreateGroup(hint, save: false, isAuto: true);
            }
        }

        return Ungrouped;
    }

    public ProjectGroup? FindById(string id) => Groups.FirstOrDefault(g => g.Id == id);

    public void PruneAutoGroups()
    {
        var stale = Groups
            .Where(g => g.IsAuto && g.Windows.Count == 0 && Rules.All(r => r.GroupId != g.Id))
            .ToList();

        foreach (var group in stale)
        {
            Groups.Remove(group);
        }
    }

    public void Save()
    {
        try
        {
            var dto = new StoreDto
            {
                Groups = Groups
                    .Where(g => !g.IsUngrouped)
                    .Select(g => new GroupDto
                    {
                        Id = g.Id,
                        Name = g.Name,
                        Color = g.ColorHex,
                        Expanded = g.IsExpanded,
                    })
                    .ToList(),
                Rules = Rules
                    .Select(r => new RuleDto { Exe = r.Exe, Keyword = r.Keyword, GroupId = r.GroupId })
                    .ToList(),
            };

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(dto, _json));
            File.Move(temp, _path, true);
        }
        catch
        {
            // 配置写失败不应影响使用
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                Groups.Add(Ungrouped);
                return;
            }

            var dto = JsonSerializer.Deserialize<StoreDto>(File.ReadAllText(_path), _json);
            if (dto is not null)
            {
                foreach (var item in dto.Groups)
                {
                    if (string.IsNullOrWhiteSpace(item.Name) || item.Id == Ungrouped.Id)
                    {
                        continue;
                    }

                    Groups.Add(new ProjectGroup(item.Id, item.Name, item.Color, isUngrouped: false)
                    {
                        IsExpanded = item.Expanded,
                    });
                }

                foreach (var rule in dto.Rules)
                {
                    if (!string.IsNullOrWhiteSpace(rule.GroupId))
                    {
                        Rules.Add(new GroupRule
                        {
                            Exe = rule.Exe,
                            Keyword = rule.Keyword,
                            GroupId = rule.GroupId,
                        });
                    }
                }
            }
        }
        catch
        {
            Groups.Clear();
        }

        Groups.Add(Ungrouped);
    }

    private static string DetectKeyword(WindowItem item)
    {
        var info = new WindowInfo
        {
            Hwnd = item.Hwnd,
            Title = item.Title,
            ProcessName = item.ProcessName,
            ExePath = item.ExePath,
        };

        var hint = WindowService.DetectProjectName(info);
        if (!string.IsNullOrWhiteSpace(hint))
        {
            return hint;
        }

        return item.Title.Length > 40 ? item.Title[..40] : item.Title;
    }

    private sealed class StoreDto
    {
        public List<GroupDto> Groups { get; set; } = new();

        public List<RuleDto> Rules { get; set; } = new();
    }

    private sealed class GroupDto
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Color { get; set; } = "#5B8CFF";

        public bool Expanded { get; set; } = true;
    }

    private sealed class RuleDto
    {
        public string Exe { get; set; } = string.Empty;

        public string? Keyword { get; set; }

        public string GroupId { get; set; } = string.Empty;
    }
}

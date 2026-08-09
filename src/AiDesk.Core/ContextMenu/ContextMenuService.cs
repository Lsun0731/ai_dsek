using Microsoft.Win32;

namespace AiDesk.Core.ContextMenu;

/// <summary>
/// 右键菜单管理服务：枚举、禁用/启用、删除注册表中的右键菜单项。
/// 所有写操作都需要管理员权限（HKCR 写入）。
/// </summary>
public sealed class ContextMenuService
{
    /// <summary>读取注册表所用的视图；64 位视图包含 32/64 位合并内容。</summary>
    private const RegistryView View = RegistryView.Registry64;

    private static RegistryKey OpenRoot() =>
        RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, View);

    /// <summary>
    /// 枚举所有位置的全部右键菜单项。
    /// 单个子键读取失败（权限/损坏）时跳过，不中断整体枚举。
    /// </summary>
    public IReadOnlyList<ContextMenuItem> Enumerate()
    {
        var items = new List<ContextMenuItem>();
        foreach (var location in Enum.GetValues<ContextMenuLocation>())
        {
            using var root = OpenRoot();
            using var parent = root.OpenSubKey(ContextMenuPathBuilder.GetPath(location));
            if (parent is null)
                continue;

            foreach (var subKeyName in parent.GetSubKeyNames())
            {
                try
                {
                    using var subKey = parent.OpenSubKey(subKeyName);
                    if (subKey is null)
                        continue;

                    var isExtension = IsExtensionLocation(location);
                    var command = isExtension
                        ? subKey.GetValue(null) as string
                        : ReadShellCommand(subKey);

                    items.Add(new ContextMenuItem
                    {
                        Name = subKeyName,
                        RawKeyName = subKeyName,
                        IsExtension = isExtension,
                        Location = location,
                        RegistryPath = ContextMenuPathBuilder.GetItemPath(location, subKeyName),
                        Command = command,
                    });
                }
                catch
                {
                    // 单个项读取失败不影响整体枚举
                }
            }
        }
        return items;
    }

    /// <summary>
    /// 禁用或启用一个菜单项。
    /// 原理：将注册表子键名前加/去 "-" 前缀（Windows 原生禁用技巧，安全可逆）。
    /// </summary>
    public void SetEnabled(ContextMenuItem item, bool enabled)
    {
        var disabled = item.IsDisabled;
        if (disabled == !enabled)
            return;

        var newName = enabled
            ? item.RawKeyName.TrimStart('-')
            : "-" + item.RawKeyName.TrimStart('-');

        using var root = OpenRoot();
        using var parent = root.OpenSubKey(ContextMenuPathBuilder.GetPath(item.Location), writable: true)
            ?? throw new InvalidOperationException($"无法打开注册表项：{item.Location}");

        var oldName = item.RawKeyName;

        // RegistryKey 无重命名 API：复制值到新键后删除旧键
        using (var oldKey = parent.OpenSubKey(oldName))
        {
            if (oldKey is null)
                throw new InvalidOperationException($"注册表项不存在：{item.RegistryPath}");

            using var newKey = parent.CreateSubKey(newName);
            foreach (var valueName in oldKey.GetValueNames())
                newKey.SetValue(valueName, oldKey.GetValue(valueName) ?? string.Empty, oldKey.GetValueKind(valueName));
            // 复制子键树（如 shell 项常见的 command 子键）
            CopySubKeys(oldKey, newKey);
        }
        parent.DeleteSubKeyTree(oldName, throwOnMissingSubKey: false);
    }

    /// <summary>删除一个菜单项（不可逆，UI 层必须二次确认）。</summary>
    public void Delete(ContextMenuItem item)
    {
        using var root = OpenRoot();
        using var parent = root.OpenSubKey(ContextMenuPathBuilder.GetPath(item.Location), writable: true)
            ?? throw new InvalidOperationException($"无法打开注册表项：{item.Location}");

        parent.DeleteSubKeyTree(item.RawKeyName, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// 只读匹配推荐精简清单在当前系统中的命中项（按位置 + 键名精确匹配，跳过已禁用的）。
    /// </summary>
    public IReadOnlyList<(RecommendedDisableItem Definition, ContextMenuItem Item)> FindRecommended(
        IReadOnlyList<RecommendedDisableItem> items)
    {
        var all = Enumerate();
        var map = all
            .Where(i => !i.IsDisabled)
            .ToDictionary(i => (i.Location, i.RawKeyName), i => i);

        var result = new List<(RecommendedDisableItem, ContextMenuItem)>();
        foreach (var rec in items)
        {
            if (map.TryGetValue((rec.Location, rec.KeyName), out var item))
                result.Add((rec, item));
        }
        return result;
    }

    /// <summary>按推荐清单批量禁用，返回实际禁用的数量（不存在的项自动跳过）。</summary>
    public int DisableRecommended(IReadOnlyList<RecommendedDisableItem> items)
    {
        var matched = FindRecommended(items);
        foreach (var (_, item) in matched)
            SetEnabled(item, false);
        return matched.Count;
    }

    /// <summary>shell 命令读取：子键默认值优先，其次 command 子键的默认值。</summary>
    private static string? ReadShellCommand(RegistryKey subKey)
    {
        var value = subKey.GetValue(null) as string;
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        using var commandKey = subKey.OpenSubKey("command");
        return commandKey?.GetValue(null) as string;
    }

    private static bool IsExtensionLocation(ContextMenuLocation location) => location switch
    {
        ContextMenuLocation.FileExtensions or
        ContextMenuLocation.DirectoryExtensions or
        ContextMenuLocation.DirectoryBackgroundExtensions or
        ContextMenuLocation.DesktopBackgroundExtensions or
        ContextMenuLocation.FolderExtensions or
        ContextMenuLocation.DriveExtensions => true,
        _ => false,
    };

    private static void CopySubKeys(RegistryKey source, RegistryKey destination)
    {
        foreach (var childName in source.GetSubKeyNames())
        {
            using var child = source.OpenSubKey(childName);
            if (child is null)
                continue;
            using var newChild = destination.CreateSubKey(childName);
            foreach (var valueName in child.GetValueNames())
                newChild.SetValue(valueName, child.GetValue(valueName) ?? string.Empty, child.GetValueKind(valueName));
            CopySubKeys(child, newChild);
        }
    }
}

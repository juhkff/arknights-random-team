using System.Xml;
using arknights_random_team.Models;

namespace arknights_random_team.Domain;

/// <summary>
/// 干员字段的校验规则，录入页 / 详情面板 / 表格内联编辑共用一份。
///
/// 原来三处各写一套：录入页查空名与重名，详情面板只查空名与等级，
/// 表格名称列可以直接写成空或与别人重名。名称在生成阵容时是分组键，
/// 重名会让「已入池人数」与「实际可抽人数」不一致，所以不能只在其中一个入口拦住。
/// </summary>
internal static class StaffValidator
{
    public const string EmptyNameMessage = "请输入干员名称";
    public const string DuplicateNameMessage = "列表中已有该干员";
    public const string InvalidNameMessage = "名称包含无法保存的控制字符";
    public const string InvalidLevelMessage = "等级格式应为「精二90级」";

    /// <summary>名称校验；<paramref name="self"/> 用于编辑场景排除自己。</summary>
    public static string? ValidateName(string? rawName, IEnumerable<Staff> existing, Staff? self = null)
    {
        var name = rawName?.Trim() ?? "";
        if (name.Length == 0)
            return EmptyNameMessage;

        try
        {
            XmlConvert.VerifyXmlChars(name);
        }
        catch (XmlException)
        {
            return InvalidNameMessage;
        }

        foreach (var staff in existing)
        {
            if (ReferenceEquals(staff, self))
                continue;
            if (string.Equals(staff.Name.Trim(), name, StringComparison.Ordinal))
                return DuplicateNameMessage;
        }

        return null;
    }

    public static string? ValidateLevel(string? text) =>
        Level.TryParse(text, out _, out _) ? null : InvalidLevelMessage;
}

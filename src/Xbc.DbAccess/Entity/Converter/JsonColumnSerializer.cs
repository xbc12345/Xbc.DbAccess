using System.Text.Json;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// JSON 列的序列化收口：写路径把属性值按声明类型序列化为字符串，读路径把 JSON 文本按属性类型反序列化。
    /// <para>序列化选项使用 System.Text.Json 默认设置（PascalCase 属性名、不缩进）。</para>
    /// </summary>
    internal static class JsonColumnSerializer
    {
        /// <summary>
        /// 把属性值序列化为 JSON 文本（按属性的声明类型序列化，保证与反序列化类型对称）。
        /// </summary>
        /// <param name="value">属性值，不为 null（null 由调用方先行处理为 DBNull）。</param>
        /// <param name="declaredType">属性声明类型。</param>
        /// <returns>JSON 文本。</returns>
        public static string Serialize(object value, Type declaredType)
        {
            return JsonSerializer.Serialize(value, declaredType);
        }

        /// <summary>
        /// 把 JSON 文本反序列化为属性值。
        /// </summary>
        /// <param name="json">JSON 文本。</param>
        /// <param name="declaredType">属性声明类型。</param>
        /// <returns>反序列化的属性值。</returns>
        public static object Deserialize(string json, Type declaredType)
        {
            return JsonSerializer.Deserialize(json, declaredType);
        }
    }
}

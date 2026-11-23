using Xbc.DbAccess.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    /// <summary>
    /// ParameterNameSanitizer 的单元测试：空基名的兜底前缀、非法字符的 Unicode 码点转义与首字符补前缀。
    /// </summary>
    public class ParameterNameSanitizerTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void CreateSafeName_NullOrEmpty_ReturnsFallbackPrefix(string baseName)
        {
            // 空基名兜底为 "p_"（不抛异常）：调用方无需预先判空
            Assert.Equal("p_", ParameterNameSanitizer.CreateSafeName(baseName));
        }

        [Theory]
        [InlineData("Name", "Name")]
        [InlineData("My Col", "Myu0020Col")]
        [InlineData("名称", "u540Du79F0")]
        [InlineData("1Price", "p_1Price")]
        [InlineData("_name", "p__name")]
        [InlineData("a1_b2", "a1_b2")]
        [InlineData("状态", "u72B6u6001")]
        public void CreateSafeName_ReplacesIllegalCharsAndPrefixesFirstChar(string baseName, string expected)
        {
            Assert.Equal(expected, ParameterNameSanitizer.CreateSafeName(baseName));
        }

        [Fact]
        public void CreateSafeName_DistinctChineseNames_StayDistinct()
        {
            // 中文列名共存是常态场景：不同中文基名清理后必须可辨识（不再统一替换为下划线而完全相同），
            // 既让分配器序号后缀兜底不再损失可观测性，也让直传参数路径不再因重名失败
            var name = ParameterNameSanitizer.CreateSafeName("名称");
            var status = ParameterNameSanitizer.CreateSafeName("状态");

            Assert.NotEqual(name, status);
            Assert.Equal("u540Du79F0", name);
            Assert.Equal("u72B6u6001", status);
        }

        [Theory]
        [InlineData("u540Du79F0")]
        [InlineData("p_1Price")]
        [InlineData("Name")]
        public void CreateSafeName_IsIdempotent(string cleanedName)
        {
            // 清理幂等：已清理的名称再清理保持不变（分配器与调用方可重复清理而不变形）
            Assert.Equal(cleanedName, ParameterNameSanitizer.CreateSafeName(cleanedName));
        }

        [Fact]
        public void CreateSafeName_AllEscapedForms_StartWithAsciiLetter()
        {
            // 纯中文基名转义后以 u 开头（ASCII 字母），无需 p_ 前缀；数字开头仍补前缀
            Assert.StartsWith("u", ParameterNameSanitizer.CreateSafeName("名称"));
            Assert.StartsWith("p_", ParameterNameSanitizer.CreateSafeName("1Price"));
        }
    }
}

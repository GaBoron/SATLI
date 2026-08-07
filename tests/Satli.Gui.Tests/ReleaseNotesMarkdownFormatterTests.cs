using Satli_Gui.Controls;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class ReleaseNotesMarkdownFormatterTests
{
    private static readonly Uri RepositoryUri = new("https://github.com/example/project/");

    [Fact]
    public void FormatsGithubReleaseMarkdownFeatures()
    {
        var markdown = """
            ## 修复

            - [x] 支持任务列表
            - 支持 ~~旧文本~~ 和 `行内代码`

            | 类型 | 内容 |
            | --- | --- |
            | 修复 | 刷新问题 |

            ```json
            {"enabled":true}
            ```

            [查看详情](docs/USAGE.md)
            """;

        var html = ReleaseNotesMarkdownFormatter.ToHtml(markdown, RepositoryUri, false);

        Assert.Contains("<h2", html);
        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("<del>旧文本</del>", html);
        Assert.Contains("<code>行内代码</code>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("class=\"language-json\"", html);
        Assert.Contains("href=\"docs/USAGE.md\"", html);
        Assert.Contains("<base href=\"https://github.com/example/project/\">", html);
    }

    [Fact]
    public void DisablesRawHtmlAndScripts()
    {
        const string markdown = "<script>alert('bad')</script>\n\n[危险链接](javascript:alert('bad'))";

        var html = ReleaseNotesMarkdownFormatter.ToHtml(markdown, RepositoryUri, true);

        Assert.DoesNotContain("<script>alert", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("script-src", html);
        Assert.Contains("color-scheme: dark", html);
    }
}

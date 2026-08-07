using System.Net;
using Markdig;

namespace Satli_Gui.Controls;

public static class ReleaseNotesMarkdownFormatter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static string ToHtml(string markdown, Uri baseUri, bool useDarkTheme)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        var body = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        var foreground = useDarkTheme ? "#f5f5f5" : "#1f1f1f";
        var secondary = useDarkTheme ? "#b8b8b8" : "#5d5d5d";
        var border = useDarkTheme ? "#454545" : "#d0d0d0";
        var surface = useDarkTheme ? "#292929" : "#f3f3f3";
        var accent = useDarkTheme ? "#75b7ff" : "#005fb8";
        var encodedBaseUri = WebUtility.HtmlEncode(baseUri.AbsoluteUri);

        return $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'none'; img-src https: data:; style-src 'unsafe-inline'; form-action 'none'">
              <base href="{{encodedBaseUri}}">
              <style>
                :root { color-scheme: {{(useDarkTheme ? "dark" : "light")}}; }
                * { box-sizing: border-box; }
                html, body { margin: 0; padding: 0; background: transparent; color: {{foreground}}; }
                body { font: 14px/1.55 "Segoe UI", "Microsoft YaHei UI", sans-serif; overflow-wrap: anywhere; }
                body > :first-child { margin-top: 0; }
                body > :last-child { margin-bottom: 0; }
                h1, h2, h3, h4, h5, h6 { line-height: 1.3; margin: 1.2em 0 .55em; font-weight: 600; }
                h1 { font-size: 1.55em; }
                h2 { font-size: 1.35em; padding-bottom: .25em; border-bottom: 1px solid {{border}}; }
                h3 { font-size: 1.18em; }
                h4, h5, h6 { font-size: 1em; }
                p { margin: .6em 0; }
                ul, ol { margin: .55em 0; padding-left: 2em; }
                li + li { margin-top: .22em; }
                li > p { margin: .25em 0; }
                a { color: {{accent}}; text-decoration: none; }
                a:hover { text-decoration: underline; }
                blockquote { margin: .75em 0; padding: .1em 1em; color: {{secondary}}; border-left: .25em solid {{border}}; }
                blockquote > :first-child { margin-top: 0; }
                blockquote > :last-child { margin-bottom: 0; }
                code { padding: .15em .35em; border-radius: 4px; background: {{surface}}; font-family: Consolas, "Cascadia Mono", monospace; font-size: .92em; }
                pre { margin: .75em 0; padding: .8em 1em; overflow: auto; border: 1px solid {{border}}; border-radius: 6px; background: {{surface}}; }
                pre code { padding: 0; background: transparent; font-size: .9em; white-space: pre; overflow-wrap: normal; }
                hr { height: 1px; margin: 1em 0; border: 0; background: {{border}}; }
                table { display: block; width: max-content; max-width: 100%; margin: .75em 0; overflow: auto; border-spacing: 0; border-collapse: collapse; }
                th, td { padding: .4em .7em; border: 1px solid {{border}}; text-align: left; }
                th { background: {{surface}}; font-weight: 600; }
                tr:nth-child(even) { background: color-mix(in srgb, {{surface}} 55%, transparent); }
                img { max-width: 100%; height: auto; }
                input[type="checkbox"] { margin: 0 .45em 0 -1.4em; vertical-align: middle; accent-color: {{accent}}; }
                del { color: {{secondary}}; }
              </style>
            </head>
            <body>{{body}}</body>
            </html>
            """;
    }
}

using AngleSharp.Html.Parser;
using Markdig;

namespace MeetupOrganiser.Web;

public static class MeetupMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml().UseSoftlineBreakAsHardlineBreak().Build();

    public static string ToHtml(string? markdown, bool preserveHeadings = false)
    {
        var document = new HtmlParser().ParseDocument(Markdown.ToHtml(markdown ?? "", Pipeline));
        foreach (var element in document.Body!.QuerySelectorAll("*"))
        {
            var href = element.GetAttribute("href");
            var alt = element.GetAttribute("alt");
            foreach (var attribute in element.Attributes.ToArray())
                element.RemoveAttribute(attribute.Name);
            if (element.LocalName == "img")
            {
                element.Replace(document.CreateTextNode(alt ?? ""));
                continue;
            }
            if (element.LocalName == "a" && Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
                uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo))
            {
                element.SetAttribute("href", uri.AbsoluteUri);
                element.SetAttribute("rel", "noopener noreferrer");
            }
            if (!preserveHeadings && element.LocalName is ("h1" or "h2" or "h3" or "h4" or "h5" or "h6"))
            {
                var paragraph = document.CreateElement("p");
                var strong = document.CreateElement("strong");
                while (element.FirstChild is { } child) strong.AppendChild(child);
                paragraph.AppendChild(strong);
                element.Replace(paragraph);
            }
        }
        return document.Body.InnerHtml;
    }
}

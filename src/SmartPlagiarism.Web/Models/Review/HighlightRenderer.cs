using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartPlagiarism.Core.Review;

namespace SmartPlagiarism.Web.Models.Review;

/// <summary>
/// Turns a document's segments into the HTML of one match-viewer pane.
///
/// Every piece of text goes through <see cref="HtmlContentBuilder.Append(string)"/>,
/// which HTML-encodes it, so whatever a student's document contains - angle
/// brackets, script tags, entities - is shown as text and never interpreted.
/// Built in code rather than in Razor because the pane preserves whitespace, and
/// the indentation between Razor tags would show up as stray spaces.
/// </summary>
public static class HighlightRenderer
{
    public static IHtmlContent Render(IEnumerable<HighlightSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var html = new HtmlContentBuilder();

        foreach (var segment in segments)
        {
            if (segment.FragmentIndex is not { } fragment)
            {
                html.Append(segment.Text);
                continue;
            }

            var passage = (fragment + 1).ToString(CultureInfo.InvariantCulture);
            var mark = new TagBuilder("mark");

            mark.AddCssClass("match-fragment");
            mark.Attributes["data-fragment"] = fragment.ToString(CultureInfo.InvariantCulture);
            mark.Attributes["tabindex"] = "0";
            mark.Attributes["role"] = "button";
            mark.Attributes["title"] = $"Passage {passage} - show it in the other document";
            mark.InnerHtml.Append(segment.Text);

            html.AppendHtml(mark);
        }

        return html;
    }
}

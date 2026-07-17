using System.Net;
using System.Text.RegularExpressions;

namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Parser del HTML SSR de blitz.gg/lol/aram-mayhem-augments. Se ancla en la
/// ESTRUCTURA estable (h3 de sección, div.augment con id numérico, alt="Tier N",
/// h4.name) y nunca en los hashes de clase de Svelte, que cambian por deploy.
/// </summary>
public static class BlitzAugmentParser
{
    private static readonly Regex SectionRx = new(
        "<h3[^>]*>\\s*(Prismatic|Gold|Silver)\\s+ARAM\\s+Mayhem\\s+Augments\\s*</h3>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CardRx = new(
        "<div class=\"augment card-container[^\"]*\"[^>]*\\bid=\"(?<id>\\d+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NameRx = new(
        "<h4 class=\"name[^\"]*\"[^>]*>(?<name>.*?)</h4>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TierRx = new(
        "alt=\"Tier (?<tier>[1-5])\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IconRx = new(
        "/augments/(?<slug>[a-z0-9_\\-]+\\.webp)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DescRx = new(
        "<p class=\"rich-description[^\"]*\"[^>]*>(?<desc>.*?)</p>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ChampRx = new(
        "/lol/champions/(?<key>[^/\"]+)/aram-mayhem",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagRx = new("<[^>]+>", RegexOptions.Compiled);

    public static AugmentTierList Parse(string html)
    {
        var sections = SectionRx.Matches(html);
        if (sections.Count == 0)
            return new AugmentTierList();

        var augments = new List<AugmentInfo>();
        var seenIds = new HashSet<int>();
        for (var s = 0; s < sections.Count; s++)
        {
            var rarity = Enum.Parse<AugmentRarity>(sections[s].Groups[1].Value, ignoreCase: true);
            var start = sections[s].Index + sections[s].Length;
            var end = s + 1 < sections.Count ? sections[s + 1].Index : html.Length;
            var body = html[start..end];

            var cards = CardRx.Matches(body);
            for (var c = 0; c < cards.Count; c++)
            {
                var cardEnd = c + 1 < cards.Count ? cards[c + 1].Index : body.Length;
                var card = body[cards[c].Index..cardEnd];
                var name = NameRx.Match(card);
                var id = int.Parse(cards[c].Groups["id"].Value);
                if (!name.Success || !seenIds.Add(id))
                    continue;
                augments.Add(new AugmentInfo
                {
                    Id = id,
                    Name = Clean(name.Groups["name"].Value),
                    Rarity = rarity,
                    Tier = TierRx.Match(card) is { Success: true } t
                        ? int.Parse(t.Groups["tier"].Value) : null,
                    IconSlug = IconRx.Match(card) is { Success: true } i
                        ? i.Groups["slug"].Value.ToLowerInvariant() : "",
                    Description = DescRx.Match(card) is { Success: true } d
                        ? Clean(d.Groups["desc"].Value) : "",
                    TopChampions = ChampRx.Matches(card)
                        .Select(m => m.Groups["key"].Value).Distinct().ToArray(),
                });
            }
        }
        return new AugmentTierList { Augments = augments };
    }

    /// <summary>Sin tags, entidades decodificadas y espacios colapsados.</summary>
    private static string Clean(string fragment) =>
        Regex.Replace(WebUtility.HtmlDecode(TagRx.Replace(fragment, " ")), "\\s+", " ").Trim();
}

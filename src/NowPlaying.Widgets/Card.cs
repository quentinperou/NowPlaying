using System.Text.Json;

namespace NowPlaying.Widgets;

/// <summary>
/// The widget as the Widgets board draws it: an Adaptive Card template (the same for every size, parts chosen with
/// $host.widgetSize, looks with $host.hostTheme) and its data. Over the cover blurred (see <see cref="Artwork"/>):
/// the cover, the text, the controls as icons (clickable images, not the board's grey buttons); dark with white text
/// on a dark board, light with dark text on a light one, where Windows writes the widget's name in dark over the
/// background. Texts in English and French, as NowPlaying is set.
/// </summary>
internal static class Card
{
    // The text over the background: white on a dark board (dark background), the theme's dark text on a light one.
    private const string Fg = "${if($host.hostTheme == 'dark', 'Light', 'Default')}";

    // Previous, play / pause, next, at the bottom of the widget (a blank line under them: off its edge).
    private const string Controls = """
        {
          "type": "Container", "height": "stretch", "verticalContentAlignment": "Bottom",
          "items": [
            {
              "type": "ColumnSet", "horizontalAlignment": "Center",
              "columns": [
                {
                  "type": "Column", "width": "auto", "verticalContentAlignment": "Center",
                  "items": [ { "type": "Image", "url": "${if($host.hostTheme == 'dark', previousIcon, previousIconOnLight)}", "altText": "${previous}", "width": "22px", "height": "22px",
                    "selectAction": { "type": "Action.Execute", "verb": "previous", "tooltip": "${previous}" } } ]
                },
                {
                  "type": "Column", "width": "auto", "spacing": "ExtraLarge", "verticalContentAlignment": "Center",
                  "items": [ { "type": "Image", "url": "${if($host.hostTheme == 'dark', playIcon, playIconOnLight)}", "altText": "${playPause}", "width": "44px", "height": "44px",
                    "selectAction": { "type": "Action.Execute", "verb": "playpause", "tooltip": "${playPause}" } } ]
                },
                {
                  "type": "Column", "width": "auto", "spacing": "ExtraLarge", "verticalContentAlignment": "Center",
                  "items": [ { "type": "Image", "url": "${if($host.hostTheme == 'dark', nextIcon, nextIconOnLight)}", "altText": "${next}", "width": "22px", "height": "22px",
                    "selectAction": { "type": "Action.Execute", "verb": "next", "tooltip": "${next}" } } ]
                }
              ]
            },
            { "type": "TextBlock", "text": " ", "size": "Small", "spacing": "None" }
          ]
        }
        """;

    public const string Template = $$"""
    {
      "type": "AdaptiveCard",
      "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
      "version": "1.5",
      "backgroundImage": { "url": "${if($host.hostTheme == 'dark', background, backgroundOnLight)}", "fillMode": "cover" },
      "body": [
        {
          "type": "Container",
          "$when": "${state == 'offline' || state == 'none'}",
          "height": "stretch", "verticalContentAlignment": "Center",
          "items": [
            { "type": "TextBlock", "text": "${message}", "weight": "Bolder", "size": "Medium", "color": "{{Fg}}", "wrap": true },
            { "type": "TextBlock", "text": "${hint}", "color": "{{Fg}}", "isSubtle": true, "wrap": true },
            {
              "type": "ActionSet", "$when": "${state == 'offline'}",
              "actions": [ { "type": "Action.Execute", "title": "${open}", "verb": "open" } ]
            }
          ]
        },
        {
          "type": "Container",
          "$when": "${(state == 'playing' || state == 'paused') && $host.widgetSize == 'small'}",
          "height": "stretch",
          "items": [
            {
              "type": "ColumnSet",
              "columns": [
                {
                  "type": "Column", "width": "auto", "$when": "${cover != ''}",
                  "items": [ { "type": "Image", "url": "${cover}", "altText": "${title}", "width": "52px", "height": "52px" } ]
                },
                {
                  "type": "Column", "width": "stretch", "verticalContentAlignment": "Center",
                  "items": [ { "type": "Image", "url": "${if($host.hostTheme == 'dark', playIcon, playIconOnLight)}", "altText": "${playPause}", "width": "36px", "height": "36px",
                    "horizontalAlignment": "Right",
                    "selectAction": { "type": "Action.Execute", "verb": "playpause", "tooltip": "${playPause}" } } ]
                }
              ]
            },
            {
              "type": "Container", "height": "stretch", "verticalContentAlignment": "Bottom",
              "items": [
                { "type": "TextBlock", "text": "${title}", "weight": "Bolder", "color": "{{Fg}}", "maxLines": 1 },
                { "type": "TextBlock", "text": "${artist}", "size": "Small", "color": "{{Fg}}", "isSubtle": true, "maxLines": 1, "spacing": "None" }
              ]
            }
          ]
        },
        {
          "type": "Container",
          "$when": "${(state == 'playing' || state == 'paused') && $host.widgetSize == 'medium'}",
          "height": "stretch",
          "items": [
            {
              "type": "ColumnSet",
              "columns": [
                {
                  "type": "Column", "width": "auto", "$when": "${cover != ''}",
                  "items": [ { "type": "Image", "url": "${cover}", "altText": "${title}", "width": "108px", "height": "108px" } ]
                },
                {
                  "type": "Column", "width": "stretch", "verticalContentAlignment": "Center",
                  "items": [
                    { "type": "TextBlock", "text": "${title}", "weight": "Bolder", "size": "Large", "color": "{{Fg}}", "maxLines": 2, "wrap": true },
                    { "type": "TextBlock", "text": "${artist}", "color": "{{Fg}}", "maxLines": 1, "spacing": "Small" },
                    { "type": "TextBlock", "text": "${app}", "size": "Small", "color": "{{Fg}}", "isSubtle": true, "maxLines": 1, "spacing": "None" }
                  ]
                }
              ]
            },
            {{Controls}}
          ]
        },
        {
          "type": "Container",
          "$when": "${(state == 'playing' || state == 'paused') && $host.widgetSize == 'large'}",
          "height": "stretch",
          "items": [
            { "type": "Image", "$when": "${cover != ''}", "url": "${cover}", "altText": "${title}", "width": "176px", "height": "176px", "horizontalAlignment": "Center" },
            { "type": "TextBlock", "text": "${title}", "weight": "Bolder", "size": "Large", "color": "{{Fg}}", "maxLines": 2, "wrap": true, "horizontalAlignment": "Center", "spacing": "Large" },
            { "type": "TextBlock", "text": "${artist}", "color": "{{Fg}}", "maxLines": 1, "spacing": "Small", "horizontalAlignment": "Center" },
            { "type": "TextBlock", "text": "${app}", "size": "Small", "color": "{{Fg}}", "isSubtle": true, "maxLines": 1, "spacing": "None", "horizontalAlignment": "Center" },
            {{Controls}}
          ]
        }
      ]
    }
    """;

    private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        ["en"] = new()
        {
            ["offline"] = "NowPlaying is not running",
            ["offlineHint"] = "Start it to see what plays here.",
            ["none"] = "Nothing playing",
            ["noneHint"] = "Play some music to see it here.",
            ["open"] = "Open NowPlaying",
            ["previous"] = "Previous",
            ["playPause"] = "Play / pause",
            ["next"] = "Next",
        },
        ["fr"] = new()
        {
            ["offline"] = "NowPlaying n'est pas lancé",
            ["offlineHint"] = "Lancez-le pour voir ici ce qui joue.",
            ["none"] = "Rien en lecture",
            ["noneHint"] = "Lancez une musique pour la voir ici.",
            ["open"] = "Ouvrir NowPlaying",
            ["previous"] = "Précédent",
            ["playPause"] = "Lecture / pause",
            ["next"] = "Suivant",
        },
    };

    /// <summary>The data of the template for what plays now.</summary>
    public static string Data(NowState now)
    {
        var t = Texts[AppLink.Language];
        string state = !now.Online ? "offline" : !now.HasTrack ? "none" : now.Playing ? "playing" : "paused";
        bool track = state is "playing" or "paused";
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["state"] = state,
            ["message"] = state == "offline" ? t["offline"] : t["none"],
            ["hint"] = state == "offline" ? t["offlineHint"] : t["noneHint"],
            ["title"] = now.Title,
            ["artist"] = now.Artist,
            ["app"] = now.App,
            ["cover"] = now.Cover,
            ["background"] = track && now.Background.Length > 0 ? now.Background : Artwork.DefaultBackground,
            ["backgroundOnLight"] = track && now.BackgroundOnLight.Length > 0 ? now.BackgroundOnLight : Artwork.DefaultBackgroundOnLight,
            ["playIcon"] = now.Playing ? Artwork.Pause : Artwork.Play,
            ["playIconOnLight"] = now.Playing ? Artwork.PauseOnLight : Artwork.PlayOnLight,
            ["previousIcon"] = Artwork.Previous,
            ["previousIconOnLight"] = Artwork.PreviousOnLight,
            ["nextIcon"] = Artwork.Next,
            ["nextIconOnLight"] = Artwork.NextOnLight,
            ["previous"] = t["previous"],
            ["playPause"] = t["playPause"],
            ["next"] = t["next"],
            ["open"] = t["open"],
        });
    }
}

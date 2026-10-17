# Double-curly-brace template parameters

[Documentation home](../README.md) | [Quickstarts](QUICKSTARTS.md) | [PPTX authoring](PPTX-TEMPLATES.md) | [User guide](USER-GUIDE.md)

The app has **three independent text-template systems**, plus PPTX picture alt-text markers. They all use `{{...}}` syntax, but their parameter names are not interchangeable.

Use the exact tokens below. Names are case-insensitive. For PPTX, avoid whitespace inside braces and keep a token in one slide paragraph with consistent formatting. Unknown tokens can remain visible rather than being rejected.

## PPTX: current event

**Where:** text boxes on slides in the group's uploaded `.pptx`. See [authoring rules](PPTX-TEMPLATES.md#text-placeholder-rules).

| Parameter | Meaning |
| --- | --- |
| `{{SpeakerName}}` | First speaker's name |
| `{{Speaker1Name}}` | Alias for first speaker's name |
| `{{Speaker2Name}}` | Second speaker's name; classifies slide as two-speaker |
| `{{TalkTitle}}` | First talk title; event-title fallback |
| `{{Talk1Title}}` | Alias for first talk title |
| `{{Talk2Title}}` | Second talk title; classifies slide as two-speaker |
| `{{ShortUrl}}` | Event short URL, Meetup URL, or default |
| `{{EventDate}}` | Legacy long date based on stored date, with literal `th` suffix |
| `{{EventTime}}` | UK start time with `UK time` suffix |
| `{{Venue}}` | Event Location / Platform, or `Online` |
| `{{EventDateLine}}` | Uppercase UK date with correct ordinal; preferred date token |
| `{{EventVenueLine}}` | Format-aware uppercase venue and BST/GMT start-time line |
| `{{EventFormat}}` | Hybrid / In Person / Online |
| `{{EventSubtitle}}` | Optional promo subtitle |

The first/second speaker means session order, not alphabetical order. Current PPTX does **not** support `{{EventTitle}}`, `{{GroupName}}`, `{{SpeakerBio}}`, `{{TalkDescription}}`, `{{Sponsors}}`, or `{{Logo}}`.

## PPTX: next event

**Where:** slide text in the same uploaded deck. The deck download chooses the earliest later event in the same group, including drafts.

| Parameter | Meaning |
| --- | --- |
| `{{NextEventTitle}}` | Next event title |
| `{{NextEventDate}}` | Next event legacy long date |
| `{{NextEventTime}}` | Next event UK start time |
| `{{NextEventDateLine}}` | Next event uppercase UK date header |
| `{{NextEventVenue}}` | Next event Location / Platform |
| `{{NextEventVenueLine}}` | Next event format-aware venue/time header |
| `{{NextEventFormat}}` | Next event format |
| `{{NextEventSubtitle}}` | Next event optional subtitle |
| `{{NextEventShortUrl}}` | Next event short URL or Meetup URL |
| `{{NextEventSpeakerName}}` | First next-event speaker |
| `{{NextEventSpeaker1Name}}` | Alias for first next-event speaker |
| `{{NextEventSpeaker2Name}}` | Second next-event speaker |
| `{{NextEventSpeakers}}` | Next-event speakers joined with ` & ` |
| `{{NextEventTalkTitle}}` | First next-event talk title |
| `{{NextEventTalk1Title}}` | Alias for first next-event talk title |
| `{{NextEventTalk2Title}}` | Second next-event talk title |

Missing values generally become `TBC`, except an empty subtitle. Slides whose text tokens are **all next-event tokens** are hidden when no later event exists. Keep current-event and next-event tokens on separate slides for predictable photo/visibility selection.

## PPTX: picture alt-text markers

**Where:** the **description or title in a picture's Alt Text**, not a textbox, notes field, or Selection Pane shape name.

| Marker | Picture replacement |
| --- | --- |
| `{{SpeakerPhoto}}` | First speaker |
| `{{Speaker1Photo}}` | First speaker |
| `{{Speaker2Photo}}` | Second speaker |
| `{{NextEventSpeakerPhoto}}` | First next-event speaker on a next-only slide |
| `{{NextEventSpeaker1Photo}}` | First next-event speaker on a next-only slide |
| `{{NextEventSpeaker2Photo}}` | Second next-event speaker on a next-only slide |

Use a real picture and include supported **text tokens on that slide too**, because uploaded-template photo processing is triggered by text-token detection. Photo markers alone do not classify speaker count.

## Meetup event description

**Where:** **Settings > General > Meetup Description Template**, or a group's **Meetup > Meetup Description Template** override.

| Parameter | Meaning |
| --- | --- |
| `{{EventTitle}}` | Event title |
| `{{EventDate}}` | Formatted event date |
| `{{EventTime}}` | UK start time |
| `{{EventDescription}}` | Stored event description/agenda |
| `{{TalkTitle}}` | First talk title |
| `{{SpeakerName}}` | Speaker name(s), joined for multiple speakers |
| `{{Talks}}` | Talk titles, speakers, and third-person descriptions |
| `{{TalkDescriptions}}` | Talk descriptions only |
| `{{SpeakerSection}}` | Speaker heading, selected bios, and contact links |
| `{{Location}}` | Event location/platform |
| `{{ZoomUrl}}` | Saved Zoom join URL |
| `{{ZoomPasscode}}` | Saved Zoom passcode |
| `{{ZoomSection}}` | Combined Zoom block, blank without a link |
| `{{GroupName}}` | Group name |
| `{{WebsiteUrl}}` | Group website |
| `{{Hashtags}}` | Group default hashtags |

Known empty values become blank. The group's description template/bio preference overrides global values when set. See the [Meetup description example](USER-GUIDE.md#meetup-description-placeholders).

## Social announcement text

**Where:** a group's **Social > Social Post Template**. One template generates separate X, BlueSky, and LinkedIn text.

| Parameter | Meaning |
| --- | --- |
| `{{GroupName}}` | Group name |
| `{{EventTitle}}` | Event title |
| `{{SpeakerNames}}` | Speaker names joined with "and" |
| `{{SpeakerMentions}}` | X/BlueSky handles with name fallback; names for LinkedIn |
| `{{TalkTitles}}` | Quoted talk titles joined with "and" |
| `{{ShortDate}}` | Short UK date, including ordinal |
| `{{ShortTime}}` | UK start time in 24-hour format |
| `{{Where}}` | Format-aware venue/Zoom summary |
| `{{Venue}}` | Venue name, with built-in venue fallback |
| `{{Format}}` | In Person / Hybrid / Online |
| `{{ShortUrl}}` | Short URL, Meetup URL, group website, or default |
| `{{Hashtags}}` | Group default hashtags |
| `{{Sponsors}}` | Sponsor handles/name fallbacks for the platform |
| `{{SponsorNames}}` | Sponsor names |

With no named sponsors, lines containing sponsor placeholders are omitted. See the [social template example](USER-GUIDE.md#social-text-placeholders).

## Parameters that also control PPTX behaviour

| Marker/token | Effect beyond text replacement |
| --- | --- |
| First-speaker name/title tokens | Classify a slide as one-speaker unless second-speaker tokens are present |
| `{{Speaker2Name}}`, `{{Talk2Title}}` | Classify a current-event slide as two-speaker |
| `{{NextEventSpeaker2Name}}`, `{{NextEventTalk2Title}}` | Classify a next-only slide as two-speaker |
| `{{EventDate}}`, `{{EventDateLine}}` | Make the slide eligible for social PNG selection |
| Literal `Social Images` divider | Hides the divider and later current-date-token slides in the presentation; **not** a curly-brace parameter |

Read the [deck-wide hiding rules](PPTX-TEMPLATES.md#slide-classification-and-hiding) before designing separate speaker slides. The app selects the first matching date-token candidate for the PNG; there is no manual slide-number selector.

The seeded `SocialPostTemplates` database rows contain some legacy **single-brace** example text. They are not an additional editable template system in the current organiser UI. Use the double-curly parameters for the screens and uploaded decks documented here.

# PPTX template authoring

[Documentation home](../README.md) | [User guide](USER-GUIDE.md) | [Troubleshooting](OPERATIONS.md)

For a consolidated list across all template systems, see [Double-curly-brace template parameters](TEMPLATE-PARAMETERS.md).

## How templates work

Each meetup group can have **one uploaded PowerPoint deck**. Author the entire presentation: welcome slides, speaker layouts, sponsor slides, announcements, closing slides, and optional social-card layouts. The app copies the deck, replaces supported text placeholders, swaps speaker photographs, and hides layout variants that do not apply.

It does **not** infer your desired layout, create a brand theme from group colours, or import sponsor logos automatically.

Use **`.pptx`**, not `.potx`, `.ppt`, or `.pptm`. Microsoft Office is needed only if you choose it as the authoring/viewing tool; the server uses Open XML and SkiaSharp.

The repository's `pptx\1 Speaker.pptx` and `pptx\2 Speakers.pptx` are the bundled **fallback decks**, with legacy sample-text replacements as well as tokens. You may use them as layout references, but replace literal example content with the documented tokens in your own uploaded deck.

## Creating a template in PowerPoint

### 1. Create a widescreen deck

Create a blank presentation and choose **Design > Slide Size > Widescreen (16:9)**. Use your brand colours, background, static logos, and suitable fonts.

Use editable text boxes for dynamic text. Put dynamic placeholders directly on slides, not only on a slide master/layout, in speaker notes, or inside a flattened background image. Those other locations are not processed as normal slide text.

For reliable social PNGs, use simple shapes/text and PNG/JPEG artwork. The PNG previewer is an approximate renderer, not PowerPoint.

### 2. Add static slides

Create welcome, agenda, code of conduct, Wi-Fi, sponsor, and closing slides as needed. Slides with no text tokens remain as authored. Uploading a deck with **no text tokens anywhere** causes generation to use the built-in fallback deck instead.

Keep static sponsor artwork inside the deck. Sponsor records in the app are used for social text, not automatically injected into slide pictures.

### 3. Add a one-speaker layout

Insert text boxes containing:

```text
{{SpeakerName}}
{{TalkTitle}}
```

Optionally add `{{EventVenueLine}}`, `{{EventSubtitle}}`, and `{{ShortUrl}}`.

Insert an actual picture as a temporary headshot. Use **Edit Alt Text** (or Format Picture > Alt Text, depending on your PowerPoint version) and put exactly:

```text
{{Speaker1Photo}}
```

in its description. The legacy Alt Text **Title** field is also supported. This is **picture alt text**, not a visible textbox or the shape's name in the Selection Pane. Use only the marker in the field; remove automatic descriptive text.

For a circular headshot, use **Picture Format > Crop > Crop to Shape > Oval** and make the picture square. The app replaces the image bytes while retaining the authored picture geometry.

### 4. Add a two-speaker layout

Duplicate/rework the slide to include:

```text
{{Speaker1Name}}
{{Talk1Title}}

{{Speaker2Name}}
{{Talk2Title}}
```

Give its two pictures alt text `{{Speaker1Photo}}` and `{{Speaker2Photo}}`.

The **text tokens** determine the slide's speaker-count classification; photo alt text alone does not. Do not split speaker 1 and speaker 2 onto separate slides and expect both slides to remain visible in a two-speaker deck: the presence of any two-speaker layout can hide all current-event speaker-1-only layouts.

### 5. Add optional next-event slides

Use only `{{NextEvent...}}` text tokens on each next-event slide:

```text
Next meetup
{{NextEventDateLine}}
{{NextEventVenueLine}}
{{NextEventSpeakerName}}
{{NextEventTalkTitle}}
{{NextEventShortUrl}}
```

Use next-event photo markers such as `{{NextEventSpeaker1Photo}}`. Literal headings such as "Next meetup" are fine; the rule concerns **tokens**, not literal text.

For a two-speaker next-event alternative, include `{{NextEventSpeaker2Name}}` or `{{NextEventTalk2Title}}`. Keep both variants in the same deck if needed.

### 6. Add social-card slides

Add a divider slide whose **entire text is `Social Images`**, with no placeholders or other captions. After it, add a one-speaker and a two-speaker promo slide.

Each social-card slide must contain `{{EventDateLine}}` (recommended) or `{{EventDate}}` so the app can find it. Also include the relevant speaker/talk text tokens and photo markers.

Example one-speaker social layout:

```text
{{EventDateLine}}
{{EventVenueLine}}
{{EventSubtitle}}

{{SpeakerName}}
{{TalkTitle}}

{{ShortUrl}}
```

Add speaker 2 name/title to the two-speaker variant. Keep long titles and names in generous boxes and allow sufficient room for the date and venue line.

The app automatically hides this divider and subsequent date-token slides from the exported **slideshow**, while still using a matching date-token slide for the social PNG. Hidden slides remain in the PPTX file.

**Keep this social section at the end.** The hide rule scans every later slide containing a current event date token, not merely the slides up to the next divider.

### 7. Save, upload, and check

1. Save as a `.pptx`.
2. Open **Groups > Edit > Custom PPTX Template**.
3. Upload the file (maximum **100 MB**) and wait for slide processing.
4. Switch between **Preview 1 speaker** and **Preview 2 speakers**.
5. Check detected placeholder counts/classifications; no-placeholders warnings mean the app cannot use your deck dynamically.
6. Choose **Save Group**.
7. Create saved one- and two-speaker test events, open **Social & Slides**, and download both decks and PNGs.
8. Inspect the PPTX in PowerPoint slide view and slideshow mode, including hidden-slide markers. Check the PNG separately.

Uploading a revised file creates a new blob/reference. Keep your editable master locally; there is no template version-history/editor UI. Upload changes through the group page instead of overwriting an existing blob to avoid stale cached previews.

## Text placeholder rules

Use the exact `{{TokenName}}` form below with **no spaces inside the braces**. Replacement is case-insensitive, but exact documented names are easiest to maintain.

PowerPoint can split typed tokens across formatting runs. The app can join runs within a paragraph to replace a token, but then the paragraph's replacement content can inherit the first run's formatting. Keep each token in a consistently formatted textbox/paragraph. Never split a token across separate shapes or paragraphs.

Unknown placeholders remain visible. Detection does not validate every token's meaning: an unsupported token can be counted in the preview without being replaced.

### Current-event tokens

| Token | Replacement |
| --- | --- |
| `{{SpeakerName}}`, `{{Speaker1Name}}` | First session's speaker |
| `{{Speaker2Name}}` | Second session's speaker |
| `{{TalkTitle}}`, `{{Talk1Title}}` | First session's talk title, with event-title fallback |
| `{{Talk2Title}}` | Second session's talk title |
| `{{ShortUrl}}` | Event short URL, then Meetup URL, then a default |
| `{{EventDate}}` | Long date using the stored event date; legacy formatter uses a literal `th` suffix |
| `{{EventTime}}` | UK time such as `18:30 UK time` |
| `{{Venue}}` | Event Location / Platform, with `Online` fallback; not the separate short Venue Name |
| `{{EventDateLine}}` | Uppercase UK date with correct ordinal suffix, for example `MONDAY 27TH APRIL 2026` |
| `{{EventVenueLine}}` | Format-aware uppercase venue/time header using venue name and BST/GMT |
| `{{EventFormat}}` | `Hybrid`, `In Person`, or `Online` |
| `{{EventSubtitle}}` | Optional promo subtitle, empty when not set |

Sessions are ordered by their `OrderIndex`; the first two supply these slots. Missing speaker/session details can produce sample-like defaults such as "Speaker One", "Speaker Two", or "Talk 2", so complete the saved event before publishing.

Prefer **`{{EventDateLine}}`** and **`{{EventVenueLine}}`** for correct UK date/time headers. `{{EventDate}}` is a legacy date format, not equivalent to `EventDateLine`.

There is currently **no current-event PPTX token** for `{{EventTitle}}`, `{{GroupName}}`, `{{SpeakerBio}}`, `{{TalkDescription}}`, `{{Sponsors}}`, or `{{Logo}}`. Some of those names exist in text-post/Meetup templates, which are different systems. Use literal group branding and supported title tokens, or change the generator explicitly if more dynamic fields are required.

### Next-event tokens

| Token | Replacement |
| --- | --- |
| `{{NextEventTitle}}` | Following event's title |
| `{{NextEventDate}}`, `{{NextEventTime}}` | Following event's legacy long date and UK time |
| `{{NextEventDateLine}}`, `{{NextEventVenueLine}}` | Following event's promo headers |
| `{{NextEventVenue}}` | Following event's Location / Platform |
| `{{NextEventFormat}}`, `{{NextEventSubtitle}}` | Following event's format and optional subtitle |
| `{{NextEventShortUrl}}` | Following event's short URL or Meetup URL |
| `{{NextEventSpeakerName}}`, `{{NextEventSpeaker1Name}}` | First following speaker |
| `{{NextEventSpeaker2Name}}` | Second following speaker |
| `{{NextEventSpeakers}}` | Following speakers joined with ` & ` |
| `{{NextEventTalkTitle}}`, `{{NextEventTalk1Title}}` | First following talk |
| `{{NextEventTalk2Title}}` | Second following talk |

For event deck downloads, the following event is the earliest event **in the same group** with a strictly later date/time. It is not filtered to Published/Scheduled, so a future draft can be selected.

Missing next-event text generally becomes `TBC`; the subtitle becomes empty. However, slides containing **only next-event tokens** are hidden entirely when no next event exists. A slide mixing current and next tokens does not follow that next-only visibility rule.

The social-image flow does not currently pass a next event into its renderer. Do not design the current event's social card around next-event data.

## Picture markers

Insert a real PNG/JPEG picture and set one of these markers in its alt-text description or title:

| Marker | Picture |
| --- | --- |
| `{{SpeakerPhoto}}`, `{{Speaker1Photo}}` | First current speaker |
| `{{Speaker2Photo}}` | Second current speaker |
| `{{NextEventSpeakerPhoto}}`, `{{NextEventSpeaker1Photo}}` | First following speaker on a next-only slide |
| `{{NextEventSpeaker2Photo}}` | Second following speaker on a next-only slide |

Photo markers are not normal text replacements; do not type them into a textbox and expect a photo.

**Put supported text tokens on the same slide too.** In the uploaded-deck path, picture swapping only runs on slides detected as having text tokens. For next-event images, keep the slide's text tokens next-only: photo data is selected from the slide's text classification, not from the marker prefix in isolation.

Photos are converted to centre-cropped **600x600 PNGs**, with orientation normalisation and initials placeholders for missing/unreadable images. The original picture's crop rectangle is removed; author the final shape/position rather than depending on a crop from the sample image.

If no photo markers exist on a classified speaker slide, the generator heuristically picks large near-square pictures (at least about one inch per side): the largest for one speaker, or the two largest ordered left-to-right for two. **Use explicit markers** to avoid replacing a square logo by mistake. Once any explicit marker is found, the heuristic is not used for the remaining pictures.

## Slide classification and hiding

| Text on a slide | Classification |
| --- | --- |
| Any `{{Speaker2Name}}` or `{{Talk2Title}}` | Two-speaker current-event layout |
| Any first-speaker name/title token, without a second-speaker token | One-speaker current-event layout |
| No speaker name/title tokens | Always layout, subject to other hide rules |
| Only next-event tokens, including second-speaker/title tokens | Two-speaker next-event layout |
| Only next-event tokens, including first-speaker/title tokens | One-speaker next-event layout |
| Only next-event aggregate/date/venue tokens | Always layout within the next-event section |

For a one-speaker event, two-speaker layouts are hidden. For two or more sessions, one-speaker layouts are hidden **when a two-speaker layout exists** in the same current/next category; otherwise the one-speaker layout remains as a fallback.

Visibility is deck-wide, not paired by slide position or by matching titles. A one-speaker intro elsewhere in the deck can therefore be hidden by a two-speaker social slide. Structure all speaker-count-dependent content as deliberate variants.

Next-only slides use the **following event's** session count, independently of the current event's count. `{{NextEventSpeakers}}` is an aggregate token and does not by itself classify a slide as one-speaker.

Current-event slides you manually hid remain hidden. Applicable next-only slides can be unhidden by the generator. Social-section slides are hidden regardless of their matching speaker count.

**Hiding is not deletion/redaction.** Other-speaker layouts, social slides, and embedded sample artwork may still be inspected in the downloaded file. Do not put confidential content on a hidden slide.

## Which slide becomes the social PNG?

The app considers slides containing `{{EventDate}}` or `{{EventDateLine}}`, across the **entire deck**, including hidden slides:

1. Prefer the first slide classified for the event's speaker count.
2. Otherwise prefer the first candidate that is not the opposite speaker-count layout.
3. Otherwise use the first date-token candidate.

The `Social Images` divider controls deck hiding, **not** PNG selection. A date-bearing welcome/speaker slide earlier in the deck can be selected instead of your intended social slide.

To make selection predictable, put current event date tokens only on the desired social-card candidates, or ensure your intended matching candidate appears first. Use literal headings or other supported tokens on earlier presentation slides.

If the custom template is missing, has no date-token candidate, or cannot be rendered, the image generator falls back to the built-in card. Built-in branding is not automatically adapted to a different group.

The built-in card is exactly **1280x720**. A custom slide is rendered at **1280 pixels wide with proportional height**; use 16:9 to get the same dimensions.

## Fonts and visual fidelity

The downloaded PPTX keeps the deck's Open XML design and is rendered by the presentation application opening it. Install the fonts required by your deck on the presenting machine, or use fonts reliably available there.

The web preview/social PNG is rendered by the app. It supports common backgrounds, pictures, shapes, grouped content, and text, but is not a full PowerPoint engine. Complex effects, charts, SmartArt, video, animation, and unsupported drawing features may be simplified or omitted. Preview differences do not necessarily mean the PPTX is damaged.

For predictable cards:

- Use straightforward textboxes and raster artwork for visual features the previewer cannot reproduce.
- Keep names/titles within generous bounds; there is no general-purpose auto-layout engine.
- Use installed fonts or bundle fonts you are licensed to redistribute.
- Check both the server-generated PNG and the deck opened in PowerPoint.

The app resolves installed fonts first, then `.ttf`/`.otf` files discovered under `Resources\fonts`, then a fallback such as Segoe UI. **Baloo 2** is bundled. The Infrastructure project currently copies `Resources\fonts\*.ttf` to build/publish output; adding `.otf` files also requires making their publish inclusion explicit.

Some long venue/time header paragraphs are shrunk automatically, but this is not a substitute for designing for long content.

## Suggested deck structure

| Order | Slide | Dynamic content |
| --- | --- | --- |
| 1 | Welcome / group branding | Static |
| 2 | Tonight: one speaker | First speaker/title and marked picture |
| 3 | Tonight: two speakers | Both names/titles and marked pictures |
| 4 | Sponsors / housekeeping | Static |
| 5 | Next event: one speaker | Next-only text tokens and picture |
| 6 | Next event: two speakers | Next-only second-speaker tokens and pictures |
| 7 | Closing | Static |
| 8 | Social Images | Exact divider text, no tokens |
| 9 | One-speaker social card | Current date/header, first speaker/title, picture, URL |
| 10 | Two-speaker social card | Current date/header, both names/titles, pictures, URL |

This is a suggested organisation, not a required slide numbering convention. There is no UI to select a social slide number; tokens determine selection.

## Troubleshooting checklist

| Symptom | Check |
| --- | --- |
| Built-in deck downloaded instead | Saved group template reference, private blob access, valid `.pptx`, and at least one supported text token |
| Tokens still visible | Exact supported names, no spaces inside braces, text on slides rather than master/notes/image |
| Wrong speaker layout visible | Text-token classification and deck-wide presence of two-speaker layouts |
| Wrong/no headshot | Picture alt text, supported text token on the slide, correct saved speaker/session order |
| Sponsor logo replaced by a face | Add explicit photo markers rather than relying on size-based picture selection |
| Wrong social slide selected | Earlier current-date-token candidates and their speaker classifications |
| Social slides appear in slideshow | Exact `Social Images` divider and current date tokens after it |
| Upcoming slide hidden or `TBC` | A later saved event in the same group, session details, next-only classification |
| PNG is not 1280x720 | Set template slide size to 16:9 |
| PNG differs from PowerPoint | Renderer support, server fonts, and simple artwork/text |
| Preview is stale | Re-upload a new file, save the group, and reopen the event assets |

For storage/runtime errors, see [operations and troubleshooting](OPERATIONS.md).

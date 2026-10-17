# Application user guide

[Documentation home](../README.md) | [PPTX templates](PPTX-TEMPLATES.md) | [API integrations](API-INTEGRATIONS.md)

The [template parameter reference](TEMPLATE-PARAMETERS.md) lists every supported `{{...}}` token by template type.

This guide covers the implemented organiser UI and speaker portal. Complete [local setup](LOCAL-SETUP.md) or [Azure setup](AZURE-SETUP.md) first.

## First-event walkthrough

1. Sign in, open **Groups**, and create/edit a group. Set its name, location, Meetup slug, organiser email, and social defaults; choose **Save Group**.
2. Open **Settings > General**, set the default organiser email, review the description template and bio preference, and choose **Save All Changes**.
3. Open **Events > Create Event > Quick Invite**. Select the group, date/time, venue, format, and one or two speakers. Add names/emails if known.
4. Choose **Create event & speaker links**. A draft event and speaker slots are saved. Copy each link, or use the email control. A valid supplied email triggers an automatic send attempt when the link appears.
5. Each speaker opens their own link, supplies their talk/profile/headshot, and chooses **Save My Details**. No organiser account is needed.
6. Open the event, review both sessions, date/time, location, registration URL, short URL, Zoom information, and status, then choose **Save Event**.
7. Open **Social & Slides**, inspect/download the PNG and PPTX, and check the deck in PowerPoint.
8. Create the Meetup event through its API or copy the title/description manually. Save the resulting real Meetup URL back on the event. Publish social posts deliberately, one platform at a time.

For your own visual branding, follow [creating a PPTX template](PPTX-TEMPLATES.md#creating-a-template-in-powerpoint).

## Navigation and access

| Screen/route | Purpose |
| --- | --- |
| Home `/` | Public landing page and sign-in |
| Dashboard `/dashboard` | Counts, upcoming meetups, quick actions, attendance |
| Groups `/groups` | Group settings and branding |
| Events `/events` | Search, filter, create, import, clone, delete |
| Speakers `/speakers` | Reusable speaker directory and Sessionize import |
| Settings `/settings` | General defaults and API credentials |
| Event editor `/events/edit/{id}` | Event details and **Social & Slides** tab |
| Quick Invite `/events/invite` | Minimal event creation and speaker invitation links |
| Speaker portal `/speaker/submit/{token}` | Single-use self-service submission |

The older `/assets?eventId=<id>` address redirects to the event's Social & Slides tab. With no event ID it returns to Events.

Organiser screens require authentication, but current access is shared across the app: there are no per-group memberships, separate read-only organiser roles, or individual ownership boundaries. In local fallback mode there is one predefined developer identity, not a password-based user directory.

## Dashboard

The dashboard shows total events, upcoming scheduled events, speaker count, group count, and a short upcoming-event list. Use **View All Events** for the full history and **Create Event** for New Event or Quick Invite.

Attendance controls retrieve Meetup counts for events with a valid Meetup URL. A dash means unknown/not available, not zero. These values are snapshots, not a live attendee registration system; use refresh to request a new count. Public-site/API access restrictions can make counts unavailable.

## Groups

Open **Groups**, choose a group's edit action or create a new group. Changes across tabs are kept with **Save Group**; navigating away without saving can lose edits.

### Group Details

- **Group Name** is required.
- **Website URL**, **Description / Tagline**, **Default Location**, and **Default Hashtags** describe the group and feed content defaults.
- **Primary/Secondary Brand Color** and **Group Logo** are stored branding values. They do not automatically recolour an uploaded PowerPoint deck or replace its artwork.
- **Organiser Email** receives submission notifications for this group; blank falls back to Settings.
- Logos accept PNG, JPG/JPEG, or WebP, up to **2 MB**. Upload/remove, then save the group.

New-group and seeded defaults contain .NET Notts/CGI examples; replace them with your actual group details.

### Meetup tab

Set the **Meetup Group Slug (urlname)** to the path segment from `https://www.meetup.com/<slug>/`, not the full URL.

An optional **Meetup API Key Override** holds a legacy token. A connected global OAuth token can still take priority; it is not a separate per-group OAuth connection. See [configuration precedence](CONFIGURATION.md#sources-and-precedence).

Choose **Speaker Bios** to inherit the global setting or select third-person/original bios. Enter a **Meetup Description Template** to override the global template, or leave it blank to inherit. The built-in default includes a specific Nottingham venue paragraph, so customise it for other locations.

### Social tab

**Short URL Stem (Bitly)** is a naming convention. A stem such as `https://bit.ly/mygroup` produces a suggestion such as `https://bit.ly/mygroupoctober2026`.

**The app does not create or verify a Bitly redirect.** Create the short link yourself and check its target. Use **Use default** on the event to apply the suggestion, or enter a different URL.

**Social Post Template** is shared by X, BlueSky, and LinkedIn for this group. Platform-specific handles are substituted when possible; LinkedIn uses speaker/sponsor names. Blank uses the built-in template. See [social text placeholders](#social-text-placeholders).

### Sponsors tab

Choose **Add Sponsor** and enter its name, website, optional logo, X handle, BlueSky handle, and LinkedIn URL. Logos have the same format/2 MB limit as group logos.

**Copy** duplicates a sponsor record in this group. **Delete** asks for confirmation; deletion is persisted when you save the group.

Use `{{Sponsors}}` or `{{SponsorNames}}` in social text to include sponsors. Sponsor database records and logo uploads do **not** automatically replace logos/banners in PPTX decks or the built-in PNG design. Author the required sponsor artwork in the custom deck.

### Custom PPTX Template tab

Upload a **`.pptx`**, up to **100 MB**, then choose **Preview 1 speaker** and **Preview 2 speakers**. Token-filled slides display sample data; slides without placeholders are dimmed/unchanged. Previews approximate the finished PowerPoint, not an Office rendering.

Choose **Save Group** after uploading. Template upload alone does not persist the group's new template reference.

See [PPTX template authoring](PPTX-TEMPLATES.md) for the complete contract. A group can use one uploaded deck containing both speaker-count variants and optional social/next-event slides.

### Deleting groups

A group with events cannot be deleted. Review/remove or move those events first if deletion is genuinely intended. Group deletion is not a storage cleanup tool.

## Settings

**General** contains the global speaker bio preference, default organiser recipient, and Meetup description template. **API Integrations** contains BlueSky, Zoom, Meetup, Sessionize, X, and LinkedIn panels.

Use **Save All Changes** before leaving the page, connecting Meetup, or publishing. Filled-credential badges indicate presence, not successful live API authentication.

Saved non-blank settings override configuration. Clearing a field may expose an environment/user-secret value rather than disabling the feature. Credentials saved here are held in SQL; see [configuration and secrets](CONFIGURATION.md).

## Speakers

The directory stores reusable profiles across all groups. Search/filter the directory, then use **Add Speaker** or edit an existing profile.

### Details and headshots

Enter the full name, optional email/pronouns, original bio, and social profiles. Supported fields include LinkedIn, BlueSky, X, Facebook, GitHub/repository, and website.

The editable **Third-person bio** is generated from the original bio when appropriate; **Regenerate** replaces it with another generated conversion. This is deterministic text conversion, not a cloud AI rewrite. Review the result, especially names/pronouns, before publishing.

Speaker headshots accept PNG/JPEG/WebP and allow up to **10 MB**. Save the profile after uploading. Usable imported remote photos are copied into configured blob storage when saved. Deck/card generation uses initials placeholders when a photo cannot be loaded.

Headshots are centre-cropped for the deck; use a clear, centred face with space around it. Missing optional contact/social data is allowed.

### Sessionize profile lookup

**Find on Sessionize** accepts a speaker name or public Sessionize profile URL:

1. Search and select the correct profile if several match.
2. Compare current and incoming bio, photo, and links.
3. Select only the fields you want to apply, set pronouns separately, and choose **Apply selected**.
4. Review and save the speaker.

Public profile search is separate from the Sessionize event API. If name search is ambiguous or fails, paste the exact public profile URL.

### Importing from a Sessionize event

Use the directory's Sessionize import control with an API ID or public Speakers endpoint. Bulk import matches existing profiles by name, adds missing speakers, and fills blank fields on existing speakers rather than overwriting populated details.

On **Add Speaker**, **Import from Sessionize > Load Speakers** lets you choose one returned speaker to populate the form. Review and save it.

### Talks and deletion

Existing speaker profiles have a **Talks** tab showing bookings in this app and available public Sessionize talks. Event links open their editor.

A speaker linked to an event session cannot be deleted. Changing a directory profile affects future content generation for every event that references that speaker; decks already downloaded and posts already published do not update automatically.

## Events

Use **Events** to search by title/speaker and filter by group/status. The create menu offers **New Event**, **Quick Invite**, and **Sync Past Meetup Events**.

### New Event and editing

Select a group, enter a title/agenda, and set date/time, duration, format, venue, location/platform, and optional promo subtitle.

The editor supports one or two speaker slots. Select an existing speaker, add one through the directory, or generate a magic link for a placeholder speaker. Choose a Sessionize talk through the talk picker, or **Create a new talk** and enter title/abstract manually. Existing populated slots offer **Change speaker or talk**.

Picking a talk can regenerate the event title/description. Review the text afterwards; the event agenda should not accidentally contain duplicate generated speaker sections.

**Save Event** persists the event and slots. Saving a new event opens its editor with the **Social & Slides** tab available. Selecting one speaker on an existing two-speaker event and saving removes the second event session.

### Dates, formats, and status

The date input is labelled **Local**, but conversion currently uses the **web server's** local timezone, not a detected browser/user timezone. Stored values are UTC. Promotional headers/social text render UK time (`Europe/London`, with BST/GMT), while the speaker portal's context shows UTC.

For a server outside the intended timezone, verify a known event end-to-end before sending invitations. Also check the scheduled start in Zoom after API creation; its current call path should not be assumed timezone-safe.

| Field | Behaviour |
| --- | --- |
| Hybrid | Promo venue line describes venue plus Zoom |
| In Person | Promo venue line describes venue only |
| Online | Venue field disabled; promo venue line uses Zoom |
| Promo Subtitle | Optional extra header line; no subtitle when blank |
| Draft / Scheduled / Published / Completed | Stored workflow labels, not timers that trigger external actions |

When no venue name is supplied, promotional formatting can fall back to **CGI**. Set the real venue and review the header preview.

### Zoom

Choose **Generate Zoom Link** to create a scheduled meeting when API credentials are configured, or to apply the fixed default meeting URL/passcode from Settings. You can also enter a join URL/passcode manually.

Save the event afterwards. Repeated API generation may create additional external meetings; changing the local event later does not automatically update/cancel the external meeting.

### Pull Sessions from Sessionize

Enter a Sessionize **API event ID** and choose **Pull Sessions**. The current editor takes the first two returned sessions and the first speaker for each, creating missing directory speakers as needed. It is not a multi-track programme synchroniser or a session-selection wizard.

Use an event ID when pulling both speakers and sessions: a single full endpoint URL is used unchanged by each client method and cannot represent both response shapes. Review both slots and save the event afterwards.

### Quick Invite and magic links

Quick Invite creates a draft event and one/two new speaker profiles/placeholders. It does not require an existing directory speaker. If a person is already in the directory, using the normal editor avoids unnecessary duplicate profiles.

You can also choose **Magic Link** on a normal event slot. This saves the event/session first and creates a placeholder profile if none is selected.

- Links expire after **14 days** by default and work for one successful submission.
- Generating a new link revokes previous unused links for that session.
- Only the SHA-256 token hash is stored; treat the full URL as a credential.
- Link controls show **Copy** and **Email link**. A valid known email triggers an automatic send attempt when a new link is displayed; generating/regenerating a link can send real email.
- If email is not configured, nothing is sent; copy the URL manually.
- Links use the hostname from which the organiser generated them. `localhost` links are not reachable by remote speakers.

There is no user-facing list for recovering raw links after leaving the page. Generate a replacement if necessary and send only the newest one.

### Speaker self-service portal

The invited speaker opens their unique link without signing in, enters the talk title/abstract, full name, email, optional pronouns/bio, headshot, and social profiles, then chooses **Save My Details**.

Success saves the profile/session and consumes the token. The app may replace a placeholder event title with the submitted talk/speaker title. It attempts an organiser notification and, for a valid speaker email, a confirmation with calendar attachment and optional inline RSVP request. Email failure does not undo the saved submission.

After submission, the speaker must ask for another link to make changes, or the organiser can edit the profile/session. The app does not track replies to calendar invitations.

### Cloning and deleting

**Clone** creates a draft dated one month after the source and copies title (with `(Copy)`), description, duration, location, group, and sessions referencing the same directory speakers.

Do not assume all other fields are cloned: review format, venue, subtitle, registration/short URLs, and Zoom details. Magic links, external event identity, attendance, and published social posts are not carried over as new external resources.

**Delete** asks for confirmation and removes the local event, sessions, associated tokens, and generated-asset records. It does not delete external Meetup events, Zoom meetings, social posts, or uploaded blobs.

### Sync Past Meetup Events

1. Filter Events to a **specific group**.
2. Choose **Create Event > Sync Past Meetup Events**.
3. Review the returned history, title/details, and inferred speakers.
4. Match each person to an existing directory profile or mark **Not the same person**. Optionally apply selected Sessionize fields, or use Meetup details.
5. Choose **Import events**.

Existing external Meetup event IDs are skipped. Imported events are marked **Completed**; speakers may be reused, enriched, or created. Review inferred speaker/talk data because external descriptions are not a guaranteed structured source.

OAuth/API access is preferred. The service can attempt a public Meetup-page fallback when credentials/access are unavailable, but public-page extraction can be incomplete or blocked. This is a one-way import, not continuous synchronisation.

## Social & Slides

Save the event before opening this tab. It prepares the event's social image, Meetup title/description, and separate LinkedIn/X/BlueSky text.

### Downloads and image refresh

Choose **Download Social PNG** or **Download PPTX Deck**. Files are generated from current stored data on request, rather than retrieved from a saved asset library. **Refresh** reloads the stored event and regenerates the image; it does not save edits or regenerate all text fields.

The built-in image renderer uses 1280x720. A custom deck's social slide is rendered at width 1280 using the deck's aspect ratio, so author it at **16:9** to produce 1280x720.

Save changes to speakers/groups/events before reloading the assets tab. Reopen/reload it to regenerate text after source/template changes.

### Meetup publication

The editable title/description fields support **Copy Title** and **Copy Description** for pasting into Meetup's create-event form.

**Important:** **Publish to Meetup** currently uses the saved event and regenerates its description from the configured template. It does **not** submit edits made only in the copyable preview fields.

Without API credentials, the service returns the group's manual create-event URL; the UI can still label this result "Published". An `/events/create` URL means **manual creation is still required**, not that a real event was created.

With working credentials, the API creates a new external event. The current tab does not automatically write the returned URL/ID or Published status back to the local event and is not an update-in-place publisher. Copy the real event URL into the event editor and save. Avoid clicking Publish again without checking Meetup, as this can create duplicates.

### LinkedIn, X, and BlueSky

Review/edit each platform's text. Leave **Attach social card image** checked to include the prepared PNG through the API, or uncheck it for text/link-only posting.

API buttons use the corresponding editable text. They post immediately and independently; there is no scheduled publishing queue, automatic retry history, or cross-platform transaction.

For manual posting, use **Web Share** / **Web Intent**, or copy the text into the platform and attach the downloaded PNG yourself. Web fallbacks do not upload images. The X/LinkedIn fallback URLs are prepared from the original generated text, not live textarea edits; copy your revised text manually. BlueSky's intent follows its edited text.

Watch the displayed X (280) and BlueSky (300) character counts, but let the platform/API response be authoritative for its actual rules. LinkedIn API posting is to a personal author, not an organisation. See [API integrations](API-INTEGRATIONS.md) for permissions, tokens, and error handling.

## Text template reference

These text systems are independent of PPTX replacement. A placeholder accepted by one is not automatically accepted by another.

### Meetup description placeholders

Use `{{Name}}` in **Settings > General > Meetup Description Template** or the group's Meetup override:

| Placeholder | Value |
| --- | --- |
| `{{EventTitle}}` | Event title |
| `{{EventDate}}`, `{{EventTime}}` | Formatted event date and UK start time |
| `{{EventDescription}}` | Event's stored description/agenda |
| `{{TalkTitle}}` | First talk title |
| `{{SpeakerName}}` | Speaker name(s) |
| `{{Talks}}` | Talk titles, speakers, and third-person descriptions |
| `{{TalkDescriptions}}` | Talk descriptions only |
| `{{SpeakerSection}}` | Speaker heading, selected bios, contact links |
| `{{Location}}` | Location/platform |
| `{{ZoomUrl}}`, `{{ZoomPasscode}}` | Saved Zoom details |
| `{{ZoomSection}}` | Combined Zoom block, blank without a link |
| `{{GroupName}}`, `{{WebsiteUrl}}`, `{{Hashtags}}` | Group values |

Placeholders are case-insensitive. Known empty values become blank; unknown names remain visible, so fix typos. Third-person/original preference primarily controls published speaker bios; review any automatically converted talk text too.

A location-neutral starting template:

```text
# {{EventTitle}}

{{EventDescription}}

{{Talks}}

{{SpeakerSection}}

When: {{EventDate}} at {{EventTime}} UK time
Where: {{Location}}

{{ZoomSection}}

{{Hashtags}}
```

Keep the event's stored agenda concise when your template adds talks/bios, to avoid duplication.

### Social text placeholders

Use these in the group's **Social > Social Post Template**:

| Placeholder | Value |
| --- | --- |
| `{{GroupName}}`, `{{EventTitle}}` | Group/event name |
| `{{SpeakerNames}}` | Names joined with "and" |
| `{{SpeakerMentions}}` | X/BlueSky handles with name fallback; LinkedIn names |
| `{{TalkTitles}}` | Quoted talk titles joined with "and" |
| `{{ShortDate}}`, `{{ShortTime}}` | Short UK date and 24-hour start time |
| `{{Where}}` | Venue/Zoom summary based on format |
| `{{Venue}}`, `{{Format}}` | Venue and format label |
| `{{ShortUrl}}` | Short URL, falling back to Meetup URL, group website, then a default |
| `{{Hashtags}}` | Group defaults |
| `{{Sponsors}}`, `{{SponsorNames}}` | Platform handles/name fallbacks, or names |

When there are no named sponsors, lines containing sponsor placeholders are dropped so a trailing "Thanks to" is not left behind.

Example:

```text
Next {{GroupName}}: {{SpeakerMentions}} on {{TalkTitles}}

{{ShortDate}} at {{ShortTime}} UK time
{{Where}}
Register: {{ShortUrl}}

{{Hashtags}}
Thanks to {{Sponsors}}
```

Shorten the template for platform limits. Handle substitution is not a guarantee of a platform-recognised rich mention.

## Before a real announcement

Review the saved date/time, format, actual registration/short-link target, both speakers' names/talks/photos, sponsor branding, and the finished PNG/PPTX. Confirm the intended external account and that API buttons will make real writes. Changes after publication require manual follow-up on the destination platform.
